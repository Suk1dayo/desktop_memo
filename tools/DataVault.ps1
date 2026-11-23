#requires -Version 5.1
<#
.SYNOPSIS
    DesktopMemo 数据保险箱 —— 把 data/ 目录加密成单个 .vault 文件。

.DESCRIPTION
    加密算法：AES-256-CBC + Encrypt-then-MAC(HMAC-SHA256)，口令派生 PBKDF2-SHA256 200,000 次。
    也可用 -Dpapi 走 Windows DPAPI（绑定当前用户，无需口令，但换机/换用户无法解密）。

    明文 = data/ 目录的 zip 打包；密文 = 单文件 .vault。
    加密后会自动回读校验，哈希一致才算成功。

    文件布局（小端）：
      [0..8)   magic "DMVAULT1"
      [8]      mode  0=口令 1=DPAPI
      [9..13)  PBKDF2 迭代次数
      [13..17) salt 长度 -> salt
      iv(16)
      wrappedKey 长度 -> wrappedKey（口令模式为空）
      mac(32)
      密文长度 -> 密文

.PARAMETER Mode
    lock   加密打包 data/  ->  .vault
    unlock 解密 .vault    ->  目录
    list   查看 .vault 元信息（不解密内容）

.EXAMPLE
    .\tools\DataVault.ps1 lock -Passphrase "你的长口令"

.EXAMPLE
    .\tools\DataVault.ps1 lock -Dpapi

.EXAMPLE
    .\tools\DataVault.ps1 unlock -VaultFile .\vault\xxx.vault -Passphrase "你的长口令" -OutDir .\restore
#>
[CmdletBinding(DefaultParameterSetName = 'Pass')]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('lock', 'unlock', 'list')]
    [string]$Mode,

    [Parameter(ParameterSetName = 'Pass')]
    [string]$Passphrase,

    [Parameter(ParameterSetName = 'Dpapi')]
    [switch]$Dpapi,

    [string]$VaultFile,
    [string]$OutDir,
    [string]$SourceDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$Root = Split-Path -Parent $PSScriptRoot
if (-not $SourceDir) { $SourceDir = Join-Path $Root 'data' }
if (-not $OutDir)    { $OutDir    = Join-Path $Root 'restore' }
$VaultDir = Join-Path $Root 'vault'

$script:Magic      = [System.Text.Encoding]::ASCII.GetBytes('DMVAULT1')
$script:Iterations = 200000

# ---------------- 基础工具 ----------------
function New-RandomBytes([int]$n) {
    $b = [byte[]]::new($n)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($b) } finally { $rng.Dispose() }
    return ,$b
}

function Get-Sha256Hex([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Test-BytesEqual([byte[]]$a, [byte[]]$b) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    if ($a.Length -ne $b.Length) { return $false }
    for ($i = 0; $i -lt $a.Length; $i++) { if ($a[$i] -ne $b[$i]) { return $false } }
    return $true
}

function Read-PromptPassphrase([string]$Prompt) {
    $sec  = Read-Host -Prompt $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Add-Int32([System.IO.MemoryStream]$ms, [int]$v) {
    $b = [BitConverter]::GetBytes($v)
    $ms.Write($b, 0, 4)
}

# ---------------- 加解密核心 ----------------
function Protect-Data {
    param([byte[]]$Plain, [string]$Pass, [switch]$UseDpapi)

    $iv  = New-RandomBytes 16
    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Mode    = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $aes.KeySize = 256
    $aes.IV      = $iv

    if ($UseDpapi) {
        Add-Type -AssemblyName System.Security
        $key     = New-RandomBytes 32
        $wrapped = [System.Security.Cryptography.ProtectedData]::Protect(
                       $key, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        $salt     = [byte[]]::new(0)
        $modeByte = [byte]1
        $hk = [System.Security.Cryptography.SHA256]::Create()
        try { $macKey = $hk.ComputeHash($key) } finally { $hk.Dispose() }
    }
    else {
        $salt = New-RandomBytes 16
        $kdf  = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
                    $Pass, $salt, $script:Iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
        try { $km = $kdf.GetBytes(64) } finally { $kdf.Dispose() }
        $key     = [byte[]]($km[0..31])
        $macKey  = [byte[]]($km[32..63])
        $wrapped = [byte[]]::new(0)
        $modeByte = [byte]0
    }

    $aes.Key = $key
    $enc = $aes.CreateEncryptor()
    try { $ct = $enc.TransformFinalBlock($Plain, 0, $Plain.Length) } finally { $enc.Dispose(); $aes.Dispose() }

    # Encrypt-then-MAC：覆盖 iv + salt + 密文
    $toMac = [byte[]]::new($iv.Length + $salt.Length + $ct.Length)
    [Array]::Copy($iv,   0, $toMac, 0, $iv.Length)
    [Array]::Copy($salt, 0, $toMac, $iv.Length, $salt.Length)
    [Array]::Copy($ct,   0, $toMac, $iv.Length + $salt.Length, $ct.Length)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($macKey)
    try { $mac = $hmac.ComputeHash($toMac) } finally { $hmac.Dispose() }

    $ms = [System.IO.MemoryStream]::new()
    $ms.Write($script:Magic, 0, $script:Magic.Length)
    $ms.WriteByte($modeByte)
    Add-Int32 $ms $script:Iterations
    Add-Int32 $ms $salt.Length;     $ms.Write($salt, 0, $salt.Length)
    $ms.Write($iv, 0, 16)
    Add-Int32 $ms $wrapped.Length;  if ($wrapped.Length) { $ms.Write($wrapped, 0, $wrapped.Length) }
    $ms.Write($mac, 0, 32)
    Add-Int32 $ms $ct.Length;       $ms.Write($ct, 0, $ct.Length)
    $out = $ms.ToArray()
    $ms.Dispose()
    return ,$out
}

function Unprotect-Data {
    param([byte[]]$Blob, [string]$Pass)

    if ($Blob.Length -lt 100) { throw '文件太小，不是合法的 .vault' }

    $magic = [byte[]]::new(8)
    [Array]::Copy($Blob, 0, $magic, 0, 8)
    if (-not (Test-BytesEqual $magic $script:Magic)) { throw '不是合法的 .vault 文件（magic 不匹配）' }

    $o        = 8
    $modeByte = $Blob[$o]; $o += 1
    $iter     = [BitConverter]::ToInt32($Blob, $o); $o += 4
    $saltLen  = [BitConverter]::ToInt32($Blob, $o); $o += 4
    if ($saltLen -lt 0 -or $saltLen -gt 64) { throw '文件头损坏（salt 长度异常）' }
    $salt = [byte[]]::new($saltLen); [Array]::Copy($Blob, $o, $salt, 0, $saltLen); $o += $saltLen
    $iv = [byte[]]::new(16);         [Array]::Copy($Blob, $o, $iv, 0, 16);          $o += 16
    $wLen = [BitConverter]::ToInt32($Blob, $o); $o += 4
    if ($wLen -lt 0 -or $wLen -gt 4096) { throw '文件头损坏（密钥长度异常）' }
    $wrapped = [byte[]]::new($wLen); [Array]::Copy($Blob, $o, $wrapped, 0, $wLen); $o += $wLen
    $mac = [byte[]]::new(32);        [Array]::Copy($Blob, $o, $mac, 0, 32);         $o += 32
    $ctLen = [BitConverter]::ToInt32($Blob, $o); $o += 4
    if ($ctLen -lt 16 -or ($o + $ctLen) -gt $Blob.Length) { throw '文件已截断或损坏' }
    $ct = [byte[]]::new($ctLen);     [Array]::Copy($Blob, $o, $ct, 0, $ctLen)

    if ($modeByte -eq 1) {
        Add-Type -AssemblyName System.Security
        $key = [System.Security.Cryptography.ProtectedData]::Unprotect(
                   $wrapped, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        $hk = [System.Security.Cryptography.SHA256]::Create()
        try { $macKey = $hk.ComputeHash($key) } finally { $hk.Dispose() }
    }
    else {
        if ([string]::IsNullOrEmpty($Pass)) { $Pass = Read-PromptPassphrase '请输入 vault 口令' }
        $kdf = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
                   $Pass, $salt, $iter, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
        try { $km = $kdf.GetBytes(64) } finally { $kdf.Dispose() }
        $key    = [byte[]]($km[0..31])
        $macKey = [byte[]]($km[32..63])
    }

    $toMac = [byte[]]::new($iv.Length + $salt.Length + $ct.Length)
    [Array]::Copy($iv,   0, $toMac, 0, $iv.Length)
    [Array]::Copy($salt, 0, $toMac, $iv.Length, $salt.Length)
    [Array]::Copy($ct,   0, $toMac, $iv.Length + $salt.Length, $ct.Length)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($macKey)
    try { $calc = $hmac.ComputeHash($toMac) } finally { $hmac.Dispose() }
    if (-not (Test-BytesEqual $calc $mac)) { throw '口令错误，或文件已被篡改（MAC 校验失败）' }

    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Mode    = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $aes.KeySize = 256
    $aes.Key = $key
    $aes.IV  = $iv
    $dec = $aes.CreateDecryptor()
    try { return ,($dec.TransformFinalBlock($ct, 0, $ct.Length)) } finally { $dec.Dispose(); $aes.Dispose() }
}

# ---------------- 业务 ----------------
switch ($Mode) {

    'lock' {
        if (-not $Dpapi -and [string]::IsNullOrEmpty($Passphrase)) {
            $Passphrase = Read-PromptPassphrase '设置 vault 口令（请至少 12 位）'
            if ($Passphrase.Length -lt 8) { throw '口令太短，至少 8 位' }
            $again = Read-PromptPassphrase '再输入一次确认'
            if ($again -ne $Passphrase) { throw '两次口令不一致' }
        }

        $srcItems = @(Get-ChildItem -Path $SourceDir -Recurse -File -Force -ErrorAction SilentlyContinue)
        if ($srcItems.Count -eq 0) { throw "源目录为空或不存在：$SourceDir" }

        $tmpZip = [System.IO.Path]::GetTempFileName() + '.zip'
        try {
            Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $tmpZip -Force
            $plain = [System.IO.File]::ReadAllBytes($tmpZip)
        }
        finally { Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue }

        $blob = Protect-Data -Plain $plain -Pass $Passphrase -UseDpapi:$Dpapi

        if (-not $VaultFile) {
            if (-not (Test-Path $VaultDir)) { New-Item -ItemType Directory -Path $VaultDir | Out-Null }
            $VaultFile = Join-Path $VaultDir ('desktop_memo_data_{0}.vault' -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
        }
        elseif ((Test-Path $VaultFile) -and -not $Force) {
            throw "目标已存在，加 -Force 覆盖：$VaultFile"
        }

        $parent = Split-Path -Parent $VaultFile
        if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        [System.IO.File]::WriteAllBytes($VaultFile, $blob)

        # 回读校验
        $back = Unprotect-Data -Blob ([System.IO.File]::ReadAllBytes($VaultFile)) -Pass $Passphrase
        $ok   = (Get-Sha256Hex $plain) -eq (Get-Sha256Hex $back)

        [pscustomobject]@{
            Mode       = 'lock'
            Vault      = (Resolve-Path $VaultFile).Path
            Cipher     = if ($Dpapi) { 'AES-256-CBC + DPAPI(CurrentUser) + HMAC-SHA256' }
                         else        { 'AES-256-CBC + PBKDF2-SHA256/200k + HMAC-SHA256' }
            PlainBytes = $plain.Length
            VaultBytes = $blob.Length
            Files      = $srcItems.Count
            Verify     = if ($ok) { 'OK 解密回读哈希一致' } else { 'FAILED' }
            Sha256     = Get-Sha256Hex $blob
        }
    }

    'unlock' {
        if (-not $VaultFile) {
            $latest = Get-ChildItem -Path $VaultDir -Filter '*.vault' -ErrorAction SilentlyContinue |
                      Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if (-not $latest) { throw 'vault 目录下没有 .vault 文件，请用 -VaultFile 指定' }
            $VaultFile = $latest.FullName
        }
        if (-not (Test-Path $VaultFile)) { throw "找不到文件：$VaultFile" }
        $vaultPath = (Resolve-Path $VaultFile).Path

        $plain = Unprotect-Data -Blob ([System.IO.File]::ReadAllBytes($vaultPath)) -Pass $Passphrase

        if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
        $tmpZip = [System.IO.Path]::GetTempFileName() + '.zip'
        [System.IO.File]::WriteAllBytes($tmpZip, $plain)
        try { Expand-Archive -Path $tmpZip -DestinationPath $OutDir -Force }
        finally { Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue }

        $outPath = (Resolve-Path $OutDir).Path
        $files   = @(Get-ChildItem -Path $outPath -Recurse -File -Force)
        [pscustomobject]@{
            Mode     = 'unlock'
            Vault    = $vaultPath
            OutDir   = $outPath
            Files    = $files.Count
            Bytes    = ($files | Measure-Object Length -Sum).Sum
            Restored = (($files | ForEach-Object { $_.FullName.Substring($outPath.Length + 1) }) -join ', ')
        }
    }

    'list' {
        if (-not $VaultFile) { $VaultFile = $VaultDir }
        $targets = if (Test-Path $VaultFile -PathType Container) {
            @(Get-ChildItem $VaultFile -Filter '*.vault' | Sort-Object LastWriteTime -Descending)
        } else { @(Get-Item $VaultFile) }
        if ($targets.Count -eq 0) { 'vault 目录为空'; break }

        foreach ($t in $targets) {
            $blob = [System.IO.File]::ReadAllBytes($t.FullName)
            $schemeFlag = $blob[8]
            $iter = [BitConverter]::ToInt32($blob, 9)
            $saltLen = [BitConverter]::ToInt32($blob, 13)
            $o = 13 + 4 + $saltLen + 16
            $wLen = [BitConverter]::ToInt32($blob, $o)
            $o = $o + 4 + $wLen + 32
            $ctLen = [BitConverter]::ToInt32($blob, $o)
            [pscustomobject]@{
                File       = $t.Name
                Modified   = $t.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
                Scheme     = if ($schemeFlag -eq 1) { 'DPAPI(本机用户)' } else { "PBKDF2-SHA256/${iter}次" }
                PlainBytes = $ctLen - 16
                VaultBytes = $blob.Length
                Sha256     = (Get-Sha256Hex $blob).Substring(0, 16) + '…'
            }
        }
    }
}

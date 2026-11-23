param(
    [int]$Port = 8765,
    [string]$Password = "",
    [switch]$InstallStartupTask = $true
)

$ErrorActionPreference = "Stop"
$BaseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServerExe = Join-Path $BaseDir "DesktopMemoServer.exe"
$DataDir = Join-Path $BaseDir "data"
$ConfigPath = Join-Path $DataDir "config.json"

if (-not (Test-Path $ServerExe)) {
    Write-Host "[错误] 未找到 DesktopMemoServer.exe，请先运行 build.bat 编译。" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir | Out-Null
}

# 读取或初始化 config.json 中的端口与口令
$cfg = @{}
if (Test-Path $ConfigPath) {
    try {
        $raw = Get-Content -Path $ConfigPath -Raw -Encoding UTF8
        $cfg = $raw | ConvertFrom-Json -AsHashtable -ErrorAction SilentlyContinue
        if (-not $cfg) {
            Add-Type -AssemblyName System.Web.Extensions
            $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
            $cfg = $ser.Deserialize($raw, [System.Collections.Generic.Dictionary[string,object]])
        }
    } catch {
        $cfg = @{}
    }
}

$cfg["ServerPort"] = $Port
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $cfg["ServerPassword"] = $Password
} elseif (-not $cfg.ContainsKey("ServerPassword") -or [string]::IsNullOrWhiteSpace([string]$cfg["ServerPassword"])) {
    $chars = "abcdefghjkmnpqrstuvwxyz23456789".ToCharArray()
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    $bytes = New-Object byte[] 8
    $rng.GetBytes($bytes)
    $gen = ""
    foreach ($b in $bytes) { $gen += $chars[$b % $chars.Length] }
    $cfg["ServerPassword"] = $gen
}

$finalPassword = [string]$cfg["ServerPassword"]
$jsonOut = $cfg | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($ConfigPath, $jsonOut, (New-Object System.Text.UTF8Encoding($false)))

# 放行 Windows 防火墙端口
$ruleName = "DesktopMemo Web Server (Port $Port)"
Write-Host "[1/3] 正在配置 Windows 防火墙入站规则 ($Port)..."
try {
    $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($existing) {
        Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    }
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    Write-Host "      已放行 TCP 端口 $Port" -ForegroundColor Green
} catch {
    Write-Host "      [提示] 防火墙配置需要管理员权限：$($_.Exception.Message)" -ForegroundColor Yellow
}

# 注册 Windows Server 开机自启计划任务并立即启动
if ($InstallStartupTask) {
    $taskName = "DesktopMemoServer"
    Write-Host "[2/3] 正在注册开机自启后台服务任务 ($taskName)..."
    try {
        $action = New-ScheduledTaskAction -Execute $ServerExe -WorkingDirectory $BaseDir
        $trigger = New-ScheduledTaskTrigger -AtStartup
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 0)
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
        Start-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Write-Host "      已注册开机自动后台运行并尝试启动。" -ForegroundColor Green
    } catch {
        Write-Host "      [提示] 计划任务需要管理员权限，改为直接前台/后台启动：$($_.Exception.Message)" -ForegroundColor Yellow
        Start-Process -FilePath $ServerExe -WorkingDirectory $BaseDir -WindowStyle Minimized
    }
}

Write-Host "[3/3] 部署完成！" -ForegroundColor Cyan
Write-Host "      本机访问地址 : http://127.0.0.1:$Port/"
try {
    $ips = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -ne "127.0.0.1" -and $_.PrefixOrigin -ne "WellKnown" }
    foreach ($ip in $ips) {
        Write-Host "      局域/公网地址: http://$($ip.IPAddress):$Port/"
    }
} catch {}
Write-Host "      网页访问口令 : $finalPassword" -ForegroundColor Green

#requires -Version 5.1
<#
.SYNOPSIS
    给 DesktopMemo 网页端配 Caddy 反代 + 自动 HTTPS（ICP 备案通过后使用）。

.DESCRIPTION
    做四件事：
      1) 准备 caddy.exe（缺失时自动下载）
      2) 生成 Caddyfile（DOMAIN -> 127.0.0.1:UPSTREAM，自动申请/续期 Let's Encrypt 证书）
      3) 监听端口收敛：放行 80/443，封禁 8765 的公网入站（只留本机回环给 Caddy 用）
      4) 注册开机自启计划任务并启动

    前置条件（缺一不可）：
      · 域名已完成 ICP 备案，且 A 记录指向本机公网 IP
      · 80 与 443 能从公网访问（Let's Encrypt 需要验证）
      · 以管理员身份运行

.PARAMETER Domain
    已备案的域名，例如 memo.example.com

.PARAMETER Email
    Let's Encrypt 到期通知邮箱，随便一个能收信的即可。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-Caddy.ps1 -Domain memo.gyer.top -Email me@example.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Domain,
    [Parameter(Mandatory = $true)][string]$Email,
    [int]$UpstreamPort = 8765,
    [string]$InstallDir = 'C:\DesktopMemo\caddy',
    [string]$CaddyExe
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host '[错误] 需要以管理员身份运行（绑定 80/443 与配置防火墙都需要提权）。' -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }

# ---------- 1. 准备 caddy.exe ----------
$caddyPath = Join-Path $InstallDir 'caddy.exe'
if ($CaddyExe) {
    if (-not (Test-Path $CaddyExe)) { throw "指定的 Caddy 不存在：$CaddyExe" }
    Copy-Item $CaddyExe $caddyPath -Force
}
elseif (-not (Test-Path $caddyPath)) {
    Write-Host '[1/4] 正在下载 Caddy ...'
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri 'https://caddyserver.com/api/download?os=windows&arch=amd64' `
                      -OutFile $caddyPath -TimeoutSec 600
}
Write-Host ("[1/4] Caddy: " + (& $caddyPath version)) -ForegroundColor Green

# ---------- 2. 生成 Caddyfile ----------
$logDir = Join-Path $InstallDir 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$caddyfile = Join-Path $InstallDir 'Caddyfile'

$content = @"
{
	email $Email
}

$Domain {
	encode zstd gzip
	reverse_proxy 127.0.0.1:$UpstreamPort {
		header_up X-Real-IP {remote_host}
	}
	log {
		output file `"$(Join-Path $logDir 'access.log')`" {
			roll_size 10mb
			roll_keep 5
		}
		format console
	}
	header {
		Strict-Transport-Security "max-age=31536000"
		X-Content-Type-Options "nosniff"
		Referrer-Policy "no-referrer"
		-Server
	}
}
"@
[System.IO.File]::WriteAllText($caddyfile, $content, (New-Object System.Text.UTF8Encoding($false)))

Write-Host '[2/4] 校验 Caddyfile ...'
& $caddyPath fmt --overwrite $caddyfile | Out-Null
& $caddyPath validate --config $caddyfile --adapter caddyfile
if ($LASTEXITCODE -ne 0) { throw 'Caddyfile 校验失败，请检查上面的报错。' }
Write-Host '      Caddyfile 语法正确。' -ForegroundColor Green

# ---------- 3. 防火墙：放行 80/443，封禁 8765 公网入站 ----------
Write-Host '[3/4] 配置防火墙 ...'
foreach ($port in 80, 443) {
    $name = "DesktopMemo HTTPS ($port)"
    Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $port `
                        -Action Allow -Profile Any | Out-Null
    Write-Host "      放行 TCP $port"
}

# 8765 只留给本机 Caddy 反代，公网不再直连
$oldName = "DesktopMemo Web Server (Port $UpstreamPort)"
Get-NetFirewallRule -DisplayName $oldName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
$blockName = "DesktopMemo Block Direct $UpstreamPort"
Get-NetFirewallRule -DisplayName $blockName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName $blockName -Direction Inbound -Protocol TCP -LocalPort $UpstreamPort `
                    -Action Block -Profile Any | Out-Null
Write-Host "      已封禁 TCP $UpstreamPort 的公网入站（本机回环不受影响）"

# ---------- 4. 注册自启任务并启动 ----------
Write-Host '[4/4] 注册开机自启任务 ...'
$taskName = 'DesktopMemoCaddy'
Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
Get-Process caddy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$action   = New-ScheduledTaskAction -Execute $caddyPath `
               -Argument "run --config `"$caddyfile`" --adapter caddyfile" -WorkingDirectory $InstallDir
$trigger  = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
               -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) `
               -ExecutionTimeLimit (New-TimeSpan -Days 0) -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
                       -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 6

$svc = Get-Process caddy -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "      Caddy 已启动 (PID $($svc[0].Id))" -ForegroundColor Green
} else {
    Write-Host '      [警告] Caddy 未在运行，可查看任务计划程序的历史记录排查。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '==================================================' -ForegroundColor Cyan
Write-Host '  部署完成' -ForegroundColor Cyan
Write-Host "  访问地址 : https://$Domain" -ForegroundColor Green
Write-Host "  反代目标 : http://127.0.0.1:$UpstreamPort" -ForegroundColor Gray
Write-Host "  Caddyfile: $caddyfile" -ForegroundColor Gray
Write-Host "  访问日志 : $(Join-Path $logDir 'access.log')" -ForegroundColor Gray
Write-Host '  首次访问时 Caddy 会自动申请证书，可能等待 5-30 秒。' -ForegroundColor Gray
Write-Host '==================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '注意：DesktopMemoServer 仍在监听 0.0.0.0:8765，靠防火墙拦截公网访问。' -ForegroundColor Yellow
Write-Host '      如已备案，请确认域名 A 记录已指向本机公网 IP。' -ForegroundColor Yellow

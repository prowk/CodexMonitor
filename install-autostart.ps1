param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = (Get-Location).Path }
$watcher = Join-Path $root 'CodexMonitor.Watcher.exe'
$monitor = Join-Path $root 'CodexMonitor.exe'
if (!(Test-Path -LiteralPath $watcher) -or !(Test-Path -LiteralPath $monitor)) { throw '请先运行 build.ps1。' }
# 与界面的开关共用同一份实现，避免脚本和菜单各自维护不同的启动项。
$mode = if ($Uninstall) { 'off' } else { 'on' }
$change = Start-Process -FilePath $monitor -ArgumentList @('--autostart', $mode) -WorkingDirectory $root -WindowStyle Hidden -PassThru
# 只等待设置命令本身；Start-Process -Wait 会连同常驻的守护子进程一起等待。
$change.WaitForExit()
$result = Join-Path $root 'autostart-results.txt'
if ($change.ExitCode -ne 0) { throw (Get-Content -LiteralPath $result -Encoding UTF8 -Raw) }
if ($Uninstall) {
    Start-Process -FilePath $watcher -ArgumentList '--stop' -WindowStyle Hidden -Wait
    Write-Output '已取消自动启停。'
} else {
    Write-Output '已启用 Codex 自动启停，登录计划任务已保存。'
}

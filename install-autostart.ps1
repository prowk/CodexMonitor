param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = (Get-Location).Path }
$watcher = Join-Path $root 'CodexMonitor.Watcher.exe'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name = 'CodexMonitorWatcher'
if ($Uninstall) {
    # 仅删除本工具创建的当前用户登录项。
    Remove-ItemProperty -LiteralPath $key -Name $name -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $watcher) { Start-Process -FilePath $watcher -ArgumentList '--stop' -WindowStyle Hidden -Wait }
    Write-Output '已取消自动启停。'
} else {
    if (!(Test-Path -LiteralPath $watcher) -or !(Test-Path -LiteralPath (Join-Path $root 'CodexMonitor.exe'))) { throw '请先运行 build.ps1。' }
    if (!(Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
    New-ItemProperty -LiteralPath $key -Name $name -Value ('"' + $watcher + '"') -PropertyType String -Force | Out-Null
    Start-Process -FilePath $watcher -WorkingDirectory $root -WindowStyle Hidden
    Write-Output '已启用 Codex 自动启停，并设置当前用户登录时启动守护程序。'
}

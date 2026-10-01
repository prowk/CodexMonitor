$ErrorActionPreference = 'Stop'
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = (Get-Location).Path }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll')
$argsList = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + (Join-Path $root 'CodexMonitor.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')))
foreach ($ref in $refs) { $argsList += '/reference:' + (Join-Path $framework $ref) }
$argsList += '/reference:' + (Join-Path $framework 'System.Xaml.dll')
$argsList += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName
& (Join-Path $framework 'csc.exe') @argsList
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
& gcc.exe '-Os' '-s' '-municode' '-mwindows' '-static-libgcc' '-finput-charset=UTF-8' (Join-Path $root 'launcher\Watcher.c') '-o' (Join-Path $root 'CodexMonitor.Watcher.exe') '-luser32' '-lshell32'
if ($LASTEXITCODE -ne 0) { throw '守护程序编译失败（需要 MinGW gcc）' }
Write-Output '已生成 CodexMonitor.exe 和 CodexMonitor.Watcher.exe'

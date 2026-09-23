$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.Windows.Forms.dll','System.Drawing.dll','System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll',"$framework\WPF\WindowsBase.dll","$framework\WPF\PresentationCore.dll","$framework\WPF\PresentationFramework.dll")
$buildArgs = @('/nologo','/target:winexe','/codepage:65001',"/out:$root\CodexStrip.exe","/win32manifest:$PSScriptRoot\app.manifest")
$buildArgs += @("/win32icon:$root\assets\app.ico","/resource:$root\assets\icon.png,StripIcon.png")
$buildArgs += $refs | ForEach-Object { '/r:' + $_ }
$buildArgs += "$PSScriptRoot\InsetPanels.cs"
$buildArgs += "$PSScriptRoot\WindowBehavior.cs"
$buildArgs += @("$PSScriptRoot\Bridge.cs","$PSScriptRoot\Model.cs","$PSScriptRoot\App.cs","$PSScriptRoot\Ui.cs","$PSScriptRoot\LocalMessages.cs","$PSScriptRoot\TrayHost.cs","$PSScriptRoot\DesktopStream.cs","$PSScriptRoot\WorkAreaReservation.cs")
& "$framework\csc.exe" @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $root\CodexStrip.exe"






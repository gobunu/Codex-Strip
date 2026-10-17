param(
    [ValidateSet('zh-CN','en')][string]$Language='zh-CN',
    [string]$OutputDirectory=''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory=$root }
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.Windows.Forms.dll','System.Drawing.dll','System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll',"$framework\WPF\WindowsBase.dll","$framework\WPF\PresentationCore.dll","$framework\WPF\PresentationFramework.dll")
$buildArgs = @('/nologo','/target:winexe','/codepage:65001',"/out:$OutputDirectory\CodexStrip.exe","/win32manifest:$PSScriptRoot\app.manifest")
if ($Language -eq 'en') { $buildArgs += '/define:ENGLISH' }
$buildArgs += @("/win32icon:$root\assets\app.ico","/resource:$root\assets\icon.png,StripIcon.png")
$buildArgs += $refs | ForEach-Object { '/r:' + $_ }
$buildArgs += "$PSScriptRoot\InsetPanels.cs"
$buildArgs += "$PSScriptRoot\SleepMode.cs"
$buildArgs += "$PSScriptRoot\SleepDiagnostics.cs"
$buildArgs += "$PSScriptRoot\ThreadRefresh.cs"
$buildArgs += "$PSScriptRoot\ThreadDiscovery.cs"
$buildArgs += "$PSScriptRoot\GlassBackdrop.cs"
$buildArgs += "$PSScriptRoot\SystemMetrics.cs"
$buildArgs += "$PSScriptRoot\WindowBehavior.cs"
$buildArgs += "$PSScriptRoot\UiText.cs"
$buildArgs += @("$PSScriptRoot\Bridge.cs","$PSScriptRoot\Model.cs","$PSScriptRoot\App.cs","$PSScriptRoot\Ui.cs","$PSScriptRoot\LocalMessages.cs","$PSScriptRoot\TrayHost.cs","$PSScriptRoot\DesktopStream.cs","$PSScriptRoot\WorkAreaReservation.cs")
& "$framework\csc.exe" @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$configSource=Join-Path $root 'CodexStrip.exe.config'
$configTarget=Join-Path $OutputDirectory 'CodexStrip.exe.config'
if ([IO.Path]::GetFullPath($configSource) -ne [IO.Path]::GetFullPath($configTarget)) { Copy-Item -LiteralPath $configSource -Destination $configTarget -Force }
Write-Output "Built $Language executable: $OutputDirectory\CodexStrip.exe"





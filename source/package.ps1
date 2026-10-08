$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$files = @('CodexStrip.exe','CodexStrip.exe.config','README.md','使用说明.md','RELEASE_NOTES.md','THIRD_PARTY_NOTICES.txt')
$paths = $files | ForEach-Object { Join-Path $root $_ }
foreach ($path in $paths) {
  if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $path" }
}
$zip = Join-Path $outputDir 'CodexStrip-portable.zip'
Compress-Archive -LiteralPath $paths -DestinationPath $zip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
  if ($archive.Entries.Count -ne $files.Count) { throw 'Unexpected package entries' }
  foreach ($entry in $archive.Entries) {
    if ($entry.FullName -notin $files) { throw "Unexpected package entry: $($entry.FullName)" }
    $stream = $entry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose(); $sha.Dispose() }
    if ($hash -cne (Get-FileHash -LiteralPath (Join-Path $root $entry.FullName) -Algorithm SHA256).Hash) { throw "Package content mismatch: $($entry.FullName)" }
  }
} finally { $archive.Dispose() }
$exeHash = (Get-FileHash -LiteralPath (Join-Path $root 'CodexStrip.exe') -Algorithm SHA256).Hash
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $outputDir 'SHA256SUMS.txt'),"$exeHash  CodexStrip.exe`n$zipHash  CodexStrip-portable.zip`n",[Text.UTF8Encoding]::new($false))
$notes = @()
$inSection = $false
foreach ($line in (Get-Content -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Encoding UTF8)) {
  if ($line -match '^## ') {
    if ($inSection) { break }
    $inSection = $true
    continue
  }
  if ($inSection) { $notes += $line }
}
$notesText = ($notes -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($notesText)) { throw 'Missing current release notes' }
[IO.File]::WriteAllText((Join-Path $outputDir 'RELEASE_NOTES.md'),$notesText + "`n",[Text.UTF8Encoding]::new($false))
Write-Output "Verified portable package, checksums and current release notes in $outputDir"

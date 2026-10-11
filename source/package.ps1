param([ValidateSet('zh-CN','en','all')][string]$Language='all')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$outputDir=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$languages=if($Language -eq 'all'){@('zh-CN','en')}else{@($Language)}
$checksums=@()
$files=@('CodexStrip.exe','CodexStrip.exe.config','README.md','README.en.md','使用说明.md','USER_GUIDE.en.md','RELEASE_NOTES.md','THIRD_PARTY_NOTICES.txt','assets/icon.png','screenshots/tasks-light.png','screenshots/tasks-dark.png','screenshots/resources.png','screenshots/resources-dark.png','screenshots/tasks-light-en.png','screenshots/tasks-dark-en.png','screenshots/resources-en.png','screenshots/resources-dark-en.png')
foreach($locale in $languages){
 $buildDir=Join-Path $outputDir ("build\"+$locale)
 & (Join-Path $PSScriptRoot 'build.ps1') -Language $locale -OutputDirectory $buildDir
 $exe=Join-Path $buildDir 'CodexStrip.exe'
 foreach($test in @('--self-test','--settings-test')){
  $process=Start-Process -FilePath $exe -ArgumentList $test -WorkingDirectory $buildDir -WindowStyle Hidden -PassThru -Wait
  if($process.ExitCode -ne 0){throw "$locale $test failed"}
 }
 $stdout=Join-Path $buildDir 'language.stdout.txt'
 $stderr=Join-Path $buildDir 'language.stderr.txt'
 $probe=Start-Process -FilePath $exe -ArgumentList '--build-language' -WorkingDirectory $buildDir -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -Wait
 if($probe.ExitCode -ne 0 -or (Get-Content -LiteralPath $stdout -Raw).Trim() -ne $locale){throw "Build language mismatch: $locale"}
 $sources=@{}
 foreach($name in $files){
  $sources[$name]=Join-Path $(if($name -in @('CodexStrip.exe','CodexStrip.exe.config')){$buildDir}else{$root}) $name
  if(!(Test-Path -LiteralPath $sources[$name] -PathType Leaf)){throw "Missing package file: $name"}
 }
 $zipName="CodexStrip-portable-$locale.zip"
 $zip=Join-Path $outputDir $zipName
 $stream=[IO.File]::Open($zip,[IO.FileMode]::Create)
 $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
 try{foreach($name in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$sources[$name],$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}
 finally{$archive.Dispose();$stream.Dispose()}
 $archive=[IO.Compression.ZipFile]::OpenRead($zip)
 try{
  if($archive.Entries.Count -ne $files.Count){throw 'Unexpected package entries'}
  foreach($entry in $archive.Entries){
   if($entry.FullName -notin $files){throw "Unexpected package entry: $($entry.FullName)"}
   $entryStream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{$hash=[BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-','')}
   finally{$entryStream.Dispose();$sha.Dispose()}
   if($hash -cne (Get-FileHash -LiteralPath $sources[$entry.FullName] -Algorithm SHA256).Hash){throw "Package content mismatch: $($entry.FullName)"}
  }
 }finally{$archive.Dispose()}
 $hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
 $line="$hash  $zipName"
 $checksums+=$line
 [IO.File]::WriteAllText((Join-Path $outputDir "SHA256SUMS-$locale.txt"),$line+"`n",[Text.UTF8Encoding]::new($false))
 Write-Output "Verified $locale portable package: $zip"
}
[IO.File]::WriteAllText((Join-Path $outputDir 'SHA256SUMS.txt'),($checksums -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
$notes=@();$inSection=$false
foreach($line in (Get-Content -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Encoding UTF8)){
 if($line -match '^## '){if($inSection){break};$inSection=$true;continue}
 if($inSection){$notes+=$line}
}
$notesText=($notes -join "`n").Trim()
if([string]::IsNullOrWhiteSpace($notesText)){throw 'Missing current release notes'}
[IO.File]::WriteAllText((Join-Path $outputDir 'RELEASE_NOTES.md'),$notesText+"`n",[Text.UTF8Encoding]::new($false))

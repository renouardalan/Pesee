# Prépare les fichiers d'une release GitHub dans release\ :
#   Pesee-<version>.zip  version PowerShell (install.ps1), fonctionne avec Smart App Control
#   Pesee.exe            version exécutable (non signée)
# La version est lue dans src\Program.cs (Program.Version).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$programCs = Get-Content (Join-Path $root 'src\Program.cs') -Raw -Encoding UTF8
if ($programCs -notmatch 'Version\s*=\s*"([^"]+)"') { throw 'Version introuvable dans src\Program.cs' }
$version = $Matches[1]

& (Join-Path $root 'build.ps1')

$release = Join-Path $root 'release'
Remove-Item $release -Recurse -Force -ErrorAction SilentlyContinue
$stage = Join-Path $release "Pesee-$version"
New-Item -ItemType Directory -Force (Join-Path $stage 'src'), (Join-Path $stage 'res') | Out-Null

Copy-Item (Join-Path $root 'Pesee.ps1'), (Join-Path $root 'install.ps1'), (Join-Path $root 'uninstall.ps1'),
    (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'src\*.cs') (Join-Path $stage 'src')
Copy-Item (Join-Path $root 'res\app.ico') (Join-Path $stage 'res')

$zip = Join-Path $release "Pesee-$version.zip"
Compress-Archive -Path $stage -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force
Copy-Item (Join-Path $root 'bin\Pesee.exe') $release

Write-Host ''
Write-Host "OK -> $release (version $version)" -ForegroundColor Green
Get-ChildItem $release | ForEach-Object { Write-Host ("   {0}  ({1:N0} Ko)" -f $_.Name, ($_.Length / 1KB)) }

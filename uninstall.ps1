# Retire "Analyser la taille" du clic droit et supprime les fichiers installes.
$ErrorActionPreference = 'Stop'
foreach ($type in 'Directory', 'Directory\Background', 'Drive') {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("Software\Classes\$type\shell\Pesee", $false)
}
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Pesee'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Write-Host ''
Write-Host "  OK : Pesee est desinstalle, 'Analyser la taille' a disparu du clic droit." -ForegroundColor Green
Write-Host ''
Start-Sleep -Seconds 4

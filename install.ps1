# Installe Pesee pour l'utilisateur courant (aucun droit administrateur requis)
# et ajoute "Analyser la taille" au clic droit des dossiers, du fond des dossiers et des disques.
# Desinstallation : uninstall.ps1
$ErrorActionPreference = 'Stop'
$from = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Pesee'

if ($from.TrimEnd('\') -ne $dest.TrimEnd('\')) {
    New-Item -ItemType Directory -Force (Join-Path $dest 'src') | Out-Null
    Remove-Item (Join-Path $dest 'src\*.cs') -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $from 'Pesee.ps1'), (Join-Path $from 'install.ps1'), (Join-Path $from 'uninstall.ps1') $dest -Force
    Copy-Item (Join-Path $from 'src\*.cs') (Join-Path $dest 'src') -Force
    Copy-Item (Join-Path $from 'res\app.ico') (Join-Path $dest 'app.ico') -Force
}

# conhost --headless : lance PowerShell sans aucune fenetre de console visible.
$conhost = Join-Path $env:WINDIR 'System32\conhost.exe'
$ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$script = Join-Path $dest 'Pesee.ps1'
$command = "`"$conhost`" --headless `"$ps`" -NoProfile -ExecutionPolicy Bypass -File `"$script`" `"%V`""
$icon = Join-Path $dest 'app.ico'

foreach ($type in 'Directory', 'Directory\Background', 'Drive') {
    $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Classes\$type\shell\Pesee")
    $k.SetValue('', 'Analyser la taille')
    $k.SetValue('Icon', $icon)
    if ($type -eq 'Directory') { $k.SetValue('MultiSelectModel', 'Single') }
    $c = $k.CreateSubKey('command')
    $c.SetValue('', $command)
    $c.Close()
    $k.Close()
}
Write-Host ''
Write-Host "  OK : 'Analyser la taille' est ajoute au clic droit." -ForegroundColor Green
Write-Host "  (Windows 11 : clic droit > Afficher plus d'options, ou Maj + clic droit)"
Write-Host ''
Start-Sleep -Seconds 4

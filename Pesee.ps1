# Pesee - affiche la taille de chaque sous-dossier d'un dossier.
# Le code C# du dossier src\ est compile en memoire puis execute dans PowerShell.
# Usage : powershell -NoProfile -ExecutionPolicy Bypass -File Pesee.ps1 "D:\dossier"
$ErrorActionPreference = 'Stop'
try {
    $files = [string[]](Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName })

    $cp = New-Object System.CodeDom.Compiler.CompilerParameters
    $cp.GenerateInMemory = $true
    $cp.CompilerOptions = '/unsafe /optimize+ /codepage:65001'
    [void]$cp.ReferencedAssemblies.AddRange([string[]]@('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll'))
    Add-Type -Path $files -CompilerParameters $cp

    $icon = Join-Path $PSScriptRoot 'app.ico'
    if (-not (Test-Path $icon)) { $icon = Join-Path $PSScriptRoot 'res\app.ico' }
    [Pesee.Program]::Run([string[]]$args, $icon)
}
catch {
    $msg = $_ | Out-String
    Set-Content -Path (Join-Path $env:TEMP 'Pesee-erreur.txt') -Value $msg -Encoding UTF8
    if ($args -notcontains '--snapshot') {
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.MessageBox]::Show($msg, 'Pesee - erreur')
    }
    exit 1
}

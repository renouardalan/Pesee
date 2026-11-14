# Compile Pesee.exe avec le compilateur C# fourni avec Windows (.NET Framework 4.8).
# Aucun SDK a installer.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

$ico = Join-Path $root 'res\app.ico'
if (-not (Test-Path $ico)) {
    & $csc /nologo /codepage:65001 /target:exe /out:"$bin\MakeIcon.exe" /r:System.Drawing.dll "$root\tools\MakeIcon.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Echec de compilation de MakeIcon' }
    & "$bin\MakeIcon.exe" $ico
}

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /unsafe /platform:anycpu `
    /out:"$bin\Pesee.exe" `
    /win32icon:"$ico" `
    /win32manifest:"$root\res\app.manifest" `
    /resource:"$ico,Pesee.app.ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw 'Echec de compilation' }
Write-Host "OK -> $bin\Pesee.exe"

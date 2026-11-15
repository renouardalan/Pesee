# Fabrique le paquet Microsoft Store de Pesée (store\out\Pesee_<version>.msixbundle).
#
# Pour le Store, utilisez les valeurs de Partner Center > votre app > Identité du produit :
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-store.ps1 `
#       -IdentityName "12345Editeur.Pesee" -Publisher "CN=XXXXXXXX-XXXX-..." -PublisherDisplayName "Votre nom"
# Sans paramètre : identité de test, pour essayer le paquet sur ce PC.
# -Sign : signe le paquet avec un certificat de test (inutile pour le Store, qui signe lui-même).
#
# Outils requis : Windows SDK 10 (makeappx, makepri, signtool) et Visual Studio Build Tools (C++).
param(
    [string]$IdentityName = 'Pesee.Test',
    [string]$Publisher = 'CN=Pesee Test',
    [string]$PublisherDisplayName = 'Pesée (test)',
    # Doit être identique au nom réservé dans Partner Center (sinon le paquet est refusé à l'envoi).
    [string]$DisplayName = 'Pesee',
    [string]$Version = '1.0.0.0',
    [string[]]$Archs = @('x64', 'arm64'),
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'store\out'
$obj = Join-Path $root 'store\obj'

if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version au format x.y.z.0 exigée par le Store (reçu : $Version)" }

# ---- Outils
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdkBin = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows SDK introuvable (makeappx.exe). Installez Microsoft.WindowsSDK.10.0.26100.' }
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$makepri = Join-Path $sdkBin.FullName 'x64\makepri.exe'
$signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
if (-not $vs) { throw 'Compilateur C++ introuvable. Installez Visual Studio Build Tools 2022 (charge de travail C++).' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

Remove-Item $out, $obj -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out, $obj | Out-Null

# ---- Pesee.exe (.NET Framework 4.8, identique pour toutes les architectures)
$ico = Join-Path $root 'res\app.ico'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /unsafe /platform:anycpu /define:STORE `
    /out:"$obj\Pesee.exe" /win32icon:"$ico" /win32manifest:"$root\res\app.manifest" `
    /resource:"$ico,Pesee.app.ico" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Echec de compilation de Pesee.exe' }

# ---- Logos du Store (compilés en mémoire : Smart App Control bloque les .exe non signés)
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/codepage:65001'
[void]$cp.ReferencedAssemblies.Add('System.Drawing.dll')
Add-Type -Path "$root\tools\MakeStoreAssets.cs", "$root\tools\MakeIcon.cs" -CompilerParameters $cp
[MakeStoreAssets]::Main([string[]]@("$obj\Assets"))

# ---- Un paquet .msix par architecture
$template = Get-Content (Join-Path $root 'store\AppxManifest.template.xml') -Raw -Encoding UTF8
$packages = Join-Path $obj 'packages'
New-Item -ItemType Directory -Force $packages | Out-Null

foreach ($arch in $Archs) {
    Write-Host "== $arch"
    $layout = Join-Path $obj "layout-$arch"
    New-Item -ItemType Directory -Force $layout | Out-Null

    # PeseeMenu.dll : compilée pour l'architecture de l'Explorateur qui la charge.
    $vcArch = @{ x64 = 'x64'; arm64 = 'x64_arm64' }[$arch]
    if (-not $vcArch) { throw "Architecture non gérée : $arch" }
    $cetFlag = if ($arch -eq 'x64') { '/CETCOMPAT' } else { '' }
    $bat = Join-Path $obj "menu-$arch.cmd"
    @"
@echo off
call "$vcvars" $vcArch >nul || exit /b 1
cl /nologo /O2 /MT /EHsc /std:c++17 /W4 /guard:cf /DUNICODE /D_UNICODE /LD "$root\menu\PeseeMenu.cpp" /Fo"$obj\PeseeMenu-$arch.obj" /Fe"$layout\PeseeMenu.dll" /link /DEF:"$root\menu\PeseeMenu.def" /DYNAMICBASE /NXCOMPAT /GUARD:CF $cetFlag shlwapi.lib ole32.lib user32.lib runtimeobject.lib || exit /b 1
"@ | Set-Content $bat -Encoding Default
& cmd.exe /c "`"$bat`""
if ($LASTEXITCODE -ne 0) { throw "Echec de compilation de PeseeMenu.dll ($arch)" }
    Remove-Item (Join-Path $layout 'PeseeMenu.lib'), (Join-Path $layout 'PeseeMenu.exp') -ErrorAction SilentlyContinue

    Copy-Item "$obj\Pesee.exe" $layout
    Copy-Item "$obj\Assets" (Join-Path $layout 'Assets') -Recurse

    $manifest = $template.Replace('{IdentityName}', $IdentityName).Replace('{Publisher}', [Security.SecurityElement]::Escape($Publisher)).
        Replace('{PublisherDisplayName}', [Security.SecurityElement]::Escape($PublisherDisplayName)).
        Replace('{DisplayName}', [Security.SecurityElement]::Escape($DisplayName)).
        Replace('{Version}', $Version).Replace('{Arch}', $arch)
    [IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

    # resources.pri : permet à Windows de choisir le logo à la bonne taille (zoom d'écran, barre des tâches...).
    $priConfig = Join-Path $obj "priconfig-$arch.xml"
    & $makepri createconfig /cf $priConfig /dq fr-FR /pv 10.0.0 /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Echec makepri createconfig' }
    [xml]$cfg = Get-Content $priConfig
    $pk = $cfg.SelectSingleNode('//packaging'); if ($pk) { [void]$pk.ParentNode.RemoveChild($pk) }
    $cfg.Save($priConfig)
    & $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Echec makepri new' }

    $msix = Join-Path $packages "Pesee_${Version}_$arch.msix"
    & $makeappx pack /o /d $layout /p $msix | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Echec makeappx pack ($arch)" }
}

# ---- Lot multi-architecture pour le Store
$bundle = Join-Path $out "Pesee_$Version.msixbundle"
& $makeappx bundle /o /bv $Version /d $packages /p $bundle | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Echec makeappx bundle' }

if ($Sign) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
            -FriendlyName 'Pesée - certificat de test' -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    & $signtool sign /q /fd SHA256 /sha1 $cert.Thumbprint $bundle
    if ($LASTEXITCODE -ne 0) { throw 'Echec de signature' }
    Export-Certificate -Cert $cert -FilePath (Join-Path $out 'Pesee-test.cer') | Out-Null
    Write-Host "Signé avec le certificat de test : $out\Pesee-test.cer"
}

Write-Host ''
Write-Host "OK -> $bundle" -ForegroundColor Green

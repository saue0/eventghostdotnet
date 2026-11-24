[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'all')]
    [string]$Runtime = 'all',
    [switch]$SelfContained,
    [switch]$Installer,
    [string]$OutputRoot = 'bin\publish',
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'EventGhostDotNet.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw "La propriété Version est absente de $project."
}

$runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
$outputDir = Join-Path $root $OutputRoot
$installerDir = Join-Path $root 'bin\installer'

$signtoolPath = $null
if ($CertificateThumbprint) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        throw "signtool.exe est introuvable. Installez le Windows SDK ou ajoutez signtool au PATH."
    }
    $signtoolPath = $signtool.Source
}

function Invoke-Signature([string]$path) {
    if (-not $CertificateThumbprint) {
        return
    }

    & $signtoolPath sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $path
    if ($LASTEXITCODE -ne 0) {
        throw "La signature de $path a échoué (code $LASTEXITCODE)."
    }
}

function Find-InnoCompiler {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    if ($candidates.Count -eq 0) {
        throw "ISCC.exe est introuvable. Installez Inno Setup 6.3 ou plus récent : https://jrsoftware.org/isdl.php"
    }
    return $candidates[0]
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$published = @()

foreach ($rid in $runtimes) {
    $target = Join-Path $outputDir "$rid-$flavor"
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    Write-Host "Publication de $rid ($flavor) dans $target"
    & dotnet publish $project -c Release -r $rid --self-contained $selfContainedValue -o $target `
        -p:EnableWindowsTargeting=true -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish a échoué pour $rid (code $LASTEXITCODE)."
    }

    $required = @(
        'EventGhostDotNet.exe',
        'EventGhostDotNet.runtimeconfig.json',
        'Plugins\plugins.manifest'
    )
    foreach ($name in @('Display', 'FileWatcher', 'Logitech', 'ProcessWatcher', 'Scheduler',
                        'SystemTrayMenu', 'Wallpaper')) {
        $required += "Plugins\$name\$name.dll"
        $required += "Plugins\$name\$name.deps.json"
    }
    foreach ($relative in $required) {
        $file = Join-Path $target $relative
        if (-not (Test-Path -LiteralPath $file)) {
            throw "Fichier publié introuvable : $file"
        }
    }

    # Les DLL des plugins sont couvertes par plugins.manifest : les signer après publication
    # invaliderait leur empreinte SHA-256 et le chargeur les refuserait.
    Invoke-Signature (Join-Path $target 'EventGhostDotNet.exe')

    $published += [pscustomobject]@{
        Runtime = $rid
        Flavor = $flavor
        Directory = $target
    }
}

if ($Installer) {
    $iscc = Find-InnoCompiler
    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

    foreach ($entry in $published) {
        $arch = if ($entry.Runtime -eq 'win-arm64') { 'arm64' } else { 'x64compatible' }
        $baseName = "EventGhostDotNet-$version-$($entry.Runtime)-$flavor"
        Write-Host "Compilation de l'installateur $baseName"
        & $iscc `
            "/DMyArch=$arch" `
            "/DSourceDir=$($entry.Directory)" `
            "/DOutputDir=$installerDir" `
            "/DAppVersion=$version" `
            "/DOutputBaseFilename=$baseName" `
            (Join-Path $root 'installer\EventGhostDotNet.iss')
        if ($LASTEXITCODE -ne 0) {
            throw "La compilation de l'installateur a échoué pour $($entry.Runtime) (code $LASTEXITCODE)."
        }

        Invoke-Signature (Join-Path $installerDir "$baseName.exe")
    }
}

Write-Host ""
Write-Host "Publications :"
foreach ($entry in $published) {
    Write-Host "  $($entry.Runtime) ($($entry.Flavor)) : $($entry.Directory)"
}
if ($Installer) {
    Write-Host "  Installateurs : $installerDir"
}

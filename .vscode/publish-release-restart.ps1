$ErrorActionPreference = 'Stop'

$workspace = (Get-Location).Path
$releaseRoots = @(
    'bin\live',
    'bin\Release\net9.0-windows',
    'bin\Release\net9.0-windows10.0.19041.0'
) | ForEach-Object {
    [System.IO.Path]::GetFullPath((Join-Path $workspace $_)).TrimEnd('\') + '\'
}
$publishDir = Join-Path $workspace 'bin\live'

$instances = @(
    Get-CimInstance Win32_Process -Filter "Name = 'EventGhostDotNet.exe'" |
        Where-Object {
            $path = $_.ExecutablePath
            $path -and @($releaseRoots | Where-Object {
                $path.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
        }
)

foreach ($instance in $instances) {
    $process = Get-Process -Id $instance.ProcessId -ErrorAction Stop
    try {
        $exitRequest = [System.Threading.EventWaitHandle]::OpenExisting(
            "Local\EventGhostDotNet.Exit.$($instance.ProcessId)"
        )
    }
    catch [System.Threading.WaitHandleCannotBeOpenedException] {
        throw "Le processus Release $($instance.ProcessId) ne prend pas en charge la sortie automatique. Quittez-le par le menu de la zone de notification, puis relancez la tâche."
    }
    try {
        if (-not $exitRequest.Set()) {
            throw "Impossible de demander la fermeture du processus Release $($instance.ProcessId)."
        }
    }
    finally {
        $exitRequest.Dispose()
    }

    if (-not $process.WaitForExit(30000)) {
        throw "Le processus Release $($instance.ProcessId) ne s'est pas fermé dans les 30 secondes. Vérifiez la confirmation de sauvegarde ; publication annulée."
    }
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

& dotnet publish -c Release -r win-x64 --self-contained false -o $publishDir `
    -p:PublishSingleFile=false `
    -p:EnableMsixTooling=true `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish a échoué (code $LASTEXITCODE)."
}

$exe = Join-Path $publishDir 'EventGhostDotNet.exe'
if (-not (Test-Path $exe)) {
    throw "Exécutable publié introuvable : $exe"
}

foreach ($plugin in @('Display', 'FileWatcher', 'Logitech', 'ProcessWatcher', 'Scheduler', 'SystemTrayMenu', 'Wallpaper')) {
    foreach ($extension in @('.dll', '.deps.json')) {
        $pluginFile = Join-Path $publishDir "Plugins\$plugin\$plugin$extension"
        if (-not (Test-Path -LiteralPath $pluginFile)) {
            throw "Fichier de plugin publié introuvable : $pluginFile"
        }
    }
}

$iconDir = Join-Path $publishDir 'Assets'
New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'Assets\EventGhostDotNet.ico') `
    -Destination (Join-Path $iconDir 'EventGhostDotNet.ico') -Force

$startedProcess = Start-Process -FilePath $exe -WorkingDirectory $publishDir -PassThru
if ($startedProcess.HasExited) {
    throw "L'application publiée s'est arrêtée immédiatement après son démarrage."
}

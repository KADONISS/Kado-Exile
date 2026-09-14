param(
    [string]$Version = "0.6.0",
    [string]$Configuration = "Release",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$releaseRoot = Join-Path $projectRoot "artifacts\Kado-Exile-$Version"
$zipPath = Join-Path $projectRoot "artifacts\Kado-Exile-$Version.zip"

dotnet test (Join-Path $projectRoot "Kado-Exile.slnx") -c $Configuration
if (Test-Path $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRoot | Out-Null

$buildRoot = Join-Path $projectRoot "src\KadoExile\bin\$Configuration\net10.0-windows"
Copy-Item -LiteralPath (Join-Path $buildRoot "Kado-Exile.dll") -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $releaseRoot
if (-not $SkipInstall) {
    $installedPluginDirectory = "C:\Users\Admin\Desktop\ExileApi-Compiled-master\Plugins\Compiled\Kado-Exile"
    $installedPlugin = Join-Path $installedPluginDirectory "Kado-Exile.dll"
    New-Item -ItemType Directory -Path $installedPluginDirectory -Force | Out-Null
    try {
        Copy-Item -LiteralPath (Join-Path $buildRoot "Kado-Exile.dll") -Destination $installedPlugin -Force
        Write-Output "Installed Kado-Exile.dll to $installedPlugin"
    }
    catch [System.IO.IOException] {
        # ExileAPI locks a loaded plugin; stage the exact replacement for the next restart.
        $pendingPlugin = "$installedPlugin.$Version.pending"
        Copy-Item -LiteralPath (Join-Path $buildRoot "Kado-Exile.dll") -Destination $pendingPlugin -Force
        Write-Warning "ExileAPI is using $installedPlugin; staged replacement at $pendingPlugin"
    }
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $releaseRoot "*") -DestinationPath $zipPath
Write-Output $zipPath

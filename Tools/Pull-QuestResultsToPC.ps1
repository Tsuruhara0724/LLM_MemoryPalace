param(
    [string]$PackageId = "",
    [string]$Destination = "",
    [string]$Device = ""
)

if ([string]::IsNullOrWhiteSpace($PackageId)) {
    $ProjectSettings = Join-Path $PSScriptRoot "..\ProjectSettings\ProjectSettings.asset"
    if (Test-Path -LiteralPath $ProjectSettings) {
        $AndroidIdLine = Get-Content -LiteralPath $ProjectSettings |
            Where-Object { $_ -match '^\s*Android:\s*(\S+)\s*$' } |
            Select-Object -First 1
        if ($AndroidIdLine -match '^\s*Android:\s*(\S+)\s*$') {
            $PackageId = $Matches[1]
        }
    }
}

if ([string]::IsNullOrWhiteSpace($PackageId)) {
    Write-Error "Could not determine the Android package id. Pass -PackageId com.your.app.id."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $Destination = Join-Path $PSScriptRoot "..\QuestResultImports\$Timestamp"
}

$Adb = Get-Command adb -ErrorAction SilentlyContinue
if ($null -eq $Adb) {
    Write-Error "adb was not found on PATH. Install Android Platform Tools or run this from a shell where adb is available."
    exit 1
}

$DeviceArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Device)) {
    $DeviceArgs = @("-s", $Device)
}

$ResolvedDestination = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $ResolvedDestination -Force | Out-Null
$QuestFilesRoot = "/sdcard/Android/data/$PackageId/files"

Write-Host "Pulling Quest experiment results:"
Write-Host "  Quest: $QuestFilesRoot"
Write-Host "  PC:    $ResolvedDestination"

& $Adb.Source @DeviceArgs pull "$QuestFilesRoot/ExperimentExports" $ResolvedDestination
$ExportExitCode = $LASTEXITCODE
& $Adb.Source @DeviceArgs pull "$QuestFilesRoot/MemoryPalaceSessionData" $ResolvedDestination
$SnapshotExitCode = $LASTEXITCODE

if ($ExportExitCode -ne 0) {
    Write-Error "Could not pull ExperimentExports. Confirm that the post-test finished and the Quest is connected with USB debugging enabled."
    exit $ExportExitCode
}

if ($SnapshotExitCode -ne 0) {
    Write-Warning "Result JSON/CSV was pulled, but the spatial snapshot folder was unavailable."
}

Write-Host "Done. Results are in: $ResolvedDestination"

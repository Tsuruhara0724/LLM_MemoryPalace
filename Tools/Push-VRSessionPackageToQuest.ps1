param(
    [string]$PackageId = "",
    [string]$Source = "",
    [string]$Device = ""
)

if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $PSScriptRoot "..\VRSessionPackages\session_package_latest.json"
}

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

$ResolvedSource = Resolve-Path -LiteralPath $Source -ErrorAction SilentlyContinue
if ($null -eq $ResolvedSource) {
    Write-Error "Session package not found: $Source. First click 'Write Quest Session Package' in the PC build/editor."
    exit 1
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

$TargetDir = "/sdcard/Android/data/$PackageId/files"
$TargetFile = "$TargetDir/session_package_latest.json"

Write-Host "Pushing Quest session package:"
Write-Host "  Source: $ResolvedSource"
Write-Host "  Target: $TargetFile"
Write-Host ""

& $Adb.Source @DeviceArgs shell mkdir -p $TargetDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Could not create target folder on Quest. Make sure the app has been launched once and adb can see the device."
    exit $LASTEXITCODE
}

& $Adb.Source @DeviceArgs push $ResolvedSource $TargetFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "adb push failed."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Done. Start or restart the Quest app; it will auto-load this local package on the Setup screen."

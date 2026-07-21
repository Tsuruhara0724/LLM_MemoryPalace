param(
    [int]$Port = 8765,
    [string]$Root = ""
)

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Join-Path $PSScriptRoot "..\VRSessionPackages"
}

$ResolvedRoot = Resolve-Path -LiteralPath $Root
Write-Host "Serving VR session packages from: $ResolvedRoot"
Write-Host ""

$Addresses = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object {
        $_.IPAddress -notlike "127.*" -and
        $_.IPAddress -notlike "169.254.*" -and
        $_.PrefixOrigin -ne "WellKnown"
    } |
    Select-Object -ExpandProperty IPAddress -Unique

Write-Host "Open one of these URLs from Quest:"
foreach ($Address in $Addresses) {
    Write-Host "  http://$Address`:$Port/session_package_latest.json"
}
Write-Host ""
Write-Host "Keep this window open while the HMD is loading data."
Write-Host "If Windows Firewall prompts, allow Python on private networks."
Write-Host ""

$Python = Get-Command python -ErrorAction SilentlyContinue
if ($Python -ne $null) {
    Push-Location $ResolvedRoot
    try {
        & $Python.Source -m http.server $Port --bind 0.0.0.0
    }
    finally {
        Pop-Location
    }
    exit $LASTEXITCODE
}

$PyLauncher = Get-Command py -ErrorAction SilentlyContinue
if ($PyLauncher -ne $null) {
    Push-Location $ResolvedRoot
    try {
        & $PyLauncher.Source -3 -m http.server $Port --bind 0.0.0.0
    }
    finally {
        Pop-Location
    }
    exit $LASTEXITCODE
}

Write-Error "Python was not found. Install Python or start another static file server for the VRSessionPackages folder."
exit 1

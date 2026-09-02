param(
    [string]$BindAddress = "127.0.0.1",
    [int]$Port = 8765,
    [string]$ExportsDir = "",
    [string]$ResultsDir = ""
)

$ErrorActionPreference = "Stop"
$serverPath = Join-Path $PSScriptRoot "server.py"
$pythonArgs = @($serverPath, "--host", $BindAddress, "--port", $Port)

if ($ExportsDir) {
    $pythonArgs += @("--exports-dir", $ExportsDir)
}
if ($ResultsDir) {
    $pythonArgs += @("--results-dir", $ResultsDir)
}

if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 @pythonArgs
    exit $LASTEXITCODE
}
if (Get-Command python -ErrorAction SilentlyContinue) {
    & python @pythonArgs
    exit $LASTEXITCODE
}

throw "Python 3 was not found. Install Python 3, then run this script again."

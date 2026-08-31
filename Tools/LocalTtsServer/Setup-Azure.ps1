$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$LocalRoot = Join-Path $ProjectRoot ".local-tts"
$Venv = Join-Path $LocalRoot "azure-venv"

New-Item -ItemType Directory -Force -Path $LocalRoot | Out-Null
if (-not (Test-Path (Join-Path $Venv "Scripts\python.exe"))) {
    py -3.12 -m venv $Venv
}

$Python = Join-Path $Venv "Scripts\python.exe"
& $Python -m pip install --upgrade pip
& $Python -m pip install fastapi uvicorn

Write-Host "Azure Speech proxy environment is ready at $Venv"
Write-Host "Next, run Start-Azure.cmd. It asks for the Azure Speech key securely and does not save it in this project."

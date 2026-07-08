$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$LocalRoot = Join-Path $ProjectRoot ".local-tts"
$Venv = Join-Path $LocalRoot "kokoro-venv"

New-Item -ItemType Directory -Force -Path $LocalRoot | Out-Null
if (-not (Test-Path (Join-Path $Venv "Scripts\python.exe"))) {
    py -3.12 -m venv $Venv
}

$Python = Join-Path $Venv "Scripts\python.exe"
& $Python -m pip install --upgrade pip
& $Python -m pip install kokoro soundfile fastapi uvicorn

Write-Host "Kokoro environment is ready at $Venv"

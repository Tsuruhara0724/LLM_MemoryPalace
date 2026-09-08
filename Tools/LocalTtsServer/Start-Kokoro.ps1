$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$env:HF_HOME = Join-Path $ProjectRoot ".local-tts\hf-cache"
$env:LOCAL_TTS_CACHE_DIR = Join-Path $ProjectRoot ".local-tts\audio-cache"
$env:LOCAL_TTS_BACKEND = "kokoro"
$env:LOCAL_TTS_DEVICE = "cpu"
$Python = Join-Path $ProjectRoot ".local-tts\kokoro-venv\Scripts\python.exe"

if (-not (Test-Path $Python)) {
    throw "Kokoro is not installed. Run Setup-Kokoro.ps1 first."
}

& $Python (Join-Path $PSScriptRoot "local_tts_server.py")

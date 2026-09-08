$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$env:HF_HOME = Join-Path $ProjectRoot ".local-tts\hf-cache"
$env:LOCAL_TTS_CACHE_DIR = Join-Path $ProjectRoot ".local-tts\audio-cache"
$env:LOCAL_TTS_MODEL_DIR = Join-Path $ProjectRoot ".local-tts\chatterbox-model"
$env:LOCAL_TTS_BACKEND = "chatterbox"
$env:LOCAL_TTS_DEVICE = "cuda"
$Python = Join-Path $ProjectRoot ".local-tts\chatterbox-venv\Scripts\python.exe"

if (-not (Test-Path $Python)) {
    throw "Chatterbox is not installed. Run Setup-Chatterbox.ps1 first."
}

& $Python (Join-Path $PSScriptRoot "local_tts_server.py")

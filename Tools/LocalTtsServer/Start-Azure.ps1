$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Python = Join-Path $ProjectRoot ".local-tts\azure-venv\Scripts\python.exe"

if (-not (Test-Path $Python)) {
    throw "Azure Speech proxy is not installed. Run Setup-Azure.cmd first."
}

$SpeechKey = $env:AZURE_SPEECH_KEY
if ([string]::IsNullOrWhiteSpace($SpeechKey)) {
    $SecureSpeechKey = Read-Host "Azure Speech key (input is hidden)" -AsSecureString
    $KeyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureSpeechKey)
    try {
        $SpeechKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($KeyPointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($KeyPointer)
    }
}

if ([string]::IsNullOrWhiteSpace($SpeechKey)) {
    throw "An Azure Speech key is required. No key was saved."
}

$SpeechRegion = $env:AZURE_SPEECH_REGION
if ([string]::IsNullOrWhiteSpace($SpeechRegion)) {
    $SpeechRegion = Read-Host "Azure Speech region [japaneast]"
}
if ([string]::IsNullOrWhiteSpace($SpeechRegion)) {
    $SpeechRegion = "japaneast"
}
if ($SpeechRegion -notmatch '^[A-Za-z0-9-]+$') {
    throw "Azure Speech region contains unsupported characters."
}

$env:AZURE_SPEECH_KEY = $SpeechKey
$env:AZURE_SPEECH_REGION = $SpeechRegion.Trim().ToLowerInvariant()
$env:AZURE_TTS_VOICE = if ([string]::IsNullOrWhiteSpace($env:AZURE_TTS_VOICE)) { "en-US-AvaMultilingualNeural" } else { $env:AZURE_TTS_VOICE }
$env:AZURE_TTS_SPANISH_VOICE = if ([string]::IsNullOrWhiteSpace($env:AZURE_TTS_SPANISH_VOICE)) { "es-ES-ElviraNeural" } else { $env:AZURE_TTS_SPANISH_VOICE }
$env:LOCAL_TTS_BACKEND = "azure"
$env:LOCAL_TTS_MODEL = "azure-speech"
$env:LOCAL_TTS_VOICE = $env:AZURE_TTS_VOICE
$env:LOCAL_TTS_CACHE_DIR = Join-Path $ProjectRoot ".local-tts\azure-audio-cache"

Write-Host "Starting Azure Speech proxy on http://127.0.0.1:8880/v1"
Write-Host "The Azure key is held only by this terminal process and is not written to Unity settings."
& $Python (Join-Path $PSScriptRoot "local_tts_server.py")

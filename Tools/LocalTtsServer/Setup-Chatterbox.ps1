$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$LocalRoot = Join-Path $ProjectRoot ".local-tts"
$Venv = Join-Path $LocalRoot "chatterbox-venv"

New-Item -ItemType Directory -Force -Path $LocalRoot | Out-Null
if (-not (Test-Path (Join-Path $Venv "Scripts\python.exe"))) {
    py -3.12 -m venv $Venv
}

$Python = Join-Path $Venv "Scripts\python.exe"
& $Python -m pip install --upgrade pip
& $Python -m pip install torch==2.6.0 torchaudio==2.6.0 --index-url https://download.pytorch.org/whl/cu124
& $Python -m pip install chatterbox-tts==0.1.7 fastapi uvicorn soundfile

$ModelDir = Join-Path $LocalRoot "chatterbox-model"
& $Python -c "from huggingface_hub import snapshot_download; snapshot_download(repo_id='ResembleAI/chatterbox', local_dir=r'$ModelDir', allow_patterns=['ve.pt','t3_mtl23ls_v2.safetensors','s3gen.pt','grapheme_mtl_merged_expanded_v1.json','conds.pt','Cangjie5_TC.json'])"

Write-Host "Chatterbox environment is ready at $Venv"

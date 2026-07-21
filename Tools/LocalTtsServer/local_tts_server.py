import hashlib
import io
import json
import os
import threading
from pathlib import Path
from typing import Optional

import numpy as np
import soundfile as sf
import torch
from fastapi import FastAPI, HTTPException
from fastapi.responses import Response
from pydantic import BaseModel


HOST = os.getenv("LOCAL_TTS_HOST", "127.0.0.1")
PORT = int(os.getenv("LOCAL_TTS_PORT", "8880"))
BACKEND = os.getenv("LOCAL_TTS_BACKEND", "chatterbox").strip().lower()
DEVICE = os.getenv("LOCAL_TTS_DEVICE", "cuda" if torch.cuda.is_available() else "cpu")
DEFAULT_LANGUAGE = os.getenv("LOCAL_TTS_LANGUAGE", "en")
DEFAULT_VOICE = os.getenv("LOCAL_TTS_VOICE", "default")
REFERENCE_AUDIO = os.getenv("LOCAL_TTS_REFERENCE_AUDIO", "").strip()
MODEL_DIR = os.getenv("LOCAL_TTS_MODEL_DIR", "").strip()
CACHE_DIR = Path(os.getenv("LOCAL_TTS_CACHE_DIR", str(Path(__file__).parent / "cache")))
CACHE_DIR.mkdir(parents=True, exist_ok=True)


class SpeechRequest(BaseModel):
    model: str = "chatterbox-multilingual"
    input: str
    voice: str = DEFAULT_VOICE
    response_format: str = "wav"
    speed: float = 0.90
    language: str = DEFAULT_LANGUAGE
    exaggeration: float = 0.35
    cfg_weight: float = 0.20
    temperature: float = 0.55
    repetition_penalty: float = 2.25
    min_p: float = 0.05
    top_p: float = 0.90


app = FastAPI(title="Memory Palace Local TTS", version="1.0")
model = None
model_sample_rate = 24000
generation_lock = threading.Lock()


def load_model() -> None:
    global model, model_sample_rate
    if BACKEND == "chatterbox":
        from chatterbox.mtl_tts import ChatterboxMultilingualTTS

        if MODEL_DIR and Path(MODEL_DIR).is_dir():
            model = ChatterboxMultilingualTTS.from_local(Path(MODEL_DIR), torch.device(DEVICE))
        else:
            model = ChatterboxMultilingualTTS.from_pretrained(device=torch.device(DEVICE))
        model_sample_rate = int(model.sr)
        return

    if BACKEND == "kokoro":
        from kokoro import KPipeline

        model = KPipeline(lang_code=kokoro_language_code(DEFAULT_LANGUAGE))
        model_sample_rate = 24000
        return

    raise RuntimeError(f"Unsupported LOCAL_TTS_BACKEND: {BACKEND}")


def kokoro_language_code(language: str) -> str:
    return {
        "en": "a",
        "en-us": "a",
        "en-gb": "b",
        "es": "e",
        "fr": "f",
        "it": "i",
        "pt": "p",
        "ja": "j",
        "zh": "z",
    }.get((language or "en").lower(), "a")


def cache_path(request: SpeechRequest) -> Path:
    key = json.dumps(
        {
            "backend": BACKEND,
            "model": request.model,
            "input": request.input,
            "voice": request.voice,
            "language": request.language,
            "speed": request.speed,
            "exaggeration": request.exaggeration,
            "cfg_weight": request.cfg_weight,
            "temperature": request.temperature,
            "repetition_penalty": request.repetition_penalty,
            "min_p": request.min_p,
            "top_p": request.top_p,
            "tempo_mode": "pause_pacing_no_pitch_shift_v1",
            "reference": REFERENCE_AUDIO,
        },
        sort_keys=True,
        ensure_ascii=False,
    ).encode("utf-8")
    return CACHE_DIR / f"{hashlib.sha256(key).hexdigest()}.wav"


def generate_chatterbox(request: SpeechRequest) -> np.ndarray:
    kwargs = {
        "language_id": request.language or DEFAULT_LANGUAGE,
        "exaggeration": max(0.0, min(1.5, request.exaggeration)),
        "cfg_weight": max(0.0, min(1.0, request.cfg_weight)),
        "temperature": max(0.1, min(1.2, request.temperature)),
        "repetition_penalty": max(1.0, min(4.0, request.repetition_penalty)),
        "min_p": max(0.0, min(0.25, request.min_p)),
        "top_p": max(0.1, min(1.0, request.top_p)),
    }
    reference = request.voice if request.voice and Path(request.voice).is_file() else REFERENCE_AUDIO
    if reference and Path(reference).is_file():
        kwargs["audio_prompt_path"] = reference
    wav = model.generate(request.input, **kwargs)
    return wav.squeeze().detach().cpu().float().numpy()


def generate_kokoro(request: SpeechRequest) -> np.ndarray:
    voice = request.voice if request.voice and request.voice != "default" else "af_heart"
    pipeline = model
    if kokoro_language_code(request.language) != kokoro_language_code(DEFAULT_LANGUAGE):
        from kokoro import KPipeline

        pipeline = KPipeline(lang_code=kokoro_language_code(request.language))
    chunks = [np.asarray(audio, dtype=np.float32) for _, _, audio in pipeline(request.input, voice=voice)]
    if not chunks:
        raise RuntimeError("Kokoro returned no audio.")
    return np.concatenate(chunks)


def apply_speed(audio: np.ndarray, speed: float) -> np.ndarray:
    speed = max(0.85, min(1.05, float(speed or 1.0)))
    if abs(speed - 1.0) < 0.015 or audio.size < 2:
        return audio.astype(np.float32, copy=False)

    audio = np.asarray(audio, dtype=np.float32).flatten()
    if speed >= 1.0:
        return audio

    # Avoid algorithmic time-stretching because it adds metallic echo on speech.
    # Instead, lightly slow the perceived pace by inserting brief silences.
    extra_ratio = (1.0 / speed) - 1.0
    interval = max(1, int(model_sample_rate * 1.45))
    pause_length = int(model_sample_rate * min(0.12, 0.055 + extra_ratio * 0.22))
    if pause_length <= 0 or audio.size <= interval:
        return audio

    silence = np.zeros(pause_length, dtype=np.float32)
    chunks = []
    cursor = 0
    while cursor < audio.size:
        next_cursor = min(audio.size, cursor + interval)
        chunks.append(audio[cursor:next_cursor])
        cursor = next_cursor
        if cursor < audio.size:
            chunks.append(silence)

    return np.concatenate(chunks).astype(np.float32, copy=False)


@app.on_event("startup")
def startup() -> None:
    load_model()


@app.get("/health")
def health() -> dict:
    return {
        "status": "ok" if model is not None else "loading",
        "backend": BACKEND,
        "device": DEVICE,
        "sample_rate": model_sample_rate,
    }


@app.get("/v1/models")
def models() -> dict:
    model_id = "chatterbox-multilingual" if BACKEND == "chatterbox" else "kokoro-82m"
    return {"object": "list", "data": [{"id": model_id, "object": "model", "owned_by": "local"}]}


@app.post("/v1/audio/speech")
def speech(request: SpeechRequest) -> Response:
    if not request.input or not request.input.strip():
        raise HTTPException(status_code=400, detail="input is empty")
    if request.response_format.lower() not in ("wav", "wave"):
        raise HTTPException(status_code=400, detail="only WAV output is supported")

    target = cache_path(request)
    with generation_lock:
        if target.is_file():
            return Response(target.read_bytes(), media_type="audio/wav", headers={"X-Local-TTS-Cache": "hit"})
        try:
            audio = generate_chatterbox(request) if BACKEND == "chatterbox" else generate_kokoro(request)
            audio = apply_speed(audio, request.speed)
            buffer = io.BytesIO()
            sf.write(buffer, audio, model_sample_rate, format="WAV", subtype="PCM_16")
            wav_bytes = buffer.getvalue()
            target.write_bytes(wav_bytes)
            return Response(wav_bytes, media_type="audio/wav", headers={"X-Local-TTS-Cache": "miss"})
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host=HOST, port=PORT)

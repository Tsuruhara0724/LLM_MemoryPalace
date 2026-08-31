import hashlib
import io
import json
import os
import re
import threading
import wave
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from xml.sax.saxutils import escape as xml_escape

from fastapi import FastAPI, HTTPException
from fastapi.responses import Response
from pydantic import BaseModel


HOST = os.getenv("LOCAL_TTS_HOST", "127.0.0.1")
PORT = int(os.getenv("LOCAL_TTS_PORT", "8880"))
BACKEND = os.getenv("LOCAL_TTS_BACKEND", "azure").strip().lower()
DEVICE = os.getenv("LOCAL_TTS_DEVICE", "cuda")
DEFAULT_LANGUAGE = os.getenv("LOCAL_TTS_LANGUAGE", "en")
AZURE_SPEECH_KEY = os.getenv("AZURE_SPEECH_KEY", "").strip()
AZURE_SPEECH_REGION = os.getenv("AZURE_SPEECH_REGION", "").strip().lower()
AZURE_TTS_VOICE = os.getenv("AZURE_TTS_VOICE", "en-US-AvaMultilingualNeural").strip()
AZURE_TTS_SPANISH_VOICE = os.getenv("AZURE_TTS_SPANISH_VOICE", "es-ES-ElviraNeural").strip()
AZURE_SPEECH_ENDPOINT = os.getenv("AZURE_SPEECH_ENDPOINT", "").strip()
AZURE_OUTPUT_FORMAT = "riff-24khz-16bit-mono-pcm"
DEFAULT_VOICE = os.getenv("LOCAL_TTS_VOICE", AZURE_TTS_VOICE).strip()
REFERENCE_AUDIO = os.getenv("LOCAL_TTS_REFERENCE_AUDIO", "").strip()
MODEL_DIR = os.getenv("LOCAL_TTS_MODEL_DIR", "").strip()
CACHE_DIR = Path(os.getenv("LOCAL_TTS_CACHE_DIR", str(Path(__file__).parent / "cache")))
CATALOG_PATH = Path(
    os.getenv(
        "LOCAL_TTS_CATALOG_PATH",
        str(Path(__file__).resolve().parents[2] / "Assets" / "Resources" / "MemPalaceDemoData.json"),
    )
)
CACHE_DIR.mkdir(parents=True, exist_ok=True)


class SpeechRequest(BaseModel):
    model: str = "azure-speech"
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
    if BACKEND == "azure":
        model = "azure-speech"
        model_sample_rate = 24000
        return

    if BACKEND == "chatterbox":
        import torch
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


def load_formal_spanish_words() -> tuple[str, ...]:
    try:
        catalog = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Unable to read the formal word catalog at {CATALOG_PATH}: {exc}") from exc

    pools = [item for item in catalog.get("wordSets", []) if item.get("setId") == "formal_32_pool"]
    if len(pools) != 1:
        raise RuntimeError(f"Expected one formal_32_pool in {CATALOG_PATH}, found {len(pools)}.")

    words = tuple(
        str(item.get("word", "")).strip()
        for item in pools[0].get("words", [])
        if str(item.get("word", "")).strip()
    )
    if len(words) != 32 or len(set(words)) != 32:
        raise RuntimeError("formal_32_pool must contain exactly 32 unique Spanish words.")
    return words


def normalize_language(language: str) -> str:
    normalized = (language or DEFAULT_LANGUAGE or "en").strip().lower()
    return "es-ES" if normalized.startswith("es") else "en-US"


def azure_voice_for(request: SpeechRequest) -> str:
    requested = (request.voice or "").strip()
    if requested and requested.lower() != "default":
        voice = requested
    elif normalize_language(request.language) == "es-ES":
        voice = AZURE_TTS_SPANISH_VOICE
    else:
        voice = AZURE_TTS_VOICE

    if not re.fullmatch(r"[A-Za-z0-9_.:-]+", voice):
        raise RuntimeError(f"Invalid Azure voice name: {voice!r}")
    return voice


def build_mixed_language_markup(text: str, spanish_words: tuple[str, ...]) -> str:
    pattern = re.compile(
        r"(?<!\w)(" + "|".join(re.escape(word) for word in sorted(spanish_words, key=len, reverse=True)) + r")(?!\w)",
        re.IGNORECASE | re.UNICODE,
    )
    parts: list[str] = []
    cursor = 0
    for match in pattern.finditer(text):
        parts.append(xml_escape(text[cursor:match.start()]))
        parts.append('<lang xml:lang="es-ES">')
        parts.append(xml_escape(match.group(0)))
        parts.append("</lang>")
        cursor = match.end()
    parts.append(xml_escape(text[cursor:]))
    return "".join(parts)


def build_azure_ssml(request: SpeechRequest, spanish_words: tuple[str, ...]) -> str:
    language = normalize_language(request.language)
    voice = azure_voice_for(request)
    text = request.input.strip()
    body = xml_escape(text) if language == "es-ES" else build_mixed_language_markup(text, spanish_words)
    speed = max(0.85, min(1.05, float(request.speed or 1.0)))
    rate_percent = int(round((speed - 1.0) * 100.0))
    rate = f"{rate_percent:+d}%" if rate_percent else "0%"
    return (
        f'<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="{language}">'
        f'<voice name="{voice}"><prosody rate="{rate}">{body}</prosody></voice>'
        "</speak>"
    )


def validate_azure_wav(wav_bytes: bytes) -> None:
    try:
        with wave.open(io.BytesIO(wav_bytes), "rb") as wav_file:
            if (
                wav_file.getnchannels() != 1
                or wav_file.getframerate() != 24000
                or wav_file.getsampwidth() != 2
                or wav_file.getnframes() <= 0
                or wav_file.getcomptype() != "NONE"
            ):
                raise RuntimeError("Azure returned WAV audio in an unexpected format.")
    except (wave.Error, EOFError) as exc:
        raise RuntimeError(f"Azure returned an invalid WAV file: {exc}") from exc


def generate_azure(request: SpeechRequest, spanish_words: tuple[str, ...]) -> bytes:
    if not AZURE_SPEECH_KEY:
        raise RuntimeError("AZURE_SPEECH_KEY is not configured on the PC proxy.")
    if not AZURE_SPEECH_REGION and not AZURE_SPEECH_ENDPOINT:
        raise RuntimeError("AZURE_SPEECH_REGION is not configured on the PC proxy.")

    endpoint = AZURE_SPEECH_ENDPOINT or (
        f"https://{AZURE_SPEECH_REGION}.tts.speech.microsoft.com/cognitiveservices/v1"
    )
    ssml = build_azure_ssml(request, spanish_words)
    azure_request = Request(
        endpoint,
        data=ssml.encode("utf-8"),
        method="POST",
        headers={
            "Ocp-Apim-Subscription-Key": AZURE_SPEECH_KEY,
            "Content-Type": "application/ssml+xml; charset=utf-8",
            "X-Microsoft-OutputFormat": AZURE_OUTPUT_FORMAT,
            "User-Agent": "MemPalaceLLM-Azure-TTS-Proxy",
        },
    )
    try:
        with urlopen(azure_request, timeout=180) as azure_response:
            wav_bytes = azure_response.read()
    except HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace").strip()
        raise RuntimeError(f"Azure Speech returned HTTP {exc.code}: {detail[:500]}") from exc
    except URLError as exc:
        raise RuntimeError(f"Azure Speech request failed: {exc.reason}") from exc

    validate_azure_wav(wav_bytes)
    return wav_bytes


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


def kokoro_default_voice(language: str) -> str:
    return "ef_dora" if kokoro_language_code(language) == "e" else "af_heart"


def cache_path(request: SpeechRequest) -> Path:
    spanish_words = load_formal_spanish_words() if BACKEND == "azure" else ()
    key = json.dumps(
        {
            "backend": BACKEND,
            "model": request.model,
            "input": request.input,
            "voice": azure_voice_for(request) if BACKEND == "azure" else request.voice,
            "language": normalize_language(request.language) if BACKEND == "azure" else request.language,
            "speed": request.speed,
            "exaggeration": request.exaggeration,
            "cfg_weight": request.cfg_weight,
            "temperature": request.temperature,
            "repetition_penalty": request.repetition_penalty,
            "min_p": request.min_p,
            "top_p": request.top_p,
            "tempo_mode": "azure_prosody_rate_v1" if BACKEND == "azure" else "pause_pacing_no_pitch_shift_v1",
            "reference": REFERENCE_AUDIO,
            "azure_region": AZURE_SPEECH_REGION if BACKEND == "azure" else "",
            "azure_output_format": AZURE_OUTPUT_FORMAT if BACKEND == "azure" else "",
            "mixed_language_ssml": "formal_pool_es_lang_v1" if BACKEND == "azure" else "",
            "spanish_words": spanish_words,
        },
        sort_keys=True,
        ensure_ascii=False,
    ).encode("utf-8")
    return CACHE_DIR / f"{hashlib.sha256(key).hexdigest()}.wav"


def generate_chatterbox(request: SpeechRequest):
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


def generate_kokoro(request: SpeechRequest):
    import numpy as np

    voice = request.voice if request.voice and request.voice != "default" else kokoro_default_voice(request.language)
    pipeline = model
    if kokoro_language_code(request.language) != kokoro_language_code(DEFAULT_LANGUAGE):
        from kokoro import KPipeline

        pipeline = KPipeline(lang_code=kokoro_language_code(request.language))
    chunks = [np.asarray(audio, dtype=np.float32) for _, _, audio in pipeline(request.input, voice=voice)]
    if not chunks:
        raise RuntimeError("Kokoro returned no audio.")
    return np.concatenate(chunks)


def apply_speed(audio, speed: float):
    import numpy as np

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
    azure_configured = bool(AZURE_SPEECH_KEY and (AZURE_SPEECH_REGION or AZURE_SPEECH_ENDPOINT))
    return {
        "status": "ok" if model is not None and (BACKEND != "azure" or azure_configured) else "configuration_required",
        "backend": BACKEND,
        "device": "azure-cloud" if BACKEND == "azure" else DEVICE,
        "sample_rate": model_sample_rate,
        "azure_region": AZURE_SPEECH_REGION if BACKEND == "azure" else "",
        "azure_voice": AZURE_TTS_VOICE if BACKEND == "azure" else "",
        "azure_key_configured": azure_configured if BACKEND == "azure" else False,
    }


@app.get("/v1/models")
def models() -> dict:
    model_id = "azure-speech" if BACKEND == "azure" else "chatterbox-multilingual" if BACKEND == "chatterbox" else "kokoro-82m"
    owner = "azure-proxy" if BACKEND == "azure" else "local"
    return {"object": "list", "data": [{"id": model_id, "object": "model", "owned_by": owner}]}


@app.post("/v1/audio/speech")
def speech(request: SpeechRequest) -> Response:
    if not request.input or not request.input.strip():
        raise HTTPException(status_code=400, detail="input is empty")
    if request.response_format.lower() not in ("wav", "wave"):
        raise HTTPException(status_code=400, detail="only WAV output is supported")

    target = cache_path(request)
    with generation_lock:
        if target.is_file():
            return Response(
                target.read_bytes(),
                media_type="audio/wav",
                headers={"X-Local-TTS-Cache": "hit", "X-TTS-Provider": "azure" if BACKEND == "azure" else BACKEND},
            )
        try:
            if BACKEND == "azure":
                wav_bytes = generate_azure(request, load_formal_spanish_words())
            else:
                import soundfile as sf

                audio = generate_chatterbox(request) if BACKEND == "chatterbox" else generate_kokoro(request)
                audio = apply_speed(audio, request.speed)
                buffer = io.BytesIO()
                sf.write(buffer, audio, model_sample_rate, format="WAV", subtype="PCM_16")
                wav_bytes = buffer.getvalue()
            target.write_bytes(wav_bytes)
            return Response(
                wav_bytes,
                media_type="audio/wav",
                headers={"X-Local-TTS-Cache": "miss", "X-TTS-Provider": "azure" if BACKEND == "azure" else BACKEND},
            )
        except Exception as exc:
            raise HTTPException(status_code=500, detail=str(exc)) from exc


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host=HOST, port=PORT)

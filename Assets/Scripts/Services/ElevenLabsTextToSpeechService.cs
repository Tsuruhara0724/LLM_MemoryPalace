using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MemPalaceLLM
{
    /// <summary>
    /// Runtime ElevenLabs speech bridge. Each Speak call sends the supplied text to
    /// eleven_multilingual_v2 and plays the returned audio through a non-spatial AudioSource.
    /// </summary>
    public sealed class ElevenLabsTextToSpeechService : IDisposable
    {
        private const string ApiBaseUrl = "https://api.elevenlabs.io/v1/text-to-speech/";
        private const string ModelId = "eleven_multilingual_v2";
        private const string GeminiTtsUrl = "https://generativelanguage.googleapis.com/v1beta/interactions";
        private const string GeminiTtsModel = "gemini-2.5-flash-preview-tts";

        public event Action<string> UtteranceCompleted;
        public event Action<string, string> UtteranceFailed;

        public bool IsSupported { get; private set; } = true;
        public bool IsReady { get; private set; }
        public bool IsSpeaking => activeRequest != null || playbackStarted;
        public bool HasPlayableClip => activeClip != null && audioSource != null && activeClip.length > 0.01f;
        public float PlaybackTime => HasPlayableClip ? Mathf.Clamp(audioSource.time, 0f, activeClip.length) : 0f;
        public float PlaybackDuration => HasPlayableClip ? activeClip.length : 0f;
        public float NormalizedPlaybackProgress => PlaybackDuration > 0.01f
            ? Mathf.Clamp01(PlaybackTime / PlaybackDuration)
            : 0f;
        public string CurrentUtteranceId => activeUtteranceId;
        public string Status { get; private set; } = "ElevenLabs has not been configured.";

        private readonly GameObject playerObject;
        private readonly AudioSource audioSource;
        private UnityWebRequest activeRequest;
        private UnityWebRequestAsyncOperation requestOperation;
        private AudioClip activeClip;
        private string activeUtteranceId = string.Empty;
        private string apiKey = string.Empty;
        private string voiceId = string.Empty;
        private string geminiApiKey = string.Empty;
        private string localTtsEndpoint = string.Empty;
        private string localTtsModel = "chatterbox-multilingual";
        private string localTtsVoice = "default";
        private string pendingText = string.Empty;
        private string playbackProvider = "Speech";
        private bool activeRequestUsesGemini;
        private bool activeRequestUsesLocal;
        private bool elevenLabsUnavailableForSession;
        private bool localTtsUnavailableForSession;
        private bool playbackStarted;

        public ElevenLabsTextToSpeechService(Transform owner)
        {
            playerObject = new GameObject("ElevenLabsSpeechPlayer");
            if (owner != null)
            {
                playerObject.transform.SetParent(owner, false);
            }

            audioSource = playerObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 1f;
        }

        public void Configure(
            string configuredApiKey,
            string configuredVoiceId,
            string configuredGeminiApiKey = null,
            string configuredLocalTtsEndpoint = null,
            string configuredLocalTtsModel = null,
            string configuredLocalTtsVoice = null)
        {
            var nextApiKey = string.IsNullOrWhiteSpace(configuredApiKey) ? string.Empty : configuredApiKey.Trim();
            var nextVoiceId = string.IsNullOrWhiteSpace(configuredVoiceId) ? string.Empty : configuredVoiceId.Trim();
            var nextGeminiApiKey = string.IsNullOrWhiteSpace(configuredGeminiApiKey) ? string.Empty : configuredGeminiApiKey.Trim();
            var nextLocalEndpoint = string.IsNullOrWhiteSpace(configuredLocalTtsEndpoint) ? string.Empty : configuredLocalTtsEndpoint.Trim().TrimEnd('/');
            var nextLocalModel = string.IsNullOrWhiteSpace(configuredLocalTtsModel) ? "chatterbox-multilingual" : configuredLocalTtsModel.Trim();
            var nextLocalVoice = string.IsNullOrWhiteSpace(configuredLocalTtsVoice) ? "default" : configuredLocalTtsVoice.Trim();
            if (string.Equals(apiKey, nextApiKey, StringComparison.Ordinal) &&
                string.Equals(voiceId, nextVoiceId, StringComparison.Ordinal) &&
                string.Equals(geminiApiKey, nextGeminiApiKey, StringComparison.Ordinal) &&
                string.Equals(localTtsEndpoint, nextLocalEndpoint, StringComparison.Ordinal) &&
                string.Equals(localTtsModel, nextLocalModel, StringComparison.Ordinal) &&
                string.Equals(localTtsVoice, nextLocalVoice, StringComparison.Ordinal))
            {
                return;
            }

            var elevenLabsCredentialsChanged = !string.Equals(apiKey, nextApiKey, StringComparison.Ordinal) ||
                                               !string.Equals(voiceId, nextVoiceId, StringComparison.Ordinal);
            var localTtsConfigurationChanged = !string.Equals(localTtsEndpoint, nextLocalEndpoint, StringComparison.Ordinal) ||
                                               !string.Equals(localTtsModel, nextLocalModel, StringComparison.Ordinal) ||
                                               !string.Equals(localTtsVoice, nextLocalVoice, StringComparison.Ordinal);
            apiKey = nextApiKey;
            voiceId = nextVoiceId;
            geminiApiKey = nextGeminiApiKey;
            localTtsEndpoint = nextLocalEndpoint;
            localTtsModel = nextLocalModel;
            localTtsVoice = nextLocalVoice;
            if (elevenLabsCredentialsChanged)
            {
                elevenLabsUnavailableForSession = false;
            }
            if (localTtsConfigurationChanged)
            {
                localTtsUnavailableForSession = false;
            }
            UpdateReadyStatus();
        }

        public void Initialize()
        {
            IsSupported = true;
            UpdateReadyStatus();
        }

        public void Tick()
        {
            if (activeRequest != null && requestOperation != null && requestOperation.isDone)
            {
                FinishSpeechRequest();
            }

            if (!playbackStarted || audioSource == null || audioSource.isPlaying)
            {
                return;
            }

            var completedId = activeUtteranceId;
            activeUtteranceId = string.Empty;
            playbackStarted = false;
            ReleaseActiveClip();
            Status = playbackProvider + " speech completed.";
            UtteranceCompleted?.Invoke(completedId);
        }

        public bool Speak(string text, string utteranceId)
        {
            if (!IsReady)
            {
                Status = "Configure Local TTS, ElevenLabs, or Gemini speech in Setup.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "ElevenLabs cannot speak empty text.";
                return false;
            }

            Stop();
            activeUtteranceId = string.IsNullOrWhiteSpace(utteranceId)
                ? "speech_" + Guid.NewGuid().ToString("N")
                : utteranceId.Trim();
            pendingText = text.Trim();

            if (CanUseLocalTts())
            {
                StartLocalTtsRequest(pendingText);
            }
            else if (CanUseElevenLabs())
            {
                StartElevenLabsRequest(pendingText);
            }
            else
            {
                StartGeminiRequest(pendingText);
            }

            return true;
        }

        private void StartLocalTtsRequest(string text)
        {
            activeRequestUsesLocal = true;
            activeRequestUsesGemini = false;
            var payload = new OpenAiSpeechRequest
            {
                model = localTtsModel,
                input = text,
                voice = localTtsVoice,
                response_format = "wav",
                language = "en",
                speed = 1f,
                exaggeration = 0.58f,
                cfg_weight = 0.35f
            };

            var url = localTtsEndpoint.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase)
                ? localTtsEndpoint
                : localTtsEndpoint + "/audio/speech";
            activeRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 180
            };
            activeRequest.SetRequestHeader("Content-Type", "application/json");
            activeRequest.SetRequestHeader("Accept", "audio/wav");
            requestOperation = activeRequest.SendWebRequest();
            Status = "Generating unlimited local speech with " + localTtsModel + ".";
        }

        private void StartElevenLabsRequest(string text)
        {
            activeRequestUsesLocal = false;
            activeRequestUsesGemini = false;

            var payload = new SpeechRequest
            {
                text = text,
                model_id = ModelId,
                voice_settings = new VoiceSettings
                {
                    stability = 0.42f,
                    similarity_boost = 0.78f,
                    style = 0.20f,
                    use_speaker_boost = true,
                    speed = 0.96f
                }
            };

            var url = ApiBaseUrl + UnityWebRequest.EscapeURL(voiceId) + "?output_format=mp3_44100_128";
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            activeRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG)
            };
            activeRequest.SetRequestHeader("Content-Type", "application/json");
            activeRequest.SetRequestHeader("Accept", "audio/mpeg");
            activeRequest.SetRequestHeader("xi-api-key", apiKey);
            requestOperation = activeRequest.SendWebRequest();
            Status = "Generating natural speech with ElevenLabs Multilingual v2.";
        }

        private void StartGeminiRequest(string text)
        {
            activeRequestUsesLocal = false;
            activeRequestUsesGemini = true;
            var payload = new GeminiTtsRequest
            {
                model = GeminiTtsModel,
                input = "Read warmly, naturally, and conversationally at a steady pace. Speak exactly this text:\n" + text,
                response_format = new GeminiResponseFormat { type = "audio" },
                generation_config = new GeminiGenerationConfig
                {
                    speech_config = new[] { new GeminiSpeechConfig { voice = "Sulafat" } }
                }
            };

            activeRequest = new UnityWebRequest(GeminiTtsUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
                downloadHandler = new DownloadHandlerBuffer()
            };
            activeRequest.SetRequestHeader("Content-Type", "application/json");
            activeRequest.SetRequestHeader("x-goog-api-key", geminiApiKey);
            activeRequest.SetRequestHeader("Api-Revision", "2026-05-20");
            requestOperation = activeRequest.SendWebRequest();
            Status = "Generating natural speech with Gemini Flash TTS.";
        }

        public bool SeekNormalized(float normalizedTime)
        {
            if (!HasPlayableClip)
            {
                return false;
            }

            var duration = activeClip.length;
            var targetTime = Mathf.Clamp01(normalizedTime) * duration;
            audioSource.time = Mathf.Clamp(targetTime, 0f, Mathf.Max(0f, duration - 0.01f));
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }

            playbackStarted = true;
            Status = "Seeking within the current spoken sentence.";
            return true;
        }

        public bool RestartCurrentClip()
        {
            return SeekNormalized(0f);
        }

        public void Stop()
        {
            if (activeRequest != null)
            {
                try
                {
                    activeRequest.Abort();
                }
                catch (Exception)
                {
                    // The request may already have completed between frames.
                }

                activeRequest.Dispose();
                activeRequest = null;
            }

            requestOperation = null;
            if (audioSource != null)
            {
                audioSource.Stop();
            }

            playbackStarted = false;
            activeUtteranceId = string.Empty;
            pendingText = string.Empty;
            activeRequestUsesGemini = false;
            activeRequestUsesLocal = false;
            ReleaseActiveClip();
        }

        public void Dispose()
        {
            Stop();
            if (playerObject != null)
            {
                UnityEngine.Object.Destroy(playerObject);
            }
        }

        private void FinishSpeechRequest()
        {
            var request = activeRequest;
            activeRequest = null;
            requestOperation = null;
            if (request == null)
            {
                return;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (activeRequestUsesLocal && !string.IsNullOrWhiteSpace(pendingText))
                {
                    var localFailure = BuildRequestFailureStatus("Local TTS", request);
                    localTtsUnavailableForSession = true;
                    Debug.LogWarning(localFailure + " Falling back to configured cloud speech.");
                    if (CanUseElevenLabs())
                    {
                        request.Dispose();
                        StartElevenLabsRequest(pendingText);
                        return;
                    }
                    if (CanUseGemini())
                    {
                        request.Dispose();
                        StartGeminiRequest(pendingText);
                        return;
                    }
                }
                else if (!activeRequestUsesGemini && CanUseGemini() && !string.IsNullOrWhiteSpace(pendingText))
                {
                    var elevenLabsFailure = BuildRequestFailureStatus("ElevenLabs", request);
                    if (request.responseCode == 401 || elevenLabsFailure.IndexOf("quota_exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        elevenLabsUnavailableForSession = true;
                    }
                    Debug.LogWarning(elevenLabsFailure + " Falling back to Gemini Flash TTS.");
                    request.Dispose();
                    StartGeminiRequest(pendingText);
                    return;
                }

                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = BuildRequestFailureStatus(GetActiveProviderLabel(), request);
                Debug.LogWarning(Status);
                request.Dispose();
                pendingText = string.Empty;
                UtteranceFailed?.Invoke(failedId, Status);
                return;
            }

            try
            {
                activeClip = activeRequestUsesLocal
                    ? DecodePcmWavClip(request.downloadHandler.data)
                    : activeRequestUsesGemini
                        ? DecodeGeminiPcmClip(request.downloadHandler.text)
                        : DownloadHandlerAudioClip.GetContent(request);
            }
            catch (Exception ex)
            {
                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = GetActiveProviderLabel() + " audio decoding failed: " + ex.Message;
                Debug.LogWarning(Status);
                request.Dispose();
                pendingText = string.Empty;
                UtteranceFailed?.Invoke(failedId, Status);
                return;
            }

            request.Dispose();
            if (activeClip == null || audioSource == null)
            {
                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = GetActiveProviderLabel() + " returned no playable audio.";
                Debug.LogWarning(Status);
                ReleaseActiveClip();
                pendingText = string.Empty;
                UtteranceFailed?.Invoke(failedId, Status);
                return;
            }

            audioSource.clip = activeClip;
            audioSource.Play();
            playbackStarted = true;
            playbackProvider = activeRequestUsesLocal
                ? "Local TTS (" + localTtsModel + ")"
                : activeRequestUsesGemini ? "Gemini Flash TTS" : "ElevenLabs Multilingual v2";
            pendingText = string.Empty;
            Status = "Speaking with " + playbackProvider + ".";
        }

        private void UpdateReadyStatus()
        {
            IsReady = CanUseLocalTts() || CanUseElevenLabs() || CanUseGemini();
            if (CanUseLocalTts())
            {
                Status = "Local unlimited TTS is ready; configured cloud speech remains available as fallback.";
            }
            else if (CanUseElevenLabs() && CanUseGemini())
            {
                Status = "ElevenLabs is ready, with Gemini Flash TTS as a free fallback.";
            }
            else if (CanUseElevenLabs())
            {
                Status = "ElevenLabs Multilingual v2 is ready.";
            }
            else if (CanUseGemini())
            {
                Status = "Gemini Flash TTS is ready.";
            }
            else
            {
                Status = "Configure a local TTS endpoint, ElevenLabs key, or Gemini API key in Setup.";
            }
        }

        private bool CanUseLocalTts()
        {
            return !localTtsUnavailableForSession && !string.IsNullOrWhiteSpace(localTtsEndpoint);
        }

        private bool CanUseElevenLabs()
        {
            return !elevenLabsUnavailableForSession &&
                   !string.IsNullOrWhiteSpace(apiKey) &&
                   !string.IsNullOrWhiteSpace(voiceId);
        }

        private bool CanUseGemini()
        {
            return !string.IsNullOrWhiteSpace(geminiApiKey);
        }

        private string GetActiveProviderLabel()
        {
            return activeRequestUsesLocal ? "Local TTS" : activeRequestUsesGemini ? "Gemini TTS" : "ElevenLabs";
        }

        private static AudioClip DecodePcmWavClip(byte[] wavBytes)
        {
            if (wavBytes == null || wavBytes.Length < 44)
            {
                throw new InvalidOperationException("WAV response is empty or truncated.");
            }

            using var stream = new MemoryStream(wavBytes, false);
            using var reader = new BinaryReader(stream);
            if (new string(reader.ReadChars(4)) != "RIFF")
            {
                throw new InvalidOperationException("WAV response is missing the RIFF header.");
            }
            reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
            {
                throw new InvalidOperationException("WAV response is missing the WAVE header.");
            }

            ushort channels = 0;
            var sampleRate = 0;
            ushort bitsPerSample = 0;
            byte[] pcm = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadInt32();
                if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                {
                    throw new InvalidOperationException("WAV response contains an invalid chunk length.");
                }

                if (chunkId == "fmt ")
                {
                    var audioFormat = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    var remaining = chunkSize - 16;
                    if (remaining > 0) reader.ReadBytes(remaining);
                    if (audioFormat != 1)
                    {
                        throw new InvalidOperationException("Only PCM WAV audio is supported.");
                    }
                }
                else if (chunkId == "data")
                {
                    pcm = reader.ReadBytes(chunkSize);
                }
                else
                {
                    reader.ReadBytes(chunkSize);
                }

                if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                {
                    reader.ReadByte();
                }
            }

            if (pcm == null || channels == 0 || sampleRate <= 0 || bitsPerSample != 16)
            {
                throw new InvalidOperationException("WAV response must contain 16-bit PCM audio.");
            }

            var totalSamples = pcm.Length / 2;
            var samples = new float[totalSamples];
            for (var i = 0; i < totalSamples; i++)
            {
                samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
            }

            var frames = totalSamples / channels;
            var clip = AudioClip.Create("LocalTtsSpeech", frames, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static string BuildRequestFailureStatus(string provider, UnityWebRequest request)
        {
            var requestError = string.IsNullOrWhiteSpace(request?.error) ? "unknown request error" : request.error;
            var status = $"{provider} speech request failed ({request?.responseCode ?? 0}): {requestError}";
            var responseDetail = request?.downloadHandler?.data != null && request.downloadHandler.data.Length > 0
                ? Encoding.UTF8.GetString(request.downloadHandler.data).Trim()
                : string.Empty;
            if (responseDetail.Length > 320)
            {
                responseDetail = responseDetail.Substring(0, 320) + "...";
            }

            return string.IsNullOrWhiteSpace(responseDetail) ? status : status + " " + responseDetail;
        }

        private static AudioClip DecodeGeminiPcmClip(string responseJson)
        {
            var response = JsonUtility.FromJson<GeminiTtsResponse>(responseJson);
            if (response?.steps == null)
            {
                throw new InvalidOperationException("Gemini response did not contain output steps.");
            }

            for (var stepIndex = 0; stepIndex < response.steps.Length; stepIndex++)
            {
                var contents = response.steps[stepIndex]?.content;
                if (contents == null)
                {
                    continue;
                }

                for (var contentIndex = 0; contentIndex < contents.Length; contentIndex++)
                {
                    var output = contents[contentIndex];
                    if (output == null || string.IsNullOrWhiteSpace(output.data) ||
                        !string.Equals(output.type, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pcm = Convert.FromBase64String(output.data);
                    var sampleCount = pcm.Length / 2;
                    if (sampleCount <= 0)
                    {
                        throw new InvalidOperationException("Gemini returned empty PCM audio.");
                    }

                    var samples = new float[sampleCount];
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                        samples[i] = sample / 32768f;
                    }

                    var sampleRate = output.sample_rate > 0 ? output.sample_rate : 24000;
                    var clip = AudioClip.Create("GeminiTtsSpeech", sampleCount, 1, sampleRate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
            }

            throw new InvalidOperationException("Gemini response did not contain an audio block.");
        }

        private void ReleaseActiveClip()
        {
            if (audioSource != null)
            {
                audioSource.clip = null;
            }

            if (activeClip != null)
            {
                UnityEngine.Object.Destroy(activeClip);
                activeClip = null;
            }
        }

        [Serializable]
        private sealed class SpeechRequest
        {
            public string text;
            public string model_id;
            public VoiceSettings voice_settings;
        }

        [Serializable]
        private sealed class OpenAiSpeechRequest
        {
            public string model;
            public string input;
            public string voice;
            public string response_format;
            public string language;
            public float speed;
            public float exaggeration;
            public float cfg_weight;
        }

        [Serializable]
        private sealed class VoiceSettings
        {
            public float stability;
            public float similarity_boost;
            public float style;
            public bool use_speaker_boost;
            public float speed;
        }

        [Serializable]
        private sealed class GeminiTtsRequest
        {
            public string model;
            public string input;
            public GeminiResponseFormat response_format;
            public GeminiGenerationConfig generation_config;
        }

        [Serializable]
        private sealed class GeminiResponseFormat
        {
            public string type;
        }

        [Serializable]
        private sealed class GeminiGenerationConfig
        {
            public GeminiSpeechConfig[] speech_config;
        }

        [Serializable]
        private sealed class GeminiSpeechConfig
        {
            public string voice;
        }

        [Serializable]
        private sealed class GeminiTtsResponse
        {
            public GeminiTtsStep[] steps;
        }

        [Serializable]
        private sealed class GeminiTtsStep
        {
            public GeminiTtsContent[] content;
        }

        [Serializable]
        private sealed class GeminiTtsContent
        {
            public string type;
            public string mime_type;
            public string data;
            public int sample_rate;
        }
    }
}

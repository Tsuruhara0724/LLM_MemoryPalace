using System;
using System.Collections.Generic;
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
        public bool IsPreloading => activeRequest != null && activeRequestIsPreload;
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
        private readonly Dictionary<string, AudioClip> speechCache = new Dictionary<string, AudioClip>();
        private UnityWebRequest activeRequest;
        private UnityWebRequestAsyncOperation requestOperation;
        private AudioClip activeClip;
        private bool activeClipIsCached;
        private string activeUtteranceId = string.Empty;
        private string activeRequestCacheKey = string.Empty;
        private bool activeRequestIsPreload;
        private string apiKey = string.Empty;
        private string voiceId = string.Empty;
        private string geminiApiKey = string.Empty;
        private string localTtsEndpoint = string.Empty;
        private string localTtsModel = "chatterbox-multilingual";
        private string localTtsVoice = "default";
        private float localTtsSpeed = 0.90f;
        private float localTtsExaggeration = 0.35f;
        private float localTtsCfgWeight = 0.20f;
        private float localTtsTemperature = 0.55f;
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
            string configuredLocalTtsVoice = null,
            float configuredLocalTtsSpeed = 0.90f,
            float configuredLocalTtsExaggeration = 0.35f,
            float configuredLocalTtsCfgWeight = 0.20f,
            float configuredLocalTtsTemperature = 0.55f)
        {
            var nextApiKey = string.IsNullOrWhiteSpace(configuredApiKey) ? string.Empty : configuredApiKey.Trim();
            var nextVoiceId = string.IsNullOrWhiteSpace(configuredVoiceId) ? string.Empty : configuredVoiceId.Trim();
            var nextGeminiApiKey = string.IsNullOrWhiteSpace(configuredGeminiApiKey) ? string.Empty : configuredGeminiApiKey.Trim();
            var nextLocalEndpoint = string.IsNullOrWhiteSpace(configuredLocalTtsEndpoint) ? string.Empty : configuredLocalTtsEndpoint.Trim().TrimEnd('/');
            var nextLocalModel = string.IsNullOrWhiteSpace(configuredLocalTtsModel) ? "chatterbox-multilingual" : configuredLocalTtsModel.Trim();
            var nextLocalVoice = string.IsNullOrWhiteSpace(configuredLocalTtsVoice) ? "default" : configuredLocalTtsVoice.Trim();
            var nextLocalSpeed = Mathf.Clamp(configuredLocalTtsSpeed, 0.85f, 1.05f);
            var nextLocalExaggeration = Mathf.Clamp(configuredLocalTtsExaggeration, 0.20f, 0.80f);
            var nextLocalCfgWeight = Mathf.Clamp(configuredLocalTtsCfgWeight, 0.05f, 0.55f);
            var nextLocalTemperature = Mathf.Clamp(configuredLocalTtsTemperature, 0.35f, 0.90f);
            if (string.Equals(apiKey, nextApiKey, StringComparison.Ordinal) &&
                string.Equals(voiceId, nextVoiceId, StringComparison.Ordinal) &&
                string.Equals(geminiApiKey, nextGeminiApiKey, StringComparison.Ordinal) &&
                string.Equals(localTtsEndpoint, nextLocalEndpoint, StringComparison.Ordinal) &&
                string.Equals(localTtsModel, nextLocalModel, StringComparison.Ordinal) &&
                string.Equals(localTtsVoice, nextLocalVoice, StringComparison.Ordinal) &&
                Mathf.Approximately(localTtsSpeed, nextLocalSpeed) &&
                Mathf.Approximately(localTtsExaggeration, nextLocalExaggeration) &&
                Mathf.Approximately(localTtsCfgWeight, nextLocalCfgWeight) &&
                Mathf.Approximately(localTtsTemperature, nextLocalTemperature))
            {
                return;
            }

            var elevenLabsCredentialsChanged = !string.Equals(apiKey, nextApiKey, StringComparison.Ordinal) ||
                                               !string.Equals(voiceId, nextVoiceId, StringComparison.Ordinal);
            var geminiCredentialsChanged = !string.Equals(geminiApiKey, nextGeminiApiKey, StringComparison.Ordinal);
            var localTtsConfigurationChanged = !string.Equals(localTtsEndpoint, nextLocalEndpoint, StringComparison.Ordinal) ||
                                               !string.Equals(localTtsModel, nextLocalModel, StringComparison.Ordinal) ||
                                               !string.Equals(localTtsVoice, nextLocalVoice, StringComparison.Ordinal) ||
                                               !Mathf.Approximately(localTtsSpeed, nextLocalSpeed) ||
                                               !Mathf.Approximately(localTtsExaggeration, nextLocalExaggeration) ||
                                               !Mathf.Approximately(localTtsCfgWeight, nextLocalCfgWeight) ||
                                               !Mathf.Approximately(localTtsTemperature, nextLocalTemperature);
            if (elevenLabsCredentialsChanged || geminiCredentialsChanged || localTtsConfigurationChanged)
            {
                Stop();
                ClearSpeechCache();
            }

            apiKey = nextApiKey;
            voiceId = nextVoiceId;
            geminiApiKey = nextGeminiApiKey;
            localTtsEndpoint = nextLocalEndpoint;
            localTtsModel = nextLocalModel;
            localTtsVoice = nextLocalVoice;
            localTtsSpeed = nextLocalSpeed;
            localTtsExaggeration = nextLocalExaggeration;
            localTtsCfgWeight = nextLocalCfgWeight;
            localTtsTemperature = nextLocalTemperature;
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
            activeRequestIsPreload = false;

            var cacheKey = BuildSpeechCacheKey(pendingText);
            if (TryPlayCachedClip(cacheKey))
            {
                Status = "Speaking from prepared speech cache.";
                return true;
            }

            if (IsLocalTtsConfigured())
            {
                if (!CanUseLocalTts())
                {
                    Status = "Local TTS is selected, but the local endpoint is not available. Cloud TTS fallback is disabled.";
                    activeUtteranceId = string.Empty;
                    pendingText = string.Empty;
                    return false;
                }

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

        public bool IsSpeechCached(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return speechCache.TryGetValue(BuildSpeechCacheKey(text.Trim()), out var clip) &&
                   clip != null &&
                   clip.length > 0.01f;
        }

        public bool Preload(string text, string preloadId = null)
        {
            if (!IsReady)
            {
                Status = "Configure Local TTS, ElevenLabs, or Gemini speech before preloading.";
                return false;
            }

            if (activeRequest != null || playbackStarted)
            {
                Status = "Speech service is busy.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "Cannot preload empty speech text.";
                return false;
            }

            pendingText = text.Trim();
            activeRequestCacheKey = BuildSpeechCacheKey(pendingText);
            if (speechCache.TryGetValue(activeRequestCacheKey, out var cachedClip) &&
                cachedClip != null &&
                cachedClip.length > 0.01f)
            {
                pendingText = string.Empty;
                activeRequestCacheKey = string.Empty;
                Status = "Speech is already prepared.";
                return true;
            }

            activeRequestIsPreload = true;
            activeUtteranceId = string.IsNullOrWhiteSpace(preloadId)
                ? "speech_preload_" + Guid.NewGuid().ToString("N")
                : preloadId.Trim();

            if (IsLocalTtsConfigured())
            {
                if (!CanUseLocalTts())
                {
                    Status = "Local TTS is selected, but the local endpoint is not available. Cloud TTS fallback is disabled.";
                    activeUtteranceId = string.Empty;
                    pendingText = string.Empty;
                    activeRequestCacheKey = string.Empty;
                    activeRequestIsPreload = false;
                    return false;
                }

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

            Status = "Preparing speech audio before study.";
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
                speed = localTtsSpeed,
                exaggeration = localTtsExaggeration,
                cfg_weight = localTtsCfgWeight,
                temperature = localTtsTemperature,
                repetition_penalty = 2.25f,
                min_p = 0.05f,
                top_p = 0.90f
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
            activeRequestCacheKey = string.Empty;
            activeRequestIsPreload = false;
            activeRequestUsesGemini = false;
            activeRequestUsesLocal = false;
            ReleaseActiveClip();
        }

        public void Dispose()
        {
            Stop();
            ClearSpeechCache();
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
                    var localFailedId = activeUtteranceId;
                    activeUtteranceId = string.Empty;
                    Status = localFailure + " Local TTS is selected, so cloud speech fallback is disabled.";
                    Debug.LogWarning(Status);
                    request.Dispose();
                    pendingText = string.Empty;
                    activeRequestCacheKey = string.Empty;
                    var localWasPreload = activeRequestIsPreload;
                    activeRequestIsPreload = false;
                    if (!localWasPreload)
                    {
                        UtteranceFailed?.Invoke(localFailedId, Status);
                    }
                    return;
                }
                else if (!activeRequestUsesGemini && CanUseGemini() && !string.IsNullOrWhiteSpace(pendingText))
                {
                    var elevenLabsFailure = BuildRequestFailureStatus("ElevenLabs", request);
                    if (request.responseCode == 401 || elevenLabsFailure.IndexOf("quota_exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        elevenLabsUnavailableForSession = true;
                    }
                    activeRequestCacheKey = BuildSpeechCacheKey(pendingText);
                    Debug.LogWarning(elevenLabsFailure + " Falling back to Gemini Flash TTS.");
                    request.Dispose();
                    StartGeminiRequest(pendingText);
                    return;
                }

                var requestFailedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = BuildRequestFailureStatus(GetActiveProviderLabel(), request);
                Debug.LogWarning(Status);
                request.Dispose();
                pendingText = string.Empty;
                activeRequestCacheKey = string.Empty;
                var requestWasPreload = activeRequestIsPreload;
                activeRequestIsPreload = false;
                if (!requestWasPreload)
                {
                    UtteranceFailed?.Invoke(requestFailedId, Status);
                }
                return;
            }

            AudioClip decodedClip;
            try
            {
                decodedClip = activeRequestUsesLocal
                    ? DecodePcmWavClip(request.downloadHandler.data)
                    : activeRequestUsesGemini
                        ? DecodeGeminiPcmClip(request.downloadHandler.text)
                        : DownloadHandlerAudioClip.GetContent(request);
            }
            catch (Exception ex)
            {
                var decodeFailedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = GetActiveProviderLabel() + " audio decoding failed: " + ex.Message;
                Debug.LogWarning(Status);
                request.Dispose();
                pendingText = string.Empty;
                activeRequestCacheKey = string.Empty;
                var wasPreload = activeRequestIsPreload;
                activeRequestIsPreload = false;
                if (!wasPreload)
                {
                    UtteranceFailed?.Invoke(decodeFailedId, Status);
                }
                return;
            }

            request.Dispose();
            if (decodedClip == null || audioSource == null)
            {
                var playableFailedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = GetActiveProviderLabel() + " returned no playable audio.";
                Debug.LogWarning(Status);
                if (decodedClip != null)
                {
                    UnityEngine.Object.Destroy(decodedClip);
                }
                pendingText = string.Empty;
                activeRequestCacheKey = string.Empty;
                var wasPreload = activeRequestIsPreload;
                activeRequestIsPreload = false;
                if (!wasPreload)
                {
                    UtteranceFailed?.Invoke(playableFailedId, Status);
                }
                return;
            }

            var cacheKey = string.IsNullOrWhiteSpace(activeRequestCacheKey)
                ? BuildSpeechCacheKey(pendingText)
                : activeRequestCacheKey;
            CacheSpeechClip(cacheKey, decodedClip);
            if (activeRequestIsPreload)
            {
                Status = "Prepared speech audio.";
                activeUtteranceId = string.Empty;
                pendingText = string.Empty;
                activeRequestCacheKey = string.Empty;
                activeRequestIsPreload = false;
                activeRequestUsesGemini = false;
                activeRequestUsesLocal = false;
                return;
            }

            activeClip = decodedClip;
            activeClipIsCached = true;
            audioSource.clip = activeClip;
            audioSource.Play();
            playbackStarted = true;
            playbackProvider = activeRequestUsesLocal
                ? "Local TTS (" + localTtsModel + ")"
                : activeRequestUsesGemini ? "Gemini Flash TTS" : "ElevenLabs Multilingual v2";
            pendingText = string.Empty;
            activeRequestCacheKey = string.Empty;
            activeRequestIsPreload = false;
            Status = "Speaking with " + playbackProvider + ".";
        }

        private void UpdateReadyStatus()
        {
            if (IsLocalTtsConfigured())
            {
                IsReady = CanUseLocalTts();
                Status = IsReady
                    ? "Local-only unlimited TTS is ready. Cloud speech fallback is disabled."
                    : "Local TTS is selected, but the local endpoint is empty or unavailable. Cloud speech fallback is disabled.";
                return;
            }

            IsReady = CanUseElevenLabs() || CanUseGemini();
            if (CanUseElevenLabs() && CanUseGemini())
            {
                Status = "ElevenLabs is ready, with Gemini Flash TTS as fallback.";
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

        private bool IsLocalTtsConfigured()
        {
            return !string.IsNullOrWhiteSpace(localTtsEndpoint);
        }

        private bool CanUseLocalTts()
        {
            return !localTtsUnavailableForSession && IsLocalTtsConfigured();
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

        private string BuildSpeechCacheKey(string text)
        {
            var normalizedText = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Trim();
            if (IsLocalTtsConfigured())
            {
                return "local|" + localTtsEndpoint + "|" + localTtsModel + "|" + localTtsVoice + "|" +
                       localTtsSpeed.ToString("0.000") + "|" +
                       localTtsExaggeration.ToString("0.000") + "|" +
                       localTtsCfgWeight.ToString("0.000") + "|" +
                       localTtsTemperature.ToString("0.000") + "|" +
                       normalizedText;
            }

            if (CanUseElevenLabs())
            {
                return "elevenlabs|" + voiceId + "|" + normalizedText;
            }

            return "gemini|" + GeminiTtsModel + "|Sulafat|" + normalizedText;
        }

        private bool TryPlayCachedClip(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) ||
                !speechCache.TryGetValue(cacheKey, out var cachedClip) ||
                cachedClip == null ||
                cachedClip.length <= 0.01f ||
                audioSource == null)
            {
                return false;
            }

            ReleaseActiveClip();
            activeClip = cachedClip;
            activeClipIsCached = true;
            audioSource.clip = activeClip;
            audioSource.time = 0f;
            audioSource.Play();
            playbackStarted = true;
            playbackProvider = "Prepared speech";
            pendingText = string.Empty;
            activeRequestCacheKey = string.Empty;
            activeRequestIsPreload = false;
            return true;
        }

        private void CacheSpeechClip(string cacheKey, AudioClip clip)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || clip == null)
            {
                return;
            }

            if (speechCache.TryGetValue(cacheKey, out var existing) && existing != null && existing != clip)
            {
                UnityEngine.Object.Destroy(existing);
            }

            speechCache[cacheKey] = clip;
        }

        private void ClearSpeechCache()
        {
            foreach (var pair in speechCache)
            {
                if (pair.Value != null && pair.Value != activeClip)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            speechCache.Clear();
        }

        private void ReleaseActiveClip()
        {
            if (audioSource != null)
            {
                audioSource.clip = null;
            }

            if (activeClip != null)
            {
                if (!activeClipIsCached)
                {
                    UnityEngine.Object.Destroy(activeClip);
                }

                activeClip = null;
            }

            activeClipIsCached = false;
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
            public float temperature;
            public float repetition_penalty;
            public float min_p;
            public float top_p;
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

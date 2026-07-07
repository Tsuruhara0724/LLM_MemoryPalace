using System;
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

        public void Configure(string configuredApiKey, string configuredVoiceId)
        {
            var nextApiKey = string.IsNullOrWhiteSpace(configuredApiKey) ? string.Empty : configuredApiKey.Trim();
            var nextVoiceId = string.IsNullOrWhiteSpace(configuredVoiceId) ? string.Empty : configuredVoiceId.Trim();
            if (string.Equals(apiKey, nextApiKey, StringComparison.Ordinal) &&
                string.Equals(voiceId, nextVoiceId, StringComparison.Ordinal))
            {
                return;
            }

            apiKey = nextApiKey;
            voiceId = nextVoiceId;
            IsReady = !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(voiceId);
            Status = IsReady
                ? "ElevenLabs Multilingual v2 is ready."
                : "Enter an ElevenLabs API key and voice ID in Setup.";
        }

        public void Initialize()
        {
            IsSupported = true;
            IsReady = !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(voiceId);
            Status = IsReady
                ? "ElevenLabs Multilingual v2 is ready."
                : "Enter an ElevenLabs API key and voice ID in Setup.";
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
            Status = "ElevenLabs speech completed.";
            UtteranceCompleted?.Invoke(completedId);
        }

        public bool Speak(string text, string utteranceId)
        {
            if (!IsReady)
            {
                Status = "Enter an ElevenLabs API key and voice ID in Setup.";
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

            var payload = new SpeechRequest
            {
                text = text.Trim(),
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
            return true;
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
            Status = "Seeking within the current ElevenLabs sentence.";
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
                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                var responseCode = request.responseCode;
                var requestError = string.IsNullOrWhiteSpace(request.error) ? "unknown request error" : request.error;
                var responseDetail = string.Empty;
                if (request.downloadHandler?.data != null && request.downloadHandler.data.Length > 0)
                {
                    responseDetail = Encoding.UTF8.GetString(request.downloadHandler.data).Trim();
                    if (responseDetail.Length > 320)
                    {
                        responseDetail = responseDetail.Substring(0, 320) + "...";
                    }
                }

                Status = $"ElevenLabs speech request failed ({responseCode}): {requestError}";
                if (!string.IsNullOrWhiteSpace(responseDetail))
                {
                    Status += " " + responseDetail;
                }
                Debug.LogWarning(Status);
                request.Dispose();
                UtteranceFailed?.Invoke(failedId, Status);
                return;
            }

            try
            {
                activeClip = DownloadHandlerAudioClip.GetContent(request);
            }
            catch (Exception ex)
            {
                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = "ElevenLabs audio decoding failed: " + ex.Message;
                Debug.LogWarning(Status);
                request.Dispose();
                UtteranceFailed?.Invoke(failedId, Status);
                return;
            }

            request.Dispose();
            if (activeClip == null || audioSource == null)
            {
                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                Status = "ElevenLabs returned no playable audio.";
                Debug.LogWarning(Status);
                ReleaseActiveClip();
                UtteranceFailed?.Invoke(failedId, Status);
                return;
            }

            audioSource.clip = activeClip;
            audioSource.Play();
            playbackStarted = true;
            Status = "Speaking with ElevenLabs Multilingual v2.";
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
        private sealed class VoiceSettings
        {
            public float stability;
            public float similarity_boost;
            public float style;
            public bool use_speaker_boost;
            public float speed;
        }
    }
}

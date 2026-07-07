using System;
using System.Collections.Concurrent;
using System.Text;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Diagnostics;
#endif

namespace MemPalaceLLM
{
    /// <summary>
    /// Small system-TTS bridge for the experiment's supported study targets.
    /// Windows uses the installed SAPI voice through Windows PowerShell.
    /// Android/Quest uses android.speech.tts.TextToSpeech.
    /// </summary>
    public sealed class SystemTextToSpeechService : IDisposable
    {
        public event Action<string> UtteranceCompleted;
        public event Action<string, string> UtteranceFailed;

        public bool IsSupported { get; private set; }
        public bool IsReady { get; private set; }
        public bool IsSpeaking => !string.IsNullOrWhiteSpace(activeUtteranceId);
        public string Status { get; private set; } = "Text-to-speech has not been initialized.";

        private string activeUtteranceId = string.Empty;
        private string pendingText = string.Empty;
        private string pendingUtteranceId = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private Process windowsSpeechProcess;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject androidTts;
        private AndroidJavaObject androidActivity;
        private AndroidInitListener androidInitListener;
        private AndroidCompletionListener androidCompletionListener;
        private readonly ConcurrentQueue<int> androidInitResults = new();
        private readonly ConcurrentQueue<string> androidCompletedUtterances = new();
#endif

        public void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InitializeAndroid();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IsSupported = true;
            IsReady = true;
            Status = "Windows system voice is ready.";
#else
            IsSupported = false;
            IsReady = false;
            Status = "System speech is supported on Windows and Android/Quest builds.";
#endif
        }

        public void Tick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            TickAndroid();
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            TickWindows();
#endif

            if (IsReady && !string.IsNullOrWhiteSpace(pendingText) && !IsSpeaking)
            {
                var text = pendingText;
                var utteranceId = pendingUtteranceId;
                pendingText = string.Empty;
                pendingUtteranceId = string.Empty;
                StartSpeech(text, utteranceId);
            }
        }

        public bool Speak(string text, string utteranceId)
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var resolvedId = string.IsNullOrWhiteSpace(utteranceId)
                ? "speech_" + Guid.NewGuid().ToString("N")
                : utteranceId.Trim();

            Stop();
            if (!IsReady)
            {
                pendingText = text.Trim();
                pendingUtteranceId = resolvedId;
                Status = "Waiting for the system voice to initialize.";
                return true;
            }

            return StartSpeech(text.Trim(), resolvedId);
        }

        public void Stop()
        {
            pendingText = string.Empty;
            pendingUtteranceId = string.Empty;
            activeUtteranceId = string.Empty;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (androidTts != null)
            {
                try
                {
                    androidTts.Call<int>("stop");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("Android TTS stop failed: " + ex.Message);
                }
            }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (windowsSpeechProcess != null)
            {
                try
                {
                    if (!windowsSpeechProcess.HasExited)
                    {
                        windowsSpeechProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("Windows TTS stop failed: " + ex.Message);
                }
                finally
                {
                    windowsSpeechProcess.Dispose();
                    windowsSpeechProcess = null;
                }
            }
#endif
        }

        public void Dispose()
        {
            Stop();

#if UNITY_ANDROID && !UNITY_EDITOR
            if (androidTts != null)
            {
                try
                {
                    androidTts.Call("shutdown");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("Android TTS shutdown failed: " + ex.Message);
                }

                androidTts.Dispose();
                androidTts = null;
            }

            androidActivity?.Dispose();
            androidActivity = null;
#endif
        }

        private bool StartSpeech(string text, string utteranceId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return StartAndroidSpeech(text, utteranceId);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return StartWindowsSpeech(text, utteranceId);
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private bool StartWindowsSpeech(string text, string utteranceId)
        {
            try
            {
                var textBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                var script =
                    "$text=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + textBase64 + "'));" +
                    "$voice=New-Object -ComObject SAPI.SpVoice;" +
                    "$voice.Rate=0;$voice.Volume=100;[void]$voice.Speak($text);";
                var commandBase64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -EncodedCommand " + commandBase64,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                windowsSpeechProcess = Process.Start(startInfo);
                if (windowsSpeechProcess == null)
                {
                    Status = "Windows could not start the system voice process.";
                    UtteranceFailed?.Invoke(utteranceId, Status);
                    return false;
                }

                activeUtteranceId = utteranceId;
                Status = "Speaking with the Windows system voice.";
                return true;
            }
            catch (Exception ex)
            {
                Status = "Windows text-to-speech failed: " + ex.Message;
                UtteranceFailed?.Invoke(utteranceId, Status);
                return false;
            }
        }

        private void TickWindows()
        {
            if (windowsSpeechProcess == null || string.IsNullOrWhiteSpace(activeUtteranceId))
            {
                return;
            }

            try
            {
                if (!windowsSpeechProcess.HasExited)
                {
                    return;
                }

                var completedId = activeUtteranceId;
                var exitCode = windowsSpeechProcess.ExitCode;
                windowsSpeechProcess.Dispose();
                windowsSpeechProcess = null;
                activeUtteranceId = string.Empty;

                if (exitCode == 0)
                {
                    Status = "Speech completed.";
                    UtteranceCompleted?.Invoke(completedId);
                }
                else
                {
                    Status = "Windows system voice exited with code " + exitCode + ".";
                    UtteranceFailed?.Invoke(completedId, Status);
                }
            }
            catch (Exception ex)
            {
                var failedId = activeUtteranceId;
                activeUtteranceId = string.Empty;
                windowsSpeechProcess?.Dispose();
                windowsSpeechProcess = null;
                Status = "Windows text-to-speech monitoring failed: " + ex.Message;
                UtteranceFailed?.Invoke(failedId, Status);
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private void InitializeAndroid()
        {
            try
            {
                IsSupported = true;
                IsReady = false;
                Status = "Initializing the Android system voice.";
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                androidActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                androidInitListener = new AndroidInitListener(status => androidInitResults.Enqueue(status));
                androidCompletionListener = new AndroidCompletionListener(id => androidCompletedUtterances.Enqueue(id));
                androidTts = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    androidActivity,
                    androidInitListener);
            }
            catch (Exception ex)
            {
                IsSupported = false;
                IsReady = false;
                Status = "Android text-to-speech initialization failed: " + ex.Message;
                UnityEngine.Debug.LogWarning(Status);
            }
        }

        private void TickAndroid()
        {
            while (androidInitResults.TryDequeue(out var result))
            {
                if (result != 0 || androidTts == null)
                {
                    IsReady = false;
                    Status = "The Android text-to-speech engine is unavailable.";
                    continue;
                }

                try
                {
                    using var locale = new AndroidJavaObject("java.util.Locale", "en", "US");
                    androidTts.Call<int>("setLanguage", locale);
                    androidTts.Call<int>("setSpeechRate", 0.92f);
                    androidTts.Call<int>("setPitch", 1.0f);
                    androidTts.Call<int>("setOnUtteranceCompletedListener", androidCompletionListener);
                    IsReady = true;
                    Status = "Android system voice is ready.";
                }
                catch (Exception ex)
                {
                    IsReady = false;
                    Status = "Android text-to-speech setup failed: " + ex.Message;
                }
            }

            while (androidCompletedUtterances.TryDequeue(out var completedId))
            {
                if (string.IsNullOrWhiteSpace(completedId) ||
                    !string.Equals(completedId, activeUtteranceId, StringComparison.Ordinal))
                {
                    continue;
                }

                activeUtteranceId = string.Empty;
                Status = "Speech completed.";
                UtteranceCompleted?.Invoke(completedId);
            }
        }

        private bool StartAndroidSpeech(string text, string utteranceId)
        {
            if (androidTts == null || !IsReady)
            {
                return false;
            }

            try
            {
                using var parameters = new AndroidJavaObject("android.os.Bundle");
                parameters.Call("putString", "utteranceId", utteranceId);
                var result = androidTts.Call<int>("speak", text, 0, parameters, utteranceId);
                if (result < 0)
                {
                    Status = "Android rejected the speech request.";
                    UtteranceFailed?.Invoke(utteranceId, Status);
                    return false;
                }

                activeUtteranceId = utteranceId;
                Status = "Speaking with the Android system voice.";
                return true;
            }
            catch (Exception ex)
            {
                Status = "Android text-to-speech failed: " + ex.Message;
                UtteranceFailed?.Invoke(utteranceId, Status);
                return false;
            }
        }

        private sealed class AndroidInitListener : AndroidJavaProxy
        {
            private readonly Action<int> callback;

            public AndroidInitListener(Action<int> callback)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                this.callback = callback;
            }

            public void onInit(int status)
            {
                callback?.Invoke(status);
            }
        }

        private sealed class AndroidCompletionListener : AndroidJavaProxy
        {
            private readonly Action<string> callback;

            public AndroidCompletionListener(Action<string> callback)
                : base("android.speech.tts.TextToSpeech$OnUtteranceCompletedListener")
            {
                this.callback = callback;
            }

            public void onUtteranceCompleted(string utteranceId)
            {
                callback?.Invoke(utteranceId);
            }
        }
#endif
    }
}

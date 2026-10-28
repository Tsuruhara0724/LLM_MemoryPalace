using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace MemPalaceLLM
{
    public sealed class OllamaLlmService
    {
        private const int ErrorPreviewLength = 800;
        private const int RequestTimeoutSeconds = 360;
        private const int GeminiServerAttemptCount = 2;
        private readonly List<string> geminiModelsUsed = new List<string>();
        private readonly string claudeApiKey;
        private readonly string claudeEndpoint;
        private readonly string claudeAuthMode;
        private readonly string openAiApiKey;
        private readonly string openAiEndpoint;
        private readonly string openAiReasoningEffort;

        public string GeminiModelsUsedSummary => string.Join(", ", geminiModelsUsed);

        public OllamaLlmService(
            string claudeApiKey = "",
            string claudeEndpoint = "",
            string claudeAuthMode = "bearer",
            string openAiApiKey = "",
            string openAiEndpoint = "",
            string openAiReasoningEffort = "low")
        {
            this.claudeApiKey = claudeApiKey?.Trim() ?? string.Empty;
            this.claudeEndpoint = claudeEndpoint?.Trim() ?? string.Empty;
            this.claudeAuthMode = string.IsNullOrWhiteSpace(claudeAuthMode)
                ? "bearer"
                : claudeAuthMode.Trim().ToLowerInvariant();
            this.openAiApiKey = openAiApiKey?.Trim() ?? string.Empty;
            this.openAiEndpoint = openAiEndpoint?.Trim() ?? string.Empty;
            this.openAiReasoningEffort = string.IsNullOrWhiteSpace(openAiReasoningEffort)
                ? "low"
                : openAiReasoningEffort.Trim().ToLowerInvariant();
        }

        [Serializable]
        private class OllamaRequestOptions
        {
            public float temperature = 0.2f;
            public int num_predict = -1;
        }

        [Serializable]
        private class OllamaGenerateRequest
        {
            public string model;
            public string prompt;
            public string system;
            public string format;
            public bool stream;
            public OllamaRequestOptions options;
        }

        [Serializable]
        private class OllamaGenerateResponse
        {
            public string model;
            public string response;
            public bool done;
            public string done_reason;
            public string error;
        }

        [Serializable]
        private class ClaudeMessageRequest
        {
            public string model;
            public int max_tokens;
            public float temperature;
            public string system;
            public ClaudeMessage[] messages;
        }

        [Serializable]
        private class ClaudeMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ClaudeMessageResponse
        {
            public string type;
            public string model;
            public ClaudeContentBlock[] content;
            public ClaudeError error;
        }

        [Serializable]
        private class ClaudeContentBlock
        {
            public string type;
            public string text;
        }

        [Serializable]
        private class ClaudeError
        {
            public string type;
            public string message;
        }

        [Serializable]
        private class OpenAiResponseRequest
        {
            public string model;
            public string instructions;
            public string input;
            public int max_output_tokens;
            public bool store;
            public OpenAiReasoning reasoning;
        }

        [Serializable]
        private class OpenAiReasoning
        {
            public string effort;
        }

        [Serializable]
        private class OpenAiResponseEnvelope
        {
            public string @object;
            public string model;
            public OpenAiOutputItem[] output;
            public OpenAiError error;
        }

        [Serializable]
        private class OpenAiOutputItem
        {
            public string type;
            public OpenAiOutputContent[] content;
        }

        [Serializable]
        private class OpenAiOutputContent
        {
            public string type;
            public string text;
        }

        [Serializable]
        private class OpenAiError
        {
            public string type;
            public string message;
        }

        [Serializable]
        private class GeminiGenerateRequest
        {
            public GeminiContent[] contents;
            public GeminiSystemInstruction systemInstruction;
            public GeminiGenerationConfig generationConfig;
        }

        [Serializable]
        private class GeminiGenerationConfig
        {
            public float temperature;
            public int maxOutputTokens;
            public string responseMimeType;
        }

        [Serializable]
        private class GeminiContent
        {
            public string role;
            public GeminiPart[] parts;
        }

        [Serializable]
        private class GeminiSystemInstruction
        {
            public GeminiPart[] parts;
        }

        [Serializable]
        private class GeminiPart
        {
            public string text;
        }

        [Serializable]
        private class GeminiGenerateResponse
        {
            public GeminiCandidate[] candidates;
            public GeminiError error;
        }

        [Serializable]
        private class GeminiCandidate
        {
            public GeminiContent content;
            public string finishReason;
        }

        [Serializable]
        private class GeminiError
        {
            public int code;
            public string message;
            public string status;
        }

        [Serializable]
        private class GeneratedStoryEnvelope
        {
            public string fullStory;
            public GeneratedStoryItem[] items;
        }

        [Serializable]
        private class GeneratedStoryItem
        {
            public string word;
            public int storyOrder;
            public string storySegment;
        }

        [Serializable]
        private class CausalStoryPlanEnvelope
        {
            public string goal;
            public CausalStoryPlanItem[] items;
        }

        [Serializable]
        private class CausalStoryPlanItem
        {
            public string word;
            public string need;
            public string action;
            public string result;
            public string goal_link;
        }

        [Serializable]
        private class GeneratedRoomPlan
        {
            public string roomId;
            public string roomName;
            public string summary;
            public string layoutType;
            public GeneratedRoomAnchor[] anchors;
        }

        [Serializable]
        private class GeneratedRoomAnchor
        {
            public string id;
            public string label;
            public string primitiveShape;
            public string colorHex;
            public Vector3 position;
            public Vector3 scale;
            public Vector3 rotationEuler;
        }

        [Serializable]
        public class FurnitureTemplateSuggestion
        {
            public string label;
            public string category;
            public string primitiveShape;
            public string colorHex;
            public Vector3 scale;
            public float defaultY;
            public float mnemonicYOffset;
            public VisualObjectSpec[] parts;
        }

        [Serializable]
        public class GuidedFurniturePlacementSuggestion
        {
            public string label;
            public Vector3 position;
            public Vector3 scale;
            public Vector3 rotationEuler;
        }

        [Serializable]
        private class GuidedFurnitureLayoutEnvelope
        {
            public GuidedFurniturePlacementSuggestion[] items;
        }

        public IEnumerator GenerateStory(
            string endpoint,
            string model,
            List<WordEntry> words,
            Action<StorySessionData> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                onError?.Invoke("Online generation endpoint is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onError?.Invoke("Online generation model is empty.");
                yield break;
            }

            if (words == null || words.Count == 0)
            {
                onError?.Invoke("No words were provided for story generation.");
                yield break;
            }

            var causalPlanRequest = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildCausalStoryPlanPrompt(words),
                system = MnemonicStoryPrompts.PlannerSystem,
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = 0.28f,
                    num_predict = 1600
                }
            };

            string causalPlanJson = null;
            string causalPlanRequestError = null;
            yield return SendOllamaJsonRequest(
                endpoint.Trim(),
                causalPlanRequest,
                value => causalPlanJson = value,
                error => causalPlanRequestError = error);

            if (!string.IsNullOrWhiteSpace(causalPlanRequestError))
            {
                onError?.Invoke("Online causal-plan request failed: " + causalPlanRequestError);
                yield break;
            }

            if (!TryPrepareCausalStoryPlan(causalPlanJson, words, out var preparedCausalPlanJson, out var causalPlanError))
            {
                // Smaller local models sometimes return only the first array item despite a valid JSON response.
                // The story writer and its repair pass still validate the actual participant-facing output, so
                // use a complete structural plan here rather than abandoning the participant's session.
                Debug.LogWarning("Online causal plan was incomplete; using a structural fallback. " + causalPlanError);
                preparedCausalPlanJson = BuildStructuralFallbackCausalPlan(words);
            }

            causalPlanJson = preparedCausalPlanJson;

            var storyRequest = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildStoryPrompt(words, causalPlanJson),
                system = MnemonicStoryPrompts.WriterSystem,
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = 0.48f,
                    num_predict = 2000
                }
            };

            string storyResponse = null;
            string storyRequestError = null;
            yield return SendOllamaJsonRequest(
                endpoint.Trim(),
                storyRequest,
                value => storyResponse = value,
                error => storyRequestError = error);

            if (!string.IsNullOrWhiteSpace(storyRequestError))
            {
                onError?.Invoke("Online story-writing request failed: " + storyRequestError);
                yield break;
            }

            if (!TryParseAndBuildStoryResponse(storyResponse, words, model.Trim(), out var story, out var validationError))
            {
                var firstFailure = validationError;
                var repairRequest = new OllamaGenerateRequest
                {
                    model = model.Trim(),
                    prompt = BuildStoryRepairPrompt(words, causalPlanJson, storyResponse, firstFailure),
                    system = MnemonicStoryPrompts.RepairSystem,
                    format = "json",
                    stream = false,
                    options = new OllamaRequestOptions
                    {
                        temperature = 0.38f,
                        num_predict = 2200
                    }
                };

                string repairedResponse = null;
                string repairRequestError = null;
                yield return SendOllamaJsonRequest(
                    endpoint.Trim(),
                    repairRequest,
                    value => repairedResponse = value,
                    error => repairRequestError = error);

                if (!string.IsNullOrWhiteSpace(repairRequestError))
                {
                    onError?.Invoke("The first story failed validation (" + firstFailure + ") and the online repair request failed: " + repairRequestError);
                    yield break;
                }

                if (!TryParseAndBuildStoryResponse(repairedResponse, words, model.Trim(), out story, out validationError))
                {
                    onError?.Invoke("Online story repair still failed validation: " + validationError + "\nRaw response preview:\n" + BuildPreview(repairedResponse));
                    yield break;
                }

                story.storySource += "_retry";
            }

            onSuccess?.Invoke(story);
        }

        public IEnumerator GenerateGeminiStory(
            string apiKey,
            string model,
            List<WordEntry> words,
            Action<StorySessionData> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("Gemini API key is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onError?.Invoke("Gemini model is empty.");
                yield break;
            }

            if (words == null || words.Count == 0)
            {
                onError?.Invoke("No words were provided for story generation.");
                yield break;
            }

            geminiModelsUsed.Clear();
            string causalPlanJson = null;
            string requestError = null;
            yield return SendGeminiGenerateRequest(
                apiKey.Trim(),
                model.Trim(),
                BuildCausalStoryPlanPrompt(words),
                MnemonicStoryPrompts.PlannerSystem,
                0.28f,
                1600,
                value => causalPlanJson = value,
                error => requestError = error);

            if (!string.IsNullOrWhiteSpace(requestError))
            {
                onError?.Invoke("Gemini causal-plan request failed: " + requestError);
                yield break;
            }

            if (!TryPrepareCausalStoryPlan(causalPlanJson, words, out var preparedCausalPlanJson, out var causalPlanError))
            {
                Debug.LogWarning("Gemini causal plan was incomplete; using a structural fallback. " + causalPlanError);
                preparedCausalPlanJson = BuildStructuralFallbackCausalPlan(words);
            }

            causalPlanJson = preparedCausalPlanJson;

            string storyResponse = null;
            requestError = null;
            yield return SendGeminiGenerateRequest(
                apiKey.Trim(),
                model.Trim(),
                BuildStoryPrompt(words, causalPlanJson),
                MnemonicStoryPrompts.WriterSystem,
                0.48f,
                2000,
                value => storyResponse = value,
                error => requestError = error);

            if (!string.IsNullOrWhiteSpace(requestError))
            {
                onError?.Invoke("Gemini story-writing request failed: " + requestError);
                yield break;
            }

            var resolvedModel = string.IsNullOrWhiteSpace(GeminiModelsUsedSummary) ? model.Trim() : GeminiModelsUsedSummary;
            if (!TryParseAndBuildStoryResponse(storyResponse, words, resolvedModel, out var story, out var validationError))
            {
                var firstFailure = validationError;
                string repairedResponse = null;
                requestError = null;
                yield return SendGeminiGenerateRequest(
                    apiKey.Trim(),
                    model.Trim(),
                    BuildStoryRepairPrompt(words, causalPlanJson, storyResponse, firstFailure),
                    MnemonicStoryPrompts.RepairSystem,
                    0.38f,
                    2200,
                    value => repairedResponse = value,
                    error => requestError = error);

                if (!string.IsNullOrWhiteSpace(requestError))
                {
                    onError?.Invoke("The first story failed validation (" + firstFailure + ") and the Gemini repair request failed: " + requestError);
                    yield break;
                }

                resolvedModel = string.IsNullOrWhiteSpace(GeminiModelsUsedSummary) ? model.Trim() : GeminiModelsUsedSummary;
                if (!TryParseAndBuildStoryResponse(repairedResponse, words, resolvedModel, out story, out validationError))
                {
                    onError?.Invoke("Gemini story repair still failed validation: " + validationError + "\nRaw response preview:\n" + BuildPreview(repairedResponse));
                    yield break;
                }

                story.storySource += "_retry";
            }

            story.storyProvider = "Gemini Online";
            story.storyModel = resolvedModel;
            story.storySource = string.Equals(story.storySource, "ollama_story_repaired", StringComparison.Ordinal)
                ? "gemini_story_repaired"
                : "gemini_story";
            onSuccess?.Invoke(story);
        }

        private IEnumerator SendOllamaJsonRequest(
            string endpoint,
            OllamaGenerateRequest requestBody,
            Action<string> onSuccess,
            Action<string> onError)
        {
            if (!string.IsNullOrWhiteSpace(openAiApiKey))
            {
                yield return SendOpenAiResponseRequest(
                    string.IsNullOrWhiteSpace(openAiEndpoint) ? endpoint : openAiEndpoint,
                    requestBody,
                    onSuccess,
                    onError);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(claudeApiKey))
            {
                yield return SendClaudeMessageRequest(
                    string.IsNullOrWhiteSpace(claudeEndpoint) ? endpoint : claudeEndpoint,
                    requestBody,
                    onSuccess,
                    onError);
                yield break;
            }

            var json = BuildProviderRequestJson(requestBody);
            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(request.error + "\n" + request.downloadHandler.text);
                yield break;
            }

            OllamaGenerateResponse response;
            try
            {
                response = JsonUtility.FromJson<OllamaGenerateResponse>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to parse the Ollama response envelope: " + ex.Message);
                yield break;
            }

            if (response == null)
            {
                onError?.Invoke("Ollama returned an empty response envelope.");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(response.error))
            {
                onError?.Invoke(response.error);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(response.response))
            {
                onError?.Invoke("Ollama returned empty generated text.");
                yield break;
            }

            onSuccess?.Invoke(response.response.Trim());
        }

        private IEnumerator SendClaudeMessageRequest(
            string endpoint,
            OllamaGenerateRequest source,
            Action<string> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                onError?.Invoke("Claude endpoint is empty.");
                yield break;
            }

            if (source == null || string.IsNullOrWhiteSpace(source.model))
            {
                onError?.Invoke("Claude model is empty.");
                yield break;
            }

            var maxTokens = source.options != null && source.options.num_predict > 0
                ? source.options.num_predict
                : 4096;
            var requestBody = new ClaudeMessageRequest
            {
                model = source.model.Trim(),
                max_tokens = Mathf.Clamp(maxTokens, 64, 16000),
                temperature = source.options == null ? 0.2f : Mathf.Clamp01(source.options.temperature),
                system = source.system ?? string.Empty,
                messages = new[]
                {
                    new ClaudeMessage
                    {
                        role = "user",
                        content = source.prompt ?? string.Empty
                    }
                }
            };

            var json = JsonUtility.ToJson(requestBody);
            using var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("anthropic-version", "2023-06-01");
            if (string.Equals(claudeAuthMode, "x-api-key", StringComparison.OrdinalIgnoreCase))
            {
                request.SetRequestHeader("x-api-key", claudeApiKey);
            }
            else
            {
                request.SetRequestHeader("Authorization", "Bearer " + claudeApiKey);
            }

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(
                    $"Claude request failed (HTTP {request.responseCode}): {request.error}\n" +
                    BuildPreview(request.downloadHandler?.text));
                yield break;
            }

            ClaudeMessageResponse response;
            try
            {
                response = JsonUtility.FromJson<ClaudeMessageResponse>(request.downloadHandler.text);
            }
            catch (Exception ex)
            {
                onError?.Invoke("Failed to parse the Claude response envelope: " + ex.Message);
                yield break;
            }

            if (response?.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                onError?.Invoke(response.error.message.Trim());
                yield break;
            }

            if (response?.content == null || response.content.Length == 0)
            {
                onError?.Invoke("Claude returned an empty response.");
                yield break;
            }

            var text = new StringBuilder();
            foreach (var block in response.content)
            {
                if (block != null && string.Equals(block.type, "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(block.text))
                {
                    if (text.Length > 0)
                    {
                        text.AppendLine();
                    }
                    text.Append(block.text.Trim());
                }
            }

            if (text.Length == 0)
            {
                onError?.Invoke("Claude returned no text content.");
                yield break;
            }

            onSuccess?.Invoke(text.ToString());
        }

        private IEnumerator SendOpenAiResponseRequest(
            string endpoint,
            OllamaGenerateRequest source,
            Action<string> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                onError?.Invoke("GPT_Luna endpoint is empty.");
                yield break;
            }

            if (source == null || string.IsNullOrWhiteSpace(source.model))
            {
                onError?.Invoke("GPT_Luna model is empty.");
                yield break;
            }

            var maxTokens = source.options != null && source.options.num_predict > 0
                ? source.options.num_predict
                : 4096;
            var requestBody = new OpenAiResponseRequest
            {
                model = source.model.Trim(),
                instructions = source.system ?? string.Empty,
                input = source.prompt ?? string.Empty,
                max_output_tokens = Mathf.Clamp(maxTokens, 64, 16000),
                store = false,
                reasoning = new OpenAiReasoning { effort = openAiReasoningEffort }
            };

            var json = JsonUtility.ToJson(requestBody);
            using var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + openAiApiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(
                    $"GPT_Luna request failed (HTTP {request.responseCode}): {request.error}\n" +
                    BuildPreview(request.downloadHandler?.text));
                yield break;
            }

            if (!TryExtractOpenAiText(request.downloadHandler.text, out var generatedText, out var parseError))
            {
                onError?.Invoke(parseError);
                yield break;
            }

            onSuccess?.Invoke(generatedText);
        }

        private static bool TryExtractOpenAiText(string responseJson, out string generatedText, out string error)
        {
            generatedText = string.Empty;
            error = string.Empty;
            OpenAiResponseEnvelope response;
            try
            {
                response = JsonUtility.FromJson<OpenAiResponseEnvelope>(responseJson);
            }
            catch (Exception ex)
            {
                error = "Failed to parse the GPT_Luna response envelope: " + ex.Message;
                return false;
            }

            if (response?.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                error = response.error.message.Trim();
                return false;
            }

            var builder = new StringBuilder();
            if (response?.output != null)
            {
                foreach (var item in response.output)
                {
                    if (item?.content == null)
                    {
                        continue;
                    }
                    foreach (var content in item.content)
                    {
                        if (content != null && !string.IsNullOrWhiteSpace(content.text))
                        {
                            if (builder.Length > 0)
                            {
                                builder.AppendLine();
                            }
                            builder.Append(content.text.Trim());
                        }
                    }
                }
            }

            generatedText = builder.ToString();
            if (string.IsNullOrWhiteSpace(generatedText))
            {
                error = "GPT_Luna returned no text content.";
                return false;
            }
            return true;
        }

        private string BuildProviderRequestJson(OllamaGenerateRequest source)
        {
            if (!string.IsNullOrWhiteSpace(openAiApiKey))
            {
                var openAiMaxTokens = source.options != null && source.options.num_predict > 0
                    ? source.options.num_predict
                    : 4096;
                return JsonUtility.ToJson(new OpenAiResponseRequest
                {
                    model = source.model?.Trim() ?? string.Empty,
                    instructions = source.system ?? string.Empty,
                    input = source.prompt ?? string.Empty,
                    max_output_tokens = Mathf.Clamp(openAiMaxTokens, 64, 16000),
                    store = false,
                    reasoning = new OpenAiReasoning { effort = openAiReasoningEffort }
                });
            }

            if (string.IsNullOrWhiteSpace(claudeApiKey))
            {
                return JsonUtility.ToJson(source);
            }

            var maxTokens = source.options != null && source.options.num_predict > 0
                ? source.options.num_predict
                : 4096;
            return JsonUtility.ToJson(new ClaudeMessageRequest
            {
                model = source.model?.Trim() ?? string.Empty,
                max_tokens = Mathf.Clamp(maxTokens, 64, 16000),
                temperature = source.options == null ? 0.2f : Mathf.Clamp01(source.options.temperature),
                system = source.system ?? string.Empty,
                messages = new[]
                {
                    new ClaudeMessage { role = "user", content = source.prompt ?? string.Empty }
                }
            });
        }

        private void ConfigureProviderRequestHeaders(UnityWebRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(openAiApiKey))
            {
                request.SetRequestHeader("Authorization", "Bearer " + openAiApiKey);
                return;
            }

            if (string.IsNullOrWhiteSpace(claudeApiKey))
            {
                return;
            }

            request.SetRequestHeader("anthropic-version", "2023-06-01");
            if (string.Equals(claudeAuthMode, "x-api-key", StringComparison.OrdinalIgnoreCase))
            {
                request.SetRequestHeader("x-api-key", claudeApiKey);
            }
            else
            {
                request.SetRequestHeader("Authorization", "Bearer " + claudeApiKey);
            }
        }

        private bool TryExtractProviderText(string responseJson, out string generatedText, out string error)
        {
            generatedText = string.Empty;
            error = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(openAiApiKey))
                {
                    return TryExtractOpenAiText(responseJson, out generatedText, out error);
                }
                if (string.IsNullOrWhiteSpace(claudeApiKey))
                {
                    var ollama = JsonUtility.FromJson<OllamaGenerateResponse>(responseJson);
                    if (ollama == null)
                    {
                        error = "The generation service returned an empty response envelope.";
                        return false;
                    }
                    if (!string.IsNullOrWhiteSpace(ollama.error))
                    {
                        error = ollama.error.Trim();
                        return false;
                    }
                    generatedText = ollama.response?.Trim() ?? string.Empty;
                }
                else
                {
                    var claude = JsonUtility.FromJson<ClaudeMessageResponse>(responseJson);
                    if (claude?.error != null && !string.IsNullOrWhiteSpace(claude.error.message))
                    {
                        error = claude.error.message.Trim();
                        return false;
                    }
                    if (claude?.content != null)
                    {
                        var builder = new StringBuilder();
                        foreach (var block in claude.content)
                        {
                            if (block != null && string.Equals(block.type, "text", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(block.text))
                            {
                                if (builder.Length > 0)
                                {
                                    builder.AppendLine();
                                }
                                builder.Append(block.text.Trim());
                            }
                        }
                        generatedText = builder.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Failed to parse the generation response envelope: " + ex.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(generatedText))
            {
                error = "The generation service returned no text content.";
                return false;
            }
            return true;
        }

        private static string BuildCausalStoryPlanPrompt(List<WordEntry> words)
        {
            return MnemonicStoryPrompts.BuildPlan(words);
        }

        private static bool TryPrepareCausalStoryPlan(
            string json,
            List<WordEntry> words,
            out string preparedJson,
            out string error)
        {
            preparedJson = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The causal plan is empty.";
                return false;
            }

            CausalStoryPlanEnvelope plan;
            try
            {
                plan = JsonUtility.FromJson<CausalStoryPlanEnvelope>(NormalizeJsonCandidate(json));
            }
            catch (Exception ex)
            {
                error = "The causal plan JSON could not be parsed: " + ex.Message;
                return false;
            }

            if (plan == null || string.IsNullOrWhiteSpace(plan.goal) || plan.items == null || plan.items.Length != words.Count)
            {
                error = "The plan must contain one goal and exactly one item per target word.";
                return false;
            }

            var expectedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < words.Count; i++)
            {
                expectedWords.Add(words[i].word);
            }

            var usedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < plan.items.Length; i++)
            {
                var item = plan.items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.word) ||
                    string.IsNullOrWhiteSpace(item.need) || string.IsNullOrWhiteSpace(item.action) ||
                    string.IsNullOrWhiteSpace(item.result) || string.IsNullOrWhiteSpace(item.goal_link))
                {
                    error = "Plan item " + (i + 1) + " is missing word, need, action, result, or goal_link.";
                    return false;
                }

                var word = item.word.Trim();
                if (!expectedWords.Contains(word) || !usedWords.Add(word))
                {
                    error = "The plan contains an unknown or repeated word: " + word;
                    return false;
                }

                if (i > 0)
                {
                    var previousResult = NormalizePlanLink(plan.items[i - 1].result);
                    var currentNeed = NormalizePlanLink(item.need);
                    if (!string.Equals(previousResult, currentNeed, StringComparison.Ordinal))
                    {
                        // Smaller local models often preserve the causal meaning while paraphrasing the link.
                        // Canonicalize that field instead of discarding an otherwise complete plan.
                        item.need = plan.items[i - 1].result.Trim();
                    }
                }
            }

            if (usedWords.Count != expectedWords.Count)
            {
                error = "The plan does not contain every selected target word exactly once.";
                return false;
            }

            preparedJson = JsonUtility.ToJson(plan);
            return true;
        }

        private static string BuildStructuralFallbackCausalPlan(List<WordEntry> words)
        {
            var plan = new CausalStoryPlanEnvelope
            {
                goal = "Finish one simple everyday task with a clear result.",
                items = new CausalStoryPlanItem[words.Count]
            };

            var priorResult = "Choose one everyday task that fits all supplied target meanings.";
            for (var i = 0; i < words.Count; i++)
            {
                var entry = words[i];
                var word = string.IsNullOrWhiteSpace(entry?.word) ? "target" : entry.word.Trim();
                var meaning = string.IsNullOrWhiteSpace(entry?.meaning) ? "supplied meaning" : entry.meaning.Trim();
                var isLast = i == words.Count - 1;
                var result = isLast
                    ? "Resolve the chosen everyday goal through the final meaning-faithful event."
                    : "Ground a natural consequence or continuing situation that cues the next event.";

                plan.items[i] = new CausalStoryPlanItem
                {
                    word = word,
                    need = priorResult,
                    action = "Plan one distinct event expressing " + meaning + "; do not force an action or state to become a tool.",
                    result = result,
                    goal_link = isLast
                        ? "Complete the same simple task."
                        : "Keep this episode meaningfully connected to the same task."
                };
                priorResult = result;
            }

            return JsonUtility.ToJson(plan);
        }

        private static string NormalizePlanLink(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string BuildStoryPrompt(List<WordEntry> words, string causalPlanJson)
        {
            return MnemonicStoryPrompts.BuildStory(words, causalPlanJson);
        }

        private static string BuildStoryRepairPrompt(
            List<WordEntry> words,
            string causalPlanJson,
            string rejectedResponse,
            string validationError)
        {
            return MnemonicStoryPrompts.BuildRepair(words, causalPlanJson, rejectedResponse, validationError);
        }

        private static bool TryParseAndBuildStoryResponse(
            string response,
            List<WordEntry> words,
            string model,
            out StorySessionData story,
            out string error)
        {
            story = null;
            if (!TryParseStoryEnvelope(response, out var envelope, out var parseError))
            {
                error = "Failed to parse story JSON: " + parseError;
                return false;
            }

            return TryBuildStorySession(envelope, words, model, out story, out error);
        }

        private static bool TryBuildStorySession(
            GeneratedStoryEnvelope envelope,
            List<WordEntry> words,
            string model,
            out StorySessionData story,
            out string error)
        {
            story = null;
            error = string.Empty;

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.fullStory))
            {
                error = "Story JSON must contain fullStory.";
                return false;
            }

            if (!TryValidateDistinctRouteWords(words, out error))
            {
                return false;
            }

            envelope.fullStory = RepairFullStoryMissingRouteWords(envelope.fullStory, words, out var repairedWords);
            if (repairedWords.Count > 0)
            {
                error = "Story omitted required target annotations: " + string.Join(", ", repairedWords.ConvertAll(word => word.word)) + ".";
                return false;
            }
            var storySource = repairedWords.Count > 0 ? "ollama_story_repaired" : "ollama_story";
            if (ContainsStoryRouteCue(envelope.fullStory, out var routeCueError))
            {
                error = routeCueError;
                return false;
            }

            if (LooksLikeFragmentedObjectScenes(envelope.fullStory, words, out var qualityError))
            {
                error = qualityError;
                return false;
            }

            if (TryBuildOrderedStoryItemsFromResponse(envelope, words, out var ordered, out var strictError))
            {
                story = CreateStorySession(envelope.fullStory, ordered, storySource, model);
                return true;
            }

            var recoveredSource = repairedWords.Count > 0 ? "ollama_story_repaired_recovered" : "ollama_story_recovered";
            if (TryBuildStorySessionFromFullStory(envelope.fullStory, words, model, recoveredSource, out story, out var recoveryError))
            {
                return true;
            }

            error = strictError + " Could not recover story order from fullStory: " + recoveryError;
            return false;
        }

        private static bool TryValidateDistinctRouteWords(List<WordEntry> words, out string error)
        {
            error = string.Empty;
            if (words == null || words.Count == 0)
            {
                error = "Story generation requires at least one selected route word.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i]?.word;
                if (string.IsNullOrWhiteSpace(word))
                {
                    error = "Selected route word " + (i + 1) + " is empty.";
                    return false;
                }

                if (!seen.Add(word.Trim()))
                {
                    error = "Selected route words must be different. Duplicate word: " + word + ".";
                    return false;
                }
            }

            return true;
        }

        private static string RepairFullStoryMissingRouteWords(string fullStory, List<WordEntry> words, out List<WordEntry> repairedWords)
        {
            repairedWords = new List<WordEntry>();
            var repairedStory = string.IsNullOrWhiteSpace(fullStory) ? string.Empty : fullStory.Trim();
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                if (FindStoryWordIndex(repairedStory, word.meaning, word.word) >= 0)
                {
                    continue;
                }

                if (TryAnnotateExistingMeaningMention(ref repairedStory, word.meaning, word.word))
                {
                    continue;
                }

                if (FindStoryWordIndex(repairedStory, word.meaning, word.word) < 0)
                {
                    repairedWords.Add(word);
                }
            }

            if (repairedWords.Count == 0)
            {
                return repairedStory;
            }

            if (repairedStory.Length > 0 && !IsSentenceBoundary(repairedStory[repairedStory.Length - 1]))
            {
                repairedStory += ".";
            }

            for (int i = 0; i < repairedWords.Count; i++)
            {
                if (repairedStory.Length > 0)
                {
                    repairedStory += " ";
                }

                repairedStory += BuildMissingRouteWordRepairSentence(repairedWords[i], i, repairedWords.Count);
            }

            return repairedStory.Trim();
        }

        private static bool TryAnnotateExistingMeaningMention(ref string story, string meaning, string word)
        {
            if (string.IsNullOrWhiteSpace(story) || string.IsNullOrWhiteSpace(meaning) || string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            var escapedMeaning = Regex.Escape(meaning.Trim()).Replace("\\ ", "\\s+");
            var pluralSuffix = meaning.Trim().IndexOf(' ') < 0 && !meaning.Trim().EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? "s?"
                : string.Empty;
            var pattern = @"\b" + escapedMeaning + pluralSuffix + @"\b(?!\s*\()";
            var match = Regex.Match(story, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            var annotated = BuildTargetStoryToken(match.Value, word);
            story = story.Substring(0, match.Index) + annotated + story.Substring(match.Index + match.Length);
            return true;
        }

        private static string BuildMissingRouteWordRepairSentence(WordEntry word, int repairIndex, int repairCount)
        {
            var meaning = string.IsNullOrWhiteSpace(word?.meaning) ? "target meaning" : word.meaning.Trim();
            var spanish = string.IsNullOrWhiteSpace(word?.word) ? "word" : word.word.Trim();
            var phrase = BuildTargetStoryToken(meaning, spanish);
            if (repairCount == 1)
            {
                return "For one bright moment, the " + phrase + " steals the scene and leaves an image too odd to forget.";
            }

            switch (repairIndex % 3)
            {
                case 0:
                    return "The " + phrase + " drifts into view with a strange little flourish, changing the mood of the scene.";
                case 1:
                    return "You notice the " + phrase + " behaving as if it belongs in a dream, vivid enough to stay in memory.";
                default:
                    return "Near the end, the " + phrase + " becomes the one playful detail that makes the whole story easy to picture.";
            }
        }

        private static bool ContainsStoryRouteCue(string fullStory, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return false;
            }

            var disallowedPatterns = new[]
            {
                @"\bnow\s+walk\s+to\b",
                @"\bwalk\s+to\s+the\b",
                @"\byou\s+see\s+a\s+picture\s+of\b",
                @"\bassigned\s+location\b",
                @"\banchor\b",
                @"\bfurniture\b"
            };

            for (int i = 0; i < disallowedPatterns.Length; i++)
            {
                if (Regex.IsMatch(fullStory, disallowedPatterns[i], RegexOptions.IgnoreCase))
                {
                    error = "Story contains route/anchor instruction text. The story must be independent from room anchors.";
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeFragmentedObjectScenes(string fullStory, List<WordEntry> words, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory) || words == null || words.Count < 4)
            {
                return false;
            }

            var narrativeOnly = StripRouteCueSentences(fullStory);
            var sentences = SplitStorySentences(narrativeOnly);
            var targetSentenceCount = 0;
            var weakBeatCount = 0;
            for (int i = 0; i < sentences.Count; i++)
            {
                var sentence = sentences[i];
                if (!ContainsAnyTargetWordPair(sentence, words))
                {
                    continue;
                }

                targetSentenceCount++;
                if (StartsLikeFragmentedObjectBeat(sentence, words))
                {
                    weakBeatCount++;
                }
            }

            var targetThreshold = Mathf.Min(5, Mathf.Max(3, words.Count - 2));
            var weakThreshold = Mathf.Max(4, words.Count / 2);
            if (targetSentenceCount >= targetThreshold && weakBeatCount >= weakThreshold)
            {
                error = "Story reads like separate object scenes rather than one continuous story. Regenerate with a stronger single-premise narrative.";
                return true;
            }

            return false;
        }

        private static string StripRouteCueSentences(string story)
        {
            if (string.IsNullOrWhiteSpace(story))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(
                story,
                @"\bNow\s+walk\s+to\s+the\s+[^.?!]+[.?!]\s*You\s+see\s+a\s+picture\s+of\s+[^.?!]+[.?!]",
                " ",
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            return cleaned.Trim();
        }

        private static List<string> SplitStorySentences(string story)
        {
            var sentences = new List<string>();
            if (string.IsNullOrWhiteSpace(story))
            {
                return sentences;
            }

            var matches = Regex.Matches(story, @"[^.!?]+[.!?]?");
            for (int i = 0; i < matches.Count; i++)
            {
                var sentence = matches[i].Value.Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    sentences.Add(sentence);
                }
            }

            return sentences;
        }

        private static bool ContainsAnyTargetWordPair(string sentence, List<WordEntry> words)
        {
            if (string.IsNullOrWhiteSpace(sentence) || words == null)
            {
                return false;
            }

            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                if (word != null && FindStoryWordIndex(sentence, word.meaning, word.word) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StartsLikeFragmentedObjectBeat(string sentence, List<WordEntry> words)
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                return false;
            }

            var trimmed = sentence.Trim();
            if (Regex.IsMatch(trimmed, @"^(Next|Then|Finally|Lastly|After that|At last)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (Regex.IsMatch(trimmed, @"^You\s+(notice|see|spot|find|observe|reach|enter|go|walk|grab)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            for (int i = 0; i < words.Count; i++)
            {
                var meaning = words[i]?.meaning;
                if (string.IsNullOrWhiteSpace(meaning))
                {
                    continue;
                }

                var escapedMeaning = Regex.Escape(meaning.Trim()).Replace("\\ ", "\\s+");
                if (Regex.IsMatch(trimmed, @"^(A|An|The|Your)\s+" + escapedMeaning + @"\b", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseStoryEnvelope(string rawText, out GeneratedStoryEnvelope envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            var cleaned = NormalizeJsonCandidate(rawText);
            try
            {
                envelope = JsonUtility.FromJson<GeneratedStoryEnvelope>(cleaned);
                if (envelope != null && !string.IsNullOrWhiteSpace(envelope.fullStory))
                {
                    return true;
                }

                error = "Parsed JSON did not contain fullStory.";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (TryRecoverStoryEnvelopeFromRawText(rawText, out envelope, out var recoveryError))
            {
                return true;
            }

            error = error + " Recovery failed: " + recoveryError;
            return false;
        }

        private static bool TryRecoverStoryEnvelopeFromRawText(string rawText, out GeneratedStoryEnvelope envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            if (!TryExtractJsonStringProperty(rawText, "fullStory", out var fullStory))
            {
                error = "Could not extract fullStory from malformed JSON.";
                return false;
            }

            envelope = new GeneratedStoryEnvelope
            {
                fullStory = fullStory,
                items = Array.Empty<GeneratedStoryItem>()
            };
            return true;
        }

        private static bool TryExtractJsonStringProperty(string rawText, string propertyName, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(rawText) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            var pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>.*?)(?=\"\\s*,\\s*\"[A-Za-z_][A-Za-z0-9_]*\"|\"\\s*})";
            var match = Regex.Match(rawText, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                return false;
            }

            value = UnescapeJsonString(match.Groups["value"].Value).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\\"", "\"")
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\\t", " ")
                .Replace("\\\\", "\\");
        }

        private static bool TryBuildOrderedStoryItemsFromResponse(
            GeneratedStoryEnvelope envelope,
            List<WordEntry> words,
            out List<WordImageItemData> ordered,
            out string error)
        {
            ordered = null;
            error = string.Empty;

            if (envelope.items == null || envelope.items.Length == 0)
            {
                error = "Story JSON must contain non-empty items.";
                return false;
            }

            var remainingByWord = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < words.Count; i++)
            {
                remainingByWord[words[i].word] = i;
            }

            ordered = new List<WordImageItemData>();
            var usedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < envelope.items.Length; i++)
            {
                var generated = envelope.items[i];
                if (generated == null || string.IsNullOrWhiteSpace(generated.word))
                {
                    error = "Story item " + (i + 1) + " is missing word.";
                    return false;
                }

                var wordKey = generated.word.Trim();
                if (!remainingByWord.TryGetValue(wordKey, out var sourceIndex))
                {
                    error = "Story item uses an unknown word: " + generated.word;
                    return false;
                }

                if (!usedWords.Add(wordKey))
                {
                    error = "Story item repeats word: " + generated.word;
                    return false;
                }

                var sourceWord = words[sourceIndex];
                if (!TryResolveStoryBeat(envelope.fullStory, generated.storySegment, sourceWord, words, out var storyBeat))
                {
                    error = "Could not resolve a story beat for " + sourceWord.word + " from fullStory.";
                    return false;
                }

                ordered.Add(new WordImageItemData
                {
                    word = sourceWord.word,
                    meaning = sourceWord.meaning,
                    anchorId = string.Empty,
                    anchorLabel = string.Empty,
                    anchorType = string.Empty,
                    storyOrder = generated.storyOrder <= 0 ? i + 1 : generated.storyOrder,
                    storySegment = BuildStorySegment(sourceWord, storyBeat, words, generated.storyOrder <= 0 ? i : generated.storyOrder - 1, words.Count),
                    imageResourcePath = WordImageCatalog.BuildResourcePath(sourceWord.word),
                    imageFilePath = string.Empty,
                    imageLoaded = false
                });
            }

            if (usedWords.Count != words.Count)
            {
                error = "Story JSON must include each selected word exactly once.";
                return false;
            }

            SortStoryItemsByFullStoryOccurrence(envelope.fullStory, ordered);
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].storyOrder = i + 1;
            }

            return true;
        }

        private static void SortStoryItemsByFullStoryOccurrence(string fullStory, List<WordImageItemData> ordered)
        {
            if (ordered == null || ordered.Count <= 1 || string.IsNullOrWhiteSpace(fullStory))
            {
                ordered?.Sort((a, b) => a.storyOrder.CompareTo(b.storyOrder));
                return;
            }

            ordered.Sort((a, b) =>
            {
                var aIndex = FindStoryWordIndex(fullStory, a?.meaning, a?.word);
                var bIndex = FindStoryWordIndex(fullStory, b?.meaning, b?.word);
                if (aIndex < 0 && bIndex < 0)
                {
                    return a.storyOrder.CompareTo(b.storyOrder);
                }

                if (aIndex < 0)
                {
                    return 1;
                }

                if (bIndex < 0)
                {
                    return -1;
                }

                var indexCompare = aIndex.CompareTo(bIndex);
                return indexCompare != 0 ? indexCompare : a.storyOrder.CompareTo(b.storyOrder);
            });
        }

        private static bool TryResolveStoryBeat(
            string fullStory,
            string generatedSegment,
            WordEntry sourceWord,
            List<WordEntry> allWords,
            out string storyBeat)
        {
            storyBeat = ExtractStoryBeatText(generatedSegment);
            if (ContainsMeaningWordPair(storyBeat, sourceWord.meaning, sourceWord.word))
            {
                return true;
            }

            var storyIndex = FindStoryWordIndex(fullStory, sourceWord.meaning, sourceWord.word);
            if (storyIndex < 0)
            {
                storyBeat = string.Empty;
                return false;
            }

            storyBeat = ExtractStorySentence(fullStory, storyIndex);
            if (IsRoutePictureSentence(storyBeat))
            {
                var followingSentence = ExtractFollowingStorySentence(fullStory, storyIndex);
                if (!string.IsNullOrWhiteSpace(followingSentence))
                {
                    storyBeat = followingSentence;
                    return true;
                }
            }

            if (!ContainsMeaningWordPair(storyBeat, sourceWord.meaning, sourceWord.word))
            {
                var normalizedBeat = NormalizeStoryBeatTargetToken(storyBeat, sourceWord);
                storyBeat = ContainsMeaningWordPair(normalizedBeat, sourceWord.meaning, sourceWord.word)
                    ? normalizedBeat
                    : EnsureStoryBeatContainsTargetToken(storyBeat, sourceWord);
            }

            return true;
        }

        private static string ExtractStoryBeatText(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return string.Empty;
            }

            const string marker = "Story beat:";
            var markerIndex = segment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return markerIndex >= 0
                ? segment.Substring(markerIndex + marker.Length).Trim()
                : segment.Trim();
        }

        private static string BuildStorySegment(WordEntry sourceWord, string storyBeat)
        {
            var meaning = string.IsNullOrWhiteSpace(sourceWord?.meaning) ? "target meaning" : sourceWord.meaning.Trim();
            var word = string.IsNullOrWhiteSpace(sourceWord?.word) ? "word" : sourceWord.word.Trim();
            var beat = string.IsNullOrWhiteSpace(storyBeat) ? BuildTargetStoryToken(meaning, word) + "." : storyBeat.Trim();
            return BuildStorySegment(sourceWord, beat, null, -1, -1);
        }

        private static string BuildStorySegment(WordEntry sourceWord, string storyBeat, List<WordEntry> allWords)
        {
            return BuildStorySegment(sourceWord, storyBeat, allWords, -1, allWords?.Count ?? -1);
        }

        private static string BuildStorySegment(WordEntry sourceWord, string storyBeat, List<WordEntry> allWords, int routeIndex, int routeCount)
        {
            var meaning = string.IsNullOrWhiteSpace(sourceWord?.meaning) ? "target meaning" : sourceWord.meaning.Trim();
            var word = string.IsNullOrWhiteSpace(sourceWord?.word) ? "word" : sourceWord.word.Trim();
            var beat = string.IsNullOrWhiteSpace(storyBeat) ? BuildTargetStoryToken(meaning, word) + "." : storyBeat.Trim();
            var normalized = NormalizeStoryBeatTargetToken(beat, sourceWord);
            return LimitStorySegmentToSingleTarget(normalized, sourceWord, allWords, routeIndex, routeCount);
        }

        private static bool TryBuildStorySessionFromFullStory(
            string fullStory,
            List<WordEntry> words,
            string model,
            string source,
            out StorySessionData story,
            out string error)
        {
            story = null;
            error = string.Empty;

            var occurrences = new List<RecoveredStoryOccurrence>();
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var storyIndex = FindStoryWordIndex(fullStory, word.meaning, word.word);
                if (storyIndex < 0)
                {
                    error = "fullStory does not contain " + BuildTargetStoryToken(word.meaning, word.word) + ".";
                    return false;
                }

                occurrences.Add(new RecoveredStoryOccurrence
                {
                    WordIndex = i,
                    StoryIndex = storyIndex,
                    Segment = ExtractStorySentence(fullStory, storyIndex)
                });
            }

            occurrences.Sort((a, b) => a.StoryIndex.CompareTo(b.StoryIndex));
            var ordered = new List<WordImageItemData>();
            for (int order = 0; order < occurrences.Count; order++)
            {
                var occurrence = occurrences[order];
                var sourceWord = words[occurrence.WordIndex];
                var storyBeat = string.IsNullOrWhiteSpace(occurrence.Segment) ? fullStory.Trim() : occurrence.Segment.Trim();
                if (IsRoutePictureSentence(storyBeat))
                {
                    var followingSentence = ExtractFollowingStorySentence(fullStory, occurrence.StoryIndex);
                    if (!string.IsNullOrWhiteSpace(followingSentence))
                    {
                        storyBeat = followingSentence.Trim();
                    }
                }

                if (!ContainsMeaningWordPair(storyBeat, sourceWord.meaning, sourceWord.word))
                {
                    var normalizedBeat = NormalizeStoryBeatTargetToken(storyBeat, sourceWord);
                    storyBeat = ContainsMeaningWordPair(normalizedBeat, sourceWord.meaning, sourceWord.word)
                        ? normalizedBeat
                        : EnsureStoryBeatContainsTargetToken(storyBeat, sourceWord);
                }

                ordered.Add(new WordImageItemData
                {
                    word = sourceWord.word,
                    meaning = sourceWord.meaning,
                    anchorId = string.Empty,
                    anchorLabel = string.Empty,
                    anchorType = string.Empty,
                    storyOrder = order + 1,
                    storySegment = BuildStorySegment(sourceWord, storyBeat, words, order, occurrences.Count),
                    imageResourcePath = WordImageCatalog.BuildResourcePath(sourceWord.word),
                    imageFilePath = string.Empty,
                    imageLoaded = false
                });
            }

            story = CreateStorySession(fullStory, ordered, source, model);
            return true;
        }

        private static StorySessionData CreateStorySession(
            string fullStory,
            List<WordImageItemData> ordered,
            string source,
            string model)
        {
            return new StorySessionData
            {
                fullStory = NormalizeDisplayStory(NormalizeFullStoryTargetTokens(fullStory, ordered)),
                storySource = source,
                storyProvider = "Ollama Local",
                storyModel = model,
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                orderedItems = ordered
            };
        }

        private static string NormalizeFullStoryTargetTokens(string fullStory, List<WordImageItemData> ordered)
        {
            var normalized = string.IsNullOrWhiteSpace(fullStory) ? string.Empty : fullStory.Trim();
            if (ordered == null)
            {
                return normalized;
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                if (item == null)
                {
                    continue;
                }

                normalized = NormalizeStoryBeatTargetToken(
                    normalized,
                    new WordEntry { word = item.word, meaning = item.meaning });
            }

            return normalized;
        }

        private static string NormalizeDisplayStory(string fullStory)
        {
            return ConvertStoryToSecondPerson(fullStory).Trim();
        }

        private static string ConvertStoryToSecondPerson(string story)
        {
            if (string.IsNullOrWhiteSpace(story))
            {
                return string.Empty;
            }

            var cleaned = story.Trim();
            cleaned = Regex.Replace(cleaned, @"\b([A-Z][a-z]{2,})\s+is\s+getting\s+ready\b", "You are getting ready");
            cleaned = Regex.Replace(cleaned, @"\b([A-Z][a-z]{2,})\s+starts\b", "You start");
            cleaned = Regex.Replace(cleaned, @"\b([A-Z][a-z]{2,})\s+finds\b", "You find");
            cleaned = Regex.Replace(cleaned, @"\b([A-Z][a-z]{2,})\s+adds\b", "You add");
            cleaned = Regex.Replace(cleaned, @"\b([A-Z][a-z]{2,})\s+packs\b", "You pack");
            cleaned = Regex.Replace(cleaned, @"\b[Ss]he\b", m => char.IsUpper(m.Value[0]) ? "You" : "you");
            cleaned = Regex.Replace(cleaned, @"\b[Hh]e\b", m => char.IsUpper(m.Value[0]) ? "You" : "you");
            cleaned = Regex.Replace(cleaned, @"\b[Hh]er\b", m => char.IsUpper(m.Value[0]) ? "Your" : "your");
            cleaned = Regex.Replace(cleaned, @"\b[Hh]is\b", m => char.IsUpper(m.Value[0]) ? "Your" : "your");
            cleaned = Regex.Replace(cleaned, @"\bherself\b", "yourself", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bhimself\b", "yourself", RegexOptions.IgnoreCase);
            return Regex.Replace(cleaned, @"\s+", " ");
        }

        private sealed class RecoveredStoryOccurrence
        {
            public int WordIndex;
            public int StoryIndex;
            public string Segment;
        }

        private sealed class TargetTokenOccurrence
        {
            public WordEntry Word;
            public int Index;
            public int EndIndex;
        }

        private static int FindStoryWordIndex(string fullStory, string meaning, string word)
        {
            if (string.IsNullOrWhiteSpace(fullStory) || string.IsNullOrWhiteSpace(word))
            {
                return -1;
            }

            var canonicalToken = BuildTargetStoryToken(meaning, word);
            var canonicalIndex = fullStory.IndexOf(canonicalToken, StringComparison.OrdinalIgnoreCase);
            if (canonicalIndex >= 0)
            {
                return canonicalIndex;
            }

            if (!string.IsNullOrWhiteSpace(meaning))
            {
                var compactCanonical = word.Trim() + "(" + meaning.Trim() + ")";
                var compactCanonicalIndex = fullStory.IndexOf(compactCanonical, StringComparison.OrdinalIgnoreCase);
                if (compactCanonicalIndex >= 0)
                {
                    return compactCanonicalIndex;
                }

                var starredCompactCanonical = "*" + compactCanonical + "*";
                var starredCompactCanonicalIndex = fullStory.IndexOf(starredCompactCanonical, StringComparison.OrdinalIgnoreCase);
                if (starredCompactCanonicalIndex >= 0)
                {
                    return starredCompactCanonicalIndex;
                }

                var oldMeaningWord = meaning.Trim() + " (" + word.Trim() + ")";
                var oldMeaningWordIndex = fullStory.IndexOf(oldMeaningWord, StringComparison.OrdinalIgnoreCase);
                if (oldMeaningWordIndex >= 0)
                {
                    return oldMeaningWordIndex;
                }

                var compactOld = "*" + meaning.Trim() + "(" + word.Trim() + ")*";
                var compactOldIndex = fullStory.IndexOf(compactOld, StringComparison.OrdinalIgnoreCase);
                if (compactOldIndex >= 0)
                {
                    return compactOldIndex;
                }
            }

            return fullStory.IndexOf("(" + word.Trim() + ")", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTargetStoryToken(string meaning, string word)
        {
            var safeMeaning = string.IsNullOrWhiteSpace(meaning) ? "target meaning" : meaning.Trim();
            var safeWord = string.IsNullOrWhiteSpace(word) ? "word" : word.Trim();
            return safeWord + " (" + safeMeaning + ")";
        }

        private static string LimitStorySegmentToSingleTarget(string storyBeat, WordEntry sourceWord, List<WordEntry> allWords, int routeIndex, int routeCount)
        {
            if (string.IsNullOrWhiteSpace(storyBeat) || sourceWord == null || allWords == null || allWords.Count <= 1)
            {
                return EnsureSentenceEnd(storyBeat ?? string.Empty);
            }

            var normalized = NormalizeStoryBeatTargetToken(storyBeat, sourceWord);
            var occurrences = FindTargetTokenOccurrences(normalized, allWords);
            if (occurrences.Count <= 1)
            {
                var singleSegment = ReplaceOtherTargetTokensWithMeanings(normalized, sourceWord, allWords);
                // A complete single-target sentence is not invalid merely because its
                // opening expresses the goal without the legacy must/need keyword list.
                // Replacing it here would discard the generated meaning-faithful event.
                if (!ContainsMeaningWordPair(singleSegment, sourceWord.meaning, sourceWord.word) ||
                    IsWeakStorySegment(singleSegment))
                {
                    singleSegment = BuildFallbackSingleTargetStorySegment(sourceWord, routeIndex, routeCount);
                }

                return EnsureSentenceEnd(singleSegment);
            }

            var sourceIndex = -1;
            for (var i = 0; i < occurrences.Count; i++)
            {
                if (SameWord(occurrences[i].Word, sourceWord))
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0)
            {
                return EnsureSentenceEnd(EnsureStoryBeatContainsTargetToken(
                    ReplaceOtherTargetTokensWithMeanings(normalized, sourceWord, allWords),
                    sourceWord));
            }

            var source = occurrences[sourceIndex];
            var segment = sourceIndex == 0
                ? normalized.Substring(0, (sourceIndex + 1 < occurrences.Count ? occurrences[sourceIndex + 1].Index : normalized.Length))
                : normalized.Substring(source.Index, (sourceIndex + 1 < occurrences.Count ? occurrences[sourceIndex + 1].Index : normalized.Length) - source.Index);

            segment = ReplaceOtherTargetTokensWithMeanings(segment, sourceWord, allWords);
            segment = TrimDanglingMultiTargetTail(segment);
            if (!ContainsMeaningWordPair(segment, sourceWord.meaning, sourceWord.word) ||
                IsWeakStorySegment(segment) ||
                IsWeakOpeningStorySegment(segment, routeIndex))
            {
                segment = BuildFallbackSingleTargetStorySegment(sourceWord, routeIndex, routeCount);
            }

            if (sourceIndex > 0 && StartsWithTargetToken(segment, sourceWord))
            {
                segment = "You notice the " + segment.TrimStart();
            }

            return EnsureSentenceEnd(segment);
        }

        private static bool IsWeakStorySegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return true;
            }

            var trimmed = segment.Trim();
            var wordCount = Regex.Matches(trimmed, @"\b[A-Za-z]+\b").Count;
            if (wordCount <= 4)
            {
                return true;
            }

            if (Regex.IsMatch(trimmed, @"\b(next problem|story can keep moving|story can continue|plot can continue|keeps? the story|keeps? the plot)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            return Regex.IsMatch(trimmed, @"^\s*(he|she|you|they|mateo|carlos)\s+(heard|saw|noticed|found)\s+it\s*\.?\s*$", RegexOptions.IgnoreCase);
        }

        private static bool IsWeakOpeningStorySegment(string segment, int routeIndex)
        {
            if (routeIndex != 0 || string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }

            return !Regex.IsMatch(
                segment,
                @"\b(must|need|needs|needed|had to|have to|trying to|try to|reach|toward|before|save|help|escape|safe|find|get to)\b",
                RegexOptions.IgnoreCase);
        }

        private static string BuildFallbackSingleTargetStorySegment(WordEntry sourceWord, int routeIndex, int routeCount)
        {
            var meaning = string.IsNullOrWhiteSpace(sourceWord?.meaning) ? "target object" : sourceWord.meaning.Trim();
            var word = string.IsNullOrWhiteSpace(sourceWord?.word) ? "word" : sourceWord.word.Trim();
            return BuildConcreteFallbackStorySegment(meaning, word, routeIndex, routeCount);
        }

        private static string BuildConcreteFallbackStorySegment(string meaning, string word, int routeIndex, int routeCount)
        {
            var token = BuildTargetStoryToken(meaning, word);
            var key = NormalizeFallbackMeaning(meaning);
            var context = BuildFallbackLinearContext(routeIndex, routeCount);
            return context + ", so " + BuildFallbackTargetAction(key, token);
        }

        private static string BuildFallbackLinearContext(int routeIndex, int routeCount)
        {
            var finalIndex = Mathf.Max(0, routeCount - 1);
            if (routeIndex <= 0)
            {
                return "You and your friend must reach a safe gate before nightfall";
            }

            if (routeIndex >= finalIndex)
            {
                return "At the last door, one more barrier keeps you outside";
            }

            switch (routeIndex % 5)
            {
                case 1:
                    return "That first action gets you into a narrow hall, but you cannot see well";
                case 2:
                    return "You move forward, but a closed gate stops you";
                case 3:
                    return "The gate opens into a dusty passage";
                case 4:
                    return "You get through the passage, but your friend loses the path";
                default:
                    return "You find the path, but a broken bridge blocks the river";
            }
        }

        private static string BuildFallbackTargetAction(string key, string token)
        {
            switch (key)
            {
                case "star":
                    return "you follow the " + token + " until you find the next door.";
                case "mirror":
                    return "you check the " + token + " and read the hidden number.";
                case "castle":
                    return "you enter the " + token + " and close the heavy door behind you.";
                case "mask":
                    return "you put on the " + token + " and breathe safely through the dust.";
                case "candle":
                    return "you light the " + token + " and see the stairs.";
                case "drum":
                    return "you beat the " + token + " and your friend hears you.";
                case "cloud":
                    return "the " + token + " covers the bright sun and lets you see the path.";
                case "bell":
                    return "you ring the " + token + " and a guard comes to help.";
                case "flashlight":
                    return "you turn on the " + token + " and see each step.";
                case "flower":
                    return "you give the " + token + " to a scared child, and the child points to a side door.";
                case "crown":
                    return "you show the " + token + " to the guard, and he opens the gate.";
                case "boat":
                    return "you get in the " + token + " and cross the river.";
                default:
                    return "you place the " + token + " under the edge and push it open.";
            }
        }

        private static string NormalizeFallbackMeaning(string meaning)
        {
            var key = Regex.Replace(meaning ?? string.Empty, @"[^a-zA-Z]+", " ").Trim().ToLowerInvariant();
            if (key.Contains("flashlight") || key.Contains("torch"))
            {
                return "flashlight";
            }

            if (key.Contains("star"))
            {
                return "star";
            }

            if (key.Contains("mirror"))
            {
                return "mirror";
            }

            if (key.Contains("castle"))
            {
                return "castle";
            }

            if (key.Contains("mask"))
            {
                return "mask";
            }

            if (key.Contains("candle"))
            {
                return "candle";
            }

            if (key.Contains("drum"))
            {
                return "drum";
            }

            if (key.Contains("cloud"))
            {
                return "cloud";
            }

            if (key.Contains("bell"))
            {
                return "bell";
            }

            if (key.Contains("flower"))
            {
                return "flower";
            }

            if (key.Contains("crown"))
            {
                return "crown";
            }

            if (key.Contains("boat"))
            {
                return "boat";
            }

            return key;
        }

        private static List<TargetTokenOccurrence> FindTargetTokenOccurrences(string text, List<WordEntry> words)
        {
            var occurrences = new List<TargetTokenOccurrence>();
            if (string.IsNullOrWhiteSpace(text) || words == null)
            {
                return occurrences;
            }

            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var index = FindStoryWordIndex(text, word?.meaning, word?.word);
                if (index < 0)
                {
                    continue;
                }

                occurrences.Add(new TargetTokenOccurrence
                {
                    Word = word,
                    Index = index,
                    EndIndex = FindTargetTokenEnd(text, word, index)
                });
            }

            occurrences.Sort((a, b) => a.Index.CompareTo(b.Index));
            return occurrences;
        }

        private static int FindTargetTokenEnd(string text, WordEntry word, int index)
        {
            if (string.IsNullOrWhiteSpace(text) || word == null || index < 0)
            {
                return Mathf.Max(0, index);
            }

            var token = BuildTargetStoryToken(word.meaning, word.word);
            if (index + token.Length <= text.Length &&
                string.Equals(text.Substring(index, token.Length), token, StringComparison.OrdinalIgnoreCase))
            {
                return index + token.Length;
            }

            var wordPair = "(" + (word.word ?? string.Empty).Trim() + ")";
            var pairIndex = text.IndexOf(wordPair, index, StringComparison.OrdinalIgnoreCase);
            return pairIndex >= 0 ? pairIndex + wordPair.Length : index;
        }

        private static string ReplaceOtherTargetTokensWithMeanings(string text, WordEntry sourceWord, List<WordEntry> allWords)
        {
            var cleaned = text ?? string.Empty;
            if (allWords == null)
            {
                return cleaned;
            }

            for (var i = 0; i < allWords.Count; i++)
            {
                var word = allWords[i];
                if (word == null || SameWord(word, sourceWord))
                {
                    continue;
                }

                cleaned = ReplaceTargetTokenWithMeaning(cleaned, word);
            }

            return cleaned;
        }

        private static string ReplaceTargetTokenWithMeaning(string text, WordEntry word)
        {
            if (string.IsNullOrWhiteSpace(text) || word == null)
            {
                return text ?? string.Empty;
            }

            var meaning = string.IsNullOrWhiteSpace(word.meaning) ? "it" : word.meaning.Trim();
            var escapedMeaning = Regex.Escape(meaning).Replace("\\ ", "\\s+");
            var escapedWord = Regex.Escape((word.word ?? string.Empty).Trim());
            var compactToken = Regex.Escape(BuildTargetStoryToken(word.meaning, word.word));

            var cleaned = Regex.Replace(text, compactToken, meaning, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedWord + @"\s*\(\s*\*?" + escapedMeaning + @"\s*\(\s*" + escapedWord + @"\s*\)\*?\s*\)\*?",
                meaning,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedMeaning + @"\s*\(\s*" + escapedWord + @"\s*\)\*?",
                meaning,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedWord + @"\s*\(\s*" + escapedMeaning + @"\s*\)\*?",
                meaning,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedWord + @"\*?\s*\(\s*" + escapedWord + @"\s*\)",
                meaning,
                RegexOptions.IgnoreCase);
            return cleaned;
        }

        private static string TrimDanglingMultiTargetTail(string segment)
        {
            var cleaned = string.IsNullOrWhiteSpace(segment) ? string.Empty : segment.Trim();
            cleaned = Regex.Replace(
                cleaned,
                @"\s+\b(and|with|while|where|which|that|revealing|showing|etched|marked)\b[^.!?]*$",
                string.Empty,
                RegexOptions.IgnoreCase);
            return cleaned.Trim(' ', ',', ';', ':', '-');
        }

        private static bool StartsWithTargetToken(string text, WordEntry word)
        {
            if (string.IsNullOrWhiteSpace(text) || word == null)
            {
                return false;
            }

            return text.TrimStart().StartsWith(
                BuildTargetStoryToken(word.meaning, word.word),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureSentenceEnd(string text)
        {
            var cleaned = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }

            var last = cleaned[cleaned.Length - 1];
            return IsSentenceBoundary(last) ? cleaned : cleaned + ".";
        }

        private static bool SameWord(WordEntry left, WordEntry right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(left.word?.Trim(), right.word?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureStoryBeatContainsTargetToken(string storyBeat, WordEntry sourceWord)
        {
            var beat = string.IsNullOrWhiteSpace(storyBeat) ? string.Empty : storyBeat.Trim();
            var token = BuildTargetStoryToken(sourceWord?.meaning, sourceWord?.word);
            if (string.IsNullOrWhiteSpace(beat))
            {
                return token + ".";
            }

            return token + " " + beat;
        }

        private static string NormalizeStoryBeatTargetToken(string storyBeat, WordEntry sourceWord)
        {
            if (string.IsNullOrWhiteSpace(storyBeat) || sourceWord == null)
            {
                return storyBeat ?? string.Empty;
            }

            var meaning = string.IsNullOrWhiteSpace(sourceWord.meaning) ? "target meaning" : sourceWord.meaning.Trim();
            var word = string.IsNullOrWhiteSpace(sourceWord.word) ? "word" : sourceWord.word.Trim();
            var token = BuildTargetStoryToken(meaning, word);
            var escapedMeaning = Regex.Escape(meaning).Replace("\\ ", "\\s+");
            var escapedWord = Regex.Escape(word);
            var cleaned = storyBeat.Trim();

            cleaned = Regex.Replace(cleaned, @"\*?" + escapedWord + @"\s*\(\s*\*?" + escapedMeaning + @"\s*\(\s*" + escapedWord + @"\s*\)\*?\s*\)\*?", token, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\*?" + escapedMeaning + @"\s*\(\s*" + escapedWord + @"\s*\)\*?", token, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\*?" + escapedWord + @"\s*\(\s*" + escapedMeaning + @"\s*\)\*?", token, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\*?" + escapedWord + @"\*?\s*\(\s*" + escapedMeaning + @"\s*\)", token, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"^\s*" + Regex.Escape(token) + @"\s*(?:[:\uFF1A\-\u2013\u2014])\s*", string.Empty, RegexOptions.IgnoreCase);
            cleaned = cleaned.Replace("*", string.Empty);

            return CollapseRepeatedTargetTokens(cleaned, token, meaning);
        }

        private static string CollapseRepeatedTargetTokens(string text, string token, string meaning)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            {
                return text ?? string.Empty;
            }

            var first = true;
            return Regex.Replace(
                text,
                Regex.Escape(token),
                _ =>
                {
                    if (first)
                    {
                        first = false;
                        return token;
                    }

                    return string.IsNullOrWhiteSpace(meaning) ? "it" : meaning.Trim();
                },
                RegexOptions.IgnoreCase);
        }

        private static string ExtractStorySentence(string fullStory, int index)
        {
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return string.Empty;
            }

            var start = FindSentenceStart(fullStory, index);
            var end = FindSentenceEnd(fullStory, index);

            return fullStory.Substring(start, end - start).Trim();
        }

        private static string ExtractFollowingStorySentence(string fullStory, int index)
        {
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return string.Empty;
            }

            var currentEnd = FindSentenceEnd(fullStory, index);
            if (currentEnd >= fullStory.Length)
            {
                return string.Empty;
            }

            return ExtractStorySentence(fullStory, Mathf.Clamp(currentEnd + 1, 0, fullStory.Length - 1));
        }

        private static bool IsRoutePictureSentence(string sentence)
        {
            return !string.IsNullOrWhiteSpace(sentence)
                && sentence.IndexOf("picture", StringComparison.OrdinalIgnoreCase) >= 0
                && sentence.IndexOf("see", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int FindSentenceStart(string fullStory, int index)
        {
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return 0;
            }

            for (int i = Mathf.Clamp(index - 1, 0, fullStory.Length - 1); i >= 0; i--)
            {
                if (IsSentenceBoundary(fullStory[i]))
                {
                    return Mathf.Clamp(i + 1, 0, fullStory.Length);
                }
            }

            return 0;
        }

        private static int FindSentenceEnd(string fullStory, int index)
        {
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return 0;
            }

            for (int i = Mathf.Clamp(index, 0, fullStory.Length - 1); i < fullStory.Length; i++)
            {
                if (IsSentenceBoundary(fullStory[i]))
                {
                    return Mathf.Clamp(i + 1, 0, fullStory.Length);
                }
            }

            return fullStory.Length;
        }

        private static bool IsSentenceBoundary(char value)
        {
            return value == '.' || value == '!' || value == '?';
        }

        private static bool ContainsMeaningWordPair(string text, string meaning, string word)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            var normalized = text.ToLowerInvariant();
            var compactToken = BuildTargetStoryToken(meaning, word).ToLowerInvariant();
            if (normalized.Contains(compactToken))
            {
                return true;
            }

            var wordPair = "(" + word.Trim().ToLowerInvariant() + ")";
            return normalized.Contains(wordPair)
                   && (string.IsNullOrWhiteSpace(meaning) || normalized.Contains(meaning.Trim().ToLowerInvariant()));
        }

        public IEnumerator GenerateGuidedFurnitureLayout(
            string endpoint,
            string model,
            string roomShape,
            bool hasBathroom,
            bool hasToilet,
            string roomDescription,
            float roomWidth,
            float roomDepth,
            List<string> furnitureRequests,
            Action<List<GuidedFurniturePlacementSuggestion>> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                onError?.Invoke("Ollama endpoint is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onError?.Invoke("Ollama model is empty.");
                yield break;
            }

            if (furnitureRequests == null || furnitureRequests.Count == 0)
            {
                onError?.Invoke("No furniture requests were selected.");
                yield break;
            }

            var requestBody = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildGuidedFurnitureLayoutPrompt(roomShape, hasBathroom, hasToilet, roomDescription, roomWidth, roomDepth, furnitureRequests),
                system = "You are a JSON API for Unity apartment furniture placement. Return exactly one valid JSON object and nothing else. No markdown. No commentary.",
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = 0.42f
                }
            };

            var json = BuildProviderRequestJson(requestBody);

            using (var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 60;
                request.SetRequestHeader("Content-Type", "application/json");
                ConfigureProviderRequestHeaders(request);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Online guided layout request failed: {request.error}\n{request.downloadHandler.text}");
                    yield break;
                }

                if (!TryExtractProviderText(request.downloadHandler.text, out var generatedText, out var providerError))
                {
                    onError?.Invoke("Online guided layout response failed: " + providerError);
                    yield break;
                }

                if (!TryParseGuidedFurnitureLayout(generatedText, out var envelope, out var parseError))
                {
                    string repairedPayload = null;
                    string repairFailure = null;

                    yield return RepairMnemonicJson(
                        endpoint.Trim(),
                        model.Trim(),
                        generatedText,
                        repairedJson => repairedPayload = repairedJson,
                        repairError => repairFailure = repairError);

                    if (!string.IsNullOrWhiteSpace(repairedPayload))
                    {
                        TryParseGuidedFurnitureLayout(repairedPayload, out envelope, out parseError);
                    }

                    if (!string.IsNullOrWhiteSpace(repairFailure))
                    {
                        parseError += $"\nRepair attempt failed: {repairFailure}";
                    }
                }

                if (envelope == null || envelope.items == null || envelope.items.Length == 0)
                {
                    onError?.Invoke("Failed to parse online guided furniture layout JSON.\n" + parseError + "\n\nRaw response preview:\n" + BuildPreview(generatedText));
                    yield break;
                }

                var results = new List<GuidedFurniturePlacementSuggestion>();
                for (int i = 0; i < envelope.items.Length; i++)
                {
                    var item = envelope.items[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.label))
                    {
                        continue;
                    }

                    item.label = item.label.Trim();
                    item.position = ClampVector(
                        item.position,
                        new Vector3(-roomWidth * 0.5f + 0.25f, 0.05f, -roomDepth * 0.5f + 0.25f),
                        new Vector3(roomWidth * 0.5f - 0.25f, 3.2f, roomDepth * 0.5f - 0.25f));
                    if (item.scale != default)
                    {
                        item.scale = ClampVector(item.scale, new Vector3(0.12f, 0.12f, 0.08f), new Vector3(3.2f, 2.8f, 2.6f));
                    }
                    item.rotationEuler = new Vector3(0f, item.rotationEuler.y, 0f);
                    results.Add(item);
                }

                onSuccess?.Invoke(results);
            }
        }

        public IEnumerator GenerateFurnitureTemplate(
            string endpoint,
            string model,
            string furnitureName,
            Action<FurnitureTemplateSuggestion> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                onError?.Invoke("Ollama endpoint is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onError?.Invoke("Ollama model is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(furnitureName))
            {
                onError?.Invoke("Furniture name is empty.");
                yield break;
            }

            var requestBody = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildFurnitureTemplatePrompt(furnitureName),
                system = "You are a JSON API for Unity low-poly furniture templates. Return exactly one valid JSON object and nothing else. No markdown. No commentary.",
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = 0f
                }
            };

            var json = BuildProviderRequestJson(requestBody);

            using (var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 45;
                request.SetRequestHeader("Content-Type", "application/json");
                ConfigureProviderRequestHeaders(request);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                if (!TryExtractProviderText(request.downloadHandler.text, out var generatedText, out var providerError))
                {
                    onError?.Invoke("Online furniture response failed: " + providerError);
                    yield break;
                }

                if (!TryParseFurnitureTemplateSuggestion(generatedText, out var suggestion, out var parseError))
                {
                    onError?.Invoke($"Failed to parse online furniture JSON: {parseError}");
                    yield break;
                }

                NormalizeFurnitureTemplateSuggestion(suggestion, furnitureName);
                onSuccess?.Invoke(suggestion);
            }
        }

        public IEnumerator GenerateRoomSpec(
            string endpoint,
            string model,
            string layoutType,
            string roomDescription,
            Action<RoomSpecDefinition> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                onError?.Invoke("Ollama endpoint is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onError?.Invoke("Ollama model is empty.");
                yield break;
            }

            var requestBody = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildRoomPrompt(layoutType, roomDescription),
                system = "You are a JSON API for Unity room layouts. Return exactly one valid JSON object and nothing else. Use only the requested schema. No markdown. No commentary.",
                format = "json",
                stream = false,
                options = new OllamaRequestOptions()
            };

            var json = BuildProviderRequestJson(requestBody);

            using (var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                ConfigureProviderRequestHeaders(request);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Online room request failed: {request.error}\n{request.downloadHandler.text}");
                    yield break;
                }

                if (!TryExtractProviderText(request.downloadHandler.text, out var generatedText, out var providerError))
                {
                    onError?.Invoke("Online room response failed: " + providerError);
                    yield break;
                }

                if (!TryParseGeneratedRoomPlan(generatedText, out var roomPlan, out var parseError))
                {
                    string repairedPayload = null;
                    string repairFailure = null;

                    yield return RepairMnemonicJson(
                        endpoint.Trim(),
                        model.Trim(),
                        generatedText,
                        repairedJson => repairedPayload = repairedJson,
                        repairError => repairFailure = repairError);

                    if (!string.IsNullOrWhiteSpace(repairedPayload))
                    {
                        TryParseGeneratedRoomPlan(repairedPayload, out roomPlan, out parseError);
                    }

                    if (!string.IsNullOrWhiteSpace(repairFailure))
                    {
                        parseError += $"\nRepair attempt failed: {repairFailure}";
                    }

                    if (roomPlan == null || roomPlan.anchors == null || roomPlan.anchors.Length == 0)
                    {
                        onError?.Invoke(
                            "Failed to parse online room JSON.\n" +
                            parseError + "\n\n" +
                            "Raw response preview:\n" + BuildPreview(generatedText));
                        yield break;
                    }
                }

                var roomSpec = BuildRoomSpecFromPlan(roomPlan, layoutType, roomDescription);
                RoomSpecCatalog.EnsureDefaults(roomSpec);
                onSuccess?.Invoke(roomSpec);
            }
        }

        public IEnumerator GenerateMnemonics(
            string endpoint,
            string model,
            List<WordEntry> words,
            Action<List<MnemonicItemData>> onSuccess,
            Action<string> onError,
            List<AnchorDefinition> assignedAnchors = null)
        {
            onError?.Invoke("Mnemonic generation is disabled in the story-only redesign. Use GenerateStory instead.");
            yield break;
        }

        public IEnumerator GenerateGeminiMnemonics(
            string apiKey,
            string model,
            List<WordEntry> words,
            Action<List<MnemonicItemData>> onSuccess,
            Action<string> onError,
            List<AnchorDefinition> assignedAnchors = null)
        {
            onError?.Invoke("Gemini mnemonic generation is disabled in the story-only redesign. Use GenerateStory instead.");
            yield break;
        }

        public IEnumerator TestGeminiConnection(
            string apiKey,
            string model,
            Action<string> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("Gemini API key is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onError?.Invoke("Gemini model is empty.");
                yield break;
            }

            string response = null;
            string error = null;
            yield return SendGeminiGenerateRequest(
                apiKey.Trim(),
                model.Trim(),
                "Return exactly this JSON object and no extra fields: {\"ok\":true,\"message\":\"ready\"}",
                "You are a JSON connectivity test endpoint. Return valid compact JSON only.",
                0f,
                64,
                text => response = text,
                err => error = err);

            if (!string.IsNullOrWhiteSpace(error))
            {
                onError?.Invoke(error);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(response)
                || response.IndexOf("\"ok\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                onError?.Invoke("Gemini returned a response, but it did not match the JSON test shape.");
                yield break;
            }

            onSuccess?.Invoke("Gemini connection test succeeded.");
        }

        public IEnumerator RegenerateMnemonicItem(
            string endpoint,
            string model,
            WordEntry word,
            int itemIndex,
            int totalWords,
            string anchorId,
            string anchorLabel,
            string rejectedVisualCue,
            string rejectedMnemonic,
            string rejectedImagePrompt,
            string rejectionReason,
            Action<MnemonicItemData> onSuccess,
            Action<string> onError)
        {
            onError?.Invoke("Mnemonic regeneration is disabled in the story-only redesign.");
            yield break;
        }

        public IEnumerator RegenerateGeminiMnemonicItem(
            string apiKey,
            string model,
            WordEntry word,
            int itemIndex,
            int totalWords,
            string anchorId,
            string anchorLabel,
            string rejectedVisualCue,
            string rejectedMnemonic,
            string rejectedImagePrompt,
            string rejectionReason,
            Action<MnemonicItemData> onSuccess,
            Action<string> onError)
        {
            onError?.Invoke("Gemini mnemonic regeneration is disabled in the story-only redesign.");
            yield break;
        }

        private IEnumerator SendGeminiGenerateRequest(
            string apiKey,
            string model,
            string prompt,
            string system,
            float temperature,
            int maxOutputTokens,
            Action<string> onSuccess,
            Action<string> onError)
        {
            var requestBody = new GeminiGenerateRequest
            {
                systemInstruction = new GeminiSystemInstruction
                {
                    parts = new[]
                    {
                        new GeminiPart { text = system }
                    }
                },
                contents = new[]
                {
                    new GeminiContent
                    {
                        role = "user",
                        parts = new[]
                        {
                            new GeminiPart { text = prompt }
                        }
                    }
                },
                generationConfig = new GeminiGenerationConfig
                {
                    temperature = temperature,
                    maxOutputTokens = maxOutputTokens,
                    responseMimeType = "application/json"
                }
            };

            var json = JsonUtility.ToJson(requestBody);
            var modelAttempts = BuildGeminiModelAttempts(model);
            string lastRetryableError = null;
            for (int modelIndex = 0; modelIndex < modelAttempts.Count; modelIndex++)
            {
                var attemptModel = modelAttempts[modelIndex];
                var url = BuildGeminiGenerateUrl(attemptModel);
                for (int attemptIndex = 0; attemptIndex < GeminiServerAttemptCount; attemptIndex++)
                {
                    using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                    {
                        var bodyRaw = Encoding.UTF8.GetBytes(json);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.timeout = RequestTimeoutSeconds;
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.SetRequestHeader("x-goog-api-key", apiKey);

                        yield return request.SendWebRequest();

                        var responseText = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            var requestError = BuildGeminiRequestError(request.error, responseText, attemptModel);
                            if (IsGeminiRetryableServerError(request.error, responseText))
                            {
                                lastRetryableError = requestError;
                                yield return WaitBeforeNextGeminiAttempt(modelIndex, attemptIndex, modelAttempts.Count);
                                continue;
                            }

                            onError?.Invoke(requestError);
                            yield break;
                        }

                        GeminiGenerateResponse response;
                        try
                        {
                            response = JsonUtility.FromJson<GeminiGenerateResponse>(responseText);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke($"Failed to parse Gemini response envelope: {ex.Message}");
                            yield break;
                        }

                        if (response == null)
                        {
                            onError?.Invoke("Gemini response was empty.");
                            yield break;
                        }

                        if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
                        {
                            var responseError = BuildGeminiResponseError(response.error, attemptModel);
                            if (IsGeminiRetryableServerError(responseError, responseText))
                            {
                                lastRetryableError = responseError;
                                yield return WaitBeforeNextGeminiAttempt(modelIndex, attemptIndex, modelAttempts.Count);
                                continue;
                            }

                            onError?.Invoke(responseError);
                            yield break;
                        }

                        var text = ExtractGeminiResponseText(response);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            onError?.Invoke("Gemini response did not contain JSON.");
                            yield break;
                        }

                        RecordGeminiModelUsed(attemptModel);
                        onSuccess?.Invoke(text);
                        yield break;
                    }
                }
            }

            onError?.Invoke(BuildGeminiAllAttemptsFailedError(lastRetryableError, modelAttempts));
        }

        private static string BuildGeminiGenerateUrl(string model)
        {
            var normalizedModel = NormalizeGeminiModelName(model);

            return "https://generativelanguage.googleapis.com/v1beta/models/"
                   + UnityWebRequest.EscapeURL(normalizedModel)
                   + ":generateContent";
        }

        private static string NormalizeGeminiModelName(string model)
        {
            var normalizedModel = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim();
            if (normalizedModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedModel = normalizedModel.Substring("models/".Length);
            }

            return normalizedModel;
        }

        private static List<string> BuildGeminiModelAttempts(string model)
        {
            var attempts = new List<string>();
            var normalizedModel = NormalizeGeminiModelName(model);
            AddGeminiModelAttempt(attempts, normalizedModel);

            if (normalizedModel.IndexOf("pro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddGeminiModelAttempt(attempts, "gemini-2.5-flash");
            }

            AddGeminiModelAttempt(attempts, "gemini-2.5-flash-lite");
            return attempts;
        }

        private static void AddGeminiModelAttempt(List<string> attempts, string model)
        {
            if (attempts == null || string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            var normalizedModel = NormalizeGeminiModelName(model);
            for (int i = 0; i < attempts.Count; i++)
            {
                if (string.Equals(attempts[i], normalizedModel, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            attempts.Add(normalizedModel);
        }

        private void RecordGeminiModelUsed(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            var normalizedModel = NormalizeGeminiModelName(model);
            for (int i = 0; i < geminiModelsUsed.Count; i++)
            {
                if (string.Equals(geminiModelsUsed[i], normalizedModel, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            geminiModelsUsed.Add(normalizedModel);
        }

        private static IEnumerator WaitBeforeNextGeminiAttempt(int modelIndex, int attemptIndex, int modelCount)
        {
            var isLastAttempt = modelIndex >= modelCount - 1 && attemptIndex >= GeminiServerAttemptCount - 1;
            if (isLastAttempt)
            {
                yield break;
            }

            var delaySeconds = 0.8f + (modelIndex * 0.6f) + (attemptIndex * 0.5f);
            yield return new WaitForSecondsRealtime(delaySeconds);
        }

        private static string ExtractGeminiResponseText(GeminiGenerateResponse response)
        {
            if (response?.candidates == null || response.candidates.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < response.candidates.Length; i++)
            {
                var parts = response.candidates[i]?.content?.parts;
                if (parts == null)
                {
                    continue;
                }

                for (int j = 0; j < parts.Length; j++)
                {
                    if (!string.IsNullOrWhiteSpace(parts[j]?.text))
                    {
                        builder.Append(parts[j].text);
                    }
                }
            }

            return builder.ToString();
        }

        private static string BuildGeminiResponseError(GeminiError error, string model)
        {
            if (error == null)
            {
                return "Gemini returned an error.";
            }

            var modelLabel = string.IsNullOrWhiteSpace(model) ? "selected model" : model;
            var details = $"Gemini returned an error from {modelLabel}: {error.code} {error.status} {error.message}".Trim();
            return details;
        }

        private static bool IsGeminiRetryableServerError(string requestError, string responseText)
        {
            var combined = ((requestError ?? string.Empty) + "\n" + (responseText ?? string.Empty)).ToLowerInvariant();
            return combined.Contains("429")
                   || combined.Contains("503")
                   || combined.Contains("too many requests")
                   || combined.Contains("quota")
                   || combined.Contains("unavailable")
                   || combined.Contains("high demand")
                   || combined.Contains("rate limit");
        }

        private static string BuildGeminiAllAttemptsFailedError(string lastRetryableError, List<string> modelAttempts)
        {
            var models = modelAttempts == null || modelAttempts.Count == 0
                ? "selected model"
                : string.Join(" -> ", modelAttempts);
            var hint = "Gemini is rate-limited or temporarily unavailable after retries and fallback (" + models + "). Try again later, or use the pre-generated catalog for covered word-anchor pairs.";
            return string.IsNullOrWhiteSpace(lastRetryableError)
                ? hint
                : hint + "\nLast error:\n" + lastRetryableError;
        }

        private static string BuildGeminiRequestError(string requestError, string responseText, string model = null)
        {
            var preview = BuildPreview(responseText);
            var combined = ((requestError ?? string.Empty) + "\n" + (responseText ?? string.Empty)).ToLowerInvariant();
            var modelLabel = string.IsNullOrWhiteSpace(model) ? "selected model" : model.Trim();
            if (combined.Contains("429")
                || combined.Contains("too many requests")
                || combined.Contains("quota"))
            {
                var hint = "Gemini quota/rate limit was reached on " + modelLabel + ". For a free API key, use gemini-2.5-flash or gemini-2.5-flash-lite; gemini-2.5-pro may require billing or have zero free-tier quota for this key.";
                return string.IsNullOrWhiteSpace(preview)
                    ? hint
                    : hint + "\n" + preview;
            }

            if (combined.Contains("503")
                || combined.Contains("unavailable")
                || combined.Contains("high demand"))
            {
                var hint = "Gemini is temporarily unavailable or under high demand on " + modelLabel + ". The app retries and falls back to gemini-2.5-flash-lite for this request.";
                return string.IsNullOrWhiteSpace(preview)
                    ? hint
                    : hint + "\n" + preview;
            }

            return $"Gemini request failed on {modelLabel}: {requestError}\n{preview}";
        }

        private static string BuildRoomPrompt(string layoutType, string roomDescription)
        {
            var layout = string.IsNullOrWhiteSpace(layoutType) ? "1DK" : layoutType.Trim();
            var description = string.IsNullOrWhiteSpace(roomDescription)
                ? "A compact apartment for a desktop memory-palace experiment with clear furniture anchors."
                : roomDescription.Trim();

            return
                "Generate a compact furniture plan for a Unity memory-palace room.\n" +
                "Unity will add the room shell, walls, floor, ceiling, cameras, and style automatically, so output only anchor furniture.\n\n" +
                "REQUESTED LAYOUT TYPE: " + layout + "\n" +
                "USER DESCRIPTION: " + description + "\n\n" +
                "Layout interpretation:\n" +
                "- The USER DESCRIPTION has highest priority. If it says only one room, single room, open plan, no partitions, or no corridor, obey that exactly.\n" +
                "- Do not force a long corridor unless the requested type or user description explicitly says long corridor / long 1LDK / sequential rooms.\n" +
                "- For Studio, Single Room, Square Studio, Gallery, or Open Plan: keep all anchors inside one continuous room.\n" +
                "- For 1K, 1DK, 1LDK, Loft, L-Shape, or Courtyard: reflect that layout with anchor positions, but still avoid unnecessary partitions.\n\n" +
                "Rules:\n" +
                "- Use a coordinate space roughly x -6 to 6, z -6 to 6, y 0 to 4.\n" +
                "- Create 6 to 12 anchor furniture items, enough for a small memory-palace demo.\n" +
                "- Preserve any specific room order, furniture names, or constraints in the user description.\n" +
                "- Choose labels that fit the described room, such as Door, Desk, Sofa, Bed, Kitchen Counter, Window, Bookshelf, Plant, Television, or Computer.\n" +
                "- Put floor furniture on the floor: y should be roughly half of the object's height.\n" +
                "- Put doors and windows on room walls, not in the center of the room.\n" +
                "- Leave clear walking space and avoid overlapping furniture footprints.\n" +
                "- Anchor ids must be lowercase snake_case and unique.\n" +
                "- primitiveShape must be Cube, Sphere, Capsule, or Cylinder.\n" +
                "- Keep output short. No environmentPrimitives.\n\n" +
                "Return exactly one JSON object matching this schema:\n" +
                "{\n" +
                "  \"roomId\": \"generated_memory_room\",\n" +
                "  \"roomName\": \"Generated Memory Room\",\n" +
                "  \"summary\": \"one sentence room summary\",\n" +
                "  \"layoutType\": \"" + layout + "\",\n" +
                "  \"anchors\": [\n" +
                "    { \"id\": \"desk\", \"label\": \"Desk\", \"primitiveShape\": \"Cube\", \"colorHex\": \"#7B5A3A\", \"position\": { \"x\": -2.2, \"y\": 0.72, \"z\": -1.4 }, \"scale\": { \"x\": 1.5, \"y\": 0.7, \"z\": 0.85 }, \"rotationEuler\": { \"x\": 0.0, \"y\": 0.0, \"z\": 0.0 } }\n" +
                "  ]\n" +
                "}";
        }

        private static string BuildFurnitureTemplatePrompt(string furnitureName)
        {
            var name = string.IsNullOrWhiteSpace(furnitureName) ? "custom furniture" : furnitureName.Trim();
            return
                "Interpret this user-entered furniture name for a Unity memory-palace room builder.\n" +
                "Return one low-poly procedural furniture template. Unity will build the model from the parts array, so all custom furniture must receive concrete parts even if the category is generic.\n\n" +
                "SUPPORTED category values:\n" +
                "toilet, bathtub, sink, fridge, door, window, cabinet, bed, sofa, chair, table, desk, counter, shelf, plant, lamp, television, computer, generic\n\n" +
                "Rules:\n" +
                "- Correct obvious typos when choosing category and label.\n" +
                "- Use a human-readable English label.\n" +
                "- primitiveShape must be Cube, Sphere, Capsule, or Cylinder.\n" +
                "- colorHex must be a muted low-poly material color.\n" +
                "- scale should be plausible in Unity meters for the whole object, within x/y/z 0.2 to 3.0.\n" +
                "- defaultY is the placement center height, usually half the furniture height.\n" +
                "- mnemonicYOffset is the height where a floating memory cue should appear above or near it.\n\n" +
                "Parts rules:\n" +
                "- Return 3 to 8 parts that make the furniture visually recognizable in a simple low-poly style.\n" +
                "- Each part is relative to the whole object, so localPosition should stay within x/y/z -0.7 to 0.7.\n" +
                "- Each part scale is a factor of the whole furniture size and should stay within x/y/z 0.04 to 1.0.\n" +
                "- Avoid text, logos, tiny details, and external assets.\n\n" +
                "Furniture name: " + name + "\n\n" +
                "Return exactly this JSON shape:\n" +
                "{\n" +
                "  \"label\": \"Computer\",\n" +
                "  \"category\": \"computer\",\n" +
                "  \"primitiveShape\": \"Cube\",\n" +
                "  \"colorHex\": \"#2F3440\",\n" +
                "  \"scale\": { \"x\": 1.2, \"y\": 0.9, \"z\": 0.65 },\n" +
                "  \"defaultY\": 0.55,\n" +
                "  \"mnemonicYOffset\": 0.95,\n" +
                "  \"parts\": [\n" +
                "    { \"label\": \"screen\", \"primitiveShape\": \"Cube\", \"colorHex\": \"#101722\", \"localPosition\": { \"x\": 0.0, \"y\": 0.20, \"z\": 0.02 }, \"scale\": { \"x\": 0.72, \"y\": 0.55, \"z\": 0.08 }, \"effect\": \"none\" },\n" +
                "    { \"label\": \"keyboard\", \"primitiveShape\": \"Cube\", \"colorHex\": \"#20242A\", \"localPosition\": { \"x\": 0.0, \"y\": -0.35, \"z\": -0.34 }, \"scale\": { \"x\": 0.70, \"y\": 0.08, \"z\": 0.24 }, \"effect\": \"none\" },\n" +
                "    { \"label\": \"mouse\", \"primitiveShape\": \"Sphere\", \"colorHex\": \"#2D3036\", \"localPosition\": { \"x\": 0.45, \"y\": -0.32, \"z\": -0.30 }, \"scale\": { \"x\": 0.16, \"y\": 0.08, \"z\": 0.22 }, \"effect\": \"none\" }\n" +
                "  ]\n" +
                "}"; 
        }

        private static string BuildGuidedFurnitureLayoutPrompt(
            string roomShape,
            bool hasBathroom,
            bool hasToilet,
            string roomDescription,
            float roomWidth,
            float roomDepth,
            List<string> furnitureRequests)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < furnitureRequests.Count; i++)
            {
                builder.Append(i + 1).Append(". ").Append(furnitureRequests[i]).AppendLine();
            }

            var halfWidth = roomWidth * 0.5f;
            var halfDepth = roomDepth * 0.5f;
            var shapeText = string.IsNullOrWhiteSpace(roomShape) ? "Rectangle" : roomShape.Trim();
            var lShapeConstraint = string.Empty;
            if (shapeText.IndexOf("L", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var sideArmWidth = Mathf.Clamp(roomWidth * 0.46f, 3.0f, roomWidth - 2.7f);
                var backArmDepth = Mathf.Clamp(roomDepth * 0.58f, 3.0f, roomDepth - 2.1f);
                var cutX = -halfWidth + sideArmWidth;
                var cutZ = halfDepth - backArmDepth;
                lShapeConstraint = "- L-Shape footprint: the front-right corner is empty/non-room space. Valid positions must satisfy x <= "
                    + cutX.ToString("F1")
                    + " OR z >= "
                    + cutZ.ToString("F1")
                    + ". Do not place furniture in the missing front-right cutout.\n";
            }

            var bathroomAreaText = shapeText.IndexOf("L", StringComparison.OrdinalIgnoreCase) >= 0
                ? "- Bathroom area, if present, is near the front-left entrance side arm, not in the missing cutout.\n"
                : "- Bathroom area, if present, is near positive x and negative z.\n";

            return
                "Place the selected furniture inside a small apartment memory-palace room.\n" +
                "The user already chose rough zones. Your job is to turn those rough zones into plausible exact positions.\n\n" +
                "ROOM:\n" +
                "- Shape: " + shapeText + "\n" +
                "- Width x Depth: " + roomWidth.ToString("F1") + " x " + roomDepth.ToString("F1") + " Unity meters\n" +
                "- Coordinate bounds: x " + (-halfWidth).ToString("F1") + " to " + halfWidth.ToString("F1") + ", z " + (-halfDepth).ToString("F1") + " to " + halfDepth.ToString("F1") + "\n" +
                "- Front/entrance wall is negative z. Back wall is positive z. Left/right are negative/positive x.\n" +
                lShapeConstraint +
                bathroomAreaText +
                "- Has bathroom: " + (hasBathroom ? "yes" : "no") + "\n" +
                "- Has toilet: " + (hasToilet ? "yes" : "no") + "\n" +
                "- User description: " + (string.IsNullOrWhiteSpace(roomDescription) ? "none" : roomDescription.Trim()) + "\n\n" +
                "SELECTED FURNITURE WITH ROUGH ZONES:\n" + builder +
                "\nPlacement rules:\n" +
                "- Return one item for every selected furniture label, preserving the exact label text.\n" +
                "- Treat the rough zone as a constraint, not as a fixed coordinate. Choose a reasonable non-overlapping exact spot in that zone.\n" +
                "- Large furniture should sit along walls: bed, wardrobe, bookshelf, sofa, desk, dining table, stove, counter.\n" +
                "- Desk and computer should be close. If Desk exists, place Computer on or just in front of the Desk, not alone on the floor.\n" +
                "- Television should face Sofa or Bed when possible; if no stand exists, place it near a wall.\n" +
                "- Air Conditioner should be high on a wall, y around 2.2.\n" +
                "- Window should be on a wall, y around 1.7.\n" +
                "- Toilet must be in Bathroom if Bathroom exists.\n" +
                "- Door should not be returned; Unity already has an entrance door.\n" +
                "- Keep a clear central walking path and avoid stacking large furniture.\n" +
                "- Use y as object center height: floor furniture about 0.4-1.2, wall/window/AC higher.\n" +
                "- Include a plausible scale for each object in Unity meters; keep small objects small and large furniture recognizable.\n" +
                "- rotationEuler.y should orient wall furniture to face inward: left/right wall often 90, front/back wall often 0.\n\n" +
                "Return exactly this JSON shape:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    { \"label\": \"Bed\", \"position\": { \"x\": -3.0, \"y\": 0.45, \"z\": 1.8 }, \"scale\": { \"x\": 2.2, \"y\": 0.45, \"z\": 1.5 }, \"rotationEuler\": { \"x\": 0.0, \"y\": 0.0, \"z\": 0.0 } },\n" +
                "    { \"label\": \"Computer\", \"position\": { \"x\": -2.2, \"y\": 1.15, \"z\": -1.4 }, \"scale\": { \"x\": 1.0, \"y\": 0.7, \"z\": 0.55 }, \"rotationEuler\": { \"x\": 0.0, \"y\": 0.0, \"z\": 0.0 } }\n" +
                "  ]\n" +
                "}";
        }

        private IEnumerator RepairMnemonicJson(
            string endpoint,
            string model,
            string brokenJson,
            Action<string> onSuccess,
            Action<string> onError)
        {
            var requestBody = new OllamaGenerateRequest
            {
                model = model,
                prompt =
                    "Repair the malformed JSON below so that it becomes one valid JSON object only.\n" +
                    "Do not change the intended content unless needed for valid JSON.\n" +
                    "If a visual_objects field is malformed, incomplete, or too complex, replace that field with an empty array [].\n" +
                    "Return only the repaired JSON object.\n\n" +
                    brokenJson,
                system = "You repair malformed JSON. Return exactly one valid JSON object. No markdown. No explanation.",
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = 0f
                }
            };

            yield return SendOllamaJsonRequest(endpoint, requestBody, onSuccess, onError);
        }

        private static bool TryParseGeneratedRoomPlan(string rawText, out GeneratedRoomPlan roomPlan, out string error)
        {
            roomPlan = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "Room-generation response text was empty.";
                return false;
            }

            var candidate = NormalizeJsonCandidate(rawText);

            try
            {
                roomPlan = JsonUtility.FromJson<GeneratedRoomPlan>(candidate);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (roomPlan == null || roomPlan.anchors == null || roomPlan.anchors.Length == 0)
            {
                error = "Parsed room JSON did not contain any anchors.";
                return false;
            }

            return true;
        }

        private static bool TryParseFurnitureTemplateSuggestion(string rawText, out FurnitureTemplateSuggestion suggestion, out string error)
        {
            suggestion = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "Furniture-generation response text was empty.";
                return false;
            }

            var candidate = NormalizeJsonCandidate(rawText);

            try
            {
                suggestion = JsonUtility.FromJson<FurnitureTemplateSuggestion>(candidate);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (suggestion == null)
            {
                error = "Parsed furniture JSON was empty.";
                return false;
            }

            return true;
        }

        private static bool TryParseGuidedFurnitureLayout(string rawText, out GuidedFurnitureLayoutEnvelope envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "Guided-layout response text was empty.";
                return false;
            }

            var candidate = NormalizeJsonCandidate(rawText);

            try
            {
                envelope = JsonUtility.FromJson<GuidedFurnitureLayoutEnvelope>(candidate);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (envelope == null || envelope.items == null || envelope.items.Length == 0)
            {
                error = "Parsed guided layout JSON did not contain any items.";
                return false;
            }

            return true;
        }

        private static RoomSpecDefinition BuildRoomSpecFromPlan(GeneratedRoomPlan plan, string layoutType, string roomDescription)
        {
            var normalizedLayout = string.IsNullOrWhiteSpace(layoutType) ? "1DK" : layoutType.Trim();
            var room = new RoomSpecDefinition
            {
                roomId = string.IsNullOrWhiteSpace(plan.roomId) ? "generated_room_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") : plan.roomId,
                roomName = string.IsNullOrWhiteSpace(plan.roomName) ? $"Generated {normalizedLayout} Memory Room" : plan.roomName,
                generatedBy = "Online AI + Unity layout builder",
                sourcePrompt = roomDescription ?? string.Empty,
                summary = string.IsNullOrWhiteSpace(plan.summary) ? $"Generated {normalizedLayout} memory-palace room." : plan.summary,
                overviewCamera = new CameraPoseDefinition
                {
                    position = new Vector3(0f, 4.2f, -13.2f),
                    eulerAngles = new Vector3(18f, 0f, 0f)
                },
                studyCamera = new CameraPoseDefinition
                {
                    position = new Vector3(0f, 1.75f, -6.2f),
                    eulerAngles = Vector3.zero
                }
            };

            ResolveRoomShellDimensions(normalizedLayout, roomDescription, out var width, out var depth);
            AddRoomShell(room, width, depth, normalizedLayout, roomDescription);

            for (int i = 0; plan.anchors != null && i < plan.anchors.Length && i < 12; i++)
            {
                var source = plan.anchors[i];
                var sourceScale = source.scale == default ? new Vector3(1.1f, 0.7f, 0.7f) : source.scale;
                var clampedScale = ClampVector(sourceScale, new Vector3(0.12f, 0.08f, 0.08f), new Vector3(3.2f, 3f, 2.4f));
                room.anchors.Add(new AnchorDefinition
                {
                    id = string.IsNullOrWhiteSpace(source.id) ? $"anchor_{i + 1}" : source.id,
                    label = string.IsNullOrWhiteSpace(source.label) ? $"Anchor {i + 1}" : source.label,
                    primitiveShape = string.IsNullOrWhiteSpace(source.primitiveShape) ? "Cube" : source.primitiveShape,
                    colorHex = string.IsNullOrWhiteSpace(source.colorHex) ? PickColor(i) : source.colorHex,
                    modelKey = RoomSpecCatalog.ResolveModelKey(source.id, source.label),
                    position = ClampVector(source.position, new Vector3(-width * 0.45f, 0.05f, -depth * 0.45f), new Vector3(width * 0.45f, 3.8f, depth * 0.45f)),
                    scale = clampedScale,
                    rotationEuler = source.rotationEuler,
                    mnemonicOffset = new Vector3(0f, Mathf.Max(0.55f, clampedScale.y * 0.6f + 0.45f), 0f),
                    labelHeight = Mathf.Max(0.8f, clampedScale.y * 0.65f + 0.65f)
                });
            }

            SanitizeGeneratedRoomAnchors(room, width, depth, normalizedLayout, roomDescription);
            return room;
        }

        private static void SanitizeGeneratedRoomAnchors(RoomSpecDefinition room, float width, float depth, string layoutType, string roomDescription)
        {
            if (room?.anchors == null || room.anchors.Count == 0)
            {
                return;
            }

            for (int i = 0; i < room.anchors.Count; i++)
            {
                SanitizeGeneratedAnchor(room.anchors[i], i, width, depth);
            }

            if (IsLongRouteLayout(layoutType, roomDescription))
            {
                ApplyLongRouteSpacing(room.anchors, width, depth);
            }

            ApplyGeneratedInteriorAffinities(room, width, depth, layoutType, roomDescription);
            ResolveGeneratedAnchorOverlaps(room.anchors, width, depth);
            ApplyGeneratedInteriorAffinities(room, width, depth, layoutType, roomDescription);

            for (int i = 0; i < room.anchors.Count; i++)
            {
                SanitizeGeneratedAnchor(room.anchors[i], i, width, depth);
            }
        }

        private static void SanitizeGeneratedAnchor(AnchorDefinition anchor, int index, float width, float depth)
        {
            if (anchor == null)
            {
                return;
            }

            var category = ResolveGeneratedAnchorCategory(anchor);
            anchor.scale = SanitizeGeneratedAnchorScale(category, anchor.scale);

            if (IsWallMountedGeneratedCategory(category))
            {
                SnapGeneratedAnchorToWall(anchor, index, width, depth, category);
            }
            else
            {
                ClampGeneratedFurniturePosition(anchor, width, depth);
                anchor.position = new Vector3(anchor.position.x, Mathf.Max(0.05f, anchor.scale.y * 0.5f), anchor.position.z);
            }

            anchor.mnemonicOffset = new Vector3(
                anchor.mnemonicOffset.x,
                Mathf.Max(0.45f, anchor.scale.y * 0.65f + 0.35f),
                anchor.mnemonicOffset.z);
            anchor.labelHeight = Mathf.Max(0.72f, anchor.scale.y * 0.65f + 0.55f);
        }

        private static string ResolveGeneratedAnchorCategory(AnchorDefinition anchor)
        {
            var text = ((anchor?.label ?? string.Empty) + " " + (anchor?.id ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "door", "entrance", "front door"))
            {
                return "door";
            }

            if (ContainsAny(text, "window", "balcony"))
            {
                return "window";
            }

            return NormalizeFurnitureCategory(string.Empty, text);
        }

        private static Vector3 SanitizeGeneratedAnchorScale(string category, Vector3 scale)
        {
            if (scale == default)
            {
                scale = DefaultFurnitureScale(category);
            }

            if (category == "door")
            {
                return new Vector3(Mathf.Clamp(scale.x, 0.95f, 1.35f), Mathf.Clamp(Mathf.Max(scale.y, 2.0f), 2.0f, 2.55f), Mathf.Clamp(scale.z, 0.08f, 0.18f));
            }

            if (category == "window")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.3f), 1.2f, 2.4f), Mathf.Clamp(Mathf.Max(scale.y, 0.9f), 0.85f, 1.55f), Mathf.Clamp(scale.z, 0.08f, 0.18f));
            }

            if (category == "fridge")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.8f), 0.7f, 1.25f), Mathf.Clamp(Mathf.Max(scale.y, 1.65f), 1.55f, 2.25f), Mathf.Clamp(Mathf.Max(scale.z, 0.65f), 0.55f, 1.0f));
            }

            if (category == "cabinet")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.8f), 0.7f, 1.6f), Mathf.Clamp(Mathf.Max(scale.y, 1.2f), 1.0f, 2.2f), Mathf.Clamp(Mathf.Max(scale.z, 0.45f), 0.35f, 0.9f));
            }

            if (category == "television")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.2f), 1.0f, 1.9f), Mathf.Clamp(Mathf.Max(scale.y, 0.75f), 0.65f, 1.25f), Mathf.Clamp(scale.z, 0.12f, 0.45f));
            }

            if (category == "computer")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.0f), 0.8f, 1.6f), Mathf.Clamp(Mathf.Max(scale.y, 0.7f), 0.6f, 1.2f), Mathf.Clamp(Mathf.Max(scale.z, 0.55f), 0.45f, 1.0f));
            }

            if (category == "bed")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.7f), 1.4f, 2.6f), Mathf.Clamp(Mathf.Max(scale.y, 0.45f), 0.35f, 0.8f), Mathf.Clamp(Mathf.Max(scale.z, 1.2f), 1.0f, 1.9f));
            }

            if (category == "sofa")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.35f), 1.1f, 2.2f), Mathf.Clamp(Mathf.Max(scale.y, 0.65f), 0.55f, 1.1f), Mathf.Clamp(Mathf.Max(scale.z, 0.7f), 0.55f, 1.15f));
            }

            if (category == "table" || category == "desk" || category == "counter")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 1.1f), 0.85f, 2.2f), Mathf.Clamp(Mathf.Max(scale.y, 0.65f), 0.55f, 1.05f), Mathf.Clamp(Mathf.Max(scale.z, 0.7f), 0.55f, 1.5f));
            }

            if (category == "chair")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.55f), 0.45f, 0.95f), Mathf.Clamp(Mathf.Max(scale.y, 0.75f), 0.65f, 1.2f), Mathf.Clamp(Mathf.Max(scale.z, 0.55f), 0.45f, 0.95f));
            }

            if (category == "toilet")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.75f), 0.6f, 1.05f), Mathf.Clamp(Mathf.Max(scale.y, 0.65f), 0.55f, 0.95f), Mathf.Clamp(Mathf.Max(scale.z, 0.85f), 0.65f, 1.2f));
            }

            if (category == "sink")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.9f), 0.75f, 1.35f), Mathf.Clamp(Mathf.Max(scale.y, 0.7f), 0.6f, 1.1f), Mathf.Clamp(Mathf.Max(scale.z, 0.65f), 0.5f, 1.0f));
            }

            if (category == "plant" || category == "lamp")
            {
                return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.35f), 0.3f, 0.8f), Mathf.Clamp(Mathf.Max(scale.y, 0.9f), 0.65f, 1.7f), Mathf.Clamp(Mathf.Max(scale.z, 0.35f), 0.3f, 0.8f));
            }

            return new Vector3(Mathf.Clamp(Mathf.Max(scale.x, 0.55f), 0.35f, 1.8f), Mathf.Clamp(Mathf.Max(scale.y, 0.55f), 0.3f, 1.8f), Mathf.Clamp(Mathf.Max(scale.z, 0.55f), 0.35f, 1.5f));
        }

        private static bool IsWallMountedGeneratedCategory(string category)
        {
            return category == "door" || category == "window";
        }

        private static void SnapGeneratedAnchorToWall(AnchorDefinition anchor, int index, float width, float depth, string category)
        {
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            var position = anchor.position;
            var text = ((anchor.label ?? string.Empty) + " " + (anchor.id ?? string.Empty)).ToLowerInvariant();
            var forceEntranceWall = category == "door" && (index == 0 || ContainsAny(text, "entrance", "front", "main door"));
            var forceBackWall = category == "window" && ContainsAny(text, "balcony", "back", "feature");

            if (forceEntranceWall)
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + anchor.scale.x * 0.6f, halfWidth - anchor.scale.x * 0.6f);
                position.z = -halfDepth + 0.11f;
                position.y = anchor.scale.y * 0.5f;
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
                anchor.position = position;
                return;
            }

            if (forceBackWall)
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + anchor.scale.x * 0.6f, halfWidth - anchor.scale.x * 0.6f);
                position.z = halfDepth - 0.11f;
                position.y = Mathf.Clamp(position.y <= 0.2f ? 1.9f : position.y, 1.25f, 2.75f);
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
                anchor.position = position;
                return;
            }

            var distanceLeft = Mathf.Abs(position.x + halfWidth);
            var distanceRight = Mathf.Abs(halfWidth - position.x);
            var distanceFront = Mathf.Abs(position.z + halfDepth);
            var distanceBack = Mathf.Abs(halfDepth - position.z);
            var nearest = Mathf.Min(Mathf.Min(distanceLeft, distanceRight), Mathf.Min(distanceFront, distanceBack));
            var wallY = category == "window"
                ? Mathf.Clamp(position.y <= 0.2f ? 1.9f : position.y, 1.25f, 2.75f)
                : anchor.scale.y * 0.5f;

            if (Mathf.Approximately(nearest, distanceLeft))
            {
                position.x = -halfWidth + 0.11f;
                position.z = Mathf.Clamp(position.z, -halfDepth + anchor.scale.x * 0.55f, halfDepth - anchor.scale.x * 0.55f);
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
            }
            else if (Mathf.Approximately(nearest, distanceRight))
            {
                position.x = halfWidth - 0.11f;
                position.z = Mathf.Clamp(position.z, -halfDepth + anchor.scale.x * 0.55f, halfDepth - anchor.scale.x * 0.55f);
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
            }
            else if (Mathf.Approximately(nearest, distanceFront))
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + anchor.scale.x * 0.55f, halfWidth - anchor.scale.x * 0.55f);
                position.z = -halfDepth + 0.11f;
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
            }
            else
            {
                position.x = Mathf.Clamp(position.x, -halfWidth + anchor.scale.x * 0.55f, halfWidth - anchor.scale.x * 0.55f);
                position.z = halfDepth - 0.11f;
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
            }

            position.y = wallY;
            anchor.position = position;
        }

        private static bool IsLongRouteLayout(string layoutType, string roomDescription)
        {
            var text = ((layoutType ?? string.Empty) + " " + (roomDescription ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(text, "long", "corridor", "hallway", "linear", "route", "sequential");
        }

        private static void ApplyLongRouteSpacing(List<AnchorDefinition> anchors, float width, float depth)
        {
            var movable = new List<AnchorDefinition>();
            for (int i = 0; i < anchors.Count; i++)
            {
                if (!IsWallMountedGeneratedCategory(ResolveGeneratedAnchorCategory(anchors[i])))
                {
                    movable.Add(anchors[i]);
                }
            }

            for (int i = 0; i < movable.Count; i++)
            {
                var anchor = movable[i];
                var t = (i + 1f) / (movable.Count + 1f);
                var laneIndex = i % 3;
                var lane = laneIndex == 0 ? -width * 0.24f : laneIndex == 1 ? width * 0.24f : 0f;
                var position = anchor.position;
                position.x = Mathf.Abs(position.x) < 0.2f ? lane : Mathf.Lerp(position.x, lane, 0.45f);
                position.z = Mathf.Lerp(-depth * 0.36f, depth * 0.36f, t);
                anchor.position = position;
                ClampGeneratedFurniturePosition(anchor, width, depth);
            }
        }

        private static void ResolveGeneratedAnchorOverlaps(List<AnchorDefinition> anchors, float width, float depth)
        {
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var changed = false;
                for (int i = 0; i < anchors.Count; i++)
                {
                    if (IsWallMountedGeneratedCategory(ResolveGeneratedAnchorCategory(anchors[i])))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < anchors.Count; j++)
                    {
                        if (IsWallMountedGeneratedCategory(ResolveGeneratedAnchorCategory(anchors[j])))
                        {
                            continue;
                        }

                        var a = anchors[i];
                        var b = anchors[j];
                        var delta = new Vector2(a.position.x - b.position.x, a.position.z - b.position.z);
                        var distance = delta.magnitude;
                        var minimumDistance = GetGeneratedFootprintRadius(a) + GetGeneratedFootprintRadius(b);
                        if (distance >= minimumDistance)
                        {
                            continue;
                        }

                        var direction = distance > 0.001f
                            ? delta / distance
                            : new Vector2(Mathf.Cos((i + 1) * 1.37f), Mathf.Sin((j + 1) * 1.91f)).normalized;
                        var push = (minimumDistance - distance) * 0.55f;
                        a.position += new Vector3(direction.x * push, 0f, direction.y * push);
                        b.position -= new Vector3(direction.x * push, 0f, direction.y * push);
                        ClampGeneratedFurniturePosition(a, width, depth);
                        ClampGeneratedFurniturePosition(b, width, depth);
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private static void ApplyGeneratedInteriorAffinities(RoomSpecDefinition room, float width, float depth, string layoutType, string roomDescription)
        {
            var wallPlacedCount = 0;
            for (int i = 0; i < room.anchors.Count; i++)
            {
                var anchor = room.anchors[i];
                var category = ResolveGeneratedAnchorCategory(anchor);
                if (IsWallMountedGeneratedCategory(category) || !ShouldGeneratedPreferWallPlacement(anchor, category))
                {
                    continue;
                }

                SnapGeneratedFurnitureNearWall(anchor, ChooseGeneratedFurnitureWall(anchor, wallPlacedCount, width, depth, layoutType, roomDescription), width, depth);
                wallPlacedCount++;
            }
        }

        private static bool ShouldGeneratedPreferWallPlacement(AnchorDefinition anchor, string category)
        {
            var text = ((anchor?.label ?? string.Empty) + " " + (anchor?.id ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(text, "chair", "dining table", "coffee table", "plant", "lamp"))
            {
                return false;
            }

            return category == "shelf"
                || category == "cabinet"
                || category == "sofa"
                || category == "bed"
                || category == "desk"
                || category == "counter"
                || category == "fridge"
                || category == "sink"
                || category == "toilet"
                || category == "bathtub"
                || category == "television"
                || category == "computer"
                || ContainsAny(text, "display", "bookcase", "wardrobe", "closet");
        }

        private static int ChooseGeneratedFurnitureWall(AnchorDefinition anchor, int wallPlacedCount, float width, float depth, string layoutType, string roomDescription)
        {
            var roomText = ((layoutType ?? string.Empty) + " " + (roomDescription ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(roomText, "gallery", "long", "corridor", "hallway", "linear", "route"))
            {
                return wallPlacedCount % 2 == 0 ? 0 : 1;
            }

            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            var distanceLeft = Mathf.Abs(anchor.position.x + halfWidth);
            var distanceRight = Mathf.Abs(halfWidth - anchor.position.x);
            var distanceBack = Mathf.Abs(halfDepth - anchor.position.z);
            var nearest = Mathf.Min(distanceLeft, Mathf.Min(distanceRight, distanceBack));
            if (nearest < Mathf.Min(width, depth) * 0.28f)
            {
                if (Mathf.Approximately(nearest, distanceLeft))
                {
                    return 0;
                }

                if (Mathf.Approximately(nearest, distanceRight))
                {
                    return 1;
                }

                return 2;
            }

            var pattern = wallPlacedCount % 3;
            return pattern == 0 ? 2 : pattern == 1 ? 0 : 1;
        }

        private static void SnapGeneratedFurnitureNearWall(AnchorDefinition anchor, int wall, float width, float depth)
        {
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            var position = anchor.position;

            if (wall == 0)
            {
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
                var extents = GetGeneratedFootprintExtents(anchor);
                position.x = -halfWidth + extents.x + 0.04f;
                position.z = Mathf.Clamp(position.z, -halfDepth + extents.y + 0.08f, halfDepth - extents.y - 0.08f);
            }
            else if (wall == 1)
            {
                anchor.rotationEuler = new Vector3(0f, 90f, 0f);
                var extents = GetGeneratedFootprintExtents(anchor);
                position.x = halfWidth - extents.x - 0.04f;
                position.z = Mathf.Clamp(position.z, -halfDepth + extents.y + 0.08f, halfDepth - extents.y - 0.08f);
            }
            else
            {
                anchor.rotationEuler = new Vector3(0f, 0f, 0f);
                var extents = GetGeneratedFootprintExtents(anchor);
                position.x = Mathf.Clamp(position.x, -halfWidth + extents.x + 0.08f, halfWidth - extents.x - 0.08f);
                position.z = halfDepth - extents.y - 0.04f;
            }

            anchor.position = new Vector3(position.x, Mathf.Max(0.05f, anchor.scale.y * 0.5f), position.z);
        }

        private static float GetGeneratedFootprintRadius(AnchorDefinition anchor)
        {
            var extents = GetGeneratedFootprintExtents(anchor);
            return Mathf.Clamp(Mathf.Max(extents.x, extents.y) + 0.14f, 0.28f, 1.45f);
        }

        private static Vector2 GetGeneratedFootprintExtents(AnchorDefinition anchor)
        {
            var scale = anchor.scale == default ? Vector3.one : anchor.scale;
            var yaw = (anchor?.rotationEuler.y ?? 0f) * Mathf.Deg2Rad;
            var cos = Mathf.Abs(Mathf.Cos(yaw));
            var sin = Mathf.Abs(Mathf.Sin(yaw));
            var halfX = scale.x * 0.5f;
            var halfZ = scale.z * 0.5f;
            return new Vector2(cos * halfX + sin * halfZ, sin * halfX + cos * halfZ);
        }

        private static void ClampGeneratedFurniturePosition(AnchorDefinition anchor, float width, float depth)
        {
            var extents = GetGeneratedFootprintExtents(anchor);
            var marginX = Mathf.Clamp(extents.x + 0.045f, 0.12f, Mathf.Max(0.13f, width * 0.48f));
            var marginZ = Mathf.Clamp(extents.y + 0.045f, 0.12f, Mathf.Max(0.13f, depth * 0.48f));
            anchor.position = new Vector3(
                Mathf.Clamp(anchor.position.x, -width * 0.5f + marginX, width * 0.5f - marginX),
                anchor.position.y,
                Mathf.Clamp(anchor.position.z, -depth * 0.5f + marginZ, depth * 0.5f - marginZ));
        }

        private static void NormalizeFurnitureTemplateSuggestion(FurnitureTemplateSuggestion suggestion, string fallbackName)
        {
            var safeFallback = string.IsNullOrWhiteSpace(fallbackName) ? "Custom Furniture" : fallbackName.Trim();
            suggestion.label = string.IsNullOrWhiteSpace(suggestion.label) ? safeFallback : suggestion.label.Trim();
            suggestion.category = NormalizeFurnitureCategory(suggestion.category, suggestion.label + " " + safeFallback);
            suggestion.primitiveShape = NormalizePrimitiveShape(suggestion.primitiveShape);
            suggestion.colorHex = string.IsNullOrWhiteSpace(suggestion.colorHex)
                ? PickColor(Mathf.Abs(suggestion.category.GetHashCode()) % 8)
                : suggestion.colorHex.Trim();

            if (suggestion.scale == default)
            {
                suggestion.scale = DefaultFurnitureScale(suggestion.category);
            }

            suggestion.scale = ClampVector(suggestion.scale, new Vector3(0.2f, 0.2f, 0.2f), new Vector3(3.0f, 3.0f, 3.0f));
            suggestion.defaultY = suggestion.defaultY <= 0f
                ? Mathf.Max(0.15f, suggestion.scale.y * 0.5f)
                : Mathf.Clamp(suggestion.defaultY, 0.05f, 2.4f);
            suggestion.mnemonicYOffset = suggestion.mnemonicYOffset <= 0f
                ? Mathf.Max(0.45f, suggestion.scale.y * 0.65f + 0.35f)
                : Mathf.Clamp(suggestion.mnemonicYOffset, 0.25f, 2.8f);
            suggestion.parts = NormalizeFurnitureTemplateParts(suggestion.parts, suggestion.category, suggestion.colorHex);
        }

        private static string NormalizeFurnitureCategory(string category, string label)
        {
            var text = ((category ?? string.Empty) + " " + (label ?? string.Empty)).ToLowerInvariant();
            if (text.Contains("television") || text.Contains("tv") || text.Contains("screen")) return "television";
            if (text.Contains("computer") || text.Contains("pc") || text.Contains("monitor") || text.Contains("laptop") || text.Contains("comput") || text.Contains("omputer")) return "computer";
            if (text.Contains("toilet") || text.Contains("wc")) return "toilet";
            if (text.Contains("bath") || text.Contains("tub")) return "bathtub";
            if (text.Contains("sink")) return "sink";
            if (text.Contains("fridge") || text.Contains("refrigerator")) return "fridge";
            if (text.Contains("door")) return "door";
            if (text.Contains("window") || text.Contains("balcony")) return "window";
            if (text.Contains("cabinet") || text.Contains("wardrobe") || text.Contains("closet")) return "cabinet";
            if (text.Contains("bed")) return "bed";
            if (text.Contains("sofa") || text.Contains("couch")) return "sofa";
            if (text.Contains("chair")) return "chair";
            if (text.Contains("desk")) return "desk";
            if (text.Contains("counter")) return "counter";
            if (text.Contains("table")) return "table";
            if (text.Contains("shelf") || text.Contains("book")) return "shelf";
            if (text.Contains("plant")) return "plant";
            if (text.Contains("lamp") || text.Contains("light")) return "lamp";
            return string.IsNullOrWhiteSpace(category) ? "generic" : category.Trim().ToLowerInvariant();
        }

        private static string NormalizePrimitiveShape(string primitiveShape)
        {
            var shape = string.IsNullOrWhiteSpace(primitiveShape) ? string.Empty : primitiveShape.Trim();
            return shape.ToLowerInvariant() switch
            {
                "sphere" => "Sphere",
                "capsule" => "Capsule",
                "cylinder" => "Cylinder",
                _ => "Cube"
            };
        }

        private static Vector3 DefaultFurnitureScale(string category)
        {
            return category switch
            {
                "television" => new Vector3(1.45f, 0.9f, 0.22f),
                "computer" => new Vector3(1.15f, 0.85f, 0.65f),
                "toilet" => new Vector3(0.85f, 0.75f, 0.95f),
                "bathtub" => new Vector3(1.7f, 0.55f, 0.95f),
                "sink" => new Vector3(1.0f, 0.7f, 0.75f),
                "fridge" => new Vector3(0.95f, 2.0f, 0.8f),
                "bed" => new Vector3(2.1f, 0.55f, 1.45f),
                "sofa" => new Vector3(1.7f, 0.85f, 0.85f),
                "chair" => new Vector3(0.65f, 0.85f, 0.65f),
                "lamp" => new Vector3(0.45f, 1.35f, 0.45f),
                _ => new Vector3(1.0f, 0.85f, 0.8f)
            };
        }

        private static VisualObjectSpec[] NormalizeFurnitureTemplateParts(VisualObjectSpec[] parts, string category, string colorHex)
        {
            var normalized = new List<VisualObjectSpec>();
            if (parts != null)
            {
                for (int i = 0; i < parts.Length && i < 10; i++)
                {
                    var source = parts[i];
                    if (source == null)
                    {
                        continue;
                    }

                    normalized.Add(new VisualObjectSpec
                    {
                        label = string.IsNullOrWhiteSpace(source.label) ? $"part_{normalized.Count + 1}" : source.label.Trim(),
                        primitiveShape = NormalizePrimitiveShape(source.primitiveShape),
                        colorHex = string.IsNullOrWhiteSpace(source.colorHex) ? colorHex : source.colorHex.Trim(),
                        localPosition = ClampVector(source.localPosition, new Vector3(-0.8f, -0.8f, -0.8f), new Vector3(0.8f, 0.8f, 0.8f)),
                        scale = ClampVector(source.scale == default ? Vector3.one * 0.25f : source.scale, Vector3.one * 0.04f, Vector3.one),
                        effect = string.IsNullOrWhiteSpace(source.effect) ? "none" : source.effect.Trim()
                    });
                }
            }

            if (normalized.Count == 0)
            {
                normalized.AddRange(DefaultFurnitureParts(category, colorHex));
            }

            return normalized.ToArray();
        }

        private static IEnumerable<VisualObjectSpec> DefaultFurnitureParts(string category, string colorHex)
        {
            var main = string.IsNullOrWhiteSpace(colorHex) ? "#8B7A65" : colorHex;
            if (category == "television")
            {
                yield return FurniturePart("screen", "Cube", "#0E1118", new Vector3(0f, 0.18f, 0f), new Vector3(0.9f, 0.62f, 0.08f));
                yield return FurniturePart("glow", "Cube", "#26445F", new Vector3(0f, 0.18f, -0.05f), new Vector3(0.75f, 0.46f, 0.035f));
                yield return FurniturePart("stand", "Cube", main, new Vector3(0f, -0.32f, 0.02f), new Vector3(0.34f, 0.25f, 0.20f));
                yield break;
            }

            if (category == "computer")
            {
                yield return FurniturePart("monitor", "Cube", "#101722", new Vector3(0f, 0.24f, 0.03f), new Vector3(0.72f, 0.52f, 0.08f));
                yield return FurniturePart("keyboard", "Cube", "#20242A", new Vector3(0f, -0.30f, -0.30f), new Vector3(0.72f, 0.07f, 0.22f));
                yield return FurniturePart("mouse", "Sphere", "#2D3036", new Vector3(0.42f, -0.28f, -0.28f), new Vector3(0.16f, 0.08f, 0.22f));
                yield break;
            }

            yield return FurniturePart("body", "Cube", main, Vector3.zero, new Vector3(0.8f, 0.7f, 0.7f));
            yield return FurniturePart("top detail", "Cube", "#C8B08A", new Vector3(0f, 0.42f, 0f), new Vector3(0.58f, 0.12f, 0.58f));
            yield return FurniturePart("accent", "Sphere", "#FFD166", new Vector3(0.38f, 0.05f, -0.35f), new Vector3(0.16f, 0.16f, 0.16f));
        }

        private static VisualObjectSpec FurniturePart(string label, string shape, string colorHex, Vector3 localPosition, Vector3 scale)
        {
            return new VisualObjectSpec
            {
                label = label,
                primitiveShape = shape,
                colorHex = colorHex,
                localPosition = localPosition,
                scale = scale,
                effect = "none"
            };
        }

        private static void ResolveRoomShellDimensions(string layoutType, string roomDescription, out float width, out float depth)
        {
            var text = ((layoutType ?? string.Empty) + " " + (roomDescription ?? string.Empty)).ToLowerInvariant();

            if (ContainsAny(text, "long", "corridor", "hallway", "linear", "長い", "廊下"))
            {
                width = 8f;
                depth = 16f;
                return;
            }

            if (ContainsAny(text, "gallery", "exhibition"))
            {
                width = 14f;
                depth = 8f;
                return;
            }

            if (ContainsAny(text, "l-shape", "l shape", "l-shaped", "l字", "l型"))
            {
                width = 13f;
                depth = 11f;
                return;
            }

            if (ContainsAny(text, "square", "single room", "one room", "only one room", "open plan", "studio", "ワンルーム"))
            {
                width = 12f;
                depth = 12f;
                return;
            }

            if (ContainsAny(text, "1ldk"))
            {
                width = 10f;
                depth = 13f;
                return;
            }

            if (ContainsAny(text, "1dk"))
            {
                width = 9f;
                depth = 11f;
                return;
            }

            if (ContainsAny(text, "1k"))
            {
                width = 8.5f;
                depth = 10f;
                return;
            }

            width = 11f;
            depth = 11f;
        }

        private static bool ShouldUseOpenRoomShell(string layoutType, string roomDescription)
        {
            var text = ((layoutType ?? string.Empty) + " " + (roomDescription ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(text, "only one room", "single room", "one room", "open plan", "no partition", "no partitions", "no corridor", "studio", "square", "gallery", "ワンルーム", "一室");
        }

        private static bool ShouldAddLayoutPartitions(string layoutType, string roomDescription)
        {
            if (ShouldUseOpenRoomShell(layoutType, roomDescription))
            {
                return false;
            }

            var text = ((layoutType ?? string.Empty) + " " + (roomDescription ?? string.Empty)).ToLowerInvariant();
            return ContainsAny(text, "1dk", "1ldk", "long 1ldk", "separate", "partition", "bedroom", "kitchen room");
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < needles.Length; i++)
            {
                if (!string.IsNullOrEmpty(needles[i]) && value.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddRoomShell(RoomSpecDefinition room, float width, float depth, string layoutType, string roomDescription)
        {
            var openRoom = ShouldUseOpenRoomShell(layoutType, roomDescription);
            var floorColor = openRoom ? "#4A3B2F" : "#3B2B22";
            var wallColor = openRoom ? "#32465D" : "#314155";
            var accentColor = openRoom ? "#C68B59" : "#506783";

            room.environmentPrimitives.Add(CreateRoomPrimitive("floor", "Floor", floorColor, new Vector3(0f, -0.1f, 0f), new Vector3(width, 0.2f, depth)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("ceiling", "Ceiling", "#1B2230", new Vector3(0f, 4.2f, 0f), new Vector3(width, 0.2f, depth)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("left_wall", "Left Wall", wallColor, new Vector3(-width * 0.5f, 2f, 0f), new Vector3(0.2f, 4f, depth)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("right_wall", "Right Wall", wallColor, new Vector3(width * 0.5f, 2f, 0f), new Vector3(0.2f, 4f, depth)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("entrance_wall", "Entrance Wall", "#263548", new Vector3(0f, 2f, -depth * 0.5f), new Vector3(width, 4f, 0.2f)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("feature_wall", "Feature Wall", "#3B526C", new Vector3(0f, 2f, depth * 0.5f), new Vector3(width, 4f, 0.2f)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("memory_rug", "Memory Rug", "#27485E", new Vector3(0f, -0.015f, -0.25f), new Vector3(width * 0.58f, 0.025f, depth * 0.45f)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("floor_inlay_a", "Floor Inlay A", accentColor, new Vector3(0f, 0.01f, 0f), new Vector3(width * 0.92f, 0.025f, 0.045f)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("floor_inlay_b", "Floor Inlay B", accentColor, new Vector3(0f, 0.012f, 0f), new Vector3(0.045f, 0.025f, depth * 0.92f)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("left_baseboard", "Left Baseboard", "#B58A62", new Vector3(-width * 0.5f + 0.11f, 0.22f, 0f), new Vector3(0.08f, 0.18f, depth * 0.96f)));
            room.environmentPrimitives.Add(CreateRoomPrimitive("right_baseboard", "Right Baseboard", "#B58A62", new Vector3(width * 0.5f - 0.11f, 0.22f, 0f), new Vector3(0.08f, 0.18f, depth * 0.96f)));

            if (ShouldAddLayoutPartitions(layoutType, roomDescription))
            {
                room.environmentPrimitives.Add(CreateRoomPrimitive("kitchen_partition", "Kitchen Partition", "#556579", new Vector3(-width * 0.18f, 1.65f, -depth * 0.18f), new Vector3(width * 0.58f, 3.1f, 0.12f)));
                room.environmentPrimitives.Add(CreateRoomPrimitive("sleep_partition", "Sleep Partition", "#5E6F82", new Vector3(width * 0.18f, 1.65f, depth * 0.20f), new Vector3(width * 0.52f, 3.1f, 0.12f)));
            }

            if (ContainsAny((layoutType ?? string.Empty).ToLowerInvariant(), "l-shape", "l shape", "l-shaped"))
            {
                room.environmentPrimitives.Add(CreateRoomPrimitive("l_shape_half_wall", "L Shape Half Wall", "#5A6D7C", new Vector3(-width * 0.18f, 0.9f, depth * 0.12f), new Vector3(0.16f, 1.8f, depth * 0.46f)));
                room.environmentPrimitives.Add(CreateRoomPrimitive("l_shape_accent_rug", "L Shape Accent Rug", "#7A5637", new Vector3(width * 0.2f, -0.005f, -depth * 0.18f), new Vector3(width * 0.35f, 0.025f, depth * 0.34f)));
            }
        }

        private static RoomPrimitiveDefinition CreateRoomPrimitive(string id, string label, string colorHex, Vector3 position, Vector3 scale)
        {
            return new RoomPrimitiveDefinition
            {
                id = id,
                label = label,
                primitiveShape = "Cube",
                colorHex = colorHex,
                position = position,
                scale = scale,
                rotationEuler = Vector3.zero,
                showLabel = false,
                labelHeight = 0f
            };
        }

        private static Vector3 ClampVector(Vector3 value, Vector3 min, Vector3 max)
        {
            return new Vector3(
                Mathf.Clamp(value.x, min.x, max.x),
                Mathf.Clamp(value.y, min.y, max.y),
                Mathf.Clamp(value.z, min.z, max.z));
        }

        private static string NormalizeJsonCandidate(string text)
        {
            var cleaned = text.Trim();
            cleaned = cleaned.Replace("\u201C", "\"").Replace("\u201D", "\"").Replace("\u2018", "'").Replace("\u2019", "'");

            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    cleaned = cleaned.Substring(firstNewline + 1);
                }
            }

            if (cleaned.EndsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(0, cleaned.LastIndexOf("```", StringComparison.Ordinal));
            }

            cleaned = cleaned.Trim();

            var extracted = ExtractTopLevelJsonObject(cleaned);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                cleaned = extracted;
            }

            cleaned = Regex.Replace(cleaned, @",(\s*[}\]])", "$1");
            return cleaned.Trim();
        }

        private static string ExtractTopLevelJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var start = text.IndexOf('{');
            if (start < 0)
            {
                return string.Empty;
            }

            var depth = 0;
            var inString = false;
            var escaping = false;

            for (int i = start; i < text.Length; i++)
            {
                var c = text[i];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }

            return string.Empty;
        }

        private static string BuildPreview(string text)
        {
            var cleaned = text ?? string.Empty;
            cleaned = cleaned.Replace("\r", "\\r").Replace("\n", "\\n");

            if (cleaned.Length > ErrorPreviewLength)
            {
                cleaned = cleaned.Substring(0, ErrorPreviewLength) + "...";
            }

            return cleaned;
        }

        private static string PickColor(int index)
        {
            var colors = new[]
            {
                "#7EC8E3", "#F4A261", "#E76F51", "#A8DADC", "#2A9D8F",
                "#FFD166", "#E9C46A", "#CCD5AE", "#457B9D", "#9B5DE5"
            };
            return colors[index % colors.Length];
        }
    }
}

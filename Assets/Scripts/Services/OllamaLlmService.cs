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
        private const int MnemonicChunkSize = 3;
        private const int GeminiMnemonicChunkSize = 1;
        private const int GeminiServerAttemptCount = 2;
        private const int ImagePromptCandidateCount = 4;
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

        private static bool IsStoryOnlyRedesignEnabled()
        {
            return true;
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
            public string opening_problem;
            public string goal;
            public string supporting_character;
            public string relationship;
            public string emotional_stake;
            public string narrative_archetype;
            public string turning_choice;
            public string final_outcome;
            public string emotional_payoff;
            public CausalStoryPlanItem[] items;
        }

        [Serializable]
        private class CausalStoryPlanItem
        {
            public string word;
            public string arc_role;
            public string story_function;
            public string need;
            public string state_before;
            public string action;
            public string result;
            public string state_after;
            public string causal_link;
            public string goal_link;
            public string memory_cue;
        }

        [Serializable]
        private class GeneratedMnemonicEnvelope
        {
            public GeneratedMnemonicItem[] items;
        }

        [Serializable]
        private class GeneratedMnemonicItem
        {
            public string word;
            public string anchor;
            public GeneratedCueBlueprint cue_blueprint;
            public string visual_cue_en;
            public string association_prompt_en;
            public string mnemonic_en;
            public string mnemonic_mode;
            public GeneratedHookJudge hook_judge;
            public string story_cue_en;
            public string image_prompt_en;
            public string[] image_prompt_candidates_en;
            public VisualObjectSpec[] visual_objects;
        }

        [Serializable]
        private class GeneratedCueBlueprint
        {
            public string targetMeaning;
            public string visualSceneCore;
            public string mainObject;
            public string anchorRelation;
            public string relativeSize;
            public string mainActionOrState;
            public string[] visibleObjects;
            public string mnemonicHookNote;
            public string mnemonicMode;
        }

        [Serializable]
        private class GeneratedHookJudge
        {
            public bool accepted;
            public int score;
            public string reason;
            public string best_hook;
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
            int candidateRank,
            IReadOnlyList<string> priorCandidateStories,
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

            var candidateInstruction = BuildStoryCandidateInstruction(candidateRank, priorCandidateStories);
            var causalPlanRequest = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildCausalStoryPlanPrompt(words) + candidateInstruction,
                system = "Plan a complete character-driven micro-story with I as the first-person narrator and exactly one named supporting character. No other people may appear. Give it a personal stake, a choice-driven turn, and an emotional payoff, with exactly one sentence beat per target. Return exactly one valid JSON object and nothing else. No prose outside JSON. No markdown.",
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
                preparedCausalPlanJson = BuildStructuralFallbackCausalPlan(words, candidateRank);
            }

            causalPlanJson = preparedCausalPlanJson;

            var storyRequest = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildStoryPrompt(words, causalPlanJson) + candidateInstruction,
                system = "Write character-driven micro-stories for adult A2-B1 English learners with I as the first-person narrator and exactly one named supporting character. No other people may appear. Never write procedures or task logs. Produce exactly one sentence per target, with a personal stake, a choice-driven turn, and a practical plus emotional payoff. Return exactly one valid JSON object and nothing else. No markdown. No commentary.",
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
                    prompt = BuildStoryRepairPrompt(words, causalPlanJson, storyResponse, firstFailure) + candidateInstruction,
                    system = "Repair a rejected character-driven micro-story for adult A2-B1 English learners. Use I as the first-person narrator and exactly one named supporting character, with no other people. Replace procedural steps with human reactions, a meaningful choice, and a practical plus emotional payoff. Return exactly one sentence per target and exactly one valid JSON object. No markdown. No commentary.",
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
                    if (TryParseAndBuildUsableStoryResponse(storyResponse, words, model.Trim(), out story, out var usableError))
                    {
                        story.storySource = "ollama_story_quality_degraded";
                        Debug.LogWarning("Story repair request failed, so the complete original story was kept. Quality issue: " + firstFailure);
                        onSuccess?.Invoke(story);
                        yield break;
                    }

                    onError?.Invoke("The first story failed validation (" + firstFailure + ") and the online repair request failed: " + repairRequestError + ". Original story was not usable: " + usableError);
                    yield break;
                }

                if (!TryParseAndBuildStoryResponse(repairedResponse, words, model.Trim(), out story, out validationError))
                {
                    var repairFailure = validationError;
                    if (!TryParseAndBuildUsableStoryResponse(repairedResponse, words, model.Trim(), out story, out var usableError) &&
                        !TryParseAndBuildUsableStoryResponse(storyResponse, words, model.Trim(), out story, out usableError))
                    {
                        var recoveryRequest = new OllamaGenerateRequest
                        {
                            model = model.Trim(),
                            prompt = BuildMinimalStoryRecoveryPrompt(words, firstFailure, repairFailure) + candidateInstruction,
                            system = "Write a fresh, logically connected first-person micro-story. Plan state changes silently, then return one valid JSON object only. Do not reuse wording or the premise from rejected drafts.",
                            format = "json",
                            stream = false,
                            options = new OllamaRequestOptions
                            {
                                temperature = 0.52f,
                                num_predict = 1800
                            }
                        };
                        string recoveryResponse = null;
                        string recoveryRequestError = null;
                        yield return SendOllamaJsonRequest(
                            endpoint.Trim(),
                            recoveryRequest,
                            value => recoveryResponse = value,
                            error => recoveryRequestError = error);

                        var recoveryValidationError = string.Empty;
                        if (!string.IsNullOrWhiteSpace(recoveryRequestError) ||
                            (!TryParseAndBuildStoryResponse(recoveryResponse, words, model.Trim(), out story, out recoveryValidationError) &&
                             !TryParseAndBuildUsableStoryResponse(recoveryResponse, words, model.Trim(), out story, out recoveryValidationError)))
                        {
                            onError?.Invoke("Online story repair failed (" + repairFailure + "). Fresh recovery also failed: " +
                                            (string.IsNullOrWhiteSpace(recoveryRequestError) ? recoveryValidationError : recoveryRequestError) +
                                            ". Earlier response was unusable: " + usableError);
                            yield break;
                        }

                        story.storySource = "ollama_story_fresh_recovery";
                        onSuccess?.Invoke(story);
                        yield break;
                    }

                    story.storySource = "ollama_story_quality_degraded";
                    Debug.LogWarning("Story repair still missed a soft quality rule, so the complete usable story was kept. Quality issue: " + repairFailure);
                }
                else
                {
                    story.storySource += "_retry";
                }
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
                "Plan a complete character-driven micro-story with I as the first-person narrator and exactly one named supporting character. No other people may appear. Give it a personal stake, a choice-driven turn, and an emotional payoff, with exactly one sentence beat per target. Return exactly one valid JSON object and nothing else. No prose outside JSON. No markdown.",
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
                preparedCausalPlanJson = BuildStructuralFallbackCausalPlan(words, 0);
            }

            causalPlanJson = preparedCausalPlanJson;

            string storyResponse = null;
            requestError = null;
            yield return SendGeminiGenerateRequest(
                apiKey.Trim(),
                model.Trim(),
                BuildStoryPrompt(words, causalPlanJson),
                "Write character-driven micro-stories for adult A2-B1 English learners with I as the first-person narrator and exactly one named supporting character. No other people may appear. Never write procedures or task logs. Produce exactly one sentence per target, with a personal stake, a choice-driven turn, and a practical plus emotional payoff. Return exactly one valid JSON object and nothing else. No markdown. No commentary.",
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
                    "Repair a rejected character-driven micro-story for adult A2-B1 English learners. Use I as the first-person narrator and exactly one named supporting character, with no other people. Replace procedural steps with human reactions, a meaningful choice, and a practical plus emotional payoff. Return exactly one sentence per target and exactly one valid JSON object. No markdown. No commentary.",
                    0.38f,
                    2200,
                    value => repairedResponse = value,
                    error => requestError = error);

                if (!string.IsNullOrWhiteSpace(requestError))
                {
                    if (TryParseAndBuildUsableStoryResponse(storyResponse, words, resolvedModel, out story, out var usableError))
                    {
                        story.storySource = "gemini_story_quality_degraded";
                        Debug.LogWarning("Gemini story repair request failed, so the complete original story was kept. Quality issue: " + firstFailure);
                        story.storyProvider = "Gemini Online";
                        story.storyModel = resolvedModel;
                        onSuccess?.Invoke(story);
                        yield break;
                    }

                    onError?.Invoke("The first story failed validation (" + firstFailure + ") and the Gemini repair request failed: " + requestError + ". Original story was not usable: " + usableError);
                    yield break;
                }

                resolvedModel = string.IsNullOrWhiteSpace(GeminiModelsUsedSummary) ? model.Trim() : GeminiModelsUsedSummary;
                if (!TryParseAndBuildStoryResponse(repairedResponse, words, resolvedModel, out story, out validationError))
                {
                    var repairFailure = validationError;
                    if (!TryParseAndBuildUsableStoryResponse(repairedResponse, words, resolvedModel, out story, out var usableError) &&
                        !TryParseAndBuildUsableStoryResponse(storyResponse, words, resolvedModel, out story, out usableError))
                    {
                        onError?.Invoke("Gemini story repair still failed validation: " + repairFailure + ". No structurally usable response remained: " + usableError);
                        yield break;
                    }

                    story.storySource = "gemini_story_quality_degraded";
                    Debug.LogWarning("Gemini story repair still missed a soft quality rule, so the complete usable story was kept. Quality issue: " + repairFailure);
                }
                else
                {
                    story.storySource += "_retry";
                }
            }

            story.storyProvider = "Gemini Online";
            story.storyModel = resolvedModel;
            if (string.IsNullOrWhiteSpace(story.storySource) ||
                story.storySource.IndexOf("quality_degraded", StringComparison.OrdinalIgnoreCase) < 0)
            {
                story.storySource = story.storySource != null && story.storySource.EndsWith("_retry", StringComparison.OrdinalIgnoreCase)
                    ? "gemini_story_repaired"
                    : "gemini_story";
            }
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

        private static string BuildStoryCandidateInstruction(
            int candidateRank,
            IReadOnlyList<string> priorCandidateStories)
        {
            var builder = new StringBuilder();
            switch (Mathf.Clamp(candidateRank, 0, 2))
            {
                case 1:
                    builder.AppendLine("CANDIDATE 2 OF 3: Write the second-best fully valid alternative.");
                    break;
                case 2:
                    builder.AppendLine("CANDIDATE 3 OF 3: Write the third-best fully valid alternative.");
                    break;
                default:
                    builder.AppendLine("CANDIDATE 1 OF 3: Write the strongest overall solution.");
                    break;
            }

            if (candidateRank > 0)
            {
                builder.AppendLine("Make this a genuinely different story, not the same template with changed names, objects, or places.");
                builder.AppendLine("Change the central problem, setting, relationship tension, turning choice, target actions, and final image.");
            }

            if (priorCandidateStories != null && priorCandidateStories.Count > 0)
            {
                builder.AppendLine("PREVIOUS ACCEPTED STORIES — do not imitate their premise or sentence skeleton:");
                for (var i = 0; i < priorCandidateStories.Count; i++)
                {
                    var story = Regex.Replace(priorCandidateStories[i] ?? string.Empty, @"\s+", " ").Trim();
                    if (story.Length > 0)
                    {
                        builder.AppendLine("Previous " + (i + 1) + ": " + story);
                    }
                }
            }

            builder.AppendLine("DIVERSITY TEST: replacing only the supporting character, goal object, or location must not turn a previous story into this one.");
            return "\n" + builder + "\n";
        }

        private static string BuildCausalStoryPlanPrompt(List<WordEntry> words)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Build a physically plausible plan for one complete everyday micro-story with exactly " + words.Count + " sentence beats.");
            builder.AppendLine("Use every target exactly once. Each plan item becomes exactly one story sentence, so choose the order that makes the strongest causal chain.");
            builder.AppendLine("Use a TOTAL-DEVELOPMENT-TOTAL arc: item 1 opens with the whole problem and success goal, the middle items develop and turn the action, and the last item explicitly completes that same goal.");
            builder.AppendLine("This must be a STORY, not a repair guide, delivery route, checklist, or chronological work report. Physical progress alone is not a plot.");
            builder.AppendLine("Use exactly two people: the first-person narrator I and one named supporting character. No friend, parent, child, neighbor, helper, guard, crowd, group, or other third person may appear.");
            builder.AppendLine("Choose one relationship between me and that supporting character. Give the supporting character a simple want or fear, and make the concrete goal matter personally to both of us.");
            builder.AppendLine("Build two connected arcs: an OUTER arc with one observable goal and an INNER arc where trust, courage, forgiveness, belonging, or care changes through a choice.");
            builder.AppendLine("The plan must be linear: item N creates the situation, feeling, discovery, or choice that item N+1 responds to. Do not make separate episodes, parallel actions, unrelated scenes, or an opening with no ending.");
            builder.AppendLine("For every adjacent pair, write a concrete bridge: item N changes one fact, and item N+1 acts because of that exact changed fact. Repeating the goal or a character name is not a bridge.");
            builder.AppendLine("Choose ONE central person, place, object, or outcome that defines a small concrete goal with a clear finish. Targets may change practical progress, what a character knows, what a character decides, or the relationship.");
            builder.AppendLine("Vary the premise to fit the targets. Use an everyday repair, delivery, search, reunion, mistake, or helpful task when suitable; do not default to storms, floods, dangerous races, or reaching safety unless the target set truly supports it.");
            builder.AppendLine("The goal must not be a list of errands. Do not use shopping, packing several items, eating lunch, sightseeing, appreciating a view, attending unrelated events, or visiting multiple destinations as the story structure.");
            builder.AppendLine("Return JSON exactly as: {\"opening_problem\":\"the concrete problem stated in sentence 1\",\"goal\":\"the observable outer success condition\",\"supporting_character\":\"the only other person's short name\",\"relationship\":\"who that person is to me\",\"emotional_stake\":\"what that person wants or fears and why the goal matters\",\"narrative_archetype\":\"search_with_reversal|promise_under_pressure|misunderstanding|discovery_changes_meaning|second_chance|secret_revealed|other\",\"turning_choice\":\"the difficult but safe choice that changes the story\",\"final_outcome\":\"the visible result proving the outer goal succeeded\",\"emotional_payoff\":\"the small reaction or callback proving the relationship or feeling changed\",\"items\":[{\"word\":\"zapato\",\"arc_role\":\"opening|development|turn|resolution\",\"story_function\":\"hook|reaction|complication|pressure|choice|consequence|final_decision|payoff\",\"need\":\"specific prior situation requiring this beat\",\"state_before\":\"one concrete fact that is true before the target event\",\"action\":\"physically possible target event, reaction, or decision\",\"result\":\"consequence that makes the next beat necessary\",\"state_after\":\"one concrete fact changed by this beat\",\"causal_link\":\"why this target event changes state_before into state_after\",\"goal_link\":\"how this beat changes the outer goal or inner arc\",\"memory_cue\":\"one distinctive concrete change caused by the target\"}]}");
            builder.AppendLine("Each item must have non-empty word, need, action, result, goal_link, and memory_cue fields.");
            builder.AppendLine("STATE CONTRACT: state_before must equal need, state_after must equal result, and item N state_after must be copied into item N+1 state_before. causal_link must name the real mechanism, evidence, decision, or belief change.");
            builder.AppendLine("Choose a narrative archetype that fits this target set. Archetypes are plot dynamics, not fixed topics; invent the concrete premise from the targets.");
            builder.AppendLine("ARC ROLES: item 1 is opening; the last item is resolution; choose one middle item as turn, where a revelation or meaningful character choice changes both the plan and the relationship; all other middle items are development.");
            builder.AppendLine("The opening need must use I, name the one supporting character, state the problem and goal, and explain why it matters. The first target must begin the story in the same beat.");
            builder.AppendLine("HARD LINK FORMAT: copy the complete result text of item N verbatim into the need field of item N+1. The strings must be exactly identical. The action in item N+1 must respond directly to that copied situation.");
            builder.AppendLine("Never attach a generic feeling such as trust, hurt, apology, or hope to an unrelated target action with so. Explain what the target reveals, changes, blocks, proves, or enables.");
            builder.AppendLine("At least three beats must be human beats: a character reacts, asks, admits, chooses, helps, refuses, remembers, trusts, or changes feeling. Do not place more than two tool-use or task-operation beats in a row.");
            builder.AppendLine("The turn cannot be only bad weather, a stuck object, a missing tool, or another physical obstacle. A character must learn something or make a choice with a consequence.");
            builder.AppendLine("The last action must cause final_outcome and emotional_payoff. The ending must prove both that the outer goal is achieved and that the personal stake was answered.");
            builder.AppendLine("A target may be used normally or imaginatively. If it cannot be intentionally handled, it must immediately cause a character decision or action in the same beat. Never spend a whole beat merely noticing, seeing, or hearing it.");
            builder.AppendLine("Keep each event-reaction-consequence compact enough to express as one clear A2-B1 sentence with one target pair. Avoid instructions, measurements, material-processing details, and repeated tool operations.");
            builder.AppendLine("For memory_cue, choose one safe, plausible detail created by the target action: a clear movement, shape, color, sound, touch, or changed state. Make cues easy to picture and different from one another. A cue must do narrative work, not decorate the scene.");
            builder.AppendLine("Reject magic, coincidence, dream logic, symbolic actions, impossible tool use, distant scenery, reflections that reveal unknown facts, and objects appearing without a source.");
            builder.AppendLine("Do not claim that an inaccessible shop supplies an item, that throwing one object summons another, or that a blunt object cuts or unlocks something without a believable mechanism.");
            builder.AppendLine("Prefer familiar, safe actions that ordinary people could perform. Do not make either character climb, enter danger, misuse a tool, or take another needless risk when a safe action would solve the problem. Keep exactly the narrator I and the one named supporting character throughout.");
            builder.AppendLine("Use simple everyday English in opening_problem, goal, final_outcome, need, action, result, and goal_link. Prefer short common words and direct verbs. Avoid poetic, rare, or literary words.");
            builder.AppendLine("Do not introduce important non-target props such as a bowl, lunch, bottle, rope, ticket, key, or borrowed book unless absolutely unavoidable for a small connecting action. The target objects must perform the important jobs.");
            builder.AppendLine("Do not list supplies or mention an object that no character uses. Every named prop must be used in the same item or the next item.");
            builder.AppendLine("Silently simulate the chain from beginning to end before returning JSON. Ask whether the final outcome answers the opening need with a clear yes, and repair the plan if it does not.");
            builder.AppendLine();
            builder.AppendLine("Targets:");
            for (var i = 0; i < words.Count; i++)
            {
                builder.Append(i + 1)
                    .Append(". word=").Append(words[i].word)
                    .Append("; meaning=").Append(words[i].meaning)
                    .AppendLine();
            }

            return builder.ToString();
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

            if (string.IsNullOrWhiteSpace(plan.opening_problem))
            {
                plan.opening_problem = plan.items.Length > 0 ? plan.items[0]?.need : plan.goal;
            }

            if (string.IsNullOrWhiteSpace(plan.final_outcome))
            {
                plan.final_outcome = plan.items.Length > 0 ? plan.items[plan.items.Length - 1]?.result : plan.goal;
            }

            if (string.IsNullOrWhiteSpace(plan.supporting_character))
            {
                plan.supporting_character = "Ana";
            }

            if (string.IsNullOrWhiteSpace(plan.relationship))
            {
                plan.relationship = "Ana is the only other person and shares the goal with me.";
            }

            if (string.IsNullOrWhiteSpace(plan.emotional_stake))
            {
                plan.emotional_stake = "The result matters to the other character personally.";
            }

            if (string.IsNullOrWhiteSpace(plan.narrative_archetype))
            {
                plan.narrative_archetype = "other";
            }

            if (string.IsNullOrWhiteSpace(plan.turning_choice))
            {
                plan.turning_choice = "I choose honesty and help Ana instead of only following the task.";
            }

            if (string.IsNullOrWhiteSpace(plan.emotional_payoff))
            {
                plan.emotional_payoff = "The other character reacts with clear relief or renewed trust.";
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

                // Preserve otherwise valid plans from smaller models while still giving the
                // story writer a concrete cue to improve. Stronger models provide a distinct
                // memory_cue; the visible result is the safest semantic fallback.
                if (string.IsNullOrWhiteSpace(item.memory_cue))
                {
                    item.memory_cue = item.result.Trim();
                }

                if (string.IsNullOrWhiteSpace(item.causal_link))
                {
                    item.causal_link = item.goal_link.Trim();
                }

                // Canonical roles make the total-development-total contract explicit even
                // when a smaller planner omits or misspells its optional arc labels.
                item.arc_role = GetCausalStoryArcRole(i, plan.items.Length);
                item.story_function = GetCausalStoryFunction(i, plan.items.Length);

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

                item.state_before = item.need.Trim();
                item.state_after = item.result.Trim();
            }

            if (usedWords.Count != expectedWords.Count)
            {
                error = "The plan does not contain every selected target word exactly once.";
                return false;
            }

            preparedJson = JsonUtility.ToJson(plan);
            return true;
        }

        private static string BuildStructuralFallbackCausalPlan(List<WordEntry> words, int candidateRank)
        {
            var frame = SelectStructuralFallbackFrame(words, candidateRank);
            var plan = new CausalStoryPlanEnvelope
            {
                opening_problem = frame.openingProblem,
                goal = frame.goal,
                supporting_character = frame.character,
                relationship = frame.relationship,
                emotional_stake = frame.emotionalStake,
                narrative_archetype = frame.archetype,
                turning_choice = frame.turningChoice,
                final_outcome = frame.finalOutcome,
                emotional_payoff = frame.emotionalPayoff,
                items = new CausalStoryPlanItem[words.Count]
            };

            var priorResult = plan.opening_problem;
            for (var i = 0; i < words.Count; i++)
            {
                var entry = words[i];
                var word = string.IsNullOrWhiteSpace(entry?.word) ? "target" : entry.word.Trim();
                var meaning = string.IsNullOrWhiteSpace(entry?.meaning) ? "object" : entry.meaning.Trim();
                var isLast = i == words.Count - 1;
                string result;
                if (isLast)
                {
                    result = plan.final_outcome + " " + plan.emotional_payoff;
                }
                else if (i == 0)
                {
                    result = frame.firstResult;
                }
                else if (i == Mathf.Max(1, (words.Count - 1) / 2))
                {
                    result = frame.turnResult;
                }
                else if (i == words.Count - 2)
                {
                    result = frame.finalDecisionResult;
                }
                else
                {
                    result = i < words.Count / 2 ? frame.risingResult : frame.consequenceResult;
                }

                plan.items[i] = new CausalStoryPlanItem
                {
                    word = word,
                    arc_role = GetCausalStoryArcRole(i, words.Count),
                    story_function = GetCausalStoryFunction(i, words.Count),
                    need = priorResult,
                    state_before = priorResult,
                    action = "Use " + BuildTargetStoryToken(meaning, word) + " in a plausible event, discovery, reaction, or choice that changes the current story state.",
                    result = result,
                    state_after = result,
                    causal_link = "State the real mechanism that makes this target event produce the result; never join unrelated clauses with so.",
                    goal_link = isLast
                        ? "This explicitly completes the opening goal and answers the personal stake."
                        : "This changes one concrete fact, belief, choice, or relationship state required by the next beat.",
                    memory_cue = "The action makes one clear, easy-to-picture change involving the " + meaning + "."
                };
                priorResult = result;
            }

            return JsonUtility.ToJson(plan);
        }

        private sealed class StructuralFallbackFrame
        {
            public string archetype;
            public string character;
            public string openingProblem;
            public string goal;
            public string relationship;
            public string emotionalStake;
            public string turningChoice;
            public string finalOutcome;
            public string emotionalPayoff;
            public string firstResult;
            public string risingResult;
            public string turnResult;
            public string consequenceResult;
            public string finalDecisionResult;
        }

        private static StructuralFallbackFrame SelectStructuralFallbackFrame(List<WordEntry> words, int candidateRank)
        {
            var hash = 17;
            if (words != null)
            {
                for (var i = 0; i < words.Count; i++)
                {
                    var key = (words[i]?.word ?? string.Empty) + "|" + (words[i]?.meaning ?? string.Empty);
                    for (var j = 0; j < key.Length; j++)
                    {
                        hash = unchecked((hash * 31) + char.ToLowerInvariant(key[j]));
                    }
                }
            }

            var frameIndex = (Math.Abs(hash % 6) + Mathf.Clamp(candidateRank, 0, 2)) % 6;
            switch (frameIndex)
            {
                case 1:
                    return CreateStructuralFallbackFrame("promise_under_pressure", "Leo", "finish our shared project before today's deadline", "Leo fears I will abandon our promise");
                case 2:
                    return CreateStructuralFallbackFrame("misunderstanding", "Maya", "prove what really happened before our friendship breaks", "Maya believes I hid an important truth");
                case 3:
                    return CreateStructuralFallbackFrame("discovery_changes_meaning", "Noah", "restore our damaged keepsake and understand its hidden message", "Noah fears our shared memory is ruined");
                case 4:
                    return CreateStructuralFallbackFrame("second_chance", "Lina", "complete the attempt we failed together yesterday", "Lina needs to know I will not quit again");
                case 5:
                    return CreateStructuralFallbackFrame("secret_revealed", "Omar", "solve our shared problem before my mistake becomes permanent", "Omar wants honesty more than an easy success");
                default:
                    return CreateStructuralFallbackFrame("search_with_reversal", "Ana", "recover our missing keepsake before it is lost", "Ana thinks I stopped caring about our promise");
            }
        }

        private static StructuralFallbackFrame CreateStructuralFallbackFrame(
            string archetype,
            string character,
            string goal,
            string emotionalStake)
        {
            return new StructuralFallbackFrame
            {
                archetype = archetype,
                character = character,
                openingProblem = "I must " + goal + " because " + emotionalStake + ".",
                goal = "I must " + goal + ".",
                relationship = character + " is the only other person and shares this goal with me.",
                emotionalStake = emotionalStake + ".",
                turningChoice = "New evidence proves my first belief was wrong, so I admit my mistake and choose a joint plan with " + character + ".",
                finalOutcome = "We " + goal + ".",
                emotionalPayoff = character + " responds with relief and renewed trust.",
                firstResult = "The first target event changes one concrete fact and gives us a specific lead.",
                risingResult = "That lead reveals evidence that corrects our earlier assumption and forces a new action.",
                turnResult = "I admit what I misunderstood, and " + character + " chooses a new plan with me.",
                consequenceResult = "Our joint choice works, leaving one clear obstacle between us and the goal.",
                finalDecisionResult = character + " removes that obstacle and leaves the final proof for me to complete."
            };
        }

        private static string GetCausalStoryArcRole(int index, int totalCount)
        {
            if (index <= 0)
            {
                return "opening";
            }

            if (index >= totalCount - 1)
            {
                return "resolution";
            }

            var turnIndex = Mathf.Max(1, (totalCount - 1) / 2);
            return index == turnIndex ? "turn" : "development";
        }

        private static string GetCausalStoryFunction(int index, int totalCount)
        {
            if (totalCount <= 1)
            {
                return "hook_and_payoff";
            }

            if (index <= 0)
            {
                return "hook";
            }

            if (index >= totalCount - 1)
            {
                return "payoff";
            }

            if (index == totalCount - 2)
            {
                return "final_decision";
            }

            var turnIndex = Mathf.Clamp(totalCount / 2, 1, totalCount - 2);
            if (index == turnIndex)
            {
                return "choice";
            }

            if (index < turnIndex)
            {
                return index == 1 ? "reaction" : "complication";
            }

            return "consequence";
        }

        private static string NormalizePlanLink(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string BuildStoryPrompt(List<WordEntry> words, string causalPlanJson)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Write one complete, warm, plot-driven English micro-story in EXACTLY " + words.Count + " sentences using the " + words.Count + " target Spanish words below.");
            builder.AppendLine("The " + words.Count + " sentences together must form a TOTAL-DEVELOPMENT-TOTAL arc, not the opening of a longer story. The story is about what characters DO and achieve, not what a scene looks or sounds like.");
            builder.AppendLine("A completed task is not automatically a story. Do not write a repair procedure, travel route, checklist, or line of operations with a result attached.");
            builder.AppendLine("Tell two linked arcs at once: an OUTER problem the characters solve and an INNER change in trust, courage, care, forgiveness, or belonging.");
            builder.AppendLine("First audit the supplied causal plan. Fix any impossible action or missing mechanism while preserving its goal and target-word order. Then write the story from the repaired plan.");
            builder.AppendLine("Every sentence must change the SAME story through an event, reaction, discovery, decision, or consequence. Practical progress by itself is not enough for every beat.");
            builder.AppendLine("Do not introduce major non-target props to do the useful work. Make the supplied target words carry the plot.");
            builder.AppendLine("SUPPLIED CAUSAL PLAN:");
            builder.AppendLine(causalPlanJson);
            builder.AppendLine();
            builder.AppendLine("NON-NEGOTIABLE STORY SHAPE");
            builder.AppendLine("1. SENTENCE COUNT: write exactly " + words.Count + " complete sentences. Every sentence must contain exactly one new target pair. Write no target-free setup, scenery, transition, reaction, or epilogue sentences.");
            builder.AppendLine("2. OPENING TOTAL — sentence 1 must use I, name the one supporting character, state the concrete problem and success goal, and explain why it matters. State my goal with must or need to, use the first target, and begin the story in this sentence.");
            builder.AppendLine("3. DEVELOPMENT — sentences 2 through " + Mathf.Max(2, words.Count - 1) + " must mix action with human response. Include a reaction, a complication, growing pressure, a choice, its consequence, and a final decision where the sentence budget permits.");
            builder.AppendLine("4. TURN — one middle sentence must reveal something important or force a meaningful choice. Bad turn: another tool is missing. Good turn: a character admits a fear, risks losing trust, chooses another person over convenience, or understands the problem differently.");
            builder.AppendLine("5. CLOSING TOTAL — sentence " + words.Count + " must use the final target to solve the outer problem and show a small human payoff such as relief, trust, gratitude, courage, reunion, or a callback to the opening fear.");
            builder.AppendLine("6. CLOSED-LOOP TEST: compare the first and last sentences. The answer to Did the characters achieve the opening goal? must be an explicit yes in the last sentence.");
            builder.AppendLine("7. Repeat at least one key plain-English person, place, or object from the opening goal in the final result so the closure is unmistakable. Do not repeat its Spanish target pair.");
            builder.AppendLine("8. The last sentence must contain both the final target action or event AND its achieved result, joined clearly with so, and, until, leaving, or letting. End immediately after that payoff.");
            builder.AppendLine("9. Never end with preparation or progress such as Now I grab it, I see the destination ahead, I am ready to leave, The door can open, or I move toward safety. Those are beginnings of missing sentences, not endings.");
            builder.AppendLine("10. TWO-PERSON LIMIT: the narrator is I and there is exactly one named supporting character. Do not introduce or mention any third person, group, crowd, family member, helper, guard, child, neighbor, or passerby.");
            builder.AppendLine("11. HUMAN ARC: at least three sentences must show the one supporting character reacting, speaking indirectly, deciding, helping, refusing, remembering, trusting, or changing feeling.");
            builder.AppendLine("12. ANTI-WORKFLOW RULE: never write more than two consecutive sentences whose main content is finding a tool, moving an object, applying material, checking work, or following route steps.");
            builder.AppendLine();
            builder.AppendLine("ACTION TEST FOR EVERY TARGET");
            builder.AppendLine("Each sentence has one compact story beat: an event, reaction, discovery, decision, or consequence involving one target. Its effect must remain in that sentence.");
            builder.AppendLine("For an object, make it the direct subject or object of a concrete action verb. A character may carry, open, close, wear, strike, repair, turn, pour, cut, block, signal with, move, or otherwise physically act on it.");
            builder.AppendLine("For a body part, feeling, sound, weather event, or abstract target that cannot be handled, it must cause an immediate character choice or action in the SAME sentence.");
            builder.AppendLine("Every sentence must show a character acting, reacting, deciding, helping, preventing, correcting, or achieving something.");
            builder.AppendLine("Across the whole story, use several interpersonal verbs such as asks, tells, admits, offers, refuses, remembers, chooses, trusts, thanks, smiles, comforts, or helps. Do not force the same verb list into every story.");
            builder.AppendLine("A target FAILS if it merely looms, glows, shines, hangs, sits, waits, stands, appears, reflects, echoes, fades, decorates the setting, or is visible in the distance without changing a character's action in that sentence.");
            builder.AppendLine("A target also FAILS if it appears only inside an as-clause, where-clause, background description, comparison, shadow, reflection, costume, procession, display, or list.");
            builder.AppendLine("Never bundle several target words into scenery or an improvised tableau. Each must perform its own necessary job in the plot.");
            builder.AppendLine("PROCEDURE TEST: if the sentences could be numbered as instructions for finishing a task, rewrite them around what the characters want, learn, choose, and feel as events change.");
            builder.AppendLine();
            builder.AppendLine("CAUSALITY TEST");
            builder.AppendLine("Do not connect unrelated actions with then, so, therefore, prompting, or causing. State the real mechanism: what changed physically, what a character learned, or why a new action became necessary.");
            builder.AppendLine("Never use a repeated frame like CHARACTER FEELS SOMETHING, so I USE TARGET. Emotion is a result or cause only when the sentence states the missing fact between them.");
            builder.AppendLine("Each sentence after the first must answer this question: Which exact fact from the previous sentence made this action happen now? If no short answer exists, rewrite both beats.");
            builder.AppendLine("Within a sentence, replace so with because and read the logic backward. If the reason becomes absurd or unrelated, use two genuinely related clauses instead.");
            builder.AppendLine("A target may reveal evidence, create a setback, change a belief, force a choice, or complete the goal. It may not be pasted onto a prewritten emotional arc.");
            builder.AppendLine("Imaginative use of a target is allowed, but it still needs a clear cause and effect. The reader must understand why that target action changes the next moment.");
            builder.AppendLine("Do not use meanwhile, elsewhere, later that day, suddenly, another problem, or scene jumps to move between targets.");
            builder.AppendLine("Bad: A relámpago (lightning) flashes outside. This only describes weather.");
            builder.AppendLine("Good: A relámpago (lightning) reveals the exit sign, so I lead Ana to it. The event changes my action now.");
            builder.AppendLine("Silently delete each target sentence. If the sentences before and after still connect, rewrite that target sentence because it is not doing narrative work.");
            builder.AppendLine();
            builder.AppendLine("MEMORY TEST");
            builder.AppendLine("Turn each plan memory_cue into a specific consequence of the target action. Give every target one distinct, safe, easy-to-picture detail such as a clear movement, shape, color, sound, touch, or changed state.");
            builder.AppendLine("The memorable detail must clarify the target meaning or its result. Do not add random oddness, decorative spectacle, or a second event just to make the sentence vivid.");
            builder.AppendLine("Vary the physical actions and results. Do not repeat a vague pattern such as use it carefully, move forward, or solve the problem.");
            builder.AppendLine("At the payoff, let at least one earlier physical change help the final success. Refer to it with plain English or a pronoun; do not repeat its Spanish target pair.");
            builder.AppendLine();
            builder.AppendLine("READABILITY, STYLE, AND TONE");
            builder.AppendLine("Use active voice, concrete verbs, character choices, small setbacks, and a satisfying practical resolution.");
            builder.AppendLine("The one supporting character must affect events rather than wait passively for me to finish a job. Give that character at least one consequential reaction or choice.");
            builder.AppendLine("Write for adult English learners at A2-B1 level. Use common words and direct verbs. If two words express the same idea, choose the simpler word.");
            builder.AppendLine("Most sentences must contain 10-18 words. Middle sentences may not exceed 20 words. The opening and closing may use up to 24 words because each must state a target action and the story-level goal or outcome.");
            builder.AppendLine("Use periods and simple connectors. Do not use semicolons, colons, em dashes, more than one comma in a sentence, nested clauses, or long while/which/that phrases.");
            builder.AppendLine("Prefer one clear subject, one target-linked action, and one short result clause per sentence. Avoid long lists of objects, tools, locations, or actions.");
            builder.AppendLine("Avoid poetic or difficult words such as shrouded, treacherous, illuminate, amplify, fashioned, dense, and swirling. Use simple words like covered, hard, light, made, thick, and moving instead.");
            builder.AppendLine("Do not use generic filler such as next problem, keep moving, the story, the plot, or the route. Name the concrete problem and the visible result.");
            builder.AppendLine("Use no more than one short atmospheric clause in the entire story. Do not describe distant scenery or ambient sounds unless a character immediately acts on them.");
            builder.AppendLine("Keep the tone bright, everyday, and emotionally safe. No horror, dream logic, uncanny living objects, supernatural transformations, or unrelated parade, dance, spectacle, or celebration.");
            builder.AppendLine("Keep every action physically plausible and reasonably safe. Do not create avoidable danger merely to connect two target words.");
            builder.AppendLine("Write in first person: the narrator and main character is always I. Use I, me, my, we, or our naturally, and never address the reader as you.");
            builder.AppendLine("Use only the narrator I and the single supporting character named in the plan. Places and objects are allowed, but no other person may appear even briefly.");
            builder.AppendLine("Do not mention the memory room, furniture anchors, route instructions, walking directions, mnemonics, or image generation.");
            builder.AppendLine("Use the sentence budget efficiently: there is no extra setup or ending outside the one-sentence-per-target structure.");
            builder.AppendLine();
            builder.AppendLine("WORD FORMAT");
            builder.AppendLine("Every target must appear once as SpanishWord (EnglishMeaning), for example zapato (shoe).");
            builder.AppendLine("Do not add a label before a sentence. Bad: zapato (shoe): I picked it up. Good: I picked up the zapato (shoe) to block the door.");
            builder.AppendLine("Never put two target tokens in the same sentence.");
            builder.AppendLine("GRAMMAR REPLACEMENT TEST: mentally replace the whole SpanishWord (EnglishMeaning) pair with EnglishMeaning. The resulting sentence must be natural English with the correct article, preposition, part of speech, and singular or plural form.");
            builder.AppendLine("Do not repeat the meaning beside the pair or write a tautology such as use a plug to plug something in. Rewrite the action naturally while keeping the exact target pair.");
            builder.AppendLine("Avoid repeating target pairs. If an earlier object must be referenced again, use a pronoun or ordinary synonym without repeating the Spanish word.");
            builder.AppendLine("Return JSON exactly in this minimal shape:");
            builder.AppendLine("{\"fullStory\":\"one continuous story paragraph with no route instructions\",\"items\":[{\"word\":\"zapato\",\"storyOrder\":1}]}");
            builder.AppendLine("Do not include storySegment or any other long text inside items.");
            builder.AppendLine("Do not use quotation marks inside fullStory.");
            builder.AppendLine("items is only the route index: include exactly one item for each target word, with no duplicated or missing target words.");
            builder.AppendLine("storyOrder must start at 1 and follow your chosen order. Do not add extra items for repeated later mentions.");
            builder.AppendLine("fullStory must be one continuous paragraph with no route instructions, no furniture names, and no assigned location names.");
            builder.AppendLine();
            builder.AppendLine("Selected target words:");
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                builder.Append(i + 1)
                    .Append(". Spanish word: ")
                    .Append(word.word)
                    .Append("; English meaning: ")
                    .Append(word.meaning)
                    .AppendLine(".");
            }

            return builder.ToString();
        }

        private static string BuildStoryRepairPrompt(
            List<WordEntry> words,
            string causalPlanJson,
            string rejectedResponse,
            string validationError)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Discard the rejected wording and rewrite it as one complete, warm, character-driven micro-story with a closed ending.");
            builder.AppendLine("Validation failure: " + (validationError ?? "unknown validation error"));
            builder.AppendLine("Return exactly: {\"fullStory\":\"...\",\"items\":[{\"word\":\"target\",\"storyOrder\":1}]}.");
            builder.AppendLine("Write EXACTLY " + words.Count + " complete sentences for the " + words.Count + " selected targets: no extra setup, description, transition, reaction, or epilogue sentence.");
            builder.AppendLine("Every selected target must appear naturally in the fullStory exactly once as SpanishWord (EnglishMeaning), and each must perform an action that changes progress toward the same goal.");
            builder.AppendLine("Every sentence must contain exactly one target pair. Each target gets exactly one sentence containing its need, target-linked action or event, immediate result, and one easy-to-picture change.");
            builder.AppendLine("Sentence 1 is the opening total: use I plus must or need to, name the one supporting character, state the concrete goal, and begin with the first target.");
            builder.AppendLine("The middle sentences are the development: every result causes the next action, and at least one middle beat creates a small setback, discovery, or change of plan.");
            builder.AppendLine("Sentence " + words.Count + " is the closing total: the final target must cause a visible completed result that explicitly solves the opening problem.");
            builder.AppendLine("Do not return a procedure, repair guide, route, checklist, or task log. A sequence of successful operations is still not a story.");
            builder.AppendLine("Write entirely in first person with I as the narrator. Never address the reader as you.");
            builder.AppendLine("Keep exactly one named supporting character active. Do not mention any other person, relative, child, neighbor, helper, guard, group, family, or crowd.");
            builder.AppendLine("At least three beats must show that one supporting character reacting, speaking indirectly, discovering, deciding, helping, refusing, remembering, trusting, or changing feeling.");
            builder.AppendLine("Make the middle turn a revelation or meaningful character choice with a consequence, not merely another broken, missing, or stuck object.");
            builder.AppendLine("Never use more than two tool-use, material-processing, object-moving, or route-following beats in a row.");
            builder.AppendLine("The last sentence must finish the practical goal and answer the opening personal stake with relief, trust, gratitude, courage, reunion, or a clear callback.");
            builder.AppendLine("Repeat at least one key plain-English person, place, or object from the opening goal in that final result without repeating an earlier Spanish target pair.");
            builder.AppendLine("The last sentence must join the final target action and achieved outcome with so, and, until, leaving, or letting. Do not end with something grabbed, noticed, found, ready, ahead, possible, or still in progress.");
            builder.AppendLine("Do not append isolated repair sentences, dream imagery, scenery-only descriptions, room-tour instructions, anchors, or furniture assignments.");
            builder.AppendLine("Keep the causal order from the plan, but repair any implausible action or weak connection.");
            builder.AppendLine("Make the story strictly linear: every sentence must follow from the previous sentence and create the reason for the next sentence. No separate episodes or scene jumps.");
            builder.AppendLine("Do not keep a generic emotional prefix and replace only the target action. Rewrite the whole sentence until its cause, target event, and result describe one indivisible beat.");
            builder.AppendLine("Audit every so, because, therefore, and but. Keep the connector only when both clauses have a specific and believable logical relationship.");
            builder.AppendLine("The target's role may be normal or imaginative. If it cannot be handled directly, it must cause an immediate character decision or action in that same sentence, not a separate description.");
            builder.AppendLine("Use A2-B1 English, common words, and direct verbs. HARD LIMIT: middle sentences may contain at most 20 words; the opening and closing may contain at most 24 words.");
            builder.AppendLine("Use periods, at most one comma per sentence, and no semicolons, colons, or em dashes.");
            builder.AppendLine("Apply the grammar replacement test to every target pair: replacing SpanishWord (EnglishMeaning) with EnglishMeaning must leave a natural English sentence without repeated meaning or tautology.");
            builder.AppendLine("Keep every action safe and physically plausible, use every named prop, and stop only after the original goal is visibly solved in the last sentence.");
            builder.AppendLine("Do not use generic filler such as next problem, keep moving, the story, the plot, or the route. Name the concrete problem and the visible result.");
            builder.AppendLine();
            builder.AppendLine("Selected targets:");
            for (var i = 0; i < words.Count; i++)
            {
                builder.Append(i + 1).Append(". ").Append(words[i].word).Append(" (").Append(words[i].meaning).AppendLine(")");
            }
            builder.AppendLine();
            builder.AppendLine("Causal plan:");
            builder.AppendLine(causalPlanJson ?? string.Empty);
            builder.AppendLine();
            builder.AppendLine("Rejected response:");
            builder.AppendLine(rejectedResponse ?? string.Empty);
            return builder.ToString();
        }

        private static string BuildMinimalStoryRecoveryPrompt(
            List<WordEntry> words,
            string firstFailure,
            string repairFailure)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Start over. Do not patch either rejected draft.");
            builder.AppendLine("Earlier failures: " + (firstFailure ?? "unknown") + " | " + (repairFailure ?? "unknown"));
            builder.AppendLine("Choose one narrative dynamic that fits the targets: search with a reversal, promise under pressure, misunderstanding, discovery that changes meaning, second chance, or secret revealed.");
            builder.AppendLine("The dynamic is not a fixed topic. Invent a concrete everyday premise from the supplied targets.");
            builder.AppendLine("Silently build exactly " + words.Count + " state transitions: state_before -> target event -> state_after.");
            builder.AppendLine("For every N, state_after N must be the concrete reason sentence N+1 happens. Generic progress, hope, trust, or another obstacle is not a causal bridge.");
            builder.AppendLine("Write exactly " + words.Count + " sentences. Sentence 1 states I must or I need to achieve one observable goal with one named supporting character.");
            builder.AppendLine("Each sentence contains exactly one target pair and one indivisible event. Middle sentences have at most 20 words; first and last have at most 24.");
            builder.AppendLine("Use I as narrator, at most one supporting character, and no other people. Include one revelation or meaningful choice in the middle.");
            builder.AppendLine("The last target event must visibly complete the opening goal and resolve the personal stake.");
            builder.AppendLine("Never attach an emotional prefix to an unrelated action with so. Use so or because only when the reverse reading is literally believable.");
            builder.AppendLine("Return only {\"fullStory\":\"...\",\"items\":[{\"word\":\"target\",\"storyOrder\":1}]} with every target listed once.");
            builder.AppendLine("Targets:");
            for (var i = 0; i < words.Count; i++)
            {
                builder.AppendLine((i + 1) + ". " + BuildTargetStoryToken(words[i]?.meaning, words[i]?.word));
            }

            return builder.ToString();
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

            return TryBuildStorySession(envelope, words, model, true, out story, out error);
        }

        private static bool TryParseAndBuildUsableStoryResponse(
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

            // This path is used only after the model's repair pass has failed. Keep structural
            // requirements, exact target coverage, and route isolation, but do not let softer
            // readability preferences erase an otherwise complete participant-facing story.
            return TryBuildStorySession(envelope, words, model, false, out story, out error);
        }

        private static bool TryBuildStorySession(
            GeneratedStoryEnvelope envelope,
            List<WordEntry> words,
            string model,
            bool enforceSoftQuality,
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

            if (HasTargetPairIntegrityProblems(envelope.fullStory, words, out var targetPairError))
            {
                error = targetPairError;
                return false;
            }

            // This is story structure, not a style preference. Keep it active on the
            // degraded acceptance path so a structurally incomplete opening can never be
            // shown as a finished candidate. The controller supplies a complete local
            // fallback if every online candidate still misses this contract.
            if (HasClosedMicroStoryStructureProblems(envelope.fullStory, words, out var structureError))
            {
                error = structureError;
                return false;
            }

            if (HasStoryNarratorOrCharacterProblems(envelope.fullStory, words, out var characterError))
            {
                error = characterError;
                return false;
            }

            if (HasStorySentenceLengthProblems(envelope.fullStory, out var sentenceLengthError))
            {
                error = sentenceLengthError;
                return false;
            }

            if (HasMechanicalCausalScaffold(envelope.fullStory, out var causalScaffoldError))
            {
                error = causalScaffoldError;
                return false;
            }

            // A closed chain can still be a numbered procedure with a result attached. Treat
            // that as a semantic structure failure: the controller will show a complete local
            // character story if both online writing passes cannot satisfy this contract.
            if (LooksLikeProceduralWorkflow(envelope.fullStory, words, out var narrativeError))
            {
                error = narrativeError;
                return false;
            }

            if (ContainsStoryRouteCue(envelope.fullStory, out var routeCueError))
            {
                error = routeCueError;
                return false;
            }

            if (enforceSoftQuality && HasLearnerReadabilityProblems(envelope.fullStory, words, out var readabilityError))
            {
                error = readabilityError;
                return false;
            }

            if (enforceSoftQuality && LooksLikeFragmentedObjectScenes(envelope.fullStory, words, out var qualityError))
            {
                error = qualityError;
                return false;
            }

            if (HasWeakMicroStoryTurn(envelope.fullStory, words, out var arcQualityError))
            {
                error = arcQualityError;
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

        private static bool ContainsAssignedAnchorLeak(
            string fullStory,
            List<AnchorDefinition> assignedAnchors,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory) || assignedAnchors == null || assignedAnchors.Count == 0)
            {
                return false;
            }

            var targetTerms = BuildTargetTermSet(words);
            var seenAnchorTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assignedAnchors.Count; i++)
            {
                var label = assignedAnchors[i]?.label;
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                var term = label.Trim();
                if (term.Length < 3 || targetTerms.Contains(term) || !seenAnchorTerms.Add(term))
                {
                    continue;
                }

                var escapedTerm = Regex.Escape(term).Replace("\\ ", "\\s+");
                if (Regex.IsMatch(fullStory, @"\b" + escapedTerm + @"\b", RegexOptions.IgnoreCase))
                {
                    error = "Story includes assigned anchor/furniture term '" + term + "'. Anchors must not influence the story text.";
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> BuildTargetTermSet(List<WordEntry> words)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (words == null)
            {
                return terms;
            }

            for (int i = 0; i < words.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(words[i]?.word))
                {
                    terms.Add(words[i].word.Trim());
                }

                if (!string.IsNullOrWhiteSpace(words[i]?.meaning))
                {
                    terms.Add(words[i].meaning.Trim());
                }
            }

            return terms;
        }

        private static bool HasTargetPairIntegrityProblems(
            string fullStory,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (words == null)
            {
                return false;
            }

            for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
            {
                var occurrenceCount = CountTargetPairOccurrences(fullStory, words[wordIndex]);
                if (occurrenceCount != 1)
                {
                    error = "Story must contain target pair " +
                            BuildTargetStoryToken(words[wordIndex]?.meaning, words[wordIndex]?.word) +
                            " exactly once, but found " + occurrenceCount + ".";
                    return true;
                }
            }

            return false;
        }

        private static bool HasClosedMicroStoryStructureProblems(
            string fullStory,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory) || words == null || words.Count == 0)
            {
                return false;
            }

            var sentences = SplitStorySentences(fullStory);
            if (sentences.Count != words.Count)
            {
                error = "Story has " + sentences.Count + " sentences for " + words.Count +
                        " targets. Write exactly one target-bearing sentence per target, with no separate setup, description, transition, or epilogue sentences.";
                return true;
            }

            for (var sentenceIndex = 0; sentenceIndex < sentences.Count; sentenceIndex++)
            {
                var sentence = sentences[sentenceIndex];
                if (!Regex.IsMatch(sentence, @"[.!]$"))
                {
                    error = "Sentence " + (sentenceIndex + 1) +
                            " must be a complete statement ending with a period or exclamation mark.";
                    return true;
                }

                if (sentence.IndexOf(';') >= 0)
                {
                    error = "Sentence " + (sentenceIndex + 1) +
                            " uses a semicolon to hide an extra beat. Keep one simple target beat in one sentence.";
                    return true;
                }

                var pairCount = 0;
                for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
                {
                    pairCount += CountTargetPairOccurrences(sentence, words[wordIndex]);
                }

                if (pairCount != 1)
                {
                    error = "Sentence " + (sentenceIndex + 1) + " contains " + pairCount +
                            " target pairs. Every sentence must contain exactly one target pair and perform one necessary story beat.";
                    return true;
                }
            }

            var opening = sentences[0];
            if (!Regex.IsMatch(opening, @"\b(?:must|need(?:s)?\s+to|have\s+to|has\s+to)\b", RegexOptions.IgnoreCase))
            {
                error = "Sentence 1 must state the whole problem and success goal with must or need to while using the first target.";
                return true;
            }

            var ending = sentences[sentences.Count - 1];
            var endingAfterTarget = ExtractTextAfterTargetPair(ending, words);
            if (LooksLikeUnresolvedStoryEnding(ending))
            {
                error = "The last sentence still shows preparation or progress. Use the final target and explicitly state the completed result that solves sentence 1's goal.";
                return true;
            }

            if (!Regex.IsMatch(
                    endingAfterTarget,
                    @"\b(?:so|and|until)\b|,\s*(?:leaving|letting|bringing|ending|allowing|making)\b",
                    RegexOptions.IgnoreCase))
            {
                error = "The last sentence must contain both the final target action and its achieved outcome, joined clearly in one sentence.";
                return true;
            }

            if (!HasExplicitCompletedOutcome(endingAfterTarget))
            {
                error = "The words after the final target do not state a completed outcome. Say what arrived, entered, finished, became safe, was repaired, or otherwise achieved the opening goal.";
                return true;
            }

            if (!EndingRepeatsOpeningGoalKeyword(opening, ending, words))
            {
                error = "The last sentence does not echo any key person, place, or object from sentence 1's goal. State the achieved original outcome, not only the last obstacle.";
                return true;
            }

            return false;
        }

        private static string ExtractTextAfterTargetPair(string sentence, List<WordEntry> words)
        {
            if (string.IsNullOrWhiteSpace(sentence) || words == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < words.Count; i++)
            {
                var index = FindStoryWordIndex(sentence, words[i]?.meaning, words[i]?.word);
                if (index < 0)
                {
                    continue;
                }

                var endIndex = FindTargetTokenEnd(sentence, words[i], index);
                return endIndex >= 0 && endIndex < sentence.Length
                    ? sentence.Substring(endIndex)
                    : string.Empty;
            }

            return string.Empty;
        }

        private static bool HasExplicitCompletedOutcome(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var nonFiniteOutcome = Regex.Match(
                text,
                @"\b(?:(?:ready|about|prepar(?:e|es|ing)|start(?:s|ed|ing)?|begin(?:s|ning)?|tr(?:y|ies|ied|ying)|attempt(?:s|ed|ing)?)\s+to|(?:can|could|will|may|might)\s+(?:now\s+)?)\s*(?:arrive|reach|enter|deliver|receive|finish|complete|solve|repair|fix|rescue|save|return|recover|restore|secure|escape|unlock|open|close|stop|end|win|work|succeed|reunite|clean)\b",
                RegexOptions.IgnoreCase);
            if (nonFiniteOutcome.Success &&
                !HasIndependentCompletedResultAfter(text, nonFiniteOutcome.Index + nonFiniteOutcome.Length))
            {
                return false;
            }

            return Regex.IsMatch(
                text,
                @"\b(?:arriv(?:e|es|ed)|reach(?:es|ed)?|enter(?:s|ed)?|deliver(?:s|ed)?|receiv(?:e|es|ed)|finish(?:es|ed)?|complet(?:e|es|ed)|solv(?:e|es|ed)|repair(?:s|ed)?|fix(?:es|ed)?|rescu(?:e|es|ed)|sav(?:e|es|ed)|return(?:s|ed)?|recover(?:s|ed)?|restor(?:e|es|ed)|secur(?:e|es|ed)|escap(?:e|es|ed)|unlock(?:s|ed)?|open(?:s|ed)?|clos(?:e|es|ed)|stop(?:s|ped)?|end(?:s|ed)?|win(?:s)?|won|work(?:s|ed)?|succeed(?:s|ed)?|reunit(?:e|es|ed)|done|built|clean(?:ed)?)\b|\b(?:is|are|becomes?|stays?|gets?)\s+(?:safe|safer|dry|inside|free|open|closed|complete|finished|over|together)\b|\bno\s+longer\b",
                RegexOptions.IgnoreCase);
        }

        private static bool EndingRepeatsOpeningGoalKeyword(
            string opening,
            string ending,
            List<WordEntry> words)
        {
            var goalMatch = Regex.Match(
                opening ?? string.Empty,
                @"\b(?:must|need(?:s)?\s+to|have\s+to|has\s+to)\s+(?<goal>[^,.;!?]+)",
                RegexOptions.IgnoreCase);
            if (!goalMatch.Success)
            {
                return false;
            }

            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "and", "but", "for", "with", "from", "into", "onto", "over", "under",
                "before", "after", "while", "because", "that", "this", "these", "those", "your",
                "you", "i", "me", "my", "we", "us", "our", "their", "his", "her", "its", "one", "same", "today", "tonight",
                "must", "need", "needs", "have", "has", "get", "make", "use", "find", "reach",
                "enter", "bring", "carry", "deliver", "repair", "fix", "stop", "save", "keep",
                "open", "close", "return", "take", "move", "protect", "rescue", "solve", "finish",
                "help", "quickly", "safely", "together"
            };

            if (words != null)
            {
                for (var i = 0; i < words.Count; i++)
                {
                    AddStoryWordsToSet(stopWords, words[i]?.word);
                }
            }

            var endingWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddStoryWordsToSet(endingWords, ending);
            var goalWords = Regex.Matches(goalMatch.Groups["goal"].Value, @"[\p{L}\p{N}]+(?:['\u2019-][\p{L}\p{N}]+)*");
            var hasCandidate = false;
            for (var i = 0; i < goalWords.Count; i++)
            {
                var candidate = goalWords[i].Value.Trim().ToLowerInvariant();
                if (candidate.Length < 3 || stopWords.Contains(candidate))
                {
                    continue;
                }

                hasCandidate = true;
                if (endingWords.Contains(candidate))
                {
                    return true;
                }
            }

            // If the goal contains only pronouns and simple verbs, there is no reliable noun
            // to compare. Let the other closed-ending checks decide instead of false-rejecting.
            return !hasCandidate;
        }

        private static void AddStoryWordsToSet(HashSet<string> destination, string text)
        {
            if (destination == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var matches = Regex.Matches(text, @"[\p{L}\p{N}]+(?:['\u2019-][\p{L}\p{N}]+)*");
            for (var i = 0; i < matches.Count; i++)
            {
                destination.Add(matches[i].Value.Trim().ToLowerInvariant());
            }
        }

        private static bool LooksLikeUnresolvedStoryEnding(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence) || sentence.TrimEnd().EndsWith("?", StringComparison.Ordinal))
            {
                return true;
            }

            var unfinishedPatterns = new[]
            {
                @"\b(?:toward|towards|ahead|onward)\b",
                @"\b(?:in\s+sight|within\s+reach|on\s+the\s+way)\b",
                @"\b(?:ready|about|prepar(?:e|es|ing)|start(?:s|ed|ing)?|begin(?:s|ning)?|tr(?:y|ies|ied|ying)|attempt(?:s|ed|ing)?)\s+to\b",
                @"\b(?:still\s+need(?:s)?\s+to|must\s+still|one\s+more\s+(?:barrier|problem|step|door|gate|task))\b",
                @"\b(?:can|could|will|may|might)\s+(?:now\s+)?(?:begin|start|try|head|go|leave|arrive|reach|enter|deliver|receive|finish|complete|solve|repair|fix|rescue|save|return|recover|restore|secure|escape|unlock|open|close|stop|end|win|work|succeed|reunite|clean)\b"
            };

            for (var i = 0; i < unfinishedPatterns.Length; i++)
            {
                var unfinished = Regex.Match(sentence, unfinishedPatterns[i], RegexOptions.IgnoreCase);
                if (unfinished.Success &&
                    !HasIndependentCompletedResultAfter(sentence, unfinished.Index + unfinished.Length))
                {
                    return true;
                }
            }

            var acquisitionOnly = Regex.IsMatch(
                sentence,
                @"^(?:(?:At\s+last|Finally),?\s+|Now\s+|Then\s+)?I\s+(?:grab|take|pick\s+up|reach\s+for|find|notice|see|hear)\b",
                RegexOptions.IgnoreCase);
            var hasResultLink = Regex.IsMatch(
                sentence,
                @"\b(?:so|and|until)\b|,\s*(?:leaving|letting|bringing|ending|allowing|making)\b",
                RegexOptions.IgnoreCase);
            return acquisitionOnly && !hasResultLink;
        }

        private static bool HasIndependentCompletedResultAfter(string text, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length)
            {
                return false;
            }

            var tail = text.Substring(startIndex);
            return Regex.IsMatch(
                tail,
                @"\b(?:but|so|and|until)\b[^.!?]{0,120}\b(?:(?:arrived|reached|entered|delivered|received|finished|completed|solved|repaired|fixed|rescued|saved|returned|recovered|restored|secured|escaped|unlocked|opened|closed|stopped|ended|won|worked|succeeded|reunited|built|cleaned)|(?:arrives|reaches|enters|delivers|receives|finishes|completes|solves|repairs|fixes|rescues|saves|returns|recovers|restores|secures|escapes|unlocks|opens|closes|stops|ends|wins|works|succeeds|reunites|cleans)|(?:I|we)\s+(?:arrive|reach|enter|deliver|receive|finish|complete|solve|repair|fix|rescue|save|return|recover|restore|secure|escape|unlock|open|close|stop|end|win|work|succeed|reunite|clean)|(?:is|are|becomes?|stays?|gets?)\s+(?:safe|safer|dry|inside|free|open|closed|complete|finished|over|together))\b",
                RegexOptions.IgnoreCase);
        }

        private static bool HasStoryNarratorOrCharacterProblems(
            string fullStory,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return false;
            }

            var sentences = SplitStorySentences(fullStory);
            if (sentences.Count == 0 || !Regex.IsMatch(sentences[0], @"\bI\b"))
            {
                error = "The story must use I as the first-person narrator from sentence 1.";
                return true;
            }

            if (Regex.IsMatch(fullStory, @"\b(?:you|your|yours|yourself)\b", RegexOptions.IgnoreCase))
            {
                error = "The story addresses the reader as you. Rewrite the protagonist consistently as I, me, my, we, or our.";
                return true;
            }

            if (Regex.IsMatch(
                    fullStory,
                    @"\b(?:friends|children|parents|people|neighbors|helpers|guards|workers|classmates|passersby|crowd|group|team|family|someone|somebody)\b|\b(?:a|an|the|another)\s+(?:friend|child|parent|neighbor|helper|guard|worker|classmate|passerby|stranger|teacher|boss|driver|doctor|nurse|clerk|customer|owner|manager|waiter|waitress|police\s+officer|firefighter|mechanic)\b|\b(?:his|her|their)\s+(?:father|mother|parent|child|son|daughter|brother|sister|friend|neighbor|teacher|boss|helper|guard)\b",
                    RegexOptions.IgnoreCase))
            {
                error = "The story introduces extra people. Keep only the narrator I and at most one supporting character.";
                return true;
            }

            var ignoredCapitalWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "I", "At", "As", "After", "Before", "Because", "But", "By", "During", "Finally",
                "For", "If", "In", "Inside", "Later", "Meanwhile", "Near", "Next", "Now", "On",
                "Once", "Outside", "Since", "So", "That", "The", "Then", "This", "Through", "To",
                "Today", "Tonight", "Until", "When", "While", "With", "Without"
            };
            if (words != null)
            {
                for (var i = 0; i < words.Count; i++)
                {
                    AddStoryWordsToSet(ignoredCapitalWords, words[i]?.word);
                    AddStoryWordsToSet(ignoredCapitalWords, words[i]?.meaning);
                }
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var capitalWords = Regex.Matches(fullStory, @"\b[A-Z][a-z]{1,20}\b");
            for (var i = 0; i < capitalWords.Count; i++)
            {
                var candidate = capitalWords[i].Value;
                if (!ignoredCapitalWords.Contains(candidate))
                {
                    names.Add(candidate);
                }
            }

            if (names.Count > 1)
            {
                error = "The story names more than one supporting character (" + string.Join(", ", names) + "). Keep at most two people total: I and one other person.";
                return true;
            }

            return false;
        }

        private static bool HasStorySentenceLengthProblems(string fullStory, out string error)
        {
            error = string.Empty;
            var sentences = SplitStorySentences(fullStory);
            for (var i = 0; i < sentences.Count; i++)
            {
                var wordCount = CountStoryWords(sentences[i]);
                var maximumWords = i == 0 || i == sentences.Count - 1 ? 24 : 20;
                if (wordCount <= maximumWords)
                {
                    continue;
                }

                error = "Sentence " + (i + 1) + " has " + wordCount +
                        " words. The hard limit is " + maximumWords +
                        " words for this " + (i == 0 || i == sentences.Count - 1 ? "opening or closing" : "middle") + " sentence.";
                return true;
            }

            return false;
        }

        private static bool HasMechanicalCausalScaffold(string fullStory, out string error)
        {
            error = string.Empty;
            var sentences = SplitStorySentences(fullStory);
            if (sentences.Count < 4)
            {
                return false;
            }

            var soConnectorCount = 0;
            for (var i = 0; i < sentences.Count; i++)
            {
                if (Regex.IsMatch(sentences[i], @",\s*so\b", RegexOptions.IgnoreCase))
                {
                    soConnectorCount++;
                }
            }

            if (soConnectorCount < Mathf.CeilToInt(sentences.Count * 0.7f))
            {
                return false;
            }

            error = "The story uses ', so' as a repeated sentence template instead of explaining different real links. Vary the sentence shapes and state the concrete cause for each action.";
            return true;
        }

        private static bool LooksLikeProceduralWorkflow(
            string fullStory,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory) || words == null || words.Count < 4)
            {
                return false;
            }

            var sentences = SplitStorySentences(fullStory);
            var humanBeatCount = 0;
            var procedureBeatCount = 0;
            var currentProcedureRun = 0;
            var longestProcedureRun = 0;
            const string humanPattern = @"\b(?:asks?|tells?|says?|calls?|answers?|admits?|promises?|remembers?|realizes?|decides?|chooses?|hesitates?|worries?|hopes?|fears?|smiles?|laughs?|cries?|thanks?|forgives?|trusts?|comforts?|helps?|offers?|refuses?|cares?|feels?|relieved|proud|lonely|afraid|brave|forgotten)\b";
            const string procedureVerb = @"(?:begin|begins|start|starts|open|opens|close|closes|turn|turns|press|presses|pull|pulls|push|pushes|lift|lifts|carry|carries|move|moves|place|places|set|sets|attach|attaches|clamp|clamps|pour|pours|cut|cuts|tighten|tightens|loosen|loosens|check|checks|inspect|inspects|search|searches|find|finds|apply|applies|coat|coats|seal|seals|wheel|wheels|repair|repairs|fix|fixes|test|tests)";

            for (var i = 0; i < sentences.Count; i++)
            {
                var sentence = sentences[i];
                if (Regex.IsMatch(sentence, humanPattern, RegexOptions.IgnoreCase))
                {
                    humanBeatCount++;
                }

                var procedureVerbCount = Regex.Matches(
                    sentence,
                    @"\b" + procedureVerb + @"\b",
                    RegexOptions.IgnoreCase).Count;
                var procedureLed = Regex.IsMatch(
                    sentence,
                    @"^(?:At\s+[^,]+,?\s+)?I\s+(?:slowly\s+|carefully\s+)?" + procedureVerb + @"\b",
                    RegexOptions.IgnoreCase);
                if (procedureLed || procedureVerbCount >= 2)
                {
                    procedureBeatCount++;
                    currentProcedureRun++;
                    longestProcedureRun = Mathf.Max(longestProcedureRun, currentProcedureRun);
                }
                else
                {
                    currentProcedureRun = 0;
                }
            }

            var proceduralMajority = procedureBeatCount >= Mathf.Max(3, Mathf.CeilToInt(sentences.Count * 0.5f));
            var requiredHumanBeats = sentences.Count >= 6 ? 3 : 2;
            if (longestProcedureRun < 3 && (!proceduralMajority || humanBeatCount >= requiredHumanBeats))
            {
                return false;
            }

            error = "The text is a workflow rather than a mini-story: too many sentences are task operations and too few show a character reaction, discovery, choice, relationship change, or emotional payoff.";
            return true;
        }

        private static bool HasWeakMicroStoryTurn(
            string fullStory,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory) || words == null || words.Count < 4)
            {
                return false;
            }

            var sentences = SplitStorySentences(fullStory);
            for (var i = 1; i < sentences.Count - 1; i++)
            {
                if (Regex.IsMatch(
                        sentences[i],
                        @"\b(?:decid(?:e|es|ed|ing)|choos(?:e|es|ing)|chose|realiz(?:e|es|ed|ing)|discover(?:s|ed|ing)?|learn(?:s|ed|ing)?|admit(?:s|ted|ting)?|refus(?:e|es|ed|ing)|offer(?:s|ed|ing)?|remember(?:s|ed|ing)?|trust(?:s|ed|ing)?|risk(?:s|ed|ing)?|confess(?:es|ed|ing)?|forgiv(?:e|es|ing)|forgave|understand(?:s|ing)?|understood|change(?:s|d)?\s+(?:his|her|their)\s+mind)\b",
                        RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }

            error = "The middle has no character-driven turn. Add a discovery or meaningful choice with a consequence; another physical obstacle or task step is not enough.";
            return true;
        }

        private static bool HasLearnerReadabilityProblems(
            string fullStory,
            List<WordEntry> words,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fullStory))
            {
                return false;
            }

            var sentences = SplitStorySentences(fullStory);
            if (sentences.Count == 0)
            {
                error = "Story has no complete readable sentences.";
                return true;
            }

            var totalWordCount = 0;
            for (var sentenceIndex = 0; sentenceIndex < sentences.Count; sentenceIndex++)
            {
                var sentence = sentences[sentenceIndex];
                var wordCount = CountStoryWords(sentence);
                totalWordCount += wordCount;

                // Opening and closing carry the story-level goal/outcome, so they receive a
                // slightly larger hard budget than the one-action middle beats.
                var isOpeningOrClosing = sentenceIndex == 0 || sentenceIndex == sentences.Count - 1;
                var maximumWords = isOpeningOrClosing ? 24 : 20;
                if (wordCount > maximumWords)
                {
                    error = "Sentence " + (sentenceIndex + 1) + " has " + wordCount +
                            " words. Rewrite the same one-target beat with " +
                            (isOpeningOrClosing ? "at most 24 words." : "at most 20 words.");
                    return true;
                }

                if (sentence.IndexOf(';') >= 0 || sentence.IndexOf(':') >= 0 ||
                    sentence.IndexOf('\u2013') >= 0 || sentence.IndexOf('\u2014') >= 0)
                {
                    error = "Sentence " + (sentenceIndex + 1) +
                            " uses dense punctuation. Rewrite it with short sentences and periods.";
                    return true;
                }

                if (Regex.Matches(sentence, ",").Count > 1)
                {
                    error = "Sentence " + (sentenceIndex + 1) +
                            " has more than one comma. Split its clauses into shorter sentences.";
                    return true;
                }

                var targetPairCount = 0;
                if (words != null)
                {
                    for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
                    {
                        var occurrenceCount = CountTargetPairOccurrences(sentence, words[wordIndex]);
                        if (occurrenceCount > 1)
                        {
                            error = "Sentence " + (sentenceIndex + 1) + " repeats target pair " +
                                    BuildTargetStoryToken(words[wordIndex]?.meaning, words[wordIndex]?.word) + ".";
                            return true;
                        }

                        if (occurrenceCount == 1)
                        {
                            targetPairCount++;
                        }
                    }
                }

                if (targetPairCount > 1)
                {
                    error = "Sentence " + (sentenceIndex + 1) +
                            " contains more than one target pair. Give each target its own short beat.";
                    return true;
                }
            }

            if (words != null && words.Count > 0)
            {
                var maximumTotalWords = Mathf.Max(28, (words.Count * 20) + 8);
                if (totalWordCount > maximumTotalWords)
                {
                    error = "Story has " + totalWordCount + " words. Shorten each one-target sentence while keeping the opening goal and final outcome.";
                    return true;
                }
            }

            var averageWordsPerSentence = totalWordCount / (float)sentences.Count;
            if (averageWordsPerSentence > 19f)
            {
                error = "Story averages " + averageWordsPerSentence.ToString("0.0") +
                        " words per sentence. Simplify each fixed one-target sentence for A2-B1 readers.";
                return true;
            }

            return false;
        }

        private static int CountStoryWords(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? 0
                : Regex.Matches(text, @"[\p{L}\p{N}]+(?:['\u2019\-][\p{L}\p{N}]+)*").Count;
        }

        private static int CountTargetPairOccurrences(string text, WordEntry word)
        {
            if (string.IsNullOrWhiteSpace(text) || word == null ||
                string.IsNullOrWhiteSpace(word.word) || string.IsNullOrWhiteSpace(word.meaning))
            {
                return 0;
            }

            var spanish = Regex.Escape(word.word.Trim()).Replace("\\ ", @"\s+");
            var meaning = Regex.Escape(word.meaning.Trim()).Replace("\\ ", @"\s+");
            var boundary = @"(?<![\p{L}\p{N}_])";
            var canonicalPattern = boundary + spanish + @"\s*\(\s*" + meaning + @"\s*\)";
            var legacyPattern = boundary + meaning + @"\s*\(\s*" + spanish + @"\s*\)";
            var canonicalCount = Regex.Matches(text, canonicalPattern, RegexOptions.IgnoreCase).Count;
            if (string.Equals(word.word.Trim(), word.meaning.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return canonicalCount;
            }

            return canonicalCount + Regex.Matches(text, legacyPattern, RegexOptions.IgnoreCase).Count;
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
            // The validated fullStory is the source of truth. A model may still emit an
            // undocumented storySegment field, but trusting it can make the per-word cards
            // differ from the complete story the participant selected.
            storyBeat = string.Empty;
            var storyIndex = FindStoryWordIndex(fullStory, sourceWord.meaning, sourceWord.word);
            if (storyIndex < 0)
            {
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
            // Hard validation already proves that this exact fullStory sentence contains
            // one and only one target pair. Preserve it unchanged for the per-word display.
            return EnsureSentenceEnd(normalized);
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
            // The prompt and validator already require a first-person narrator. Rewriting
            // names after validation could erase the one supporting character.
            return Regex.Replace(fullStory ?? string.Empty, @"\s+", " ").Trim();
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
                if (!ContainsMeaningWordPair(singleSegment, sourceWord.meaning, sourceWord.word) ||
                    IsWeakStorySegment(singleSegment) ||
                    IsWeakOpeningStorySegment(singleSegment, routeIndex))
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

        private static string NormalizeStoryBeatTargetTokenLegacy(string storyBeat, WordEntry sourceWord)
        {
            if (string.IsNullOrWhiteSpace(storyBeat) || sourceWord == null)
            {
                return storyBeat ?? string.Empty;
            }

            var meaning = string.IsNullOrWhiteSpace(sourceWord.meaning) ? "target meaning" : sourceWord.meaning.Trim();
            var word = string.IsNullOrWhiteSpace(sourceWord.word) ? "word" : sourceWord.word.Trim();
            var token = BuildTargetStoryToken(meaning, word);
            var cleaned = storyBeat.Trim();

            var escapedMeaning = Regex.Escape(meaning).Replace("\\ ", "\\s+");
            var escapedWord = Regex.Escape(word);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedWord + @"\s*\(\s*\*?" + escapedMeaning + @"\s*\(\s*" + escapedWord + @"\s*\)\*?\s*\)\*?",
                token,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedMeaning + @"\s*\(\s*" + escapedWord + @"\s*\)\*?",
                token,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedWord + @"\s*\(\s*" + escapedMeaning + @"\s*\)\*?",
                token,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\*?" + escapedWord + @"\*?\s*\(\s*" + escapedMeaning + @"\s*\)",
                token,
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"^\s*" + Regex.Escape(token) + @"\s*[:：\-–—]\s*",
                string.Empty,
                RegexOptions.IgnoreCase);
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
            if (IsStoryOnlyRedesignEnabled())
            {
                onError?.Invoke("Mnemonic generation is disabled in the story-only redesign. Use GenerateStory instead.");
                yield break;
            }

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

            var items = new List<MnemonicItemData>();
            var normalizedEndpoint = endpoint.Trim();
            var normalizedModel = model.Trim();
            var totalWords = words == null ? 0 : words.Count;
            for (int offset = 0; offset < totalWords; offset += MnemonicChunkSize)
            {
                var chunkCount = Math.Min(MnemonicChunkSize, totalWords - offset);
                var chunkWords = words.GetRange(offset, chunkCount);
                var chunkAnchors = assignedAnchors != null && assignedAnchors.Count >= offset + chunkCount
                    ? assignedAnchors.GetRange(offset, chunkCount)
                    : null;
                List<MnemonicItemData> chunkItems = null;
                string chunkError = null;

                yield return GenerateMnemonicChunk(
                    normalizedEndpoint,
                    normalizedModel,
                    chunkWords,
                    chunkAnchors,
                    offset,
                    totalWords,
                    generatedItems => chunkItems = generatedItems,
                    error => chunkError = error);

                if (!string.IsNullOrWhiteSpace(chunkError))
                {
                    onError?.Invoke($"Ollama request failed for words {offset + 1}-{offset + chunkCount}: {chunkError}");
                    yield break;
                }

                if (chunkItems == null || chunkItems.Count == 0)
                {
                    onError?.Invoke($"Ollama returned empty mnemonic data for words {offset + 1}-{offset + chunkCount}.");
                    yield break;
                }

                items.AddRange(chunkItems);
            }

            onSuccess?.Invoke(items);
        }

        public IEnumerator GenerateGeminiMnemonics(
            string apiKey,
            string model,
            List<WordEntry> words,
            Action<List<MnemonicItemData>> onSuccess,
            Action<string> onError,
            List<AnchorDefinition> assignedAnchors = null)
        {
            if (IsStoryOnlyRedesignEnabled())
            {
                onError?.Invoke("Gemini mnemonic generation is disabled in the story-only redesign. Use GenerateStory instead.");
                yield break;
            }

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

            var items = new List<MnemonicItemData>();
            var normalizedApiKey = apiKey.Trim();
            var normalizedModel = model.Trim();
            var totalWords = words == null ? 0 : words.Count;
            for (int offset = 0; offset < totalWords; offset += GeminiMnemonicChunkSize)
            {
                var chunkCount = Math.Min(GeminiMnemonicChunkSize, totalWords - offset);
                var chunkWords = words.GetRange(offset, chunkCount);
                var chunkAnchors = assignedAnchors != null && assignedAnchors.Count >= offset + chunkCount
                    ? assignedAnchors.GetRange(offset, chunkCount)
                    : null;
                List<MnemonicItemData> chunkItems = null;
                string chunkError = null;

                yield return GenerateGeminiMnemonicChunk(
                    normalizedApiKey,
                    normalizedModel,
                    chunkWords,
                    chunkAnchors,
                    offset,
                    totalWords,
                    generatedItems => chunkItems = generatedItems,
                    error => chunkError = error);

                if (!string.IsNullOrWhiteSpace(chunkError))
                {
                    onError?.Invoke($"Gemini request failed for words {offset + 1}-{offset + chunkCount}: {chunkError}");
                    yield break;
                }

                if (chunkItems == null || chunkItems.Count == 0)
                {
                    onError?.Invoke($"Gemini returned empty mnemonic data for words {offset + 1}-{offset + chunkCount}.");
                    yield break;
                }

                items.AddRange(chunkItems);
            }

            onSuccess?.Invoke(items);
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
            if (IsStoryOnlyRedesignEnabled())
            {
                onError?.Invoke("Mnemonic regeneration is disabled in the story-only redesign.");
                yield break;
            }

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

            if (word == null || string.IsNullOrWhiteSpace(word.word))
            {
                onError?.Invoke("Target word is empty.");
                yield break;
            }

            var normalizedEndpoint = endpoint.Trim();
            var normalizedModel = model.Trim();
            var fallbackAnchor = RoomSpecCatalog.TryGetAnchor(anchorId, out var existingAnchor)
                ? existingAnchor
                : RoomSpecCatalog.GetAssignmentAnchor(itemIndex, Mathf.Max(1, totalWords));
            var resolvedAnchorId = string.IsNullOrWhiteSpace(anchorId) ? fallbackAnchor.id : anchorId;
            var resolvedAnchorLabel = string.IsNullOrWhiteSpace(anchorLabel) ? fallbackAnchor.label : anchorLabel;

            GeneratedMnemonicEnvelope visualCueEnvelope = null;
            string visualCueError = null;
            yield return RequestMnemonicEnvelope(
                normalizedEndpoint,
                normalizedModel,
                BuildSingleVisualCueRegenerationPrompt(
                    word,
                    itemIndex,
                    totalWords,
                    resolvedAnchorId,
                    resolvedAnchorLabel,
                    rejectedVisualCue,
                    rejectedMnemonic,
                    rejectedImagePrompt,
                    rejectionReason),
                "You write JSON for Call 1 of a Unity memory-palace app. Return one replacement image-focused visual cue scene with association prompts and exactly four image prompt candidates. Do not generate mnemonic or story text. No markdown or commentary.",
                0.48f,
                1400,
                "replacement visual cue",
                null,
                parsed => visualCueEnvelope = parsed,
                error => visualCueError = error);

            if (!string.IsNullOrWhiteSpace(visualCueError))
            {
                onError?.Invoke(visualCueError);
                yield break;
            }

            var visualItems = AlignGeneratedItemsToWords(new List<WordEntry> { word }, visualCueEnvelope?.items);
            var visualCue = visualItems.Count > 0 ? visualItems[0] : null;
            if (visualCue == null)
            {
                onError?.Invoke("Ollama returned empty replacement visual cue data.");
                yield break;
            }

            GeneratedMnemonicEnvelope mnemonicLinkEnvelope = null;
            string mnemonicLinkError = null;
            yield return RequestMnemonicEnvelope(
                normalizedEndpoint,
                normalizedModel,
                BuildSingleMnemonicLinkPrompt(word, resolvedAnchorId, resolvedAnchorLabel, visualCue),
                "You write JSON for Call 2 of a Unity memory-palace app. Return only the final learner-facing mnemonic_en plus mnemonic_mode and hook_judge. Use STORY_ONLY when the hook is weak. No markdown or commentary.",
                0.38f,
                1200,
                "replacement final mnemonic",
                null,
                parsed => mnemonicLinkEnvelope = parsed,
                error => mnemonicLinkError = error);

            if (!string.IsNullOrWhiteSpace(mnemonicLinkError))
            {
                onError?.Invoke(mnemonicLinkError);
                yield break;
            }

            var mnemonicItems = AlignGeneratedItemsToWords(new List<WordEntry> { word }, mnemonicLinkEnvelope?.items);
            var mnemonicLink = mnemonicItems.Count > 0 ? mnemonicItems[0] : null;
            var generated = MergeGeneratedMnemonicFields(word, visualCue, mnemonicLink);

            var replacement = BuildMnemonicItemData(
                word,
                generated,
                itemIndex,
                resolvedAnchorId,
                resolvedAnchorLabel);

            onSuccess?.Invoke(replacement);
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
            if (IsStoryOnlyRedesignEnabled())
            {
                onError?.Invoke("Gemini mnemonic regeneration is disabled in the story-only redesign.");
                yield break;
            }

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

            if (word == null || string.IsNullOrWhiteSpace(word.word))
            {
                onError?.Invoke("Target word is empty.");
                yield break;
            }

            var normalizedApiKey = apiKey.Trim();
            var normalizedModel = model.Trim();
            var fallbackAnchor = RoomSpecCatalog.TryGetAnchor(anchorId, out var existingAnchor)
                ? existingAnchor
                : RoomSpecCatalog.GetAssignmentAnchor(itemIndex, Mathf.Max(1, totalWords));
            var resolvedAnchorId = string.IsNullOrWhiteSpace(anchorId) ? fallbackAnchor.id : anchorId;
            var resolvedAnchorLabel = string.IsNullOrWhiteSpace(anchorLabel) ? fallbackAnchor.label : anchorLabel;

            GeneratedMnemonicEnvelope visualCueEnvelope = null;
            string visualCueError = null;
            yield return RequestGeminiMnemonicEnvelope(
                normalizedApiKey,
                normalizedModel,
                BuildSingleVisualCueRegenerationPrompt(
                    word,
                    itemIndex,
                    totalWords,
                    resolvedAnchorId,
                    resolvedAnchorLabel,
                    rejectedVisualCue,
                    rejectedMnemonic,
                    rejectedImagePrompt,
                    rejectionReason),
                "You write JSON for Call 1 of a Unity memory-palace app. Return one replacement image-focused visual cue scene with association prompts and exactly four image prompt candidates. Do not generate mnemonic or story text. No markdown or commentary.",
                0.42f,
                2200,
                "replacement visual cue",
                null,
                parsed => visualCueEnvelope = parsed,
                error => visualCueError = error);

            if (!string.IsNullOrWhiteSpace(visualCueError))
            {
                onError?.Invoke(visualCueError);
                yield break;
            }

            var visualItems = AlignGeneratedItemsToWords(new List<WordEntry> { word }, visualCueEnvelope?.items);
            var visualCue = visualItems.Count > 0 ? visualItems[0] : null;
            if (visualCue == null)
            {
                onError?.Invoke("Gemini returned empty replacement visual cue data.");
                yield break;
            }

            GeneratedMnemonicEnvelope mnemonicLinkEnvelope = null;
            string mnemonicLinkError = null;
            yield return RequestGeminiMnemonicEnvelope(
                normalizedApiKey,
                normalizedModel,
                BuildSingleMnemonicLinkPrompt(word, resolvedAnchorId, resolvedAnchorLabel, visualCue),
                "You write JSON for Call 2 of a Unity memory-palace app. Return only the final learner-facing mnemonic_en plus mnemonic_mode and hook_judge. Use STORY_ONLY when the hook is weak. No markdown or commentary.",
                0.34f,
                1800,
                "replacement final mnemonic",
                null,
                parsed => mnemonicLinkEnvelope = parsed,
                error => mnemonicLinkError = error);

            if (!string.IsNullOrWhiteSpace(mnemonicLinkError))
            {
                onError?.Invoke(mnemonicLinkError);
                yield break;
            }

            var mnemonicItems = AlignGeneratedItemsToWords(new List<WordEntry> { word }, mnemonicLinkEnvelope?.items);
            var mnemonicLink = mnemonicItems.Count > 0 ? mnemonicItems[0] : null;
            var generated = MergeGeneratedMnemonicFields(word, visualCue, mnemonicLink);

            var replacement = BuildMnemonicItemData(
                word,
                generated,
                itemIndex,
                resolvedAnchorId,
                resolvedAnchorLabel,
                "gemini_live");

            onSuccess?.Invoke(replacement);
        }

        private IEnumerator GenerateMnemonicChunk(
            string endpoint,
            string model,
            List<WordEntry> words,
            List<AnchorDefinition> assignedAnchors,
            int globalOffset,
            int totalWords,
            Action<List<MnemonicItemData>> onSuccess,
            Action<string> onError)
        {
            GeneratedMnemonicEnvelope visualCueEnvelope = null;
            string visualCueError = null;
            yield return RequestMnemonicEnvelope(
                endpoint,
                model,
                BuildVisualCuePrompt(words, globalOffset, totalWords, assignedAnchors),
                "You write JSON for Call 1 of a Unity memory-palace app. Return image-focused visual cue scene fields, association prompts, and exactly four image prompt candidates per item. Do not generate mnemonic or story text. Return exactly one JSON object with top-level items. No markdown or commentary.",
                0.42f,
                Mathf.Clamp(words.Count * 560 + 520, 1400, 2600),
                "visual cue",
                BuildCompactVisualCueRetryPrompt(words, globalOffset, totalWords, assignedAnchors),
                parsed => visualCueEnvelope = parsed,
                error => visualCueError = error);

            if (!string.IsNullOrWhiteSpace(visualCueError))
            {
                onError?.Invoke(visualCueError);
                yield break;
            }

            var alignedVisualCueItems = AlignGeneratedItemsToWords(words, visualCueEnvelope?.items);
            if (alignedVisualCueItems.Count == 0)
            {
                onError?.Invoke("Ollama returned empty visual cue data.");
                yield break;
            }

            GeneratedMnemonicEnvelope mnemonicLinkEnvelope = null;
            string mnemonicLinkError = null;
            yield return RequestMnemonicEnvelope(
                endpoint,
                model,
                BuildMnemonicLinkPrompt(words, globalOffset, totalWords, assignedAnchors, alignedVisualCueItems),
                "You write JSON for Call 2 of a Unity memory-palace app. Return only final learner-facing mnemonic_en plus mnemonic_mode and hook_judge for each item. Use STORY_ONLY when the hook is weak. No markdown or commentary.",
                0.38f,
                Mathf.Clamp(words.Count * 420 + 480, 1200, 2400),
                "final mnemonic",
                null,
                parsed => mnemonicLinkEnvelope = parsed,
                error => mnemonicLinkError = error);

            if (!string.IsNullOrWhiteSpace(mnemonicLinkError))
            {
                onError?.Invoke(mnemonicLinkError);
                yield break;
            }

            var alignedMnemonicLinkItems = AlignGeneratedItemsToWords(words, mnemonicLinkEnvelope?.items);
            var items = new List<MnemonicItemData>();

            for (int i = 0; i < alignedVisualCueItems.Count && i < words.Count; i++)
            {
                var sourceWord = words[i];
                var visualCue = alignedVisualCueItems[i];
                if (visualCue == null)
                {
                    continue;
                }

                var globalIndex = globalOffset + i;
                var anchor = ResolveAssignedAnchor(assignedAnchors, i, globalIndex, totalWords);
                var mnemonicLink = i < alignedMnemonicLinkItems.Count ? alignedMnemonicLinkItems[i] : null;
                var merged = MergeGeneratedMnemonicFields(sourceWord, visualCue, mnemonicLink);

                items.Add(BuildMnemonicItemData(sourceWord, merged, globalIndex, anchor.id, anchor.label));
            }

            onSuccess?.Invoke(items);
        }

        private IEnumerator GenerateGeminiMnemonicChunk(
            string apiKey,
            string model,
            List<WordEntry> words,
            List<AnchorDefinition> assignedAnchors,
            int globalOffset,
            int totalWords,
            Action<List<MnemonicItemData>> onSuccess,
            Action<string> onError)
        {
            GeneratedMnemonicEnvelope visualCueEnvelope = null;
            string visualCueError = null;
            yield return RequestGeminiMnemonicEnvelope(
                apiKey,
                model,
                BuildVisualCuePrompt(words, globalOffset, totalWords, assignedAnchors),
                "You write JSON for Call 1 of a Unity memory-palace app. Return image-focused visual cue scene fields, association prompts, and exactly four image prompt candidates per item. Do not generate mnemonic or story text. Return exactly one JSON object with top-level items. No markdown or commentary.",
                0.34f,
                Mathf.Clamp(words.Count * 640 + 720, 1800, 3200),
                "visual cue",
                BuildCompactVisualCueRetryPrompt(words, globalOffset, totalWords, assignedAnchors),
                parsed => visualCueEnvelope = parsed,
                error => visualCueError = error);

            if (!string.IsNullOrWhiteSpace(visualCueError))
            {
                onError?.Invoke(visualCueError);
                yield break;
            }

            var alignedVisualCueItems = AlignGeneratedItemsToWords(words, visualCueEnvelope?.items);
            if (alignedVisualCueItems.Count == 0)
            {
                onError?.Invoke("Gemini returned empty visual cue data.");
                yield break;
            }

            GeneratedMnemonicEnvelope mnemonicLinkEnvelope = null;
            string mnemonicLinkError = null;
            yield return RequestGeminiMnemonicEnvelope(
                apiKey,
                model,
                BuildMnemonicLinkPrompt(words, globalOffset, totalWords, assignedAnchors, alignedVisualCueItems),
                "You write JSON for Call 2 of a Unity memory-palace app. Return only final learner-facing mnemonic_en plus mnemonic_mode and hook_judge for each item. Use STORY_ONLY when the hook is weak. No markdown or commentary.",
                0.3f,
                Mathf.Clamp(words.Count * 520 + 720, 1600, 3200),
                "final mnemonic",
                null,
                parsed => mnemonicLinkEnvelope = parsed,
                error => mnemonicLinkError = error);

            if (!string.IsNullOrWhiteSpace(mnemonicLinkError))
            {
                onError?.Invoke(mnemonicLinkError);
                yield break;
            }

            var alignedMnemonicLinkItems = AlignGeneratedItemsToWords(words, mnemonicLinkEnvelope?.items);
            var items = new List<MnemonicItemData>();

            for (int i = 0; i < alignedVisualCueItems.Count && i < words.Count; i++)
            {
                var sourceWord = words[i];
                var visualCue = alignedVisualCueItems[i];
                if (visualCue == null)
                {
                    continue;
                }

                var globalIndex = globalOffset + i;
                var anchor = ResolveAssignedAnchor(assignedAnchors, i, globalIndex, totalWords);
                var mnemonicLink = i < alignedMnemonicLinkItems.Count ? alignedMnemonicLinkItems[i] : null;
                var merged = MergeGeneratedMnemonicFields(sourceWord, visualCue, mnemonicLink);

                items.Add(BuildMnemonicItemData(sourceWord, merged, globalIndex, anchor.id, anchor.label, "gemini_live"));
            }

            onSuccess?.Invoke(items);
        }

        private IEnumerator RequestMnemonicEnvelope(
            string endpoint,
            string model,
            string prompt,
            string system,
            float temperature,
            int numPredict,
            string responseLabel,
            string retryPrompt,
            Action<GeneratedMnemonicEnvelope> onSuccess,
            Action<string> onError)
        {
            var requestBody = new OllamaGenerateRequest
            {
                model = model,
                prompt = prompt,
                system = system,
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = temperature,
                    num_predict = numPredict
                }
            };

            string rawResponse = null;
            string requestError = null;
            yield return SendOllamaGenerateRequest(
                endpoint,
                requestBody,
                responseText => rawResponse = responseText,
                error => requestError = error);

            if (!string.IsNullOrWhiteSpace(requestError))
            {
                onError?.Invoke(requestError);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                onError?.Invoke("Ollama response did not contain " + responseLabel + " JSON.");
                yield break;
            }

            var responseForError = rawResponse;
            if (!TryParseMnemonicEnvelope(rawResponse, out var parsedEnvelope, out var parseError))
            {
                if (!string.IsNullOrWhiteSpace(retryPrompt) && ShouldRetryMnemonicResponse(rawResponse))
                {
                    string retryResponse = null;
                    string retryError = null;
                    var retryRequestBody = new OllamaGenerateRequest
                    {
                        model = model,
                        prompt = retryPrompt,
                        system = system,
                        format = "json",
                        stream = false,
                        options = new OllamaRequestOptions
                        {
                            temperature = 0.2f,
                            num_predict = numPredict
                        }
                    };

                    yield return SendOllamaGenerateRequest(
                        endpoint,
                        retryRequestBody,
                        responseText => retryResponse = responseText,
                        error => retryError = error);

                    if (!string.IsNullOrWhiteSpace(retryResponse)
                        && TryParseMnemonicEnvelope(retryResponse, out parsedEnvelope, out parseError))
                    {
                        responseForError = retryResponse;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(retryError))
                        {
                            parseError += $"\nRetry request failed: {retryError}";
                        }

                        if (!string.IsNullOrWhiteSpace(retryResponse))
                        {
                            responseForError = retryResponse;
                            parseError += "\nRetry response preview:\n" + BuildPreview(retryResponse);
                        }
                    }
                }

                if (parsedEnvelope == null || parsedEnvelope.items == null || parsedEnvelope.items.Length == 0)
                {
                    string repairedPayload = null;
                    string repairFailure = null;

                    yield return RepairMnemonicJson(
                        endpoint,
                        model,
                        responseForError,
                        repairedJson => repairedPayload = repairedJson,
                        repairError => repairFailure = repairError);

                    if (!string.IsNullOrWhiteSpace(repairedPayload))
                    {
                        TryParseMnemonicEnvelope(repairedPayload, out parsedEnvelope, out parseError);
                    }

                    if (!string.IsNullOrWhiteSpace(repairFailure))
                    {
                        parseError += $"\nRepair attempt failed: {repairFailure}";
                    }

                    if (parsedEnvelope == null || parsedEnvelope.items == null || parsedEnvelope.items.Length == 0)
                    {
                        onError?.Invoke(
                            "Failed to parse Ollama " + responseLabel + " JSON.\n" +
                            parseError + "\n\n" +
                            "Raw response preview:\n" + BuildPreview(responseForError));
                        yield break;
                    }
                }
            }

            onSuccess?.Invoke(parsedEnvelope);
        }

        private IEnumerator RequestGeminiMnemonicEnvelope(
            string apiKey,
            string model,
            string prompt,
            string system,
            float temperature,
            int maxOutputTokens,
            string responseLabel,
            string retryPrompt,
            Action<GeneratedMnemonicEnvelope> onSuccess,
            Action<string> onError)
        {
            string rawResponse = null;
            string requestError = null;
            yield return SendGeminiGenerateRequest(
                apiKey,
                model,
                prompt,
                system,
                temperature,
                maxOutputTokens,
                responseText => rawResponse = responseText,
                error => requestError = error);

            if (!string.IsNullOrWhiteSpace(requestError))
            {
                onError?.Invoke(requestError);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                onError?.Invoke("Gemini response did not contain " + responseLabel + " JSON.");
                yield break;
            }

            var responseForError = rawResponse;
            if (!TryParseMnemonicEnvelope(rawResponse, out var parsedEnvelope, out var parseError))
            {
                if (!string.IsNullOrWhiteSpace(retryPrompt) && ShouldRetryMnemonicResponse(rawResponse))
                {
                    string retryResponse = null;
                    string retryError = null;
                    yield return SendGeminiGenerateRequest(
                        apiKey,
                        model,
                        retryPrompt,
                        system,
                        0.2f,
                        maxOutputTokens,
                        responseText => retryResponse = responseText,
                        error => retryError = error);

                    if (!string.IsNullOrWhiteSpace(retryResponse)
                        && TryParseMnemonicEnvelope(retryResponse, out parsedEnvelope, out parseError))
                    {
                        responseForError = retryResponse;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(retryError))
                        {
                            parseError += $"\nRetry request failed: {retryError}";
                        }

                        if (!string.IsNullOrWhiteSpace(retryResponse))
                        {
                            responseForError = retryResponse;
                            parseError += "\nRetry response preview:\n" + BuildPreview(retryResponse);
                        }
                    }
                }

                if (parsedEnvelope == null || parsedEnvelope.items == null || parsedEnvelope.items.Length == 0)
                {
                    string repairedPayload = null;
                    string repairFailure = null;

                    yield return RepairGeminiMnemonicJson(
                        apiKey,
                        model,
                        responseForError,
                        repairedJson => repairedPayload = repairedJson,
                        repairError => repairFailure = repairError);

                    if (!string.IsNullOrWhiteSpace(repairedPayload))
                    {
                        TryParseMnemonicEnvelope(repairedPayload, out parsedEnvelope, out parseError);
                    }

                    if (!string.IsNullOrWhiteSpace(repairFailure))
                    {
                        parseError += $"\nRepair attempt failed: {repairFailure}";
                    }

                    if (parsedEnvelope == null || parsedEnvelope.items == null || parsedEnvelope.items.Length == 0)
                    {
                        onError?.Invoke(
                            "Failed to parse Gemini " + responseLabel + " JSON.\n" +
                            parseError + "\n\n" +
                            "Raw response preview:\n" + BuildPreview(responseForError));
                        yield break;
                    }
                }
            }

            onSuccess?.Invoke(parsedEnvelope);
        }

        private static MnemonicItemData BuildMnemonicItemData(
            WordEntry sourceWord,
            GeneratedMnemonicItem generated,
            int itemIndex,
            string anchorId,
            string anchorLabel,
            string mnemonicSource = "ollama_live")
        {
            return new MnemonicItemData
            {
                word = sourceWord.word,
                meaning = sourceWord.meaning,
                anchorId = anchorId,
                anchorLabel = anchorLabel,
                anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchorId, anchorLabel),
                mnemonicSource = mnemonicSource,
                visualCue = generated.visual_cue_en,
                associationPrompt = FirstNonEmpty(generated.association_prompt_en, generated.image_prompt_en, generated.visual_cue_en),
                mnemonic = generated.mnemonic_en,
                mnemonicMode = NormalizeMnemonicMode(generated),
                hookAccepted = IsAcceptedMnemonicHook(generated),
                hookScore = generated?.hook_judge?.score ?? 0,
                hookReason = generated?.hook_judge?.reason,
                mnemonicHook = generated?.hook_judge?.best_hook,
                storyCue = string.Empty,
                imagePrompt = generated.image_prompt_en,
                imagePromptCandidates = NormalizeImagePromptCandidates(generated),
                cueBlueprint = ConvertCueBlueprint(generated?.cue_blueprint),
                objectShape = PickShape(itemIndex),
                colorHex = PickColor(itemIndex),
                visualObjects = NormalizeVisualObjects(generated.visual_objects, itemIndex)
            };
        }

        private static CueBlueprintData ConvertCueBlueprint(GeneratedCueBlueprint source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new CueBlueprintData
            {
                targetMeaning = source.targetMeaning,
                visualSceneCore = source.visualSceneCore,
                mainObject = source.mainObject,
                anchorRelation = source.anchorRelation,
                relativeSize = source.relativeSize,
                mainActionOrState = source.mainActionOrState,
                mnemonicHookNote = source.mnemonicHookNote,
                mnemonicMode = source.mnemonicMode,
                visibleObjects = new List<string>()
            };

            if (source.visibleObjects != null)
            {
                for (int i = 0; i < source.visibleObjects.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(source.visibleObjects[i]))
                    {
                        result.visibleObjects.Add(source.visibleObjects[i].Trim());
                    }
                }
            }

            return result;
        }

        private static string NormalizeMnemonicMode(GeneratedMnemonicItem generated)
        {
            var mode = generated?.mnemonic_mode?.Trim();
            var accepted = IsAcceptedMnemonicHook(generated);
            if (accepted && string.Equals(mode, "HOOK_PLUS_STORY", StringComparison.OrdinalIgnoreCase))
            {
                return "HOOK_PLUS_STORY";
            }

            if (accepted && string.IsNullOrWhiteSpace(mode))
            {
                return "HOOK_PLUS_STORY";
            }

            return "STORY_ONLY";
        }

        private static bool IsAcceptedMnemonicHook(GeneratedMnemonicItem generated)
        {
            return generated?.hook_judge != null
                   && generated.hook_judge.accepted
                   && generated.hook_judge.score >= 7;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }

        private static List<string> NormalizeImagePromptCandidates(GeneratedMnemonicItem generated)
        {
            var candidates = new List<string>();
            AddPromptCandidates(candidates, generated?.image_prompt_candidates_en);
            AddPromptCandidate(candidates, generated?.image_prompt_en);

            var baseAssociation = FirstNonEmpty(
                generated?.association_prompt_en,
                generated?.image_prompt_en,
                generated?.visual_cue_en);

            if (!string.IsNullOrWhiteSpace(baseAssociation))
            {
                AddPromptCandidate(candidates, baseAssociation);
                AddPromptCandidate(candidates, baseAssociation + ", object-on-anchor close-up, clear foreground");
                AddPromptCandidate(candidates, baseAssociation + ", action-focused close-up, cue physically interacting with anchor");
                AddPromptCandidate(candidates, baseAssociation + ", unusual but realistic object relation, simple composition");
                AddPromptCandidate(candidates, baseAssociation + ", simplest literal version, large recognizable cue objects");
            }

            while (candidates.Count > ImagePromptCandidateCount)
            {
                candidates.RemoveAt(candidates.Count - 1);
            }

            return candidates;
        }

        private static void AddPromptCandidates(List<string> candidates, string[] prompts)
        {
            if (prompts == null)
            {
                return;
            }

            for (int i = 0; i < prompts.Length; i++)
            {
                AddPromptCandidate(candidates, prompts[i]);
            }
        }

        private static void AddPromptCandidate(List<string> candidates, string prompt)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            var cleaned = Regex.Replace(prompt.Trim(), @"\s+", " ");
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i], cleaned, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(cleaned);
        }

        private IEnumerator SendOllamaGenerateRequest(
            string endpoint,
            OllamaGenerateRequest requestBody,
            Action<string> onSuccess,
            Action<string> onError)
        {
            yield return SendOllamaJsonRequest(endpoint, requestBody, onSuccess, onError);
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

        private static List<GeneratedMnemonicItem> AlignGeneratedItemsToWords(List<WordEntry> requestedWords, GeneratedMnemonicItem[] generatedItems)
        {
            var alignedItems = new List<GeneratedMnemonicItem>();
            if (requestedWords == null || requestedWords.Count == 0)
            {
                return alignedItems;
            }

            var remainingItems = new List<GeneratedMnemonicItem>();
            if (generatedItems != null)
            {
                for (int i = 0; i < generatedItems.Length; i++)
                {
                    if (generatedItems[i] != null)
                    {
                        remainingItems.Add(generatedItems[i]);
                    }
                }
            }

            for (int i = 0; i < requestedWords.Count; i++)
            {
                var matchIndex = FindGeneratedMnemonicMatchIndex(remainingItems, requestedWords[i]?.word);
                if (matchIndex >= 0)
                {
                    alignedItems.Add(remainingItems[matchIndex]);
                    remainingItems.RemoveAt(matchIndex);
                    continue;
                }

                if (remainingItems.Count > 0)
                {
                    alignedItems.Add(remainingItems[0]);
                    remainingItems.RemoveAt(0);
                    continue;
                }

                alignedItems.Add(null);
            }

            return alignedItems;
        }

        private static int FindGeneratedMnemonicMatchIndex(List<GeneratedMnemonicItem> candidates, string requestedWord)
        {
            var requestedKey = NormalizeMnemonicWordKey(requestedWord);
            if (string.IsNullOrWhiteSpace(requestedKey) || candidates == null)
            {
                return -1;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(NormalizeMnemonicWordKey(candidates[i]?.word), requestedKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeMnemonicWordKey(string word)
        {
            return string.IsNullOrWhiteSpace(word)
                ? string.Empty
                : word.Trim().ToLowerInvariant();
        }

        private static string BuildVisualCuePrompt(List<WordEntry> words, int globalOffset, int totalWords, List<AnchorDefinition> assignedAnchors = null)
        {
            var wordLines = BuildMnemonicWordLines(words, globalOffset);
            var anchorLines = BuildMnemonicAnchorLines(words, globalOffset, totalWords, assignedAnchors);
            var ragGuidance = MnemonicCueFrameRag.BuildBatchGuidance(words, globalOffset, totalWords, assignedAnchors);
            var academicSafetyRules = BuildAcademicSafetyRules();
            var hiddenCueRules = BuildHiddenCueRejectionRules();

            return
                "CALL 1 of 2: Generate only the Association Image Cue / visual cue for VR memory palace vocabulary learning.\n" +
                "Return exactly " + words.Count + " item(s), only for the WORDS listed below.\n\n" +
                "Use this complete word and anchor data:\n\n" +
                "WORDS:\n" + wordLines +
                "\nPRE-ASSIGNED ANCHORS:\n" + anchorLines +
                "\n" + ragGuidance +
                academicSafetyRules +
                "Goal for Call 1:\n" +
                "- Generate only a realistic visual cue that specifically retrieves the target meaning.\n" +
                "- Do not generate mnemonic text or story text yet.\n" +
                "- Do not consider the Spanish word form, pronunciation, spelling, cognates, puns, or sound similarity.\n" +
                "- Treat the Spanish word only as an item identifier. The scene should be planned from the meaning and assigned anchor only.\n" +
                "- The final scene must be meaning-first and image-stable.\n\n" +
                "Visual cue candidate ranking:\n" +
                "- Silently draft 4 candidate visual cues for each word and output only the best scene.\n" +
                "- Score candidates on: immediate meaning retrieval, target-meaning event quality, anchor participation, cue-anchor separateness, realistic indoor physical visibility, visual_objects completeness, association_prompt concreteness, and image_prompt specificity.\n" +
                "- Prefer a novel but physically possible relation between cue object and anchor: wedged under, hanging from, clipped to, spilling from, wrapped around, leaning against, cushioning, covering, contained by, or balanced in a visible support.\n" +
                "- Reject candidates that rely on word sounds, spelling, labels, written text, broad travel/context drift, a whole-room view, a real outdoor scene, target-object display, or a weak object merely resting near the anchor.\n\n" +
                hiddenCueRules +
                "Scene rules:\n" +
                "- Use the exact anchor_id and anchor label from PRE-ASSIGNED ANCHORS. Never change the assigned anchor.\n" +
                "- visual_cue_en must start naturally with \"At the {AnchorLabel}, ...\".\n" +
                "- Build a target-meaning event, not a target-object display: the scene should make the learner infer the meaning through a small realistic event.\n" +
                "- The foreground cue should be more memorable than the anchor, but the anchor must remain visible as the fixed location.\n" +
                "- Make the cue physical, visible, and specific: a concrete noun plus a clear action, contact point, or state.\n" +
                "- The cue should interact with the assigned anchor through a clear physical relation, not plain placement beside the anchor.\n" +
                "- Avoid exhibit-like scenes where a model, miniature, toy, or target object simply sits, rests, perches, balances, or is displayed on furniture.\n" +
                "- For nature, weather, sky, water, landscape, outdoor place, travel, public-space, or large-environment nouns, use ordinary indoor proxy objects in action, such as sand spilling into a dune, a security tray with travel items, or a narrow path forced between objects.\n" +
                "- Do not show the real full outdoor scene directly.\n" +
                "- Keep it simple: one anchor, one foreground cue, one memorable visible action or state.\n\n" +
                "Bad-to-good cue scene examples:\n" +
                "- Bad: an airport model sits on a cabinet shelf. Good: a security tray on the cabinet shelf holds shoes, passport, boarding pass, and a luggage tag.\n" +
                "- Bad: a narrow hallway model is displayed on an air conditioner. Good: two books form a narrow passage with a tiny door card at one end.\n" +
                "- Bad: a sand dune is balanced on a chair armrest. Good: sand spills from the chair armrest and piles into a small dune on the seat.\n" +
                "- Bad: a fire pit burns across a door frame. Good: a safe electric flame lantern hangs from the door handle, glowing like a campfire.\n\n" +
                "Image generation preparation:\n" +
                "- Do not use mnemonic_en as an image prompt.\n" +
                "- First create cue_blueprint internally, then derive visual_cue_en, association_prompt_en, image_prompt_en, and visual_objects from it.\n" +
                "- cue_blueprint separates meaning, main object, anchor relation, and action/state so fields do not collapse into duplicates.\n" +
                "- First create association_prompt_en: a short concrete visual description of the association scene.\n" +
                "- association_prompt_en must include only visible objects, actions, and physical relations.\n" +
                "- association_prompt_en must not include explanations, learning instructions, translations, word meanings, or phrases such as \"helps recall\".\n" +
                "- image_prompt_en must be derived from association_prompt_en and emphasize foreground clarity.\n" +
                "- image_prompt_en and all image_prompt_candidates_en must be English-first and must not describe the whole room.\n" +
                "- Generate exactly 4 visually different image_prompt_candidates_en while keeping the same memory link: object-on-anchor close-up, action-focused version, unusual-but-realistic object relation, and simplest literal version.\n\n" +
                "Avoid:\n" +
                "- mnemonic explanations, Spanish sound hooks, puns, cognate notes, syllable hints, or learner-facing cue stories;\n" +
                "- written words, labels, captions, alphabet letters, logos, arrows, icons, signs, or UI symbols;\n" +
                "- tiny dots, vague glow, colored light, mood lighting, smoke, haze, rhythm, or atmosphere as the main clue;\n" +
                "- whole-room scenes, empty rooms, interior design views, unanchored outdoor scenes, full natural landscapes, full city views, disasters, explosions, battle, horror, gore, or large smoke clouds;\n" +
                "- unsafe associations: sexual content, gambling, drugs, crime, weapons, violence, horror, gore, or stigmatizing imagery.\n\n" +
                "Field-specific rules:\n" +
                "- visual_cue_en should be 12 to 24 words and describe only the visible scene.\n" +
                "- association_prompt_en should be 6 to 18 words, English, drawable, and free of teaching/explanation language.\n" +
                "- image_prompt_en should be 6 to 16 words and name only the foreground cue/action plus its anchor contact point.\n" +
                "- image_prompt_candidates_en must contain exactly 4 short English prompts, each focused on foreground cue clarity and anchor interaction.\n" +
                "- cue_blueprint.targetMeaning must be the English meaning only, not a Spanish-word hook.\n" +
                "- cue_blueprint.visualSceneCore must summarize the visible event in one short phrase.\n" +
                "- cue_blueprint.mnemonicMode should be STORY_ONLY unless there is an obviously strong natural word-form hook candidate.\n" +
                "- visual_objects must list every concrete foreground object used for meaning retrieval.\n" +
                "- Do not include the anchor itself in visual_objects unless the anchor is also part of the foreground cue.\n" +
                "- Do not include abstract ideas, emotions, meanings, or invisible sound hints in visual_objects.\n" +
                "- Do not output mnemonic_en or story_cue_en in Call 1.\n\n" +
                "Before outputting JSON, silently check each item:\n" +
                "1. Can the visual scene retrieve the meaning without reading the Spanish word?\n" +
                "2. Are the anchor and foreground cue both visible and close together?\n" +
                "3. Does association_prompt_en contain only drawable objects, actions, and physical relations?\n" +
                "4. Does image_prompt_en name drawable prop objects rather than a concept-only word?\n" +
                "5. Are there exactly 4 diverse image_prompt_candidates_en?\n" +
                "6. Is the scene neutral and participant-safe for academic research?\n\n" +
                "Output only valid JSON in this shape:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"the word\",\n" +
                "      \"anchor\": \"the assigned anchor_id\",\n" +
                "      \"cue_blueprint\": {\n" +
                "        \"targetMeaning\": \"English target meaning\",\n" +
                "        \"visualSceneCore\": \"short event summary\",\n" +
                "        \"mainObject\": \"main concrete cue object\",\n" +
                "        \"anchorRelation\": \"visible physical relation to anchor\",\n" +
                "        \"relativeSize\": \"small / medium / large foreground cue\",\n" +
                "        \"mainActionOrState\": \"action or state\",\n" +
                "        \"visibleObjects\": [\"concrete object 1\"],\n" +
                "        \"mnemonicHookNote\": \"strong hook candidate or empty\",\n" +
                "        \"mnemonicMode\": \"STORY_ONLY\"\n" +
                "      },\n" +
                "      \"visual_cue_en\": \"At the assigned AnchorLabel, concrete foreground cue action.\",\n" +
                "      \"association_prompt_en\": \"short drawable association scene, no explanation\",\n" +
                "      \"image_prompt_en\": \"foreground cue/action and anchor contact point only\",\n" +
                "      \"image_prompt_candidates_en\": [\n" +
                "        \"object-on-anchor close-up version\",\n" +
                "        \"action-focused version\",\n" +
                "        \"unusual but realistic object relation version\",\n" +
                "        \"simplest literal version\"\n" +
                "      ],\n" +
                "      \"visual_objects\": [\n" +
                "        {\n" +
                "          \"label\": \"concrete foreground object\",\n" +
                "          \"primitiveShape\": \"Cube\",\n" +
                "          \"colorHex\": \"#7EC8E3\",\n" +
                "          \"localPosition\": { \"x\": 0, \"y\": 0, \"z\": 0 },\n" +
                "          \"scale\": { \"x\": 0.45, \"y\": 0.45, \"z\": 0.45 },\n" +
                "          \"effect\": \"meaning cue\"\n" +
                "        }\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        private static string BuildCompactVisualCueRetryPrompt(List<WordEntry> words, int globalOffset, int totalWords, List<AnchorDefinition> assignedAnchors = null)
        {
            var assignmentLines = BuildMnemonicAssignmentLines(words, globalOffset, totalWords, assignedAnchors);
            var ragGuidance = MnemonicCueFrameRag.BuildBatchGuidance(words, globalOffset, totalWords, assignedAnchors);
            var academicSafetyRules = BuildAcademicSafetyRules();
            var hiddenCueRules = BuildHiddenCueRejectionRules();

            return
                "CALL 1 retry: Generate only visual cue scenes from the complete DATA below.\n" +
                "DATA:\n" + assignmentLines +
                "\n" + ragGuidance +
                academicSafetyRules +
                "\nReturn exactly " + words.Count + " items in one JSON object whose top-level key is items.\n" +
                "Use each anchor_id and anchor_label exactly.\n" +
                "Generate only visual_cue_en, association_prompt_en, image_prompt_en, image_prompt_candidates_en, and visual_objects.\n" +
                "Also include cue_blueprint for each item; build every visual field from that blueprint.\n" +
                "Do not output mnemonic_en or story_cue_en. Do not consider Spanish sound, spelling, cognates, or puns.\n" +
                "The visual cue must retrieve the meaning first through a realistic target-meaning event at the assigned anchor.\n" +
                "Do not make target-object displays: no model/miniature/toy simply sitting, resting, perched, balanced, or displayed on furniture.\n" +
                "For nature/outdoor/place/travel/large meanings, use indoor proxy objects in action rather than real outdoor scenes or static models.\n" +
                "association_prompt_en must be a short English drawable scene: visible objects, action, and physical relation only; no teaching explanation.\n" +
                "Use a novel but physically possible cue-anchor relation, not a plain object resting beside the anchor.\n" +
                "image_prompt_candidates_en must contain exactly 4 diverse foreground prompts: object-on-anchor close-up, action-focused, unusual-but-realistic relation, and simplest literal.\n" +
                hiddenCueRules +
                "Output only this JSON shape:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"the word\",\n" +
                "      \"anchor\": \"the assigned anchor_id\",\n" +
                "      \"cue_blueprint\": { \"targetMeaning\": \"meaning\", \"visualSceneCore\": \"event\", \"mainObject\": \"object\", \"anchorRelation\": \"relation\", \"relativeSize\": \"foreground\", \"mainActionOrState\": \"action\", \"visibleObjects\": [\"object\"], \"mnemonicHookNote\": \"\", \"mnemonicMode\": \"STORY_ONLY\" },\n" +
                "      \"visual_cue_en\": \"At the assigned AnchorLabel, concrete foreground cue action.\",\n" +
                "      \"association_prompt_en\": \"short drawable association scene, no explanation\",\n" +
                "      \"image_prompt_en\": \"foreground cue/action and anchor contact point only\",\n" +
                "      \"image_prompt_candidates_en\": [\"object-on-anchor close-up\", \"action-focused\", \"unusual realistic relation\", \"simplest literal\"],\n" +
                "      \"visual_objects\": []\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        private static string BuildVisualBlueprintPromptLine(List<GeneratedMnemonicItem> visualCueItems, int index)
        {
            if (visualCueItems == null || index < 0 || index >= visualCueItems.Count || visualCueItems[index] == null)
            {
                return "none";
            }

            var item = visualCueItems[index];
            var blueprint = item.cue_blueprint;
            var parts = new List<string>();
            AddBlueprintPromptPart(parts, "visualSceneCore", blueprint?.visualSceneCore);
            AddBlueprintPromptPart(parts, "mainObject", blueprint?.mainObject);
            AddBlueprintPromptPart(parts, "anchorRelation", blueprint?.anchorRelation);
            AddBlueprintPromptPart(parts, "mainActionOrState", blueprint?.mainActionOrState);
            AddBlueprintPromptPart(parts, "mnemonicMode", blueprint?.mnemonicMode);
            AddBlueprintPromptPart(parts, "visualCue", item.visual_cue_en);
            return parts.Count == 0 ? "none" : string.Join(" | ", parts);
        }

        private static void AddBlueprintPromptPart(List<string> parts, string label, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            parts.Add(label + "=" + CompactPromptLine(value));
        }

        private static string BuildMnemonicLinkPrompt(
            List<WordEntry> words,
            int globalOffset,
            int totalWords,
            List<AnchorDefinition> assignedAnchors = null,
            List<GeneratedMnemonicItem> visualCueItems = null)
        {
            var linkLines = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var globalIndex = globalOffset + i;
                var wordNumber = globalIndex + 1;
                var anchor = ResolveAssignedAnchor(assignedAnchors, i, globalIndex, totalWords);
                linkLines.Append(wordNumber)
                    .Append(". word=").Append(SafePromptText(word.word))
                    .Append("; meaning=").Append(SafePromptText(word.meaning))
                    .Append("; anchor_id=").Append(anchor.id)
                    .Append("; anchor_label=").Append(anchor.label)
                    .Append("; scene_blueprint=").Append(BuildVisualBlueprintPromptLine(visualCueItems, i))
                    .AppendLine();
            }

            var academicSafetyRules = BuildAcademicSafetyRules();

            return
                "CALL 2 of 2: Generate the final learner-facing Mnemonic for memory-palace vocabulary learning.\n\n" +
                "INPUT WORDS AND ANCHORS:\n" + linkLines +
                "\n" + academicSafetyRules +
                "Goal for Call 2:\n" +
                "- mnemonic_en is the only learner-facing study text.\n" +
                "- Use scene_blueprint only as the story basis, so the final mnemonic and image cue point to the same memory event.\n" +
                "- Do not copy image_prompt or association_prompt wording; rewrite the story naturally for a learner.\n" +
                "- First, silently generate possible mnemonic hooks from the Spanish word form.\n" +
                "- Judge whether the best hook adds real memory value beyond a story-only mnemonic.\n" +
                "- Use a hook only when it creates a memorable intermediate cue, phrase, image, or action that helps the learner retrieve the Spanish word form.\n" +
                "- If the hook is weak, forced, circular, or based only on partial spelling overlap, reject it and write a story-only mnemonic.\n\n" +
                "Hook acceptance rules:\n" +
                "- Accept a hook only if it is easy to notice from the Spanish word, produces a concrete phrase/image/action/object, connects naturally to the assigned anchor, helps retrieve the word form, and is better than a story-only mnemonic.\n" +
                "- Reject hooks that only share a few letters with the English meaning, only say \"sounds like\" without a memorable phrase or image, repeat the meaning, feel forced, create confusion, or make the mnemonic less clear.\n" +
                "- Examples: reject isla -> isl -> island because shared letters are too weak; reject aeropuerto -> airport because it is just the English meaning, not a cue for the Spanish form; reject pasillo -> pass because it is too broad unless it creates a vivid passage action; accept cascada -> cascade because the near-cognate gives a clear falling-water cue; reject cartera -> car tear a and barrio -> bar/rio when they distract from wallet or neighborhood; accept carretera -> carry the road because it creates a clear action image.\n" +
                "- score >= 7 and accepted = true means HOOK_PLUS_STORY. Anything else means STORY_ONLY.\n\n" +
                "Final mnemonic_en rules:\n" +
                "- If mnemonic_mode is HOOK_PLUS_STORY, write exactly two short paragraphs separated by a blank line: paragraph 1 explains the accepted hook; paragraph 2 tells a vivid anchor-based story using the target meaning.\n" +
                "- If mnemonic_mode is STORY_ONLY, write exactly one short paragraph: tell a vivid anchor-based story and include the Spanish word naturally once.\n" +
                "- Mention the assigned anchor label naturally.\n" +
                "- Do not invent a pun, sound-alike, spelling trick, fake etymology, or weak hook when STORY_ONLY is cleaner.\n" +
                "- Do not mention that no hook was found.\n" +
                "- Do not introduce image composition, camera direction, visual prompt language, generated images, association prompts, or 3D proxy props.\n" +
                "- Avoid generic phrases like \"links to,\" \"is associated with,\" \"helps remember,\" \"retrieves,\" \"bind syllables,\" \"action rhythm,\" or \"same scene.\"\n" +
                "- Keep the final text compact: STORY_ONLY 25 to 55 words; HOOK_PLUS_STORY 35 to 75 words total.\n\n" +
                "Few-shot examples:\n" +
                "word: isla; meaning: island; anchor_label: Door\n" +
                "mnemonic_mode: STORY_ONLY\n" +
                "hook_judge: {\"accepted\":false,\"score\":4,\"reason\":\"Shared letters are too weak and do not create a memorable intermediate cue.\",\"best_hook\":\"isl -> island\"}\n" +
                "mnemonic_en: \"At the Door, it opens onto a tiny island instead of another room. Sand and seawater spill across the threshold, and the sound of waves fixes isla to island.\"\n\n" +
                "word: carretera; meaning: highway; anchor_label: Chair\n" +
                "mnemonic_mode: HOOK_PLUS_STORY\n" +
                "hook_judge: {\"accepted\":true,\"score\":8,\"reason\":\"Carry the road is easy to hear from carretera and creates a clear action image.\",\"best_hook\":\"carry the road\"}\n" +
                "mnemonic_en: \"Carretera can become carry the road: the sound turns into someone carrying a road.\\n\\nAt the Chair, a long highway lies across the seat and armrests like something being carried on your lap.\"\n\n" +
                "word: cartera; meaning: wallet; anchor_label: Wardrobe\n" +
                "mnemonic_mode: STORY_ONLY\n" +
                "hook_judge: {\"accepted\":false,\"score\":3,\"reason\":\"Car tear a is forced and distracts from wallet, so a story-only cue is cleaner.\",\"best_hook\":\"car tear a\"}\n" +
                "mnemonic_en: \"At the Wardrobe, a wallet drops from a coat pocket and opens on the shelf, cards sliding out in a neat fan. That small wallet is the cartera.\"\n\n" +
                "Hard constraints:\n" +
                "- mnemonic_en is not used for image generation, so do not optimize it for drawing.\n" +
                "- Keep the link neutral and participant-safe for academic research.\n\n" +
                "Field rules:\n" +
                "- Output only word, anchor, mnemonic_mode, hook_judge, and mnemonic_en.\n" +
                "- Do not output story_cue_en, visual_cue_en, association_prompt_en, image_prompt_en, or visual_objects.\n\n" +
                "Before outputting JSON, silently check each item:\n" +
                "1. Did you reject weak spelling-only or forced hooks?\n" +
                "2. Does mnemonic_en match the selected mnemonic_mode format?\n" +
                "3. Does mnemonic_en include the Spanish word naturally without becoming meta explanation?\n" +
                "4. Did you avoid image-prompt language and separate story fields?\n" +
                "5. Is the text free of sexual, gambling, drug, crime, weapon, horror, gore, and other unsafe associations?\n\n" +
                "Output only valid JSON in this shape:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"the word\",\n" +
                "      \"anchor\": \"the assigned anchor_id\",\n" +
                "      \"mnemonic_mode\": \"STORY_ONLY\",\n" +
                "      \"hook_judge\": { \"accepted\": false, \"score\": 0, \"reason\": \"short reason\", \"best_hook\": \"best candidate hook or empty\" },\n" +
                "      \"mnemonic_en\": \"final learner-facing mnemonic text\"\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        private static string BuildSingleVisualCueRegenerationPrompt(
            WordEntry word,
            int itemIndex,
            int totalWords,
            string anchorId,
            string anchorLabel,
            string rejectedVisualCue,
            string rejectedMnemonic,
            string rejectedImagePrompt,
            string rejectionReason)
        {
            var fallbackAnchor = RoomSpecCatalog.GetAssignmentAnchor(itemIndex, Mathf.Max(1, totalWords));
            var resolvedAnchorId = string.IsNullOrWhiteSpace(anchorId) ? fallbackAnchor.id : anchorId.Trim();
            var resolvedAnchorLabel = string.IsNullOrWhiteSpace(anchorLabel) ? fallbackAnchor.label : anchorLabel.Trim();
            var reason = string.IsNullOrWhiteSpace(rejectionReason)
                ? "The previous cue scene was rejected as too weak, forced, or hard to generate as a clear anchor-plus-cue image."
                : rejectionReason.Trim();
            var ragGuidance = MnemonicCueFrameRag.BuildSingleGuidance(word, itemIndex + 1, resolvedAnchorId, resolvedAnchorLabel);
            var academicSafetyRules = BuildAcademicSafetyRules();
            var hiddenCueRules = BuildHiddenCueRejectionRules();

            return
                "CALL 1 of 2: Regenerate only the Association Image Cue / visual cue because the current cue scene was rejected.\n\n" +
                "TARGET DATA:\n" +
                "word=" + word.word.Trim() + "\n" +
                "meaning=" + (word.meaning ?? string.Empty).Trim() + "\n" +
                "anchor_id=" + resolvedAnchorId + "\n" +
                "anchor_label=" + resolvedAnchorLabel + "\n\n" +
                ragGuidance +
                academicSafetyRules +
                "REJECTED CURRENT CUE, for diagnosis only. Do not copy it if the visual idea is weak:\n" +
                "visual_cue_en=" + CompactPromptLine(rejectedVisualCue) + "\n" +
                "mnemonic_en=" + CompactPromptLine(rejectedMnemonic) + "\n" +
                "image_prompt_en=" + CompactPromptLine(rejectedImagePrompt) + "\n" +
                "rejection_reason=" + CompactPromptLine(reason) + "\n\n" +
                "Goal for Call 1:\n" +
                "- Generate only a new realistic visual cue that specifically retrieves the target meaning.\n" +
                "- First create cue_blueprint, then derive visual_cue_en, association_prompt_en, image_prompt_en, image_prompt_candidates_en, and visual_objects from it.\n" +
                "- Do not generate mnemonic_en or story_cue_en yet.\n" +
                "- Do not consider the Spanish word form, pronunciation, spelling, cognates, puns, or sound similarity.\n" +
                "- Treat the Spanish word only as an item identifier; plan the scene from the meaning and assigned anchor only.\n" +
                "- Build a target-meaning event, not a target-object display or furniture exhibit.\n" +
                "- The cue object must remain separate from the anchor, not become a color, texture, decoration, or transformed version of the anchor.\n" +
                "- The image model should be able to draw both the anchor and cue object in the same close-up frame.\n\n" +
                "Candidate ranking / rejection:\n" +
                "- Silently draft 5 visual cue candidates, reject weak ones, and output only the best one.\n" +
                "- Score candidates on: immediate meaning retrieval, target-meaning event quality, anchor participation, cue-anchor separateness, realistic indoor visibility, association_prompt concreteness, image_prompt specificity, and visual_objects completeness.\n" +
                "- Prefer a novel but physically possible relation between cue object and anchor: wedged under, hanging from, clipped to, spilling from, wrapped around, leaning against, cushioning, covering, contained by, or balanced in a visible support.\n" +
                "- Reject candidates that use word-form hints, real outdoor landscapes, whole-room views, concept-only cues, unsafe associations, target-object display, or weak props merely resting near the anchor.\n\n" +
                hiddenCueRules +
                "For nature, outdoor, place, travel, public-space, machine, building, or large-environment meanings:\n" +
                "- Use ordinary indoor proxy objects in action, not a static model, miniature, toy, or exhibit of the target object.\n" +
                "- Prefer target-meaning events: security trays for airport, sand spilling into dunes for desert, objects forming a narrow passage for hallway.\n" +
                "- image_prompt_en must name the prop, material, and relation to the anchor, not just the concept word.\n\n" +
                "Field rules:\n" +
                "- visual_cue_en should be 12 to 24 words and start naturally with \"At the " + resolvedAnchorLabel + ", ...\".\n" +
                "- visual_cue_en describes only the visible scene.\n" +
                "- cue_blueprint must separate target meaning, visual scene core, main object, anchor relation, action/state, visible objects, hook note, and mode.\n" +
                "- association_prompt_en should be 6 to 18 English words: visible objects, action, and physical relation only; no teaching explanation.\n" +
                "- image_prompt_en should be 6 to 16 words, naming only the foreground cue/action plus its anchor contact point.\n" +
                "- image_prompt_candidates_en must contain exactly 4 diverse English prompts: object-on-anchor close-up, action-focused, unusual-but-realistic relation, and simplest literal.\n" +
                "- visual_objects must list every concrete foreground cue object; do not list the anchor unless it is part of the foreground cue.\n\n" +
                "Output only valid JSON in this schema:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"" + word.word.Trim() + "\",\n" +
                "      \"anchor\": \"" + resolvedAnchorId + "\",\n" +
                "      \"cue_blueprint\": { \"targetMeaning\": \"" + (word.meaning ?? string.Empty).Trim() + "\", \"visualSceneCore\": \"event\", \"mainObject\": \"object\", \"anchorRelation\": \"relation\", \"relativeSize\": \"foreground\", \"mainActionOrState\": \"action\", \"visibleObjects\": [\"object\"], \"mnemonicHookNote\": \"\", \"mnemonicMode\": \"STORY_ONLY\" },\n" +
                "      \"visual_cue_en\": \"At the " + resolvedAnchorLabel + ", concrete foreground cue action.\",\n" +
                "      \"association_prompt_en\": \"short drawable association scene, no explanation\",\n" +
                "      \"image_prompt_en\": \"foreground cue/action and anchor contact point only\",\n" +
                "      \"image_prompt_candidates_en\": [\"object-on-anchor close-up\", \"action-focused\", \"unusual realistic relation\", \"simplest literal\"],\n" +
                "      \"visual_objects\": []\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        private static string BuildSingleMnemonicLinkPrompt(
            WordEntry word,
            string anchorId,
            string anchorLabel,
            GeneratedMnemonicItem visualCue = null)
        {
            var academicSafetyRules = BuildAcademicSafetyRules();
            var sceneBlueprint = BuildVisualBlueprintPromptLine(
                visualCue == null ? null : new List<GeneratedMnemonicItem> { visualCue },
                0);

            return
                "CALL 2 of 2: Generate the final learner-facing Mnemonic for memory-palace vocabulary learning.\n\n" +
                "INPUT WORD AND ANCHOR:\n" +
                "word=" + word.word.Trim() + "\n" +
                "meaning=" + (word.meaning ?? string.Empty).Trim() + "\n" +
                "anchor_id=" + anchorId + "\n" +
                "anchor_label=" + anchorLabel + "\n\n" +
                "scene_blueprint=" + sceneBlueprint + "\n\n" +
                academicSafetyRules +
                "Goal for Call 2:\n" +
                "- mnemonic_en is the only learner-facing study text.\n" +
                "- Use scene_blueprint only as the story basis, so the final mnemonic and image cue point to the same memory event.\n" +
                "- Do not copy image_prompt or association_prompt wording; rewrite the story naturally for a learner.\n" +
                "- First, silently generate possible mnemonic hooks from the Spanish word form.\n" +
                "- Judge whether the best hook adds real memory value beyond a story-only mnemonic.\n" +
                "- Use a hook only when it creates a memorable intermediate cue, phrase, image, or action that helps the learner retrieve the Spanish word form.\n" +
                "- If the hook is weak, forced, circular, or based only on partial spelling overlap, reject it and write a story-only mnemonic.\n\n" +
                "Hook acceptance rules:\n" +
                "- Accept a hook only if it is easy to notice from the Spanish word, produces a concrete phrase/image/action/object, connects naturally to the assigned anchor, helps retrieve the word form, and is better than a story-only mnemonic.\n" +
                "- Reject hooks that only share a few letters with the English meaning, only say \"sounds like\" without a memorable phrase or image, repeat the meaning, feel forced, create confusion, or make the mnemonic less clear.\n" +
                "- Examples: reject isla -> isl -> island because shared letters are too weak; reject aeropuerto -> airport because it is just the English meaning, not a cue for the Spanish form; reject pasillo -> pass because it is too broad unless it creates a vivid passage action; accept cascada -> cascade because the near-cognate gives a clear falling-water cue; reject cartera -> car tear a and barrio -> bar/rio when they distract from wallet or neighborhood; accept carretera -> carry the road because it creates a clear action image.\n" +
                "- score >= 7 and accepted = true means HOOK_PLUS_STORY. Anything else means STORY_ONLY.\n\n" +
                "Final mnemonic_en rules:\n" +
                "- If mnemonic_mode is HOOK_PLUS_STORY, write exactly two short paragraphs separated by a blank line: paragraph 1 explains the accepted hook; paragraph 2 tells a vivid anchor-based story using the target meaning.\n" +
                "- If mnemonic_mode is STORY_ONLY, write exactly one short paragraph: tell a vivid anchor-based story and include the Spanish word naturally once.\n" +
                "- Mention the assigned anchor label naturally.\n" +
                "- Do not invent a pun, sound-alike, spelling trick, fake etymology, or weak hook when STORY_ONLY is cleaner.\n" +
                "- Do not mention that no hook was found.\n" +
                "- Do not write camera framing, image prompt language, generated-image language, or a separate story field.\n" +
                "- Avoid generic phrases like \"links to,\" \"is associated with,\" \"helps remember,\" \"retrieves,\" \"bind syllables,\" \"action rhythm,\" or \"same scene.\"\n" +
                "- Keep the final text compact: STORY_ONLY 25 to 55 words; HOOK_PLUS_STORY 35 to 75 words total.\n\n" +
                "Hard constraints:\n" +
                "- mnemonic_en is not used for image generation, so do not optimize it for drawing.\n" +
                "- Keep the link neutral and participant-safe for academic research.\n\n" +
                "Field rules:\n" +
                "- Output only word, anchor, mnemonic_mode, hook_judge, and mnemonic_en.\n" +
                "- Do not output story_cue_en, visual_cue_en, association_prompt_en, image_prompt_en, or visual_objects.\n\n" +
                "Output only valid JSON in this schema:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"" + word.word.Trim() + "\",\n" +
                "      \"anchor\": \"" + anchorId + "\",\n" +
                "      \"mnemonic_mode\": \"STORY_ONLY\",\n" +
                "      \"hook_judge\": { \"accepted\": false, \"score\": 0, \"reason\": \"short reason\", \"best_hook\": \"best candidate hook or empty\" },\n" +
                "      \"mnemonic_en\": \"final learner-facing mnemonic text\"\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        private static string BuildMnemonicWordLines(List<WordEntry> words, int globalOffset)
        {
            var wordLines = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var wordNumber = globalOffset + i + 1;
                wordLines.Append(wordNumber)
                    .Append(". ")
                    .Append(words[i].word)
                    .Append(" - ")
                    .Append(words[i].meaning);

                wordLines.AppendLine();
            }

            return wordLines.ToString();
        }

        private static string BuildMnemonicAnchorLines(List<WordEntry> words, int globalOffset, int totalWords, List<AnchorDefinition> assignedAnchors = null)
        {
            var anchorLines = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var globalIndex = globalOffset + i;
                var wordNumber = globalIndex + 1;
                var anchor = ResolveAssignedAnchor(assignedAnchors, i, globalIndex, totalWords);
                anchorLines.Append("- word ").Append(wordNumber).Append(" -> ").Append(anchor.id).Append(": ").Append(anchor.label).AppendLine();
            }

            return anchorLines.ToString();
        }

        private static string BuildMnemonicAssignmentLines(List<WordEntry> words, int globalOffset, int totalWords, List<AnchorDefinition> assignedAnchors = null)
        {
            var assignmentLines = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var globalIndex = globalOffset + i;
                var wordNumber = globalIndex + 1;
                var anchor = ResolveAssignedAnchor(assignedAnchors, i, globalIndex, totalWords);
                assignmentLines.Append(wordNumber)
                    .Append(". word=").Append(words[i].word)
                    .Append("; meaning=").Append(words[i].meaning)
                    .Append("; anchor_id=").Append(anchor.id)
                    .Append("; anchor_label=").Append(anchor.label)
                    .AppendLine();
            }

            return assignmentLines.ToString();
        }

        private static AnchorDefinition ResolveAssignedAnchor(List<AnchorDefinition> assignedAnchors, int localIndex, int globalIndex, int totalWords)
        {
            if (assignedAnchors != null
                && localIndex >= 0
                && localIndex < assignedAnchors.Count
                && assignedAnchors[localIndex] != null)
            {
                return assignedAnchors[localIndex];
            }

            return RoomSpecCatalog.GetAssignmentAnchor(globalIndex, totalWords);
        }

        private static GeneratedMnemonicItem MergeGeneratedMnemonicFields(
            WordEntry sourceWord,
            GeneratedMnemonicItem visualCue,
            GeneratedMnemonicItem mnemonicLink)
        {
            var merged = new GeneratedMnemonicItem
            {
                word = string.IsNullOrWhiteSpace(visualCue?.word) ? sourceWord.word : visualCue.word,
                anchor = string.IsNullOrWhiteSpace(visualCue?.anchor) ? mnemonicLink?.anchor : visualCue.anchor,
                cue_blueprint = visualCue?.cue_blueprint,
                visual_cue_en = visualCue?.visual_cue_en,
                association_prompt_en = visualCue?.association_prompt_en,
                image_prompt_en = visualCue?.image_prompt_en,
                image_prompt_candidates_en = visualCue?.image_prompt_candidates_en,
                visual_objects = visualCue?.visual_objects,
                mnemonic_en = mnemonicLink?.mnemonic_en,
                mnemonic_mode = mnemonicLink?.mnemonic_mode,
                hook_judge = mnemonicLink?.hook_judge,
                story_cue_en = string.Empty
            };

            if (string.IsNullOrWhiteSpace(merged.anchor))
            {
                merged.anchor = mnemonicLink?.anchor;
            }

            if (string.IsNullOrWhiteSpace(merged.mnemonic_en))
            {
                merged.mnemonic_en = BuildMnemonicLinkFallbackEn(sourceWord);
            }

            if (string.IsNullOrWhiteSpace(merged.mnemonic_mode))
            {
                merged.mnemonic_mode = "STORY_ONLY";
            }

            return merged;
        }

        private static string BuildMnemonicLinkFallbackEn(WordEntry word)
        {
            var meaning = string.IsNullOrWhiteSpace(word?.meaning) ? "the target meaning" : word.meaning.Trim();
            var spanish = string.IsNullOrWhiteSpace(word?.word) ? "the Spanish word" : word.word.Trim();
            return $"At the assigned anchor, a vivid {meaning} moment unfolds so clearly that the name {spanish} attaches to it.";
        }

        private static string SafePromptText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

        private static string CompactPromptLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(empty)";
            }

            var cleaned = Regex.Replace(text.Trim(), @"\s+", " ");
            return cleaned.Length > 420 ? cleaned.Substring(0, 420) + "..." : cleaned;
        }

        private static string BuildAcademicSafetyRules()
        {
            return
                "Academic participant-safety rules:\n" +
                "- This is an academic study. Use neutral, non-disturbing, participant-safe mnemonics only.\n" +
                "- Never use sexual or pornographic content, gambling, casino, betting, drugs, drug trafficking, narcotics, crime, criminals, crime cartels, mafia, gangs, weapons, violence, blood, horror, gore, disasters, hate, abuse, self-harm, or stigmatizing imagery.\n" +
                "- Do not use unsafe associations as word-form hooks, even if they sound like or resemble the Spanish word.\n" +
                "- If a Spanish word has an unsafe English association, ignore that association and cue the target meaning with a neutral physical object or action.\n" +
                "- For cartel when the target meaning is poster, show a neutral poster, tape, clip, frame, or notice-board display; do not show criminals, drug traffickers, wanted posters, mugshots, gangs, drugs, or crime.\n\n";
        }

        private static string BuildHiddenCueRejectionRules()
        {
            return
                "Hidden cue rejection rules for Image Scene generation:\n" +
                "- Reject visual cue candidates where the meaning cue is hidden, swallowed by the anchor, embedded inside furniture, or likely to disappear in image generation.\n" +
                "- Avoid relations such as in a pocket, inside a drawer, tucked away, hidden under fabric, covered by another object, blended into the anchor surface, or stored securely out of view.\n" +
                "- For small cue objects such as wallet, key, ring, coin, card, ticket, bottle, jar, or small model, keep the cue fully exposed, visually separate from the anchor, and easy to inspect.\n" +
                "- If the cue must involve a container or holder, make the container visibly open and the target cue clearly visible at the opening.\n\n";
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

        private IEnumerator RepairGeminiMnemonicJson(
            string apiKey,
            string model,
            string brokenJson,
            Action<string> onSuccess,
            Action<string> onError)
        {
            yield return SendGeminiGenerateRequest(
                apiKey,
                model,
                "Repair the malformed JSON below so that it becomes one valid JSON object only.\n" +
                "Do not change the intended content unless needed for valid JSON.\n" +
                "If a visual_objects field is malformed, incomplete, or too complex, replace that field with an empty array [].\n" +
                "Return only the repaired JSON object.\n\n" +
                brokenJson,
                "You repair malformed JSON. Return exactly one valid JSON object. No markdown. No explanation.",
                0f,
                2400,
                onSuccess,
                onError);
        }

        private static bool TryParseMnemonicEnvelope(string rawText, out GeneratedMnemonicEnvelope envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "LLM response text was empty.";
                return false;
            }

            var candidate = NormalizeJsonCandidate(rawText);
            if (TryDeserializeMnemonicEnvelope(candidate, out envelope, out error))
            {
                return true;
            }

            var strippedCandidate = StripMnemonicVisualObjects(candidate);
            if (!string.Equals(strippedCandidate, candidate, StringComparison.Ordinal)
                && TryDeserializeMnemonicEnvelope(strippedCandidate, out envelope, out error))
            {
                return true;
            }

            var closedCandidate = ClosePartialJson(strippedCandidate);
            if (!string.Equals(closedCandidate, strippedCandidate, StringComparison.Ordinal)
                && TryDeserializeMnemonicEnvelope(closedCandidate, out envelope, out error))
            {
                return true;
            }

            return false;
        }

        private static bool ShouldRetryMnemonicResponse(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return false;
            }

            var candidate = NormalizeJsonCandidate(rawText);
            return candidate.IndexOf("\"items\"", StringComparison.OrdinalIgnoreCase) < 0
                   || candidate.IndexOf("\"error\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryDeserializeMnemonicEnvelope(string candidate, out GeneratedMnemonicEnvelope envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(candidate))
            {
                error = "No JSON object could be extracted from the generation response.";
                return false;
            }

            try
            {
                envelope = JsonUtility.FromJson<GeneratedMnemonicEnvelope>(candidate);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (envelope == null || envelope.items == null || envelope.items.Length == 0)
            {
                error = "Parsed JSON did not contain any mnemonic items.";
                return false;
            }

            return true;
        }

        private static string StripMnemonicVisualObjects(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.IndexOf("\"visual_objects\"", StringComparison.Ordinal) < 0)
            {
                return json;
            }

            var cleaned = Regex.Replace(
                json,
                @",?\s*""visual_objects""\s*:\s*\[[\s\S]*?\](?=\s*[,}])",
                string.Empty);

            while (cleaned.IndexOf("\"visual_objects\"", StringComparison.Ordinal) >= 0)
            {
                var keyIndex = cleaned.LastIndexOf("\"visual_objects\"", StringComparison.Ordinal);
                if (keyIndex < 0)
                {
                    break;
                }

                var cutIndex = keyIndex;
                var scan = keyIndex - 1;
                while (scan >= 0 && char.IsWhiteSpace(cleaned[scan]))
                {
                    scan--;
                }

                if (scan >= 0 && cleaned[scan] == ',')
                {
                    cutIndex = scan;
                }

                cleaned = cleaned.Substring(0, cutIndex).TrimEnd();
            }

            cleaned = Regex.Replace(cleaned, @",(\s*[}\]])", "$1");
            return cleaned.Trim();
        }

        private static string ClosePartialJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            var cleaned = json.TrimEnd();
            while (cleaned.EndsWith(",", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 1).TrimEnd();
            }

            var stack = new Stack<char>();
            var inString = false;
            var escaping = false;

            for (int i = 0; i < cleaned.Length; i++)
            {
                var c = cleaned[i];

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

                if (c == '{' || c == '[')
                {
                    stack.Push(c);
                }
                else if ((c == '}' || c == ']') && stack.Count > 0)
                {
                    var expectedOpen = c == '}' ? '{' : '[';
                    if (stack.Peek() == expectedOpen)
                    {
                        stack.Pop();
                    }
                }
            }

            if (stack.Count == 0)
            {
                return cleaned;
            }

            var builder = new StringBuilder(cleaned);
            while (stack.Count > 0)
            {
                builder.Append(stack.Pop() == '{' ? '}' : ']');
            }

            return builder.ToString();
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

        private static List<VisualObjectSpec> NormalizeVisualObjects(VisualObjectSpec[] generatedObjects, int itemIndex)
        {
            return new List<VisualObjectSpec>();
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

        private static string PickShape(int index)
        {
            var shapes = new[] { "Sphere", "Cube", "Capsule", "Cylinder" };
            return shapes[index % shapes.Length];
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

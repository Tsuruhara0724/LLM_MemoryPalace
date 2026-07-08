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

        public string GeminiModelsUsedSummary => string.Join(", ", geminiModelsUsed);

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
            List<AnchorDefinition> assignedAnchors,
            Action<StorySessionData> onSuccess,
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

            if (words == null || words.Count == 0)
            {
                onError?.Invoke("No words were provided for story generation.");
                yield break;
            }

            if (assignedAnchors == null || assignedAnchors.Count < words.Count)
            {
                onError?.Invoke("Anchor assignments are incomplete.");
                yield break;
            }

            var causalPlanRequest = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildCausalStoryPlanPrompt(words),
                system = "You are a strict causal story planner. Return exactly one valid JSON object and nothing else. No prose outside JSON. No markdown.",
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
                onError?.Invoke("Ollama causal-plan request failed: " + causalPlanRequestError);
                yield break;
            }

            if (!TryPrepareCausalStoryPlan(causalPlanJson, words, out var preparedCausalPlanJson, out var causalPlanError))
            {
                onError?.Invoke("Ollama produced an invalid causal plan: " + causalPlanError + "\nRaw plan preview:\n" + BuildPreview(causalPlanJson));
                yield break;
            }

            causalPlanJson = preparedCausalPlanJson;

            var storyRequest = new OllamaGenerateRequest
            {
                model = model.Trim(),
                prompt = BuildStoryPrompt(words, causalPlanJson),
                system = "You are a strict causal fiction editor and JSON API. Audit the supplied plan, repair any physically impossible link, then return exactly one valid JSON object and nothing else. No markdown. No commentary.",
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
                onError?.Invoke("Ollama story-writing request failed: " + storyRequestError);
                yield break;
            }

            if (!TryParseAndBuildStoryResponse(storyResponse, words, assignedAnchors, model.Trim(), out var story, out var validationError))
            {
                var firstFailure = validationError;
                var repairRequest = new OllamaGenerateRequest
                {
                    model = model.Trim(),
                    prompt = BuildStoryRepairPrompt(words, causalPlanJson, storyResponse, firstFailure),
                    system = "You repair a rejected causal micro-story. Return exactly one valid JSON object and nothing else. No markdown. No commentary.",
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
                    onError?.Invoke("The first story failed validation (" + firstFailure + ") and the Ollama repair request failed: " + repairRequestError);
                    yield break;
                }

                if (!TryParseAndBuildStoryResponse(repairedResponse, words, assignedAnchors, model.Trim(), out story, out validationError))
                {
                    onError?.Invoke("Ollama story repair still failed validation: " + validationError + "\nRaw response preview:\n" + BuildPreview(repairedResponse));
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
            List<AnchorDefinition> assignedAnchors,
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

            if (assignedAnchors == null || assignedAnchors.Count < words.Count)
            {
                onError?.Invoke("Anchor assignments are incomplete.");
                yield break;
            }

            geminiModelsUsed.Clear();
            string causalPlanJson = null;
            string requestError = null;
            yield return SendGeminiGenerateRequest(
                apiKey.Trim(),
                model.Trim(),
                BuildCausalStoryPlanPrompt(words),
                "You are a strict causal story planner. Return exactly one valid JSON object and nothing else. No prose outside JSON. No markdown.",
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
                onError?.Invoke("Gemini produced an invalid causal plan: " + causalPlanError + "\nRaw plan preview:\n" + BuildPreview(causalPlanJson));
                yield break;
            }

            causalPlanJson = preparedCausalPlanJson;

            string storyResponse = null;
            requestError = null;
            yield return SendGeminiGenerateRequest(
                apiKey.Trim(),
                model.Trim(),
                BuildStoryPrompt(words, causalPlanJson),
                "You are a strict causal fiction editor and JSON API. Audit the supplied plan, repair any physically impossible link, then return exactly one valid JSON object and nothing else. No markdown. No commentary.",
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
            if (!TryParseAndBuildStoryResponse(storyResponse, words, assignedAnchors, resolvedModel, out var story, out var validationError))
            {
                var firstFailure = validationError;
                string repairedResponse = null;
                requestError = null;
                yield return SendGeminiGenerateRequest(
                    apiKey.Trim(),
                    model.Trim(),
                    BuildStoryRepairPrompt(words, causalPlanJson, storyResponse, firstFailure),
                    "You repair a rejected causal micro-story. Return exactly one valid JSON object and nothing else. No markdown. No commentary.",
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
                if (!TryParseAndBuildStoryResponse(repairedResponse, words, assignedAnchors, resolvedModel, out story, out validationError))
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

        private static IEnumerator SendOllamaJsonRequest(
            string endpoint,
            OllamaGenerateRequest requestBody,
            Action<string> onSuccess,
            Action<string> onError)
        {
            var json = JsonUtility.ToJson(requestBody);
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

        private static string BuildCausalStoryPlanPrompt(List<WordEntry> words)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Build a physically plausible causal event plan for one short everyday story.");
            builder.AppendLine("Use every target exactly once. Choose the order that makes the strongest causal chain.");
            builder.AppendLine("Choose ONE target as the central destination, person, place, or object that defines the story's urgent goal. Every other target must help, hinder, protect, repair, unlock, signal, transport, or otherwise change progress toward that same goal.");
            builder.AppendLine("The goal must not be a list of errands. Do not use shopping, packing several items, eating lunch, sightseeing, appreciating a view, attending unrelated events, or visiting multiple destinations as the story structure.");
            builder.AppendLine("Return JSON exactly as: {\"goal\":\"one concrete urgent goal\",\"items\":[{\"word\":\"zapato\",\"need\":\"specific prior obstacle requiring it\",\"action\":\"intentional physically possible action with it\",\"result\":\"visible result that makes the next item necessary\",\"goal_link\":\"how this beat changes progress toward the single goal\"}]}");
            builder.AppendLine("Each item must have non-empty word, need, action, result, and goal_link fields.");
            builder.AppendLine("HARD LINK FORMAT: copy the complete result text of item N verbatim into the need field of item N+1. The strings must be exactly identical. The action in item N+1 must respond directly to that copied situation. The last result must solve the goal.");
            builder.AppendLine("Reject magic, coincidence, dream logic, symbolic actions, impossible tool use, distant scenery, reflections that reveal unknown facts, and objects appearing without a source.");
            builder.AppendLine("Do not claim that an inaccessible shop supplies an item, that throwing one object summons another, or that a blunt object cuts or unlocks something without a believable mechanism.");
            builder.AppendLine("Prefer familiar actions that ordinary people could perform. Keep one route toward one destination and at least two active characters.");
            builder.AppendLine("Do not introduce important non-target props such as a bowl, lunch, bottle, rope, ticket, key, or borrowed book unless absolutely unavoidable for a small connecting action. The target objects must perform the important jobs.");
            builder.AppendLine("Silently simulate the chain from beginning to end before returning JSON. Repair any step whose result would not really follow from its action.");
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

        private static string NormalizePlanLink(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string BuildStoryPrompt(List<WordEntry> words, string causalPlanJson)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Write one warm, plot-driven English micro-story using exactly the " + words.Count + " target Spanish words below.");
            builder.AppendLine("The story is about what characters DO, not what a scene looks or sounds like.");
            builder.AppendLine("First audit the supplied causal plan. Fix any impossible action or missing mechanism while preserving its goal and target-word order. Then write the story from the repaired plan.");
            builder.AppendLine("Every sentence must visibly change progress toward the SAME goal. Do not let a character suddenly shop, eat, admire scenery, adjust clothing, or start another errand unless that action is indispensable to solving the original problem.");
            builder.AppendLine("Do not introduce major non-target props to do the useful work. Make the supplied target words carry the plot.");
            builder.AppendLine("SUPPLIED CAUSAL PLAN:");
            builder.AppendLine(causalPlanJson);
            builder.AppendLine();
            builder.AppendLine("NON-NEGOTIABLE STORY SHAPE");
            builder.AppendLine("1. Give you and one other active character one ordinary, concrete problem to solve together.");
            builder.AppendLine("2. Keep one setting, one goal, and one continuous chain of events from the opening problem to its resolution.");
            builder.AppendLine("3. Write roughly one sentence per target word and normally introduce exactly one new target pair in each sentence.");
            builder.AppendLine("4. Every target sentence must contain all three parts: a reason the character needs the target, an intentional physical action involving it, and an immediate visible result.");
            builder.AppendLine("5. That visible result must create the reason for the next sentence. The final target action must solve the original problem.");
            builder.AppendLine();
            builder.AppendLine("ACTION TEST FOR EVERY TARGET");
            builder.AppendLine("The target must be the direct subject or object of a concrete action verb. A character should carry, open, close, wear, strike, repair, turn, pour, cut, block, signal with, climb, move, or otherwise physically act on it.");
            builder.AppendLine("At least 80 percent of the sentences must show a character intentionally acting, reacting, deciding, helping, preventing, or correcting something.");
            builder.AppendLine("A target FAILS if it merely looms, glows, shines, hangs, sits, waits, stands, appears, reflects, echoes, fades, decorates the setting, or is visible in the distance.");
            builder.AppendLine("A target also FAILS if it appears only inside an as-clause, where-clause, background description, comparison, shadow, reflection, costume, procession, display, or list.");
            builder.AppendLine("Never bundle several target words into scenery or an improvised tableau. Each must perform its own necessary job in the plot.");
            builder.AppendLine();
            builder.AppendLine("CAUSALITY TEST");
            builder.AppendLine("Do not connect unrelated actions with then, so, therefore, prompting, or causing. State the real mechanism: what changed physically, what a character learned, or why a new action became necessary.");
            builder.AppendLine("Bad: The drum echoes in the distance where a flower seller waits. Both targets are scenery.");
            builder.AppendLine("Good: The loose gate traps Ana, so you beat the drum to call the flower seller; the seller cuts a tough flower stem and uses it to lift the jammed latch.");
            builder.AppendLine("Silently delete each target sentence. If the sentences before and after still connect, rewrite that target sentence because it is not doing narrative work.");
            builder.AppendLine();
            builder.AppendLine("STYLE AND TONE");
            builder.AppendLine("Use active voice, concrete verbs, character choices, small setbacks, and a satisfying practical resolution.");
            builder.AppendLine("Use no more than one short atmospheric clause in the entire story. Do not describe distant scenery or ambient sounds unless a character immediately acts on them.");
            builder.AppendLine("Keep the tone bright, everyday, and emotionally safe. No horror, dream logic, uncanny living objects, supernatural transformations, or unrelated parade, dance, spectacle, or celebration.");
            builder.AppendLine("Write in second person: the main character is always you. Do not name the main character or use he, she, his, or her for the main character.");
            builder.AppendLine("Do not mention the memory room, furniture anchors, route instructions, walking directions, mnemonics, or image generation.");
            builder.AppendLine("Aim for about 160-210 words for eight targets, scaling proportionally for other counts.");
            builder.AppendLine();
            builder.AppendLine("WORD FORMAT");
            builder.AppendLine("Every target must appear once as the exact English meaning followed immediately by the Spanish word in parentheses, for example shoe (zapato).");
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
            builder.AppendLine("Rewrite the rejected response into one coherent, warm, action-driven causal story.");
            builder.AppendLine("Validation failure: " + (validationError ?? "unknown validation error"));
            builder.AppendLine("Return exactly: {\"fullStory\":\"...\",\"items\":[{\"word\":\"target\",\"storyOrder\":1}]}.");
            builder.AppendLine("Every selected target must appear naturally in the fullStory exactly as English meaning (Spanish word), and each must perform an action that changes progress toward the same goal.");
            builder.AppendLine("Do not append isolated repair sentences, dream imagery, scenery-only descriptions, room-tour instructions, anchors, or furniture assignments.");
            builder.AppendLine("Keep the causal order from the plan, but repair any implausible action or weak connection.");
            builder.AppendLine();
            builder.AppendLine("Selected targets:");
            for (var i = 0; i < words.Count; i++)
            {
                builder.Append(i + 1).Append(". ").Append(words[i].meaning).Append(" (").Append(words[i].word).AppendLine(")");
            }
            builder.AppendLine();
            builder.AppendLine("Causal plan:");
            builder.AppendLine(causalPlanJson ?? string.Empty);
            builder.AppendLine();
            builder.AppendLine("Rejected response:");
            builder.AppendLine(rejectedResponse ?? string.Empty);
            return builder.ToString();
        }

        private static bool TryParseAndBuildStoryResponse(
            string response,
            List<WordEntry> words,
            List<AnchorDefinition> assignedAnchors,
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

            return TryBuildStorySession(envelope, words, assignedAnchors, model, out story, out error);
        }

        private static bool TryBuildStorySession(
            GeneratedStoryEnvelope envelope,
            List<WordEntry> words,
            List<AnchorDefinition> assignedAnchors,
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

            if (TryBuildOrderedStoryItemsFromResponse(envelope, words, assignedAnchors, out var ordered, out var strictError))
            {
                story = CreateStorySession(envelope.fullStory, ordered, storySource, model);
                return true;
            }

            var recoveredSource = repairedWords.Count > 0 ? "ollama_story_repaired_recovered" : "ollama_story_recovered";
            if (TryBuildStorySessionFromFullStory(envelope.fullStory, words, assignedAnchors, model, recoveredSource, out story, out var recoveryError))
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

            var annotated = match.Value + " (" + word.Trim() + ")";
            story = story.Substring(0, match.Index) + annotated + story.Substring(match.Index + match.Length);
            return true;
        }

        private static string BuildMissingRouteWordRepairSentence(WordEntry word, int repairIndex, int repairCount)
        {
            var meaning = string.IsNullOrWhiteSpace(word?.meaning) ? "target meaning" : word.meaning.Trim();
            var spanish = string.IsNullOrWhiteSpace(word?.word) ? "word" : word.word.Trim();
            var phrase = meaning + " (" + spanish + ")";
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
            List<AnchorDefinition> assignedAnchors,
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
                var anchor = assignedAnchors[sourceIndex];
                if (!TryResolveStoryBeat(envelope.fullStory, generated.storySegment, sourceWord, out var storyBeat))
                {
                    error = "Could not resolve a story beat for " + sourceWord.word + " from fullStory.";
                    return false;
                }

                ordered.Add(new WordImageItemData
                {
                    word = sourceWord.word,
                    meaning = sourceWord.meaning,
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    anchorType = RoomSpecCatalog.ResolveModelKey(anchor.id, anchor.label),
                    storyOrder = generated.storyOrder <= 0 ? i + 1 : generated.storyOrder,
                    storySegment = BuildStorySegment(sourceWord, storyBeat),
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

        private static bool TryResolveStoryBeat(string fullStory, string generatedSegment, WordEntry sourceWord, out string storyBeat)
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
                storyBeat = sourceWord.meaning + " (" + sourceWord.word + "): " + storyBeat;
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
            var beat = string.IsNullOrWhiteSpace(storyBeat) ? meaning + " (" + word + ")." : storyBeat.Trim();
            return beat;
        }

        private static bool TryBuildStorySessionFromFullStory(
            string fullStory,
            List<WordEntry> words,
            List<AnchorDefinition> assignedAnchors,
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
                    error = "fullStory does not contain " + word.meaning + " (" + word.word + ").";
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
                var anchor = assignedAnchors[Mathf.Clamp(occurrence.WordIndex, 0, assignedAnchors.Count - 1)];
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
                    storyBeat = sourceWord.meaning + " (" + sourceWord.word + "): " + storyBeat;
                }

                ordered.Add(new WordImageItemData
                {
                    word = sourceWord.word,
                    meaning = sourceWord.meaning,
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    anchorType = RoomSpecCatalog.ResolveModelKey(anchor.id, anchor.label),
                    storyOrder = order + 1,
                    storySegment = BuildStorySegment(sourceWord, storyBeat),
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
                fullStory = NormalizeDisplayStory(fullStory),
                storySource = source,
                storyProvider = "Ollama Local",
                storyModel = model,
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                orderedItems = ordered
            };
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

        private static int FindStoryWordIndex(string fullStory, string meaning, string word)
        {
            if (string.IsNullOrWhiteSpace(fullStory) || string.IsNullOrWhiteSpace(word))
            {
                return -1;
            }

            var wordPair = "(" + word.Trim() + ")";
            if (!string.IsNullOrWhiteSpace(meaning))
            {
                var meaningPair = meaning.Trim() + " " + wordPair;
                var meaningPairIndex = fullStory.IndexOf(meaningPair, StringComparison.OrdinalIgnoreCase);
                if (meaningPairIndex >= 0)
                {
                    return meaningPairIndex;
                }
            }

            return fullStory.IndexOf(wordPair, StringComparison.OrdinalIgnoreCase);
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

            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 60;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Ollama guided layout request failed: {request.error}\n{request.downloadHandler.text}");
                    yield break;
                }

                OllamaGenerateResponse response;
                try
                {
                    response = JsonUtility.FromJson<OllamaGenerateResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Failed to parse Ollama guided layout response envelope: {ex.Message}");
                    yield break;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.response))
                {
                    onError?.Invoke("Ollama guided layout response was empty.");
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(response.error))
                {
                    onError?.Invoke($"Ollama returned a guided layout error: {response.error}");
                    yield break;
                }

                if (!TryParseGuidedFurnitureLayout(response.response, out var envelope, out var parseError))
                {
                    string repairedPayload = null;
                    string repairFailure = null;

                    yield return RepairMnemonicJson(
                        endpoint.Trim(),
                        model.Trim(),
                        response.response,
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
                    onError?.Invoke("Failed to parse Ollama guided furniture layout JSON.\n" + parseError + "\n\nRaw response preview:\n" + BuildPreview(response.response));
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

            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 45;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                OllamaGenerateResponse response;
                try
                {
                    response = JsonUtility.FromJson<OllamaGenerateResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Failed to parse Ollama furniture envelope: {ex.Message}");
                    yield break;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.response))
                {
                    onError?.Invoke("Ollama furniture response was empty.");
                    yield break;
                }

                if (!TryParseFurnitureTemplateSuggestion(response.response, out var suggestion, out var parseError))
                {
                    onError?.Invoke($"Failed to parse Ollama furniture JSON: {parseError}");
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

            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(endpoint.Trim(), UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Ollama room request failed: {request.error}\n{request.downloadHandler.text}");
                    yield break;
                }

                OllamaGenerateResponse response;
                try
                {
                    response = JsonUtility.FromJson<OllamaGenerateResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Failed to parse Ollama room response envelope: {ex.Message}");
                    yield break;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.response))
                {
                    onError?.Invoke("Ollama room response was empty.");
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(response.error))
                {
                    onError?.Invoke($"Ollama returned a room generation error: {response.error}");
                    yield break;
                }

                if (!TryParseGeneratedRoomPlan(response.response, out var roomPlan, out var parseError))
                {
                    string repairedPayload = null;
                    string repairFailure = null;

                    yield return RepairMnemonicJson(
                        endpoint.Trim(),
                        model.Trim(),
                        response.response,
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
                            "Failed to parse Ollama room JSON.\n" +
                            parseError + "\n\n" +
                            "Raw response preview:\n" + BuildPreview(response.response));
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
            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Ollama request failed: {request.error}\n{request.downloadHandler.text}");
                    yield break;
                }

                OllamaGenerateResponse response;
                try
                {
                    response = JsonUtility.FromJson<OllamaGenerateResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Failed to parse Ollama response envelope: {ex.Message}");
                    yield break;
                }

                if (response == null)
                {
                    onError?.Invoke("Ollama response was empty.");
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(response.error))
                {
                    onError?.Invoke($"Ollama returned an error: {response.error}");
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(response.response))
                {
                    onError?.Invoke("Ollama response did not contain JSON.");
                    yield break;
                }

                onSuccess?.Invoke(response.response);
            }
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

            var json = JsonUtility.ToJson(requestBody);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                OllamaGenerateResponse response;
                try
                {
                    response = JsonUtility.FromJson<OllamaGenerateResponse>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Could not parse repair envelope: {ex.Message}");
                    yield break;
                }

                if (response == null || string.IsNullOrWhiteSpace(response.response))
                {
                    onError?.Invoke("Repair response was empty.");
                    yield break;
                }

                onSuccess?.Invoke(response.response);
            }
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
                error = "No JSON object could be extracted from the Ollama response.";
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
                error = "Ollama room response text was empty.";
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
                error = "Ollama furniture response text was empty.";
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
                error = "Ollama guided layout response text was empty.";
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
                generatedBy = "Ollama room planner + Unity layout builder",
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

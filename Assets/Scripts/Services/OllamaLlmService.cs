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
        private class GeneratedMnemonicEnvelope
        {
            public GeneratedMnemonicItem[] items;
        }

        [Serializable]
        private class GeneratedMnemonicItem
        {
            public string word;
            public string anchor;
            public string visual_cue_en;
            public string visual_cue_ja;
            public string mnemonic_en;
            public string mnemonic_ja;
            public string image_prompt_en;
            public string image_prompt_ja;
            public VisualObjectSpec[] visual_objects;
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

            var items = new List<MnemonicItemData>();
            var normalizedEndpoint = endpoint.Trim();
            var normalizedModel = model.Trim();
            var totalWords = words == null ? 0 : words.Count;
            for (int offset = 0; offset < totalWords; offset += MnemonicChunkSize)
            {
                var chunkCount = Math.Min(MnemonicChunkSize, totalWords - offset);
                var chunkWords = words.GetRange(offset, chunkCount);
                List<MnemonicItemData> chunkItems = null;
                string chunkError = null;

                yield return GenerateMnemonicChunk(
                    normalizedEndpoint,
                    normalizedModel,
                    chunkWords,
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

        private IEnumerator GenerateMnemonicChunk(
            string endpoint,
            string model,
            List<WordEntry> words,
            int globalOffset,
            int totalWords,
            Action<List<MnemonicItemData>> onSuccess,
            Action<string> onError)
        {
            var requestBody = new OllamaGenerateRequest
            {
                model = model,
                prompt = BuildPrompt(words, globalOffset, totalWords),
                system = "You write JSON for a Unity memory-palace app. The prompt contains complete word and anchor data. Return exactly one valid JSON object whose top-level key is items. Use double quotes for every key and string. Escape any inner quotes. No markdown or commentary.",
                format = "json",
                stream = false,
                options = new OllamaRequestOptions
                {
                    temperature = 0.5f,
                    num_predict = Mathf.Clamp(words.Count * 420 + 480, 1100, 2200)
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
                onError?.Invoke("Ollama response did not contain mnemonic JSON.");
                yield break;
            }

            var responseForError = rawResponse;
            if (!TryParseMnemonicEnvelope(rawResponse, out var parsedEnvelope, out var parseError))
            {
                if (ShouldRetryMnemonicResponse(rawResponse))
                {
                    string retryResponse = null;
                    string retryError = null;
                    var retryRequestBody = new OllamaGenerateRequest
                    {
                        model = model,
                        prompt = BuildCompactMnemonicRetryPrompt(words, globalOffset, totalWords),
                        system = "You write JSON for a Unity memory-palace app. The prompt contains complete word and anchor data. Return only one JSON object whose top-level key is items.",
                        format = "json",
                        stream = false,
                        options = new OllamaRequestOptions
                        {
                            temperature = 0.2f,
                            num_predict = Mathf.Clamp(words.Count * 420 + 480, 1100, 2200)
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
                            "Failed to parse Ollama mnemonic JSON.\n" +
                            parseError + "\n\n" +
                            "Raw response preview:\n" + BuildPreview(responseForError));
                        yield break;
                    }
                }
            }

            if (parsedEnvelope == null || parsedEnvelope.items == null || parsedEnvelope.items.Length == 0)
            {
                onError?.Invoke("Ollama returned empty mnemonic data.");
                yield break;
            }

            var items = new List<MnemonicItemData>();
            var alignedGeneratedItems = AlignGeneratedItemsToWords(words, parsedEnvelope.items);

            for (int i = 0; i < alignedGeneratedItems.Count && i < words.Count; i++)
            {
                var sourceWord = words[i];
                var generated = alignedGeneratedItems[i];
                if (generated == null)
                {
                    continue;
                }

                var globalIndex = globalOffset + i;
                var anchor = RoomSpecCatalog.GetAssignmentAnchor(globalIndex, totalWords);

                items.Add(new MnemonicItemData
                {
                    word = sourceWord.word,
                    meaning = sourceWord.meaning,
                    meaningJa = sourceWord.meaningJa,
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    visualCue = generated.visual_cue_en,
                    visualCueJa = string.IsNullOrWhiteSpace(generated.visual_cue_ja) ? generated.visual_cue_en : generated.visual_cue_ja,
                    mnemonic = generated.mnemonic_en,
                    mnemonicJa = string.IsNullOrWhiteSpace(generated.mnemonic_ja) ? generated.mnemonic_en : generated.mnemonic_ja,
                    imagePrompt = generated.image_prompt_en,
                    imagePromptJa = string.IsNullOrWhiteSpace(generated.image_prompt_ja) ? generated.image_prompt_en : generated.image_prompt_ja,
                    objectShape = PickShape(globalIndex),
                    colorHex = PickColor(globalIndex),
                    visualObjects = NormalizeVisualObjects(generated.visual_objects, globalIndex)
                });
            }

            onSuccess?.Invoke(items);
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

        private static string BuildPrompt(List<WordEntry> words, int globalOffset, int totalWords)
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

                if (!string.IsNullOrWhiteSpace(words[i].meaningJa))
                {
                    wordLines.Append(" (").Append(words[i].meaningJa).Append(')');
                }

                wordLines.AppendLine();
            }

            var anchorLines = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var globalIndex = globalOffset + i;
                var wordNumber = globalIndex + 1;
                var anchor = RoomSpecCatalog.GetAssignmentAnchor(globalIndex, totalWords);
                anchorLines.Append("- word ").Append(wordNumber).Append(" -> ").Append(anchor.id).Append(": ").Append(anchor.label).AppendLine();
            }

            return
                "You are generating VR memory palace mnemonics for foreign-language vocabulary learning.\n" +
                "Return exactly " + words.Count + " item(s), only for the WORDS listed below.\n\n" +
                "Use this complete word and anchor data:\n\n" +
                "WORDS:\n" + wordLines +
                "\nPRE-ASSIGNED ANCHORS:\n" + anchorLines +
                "\nYour output is exactly one JSON object with an items array matching the shape below.\n\n" +
                "Main objective:\n" +
                "Each item must help the learner retrieve the meaning first, and then support recall of the Spanish word form. The cue must be realistic, concrete, easy to imagine, and suitable for placement on an ordinary room object.\n\n" +
                "Use the exact anchor_id and anchor label from PRE-ASSIGNED ANCHORS. Never change the assigned anchor.\n\n" +
                "For each word, silently plan the mnemonic in this order:\n\n" +
                "1. Meaning cue:\n" +
                "Choose one concrete visible cue that directly retrieves the meaning.\n\n" +
                "2. Optional word-form hook:\n" +
                "Check whether the Spanish word has a clean and simple recall hook.\n" +
                "Use this priority:\n" +
                "a) direct sound similarity or cognate-like similarity with the meaning word;\n" +
                "b) a visible secondary object or action already compatible with the meaning scene;\n" +
                "c) no word-form hook, if any hook would be forced or confusing.\n\n" +
                "3. Unified scene:\n" +
                "If a word-form hook uses a concrete object, include that object visibly in visual_cue_en.\n" +
                "If the hook is only a direct sound similarity, mention it only in mnemonic_en.\n" +
                "The final scene must still be meaning-first.\n\n" +
                "4. Candidate ranking / rejection:\n" +
                "For each word, silently draft 3 different candidate mnemonics before choosing the final one.\n" +
                "For nature, weather, sky, water, landscape, outdoor, travel, place, or large-environment meanings, draft 3 different indoor proxy candidates.\n" +
                "Score the candidates silently on: clear meaning retrieval, anchor involvement, indoor physical visibility, no real outdoor scene, visual_objects completeness, specific image_prompt_en, and natural word-form hook.\n" +
                "Reject candidates where the cue is only the natural concept itself, only floats near the anchor, ignores the anchor, lacks a physical proxy, or forces a weak word-form pun.\n" +
                "Output only the highest-scoring candidate in the final JSON.\n\n" +
                "Nature / outdoor cue frames:\n" +
                "For nature, weather, sky, water, landscape, outdoor place, travel, public-space, or large-environment nouns, do not show the real outdoor scene directly.\n" +
                "Choose one indoor physical proxy: crafted model, cotton/paper/felt object, hanging mobile, contained effect in a bowl/jar/tray/bucket, miniature diorama, or framed glimpse through the anchor.\n" +
                "The proxy must be visibly clipped to, resting on, hanging from, contained by, placed beside, or attached to the assigned anchor.\n" +
                "Good examples: cotton cloud mobile clipped to a chair backrest; paper raindrops hanging from a lamp; tiny waterfall model pouring into a bucket below an air conditioner; miniature houses and neighbors on a doormat.\n" +
                "Avoid vague phrases like \"cloud shape,\" \"floating cloud,\" \"waterfall flows,\" \"a beach appears,\" or \"outdoor scene\" without a physical indoor proxy.\n\n" +
                "Scene design rules:\n" +
                "- Include the assigned anchor label in visual_cue_en.\n" +
                "- The foreground cue should be more memorable than the anchor.\n" +
                "- Make the cue physical, visible, and specific: a concrete noun plus a clear action or state.\n" +
                "- The cue should be understandable in one mental picture.\n" +
                "- Use realistic mini-scenes rather than fantasy, disasters, battles, horror, explosions, or surreal transformations.\n" +
                "- For nouns, show the object itself or a closely related object doing a defining action.\n" +
                "- For abstract nouns, turn the idea into a tangible physical state, contrast, container state, damage state, or simple interaction.\n" +
                "- For person nouns, show a small person doing the defining behavior.\n" +
                "- For nature/outdoor nouns, show the indoor proxy object, not the full natural phenomenon or real outdoor place.\n" +
                "- Keep it simple: one anchor, one foreground cue, one memorable action.\n" +
                "- The scene should feel like an object or small action placed on, beside, under, or attached to the anchor, not a whole-room redesign.\n\n" +
                "Cue Story rules:\n" +
                "- visual_cue_en and mnemonic_en must describe the same mnemonic, not two separate ideas.\n" +
                "- mnemonic_en must describe one unified retrieval path based on the same visible scene.\n" +
                "- First state how the visible cue retrieves the meaning.\n" +
                "- Then, only if natural, state how a visible detail, sound similarity, or simple cognate-like association helps recall the Spanish word form.\n" +
                "- Do not invent a new sound-hint object in mnemonic_en unless it already appears in visual_cue_en and visual_objects.\n" +
                "- A sound hook may be mentioned without adding a new object only when it is a direct sound similarity between the Spanish word and the meaning word.\n" +
                "- Do not write tautologies such as \"the curtain reminds you of cortina because cortina means curtain.\"\n" +
                "- Do not explain the word using the word itself, or the meaning using the meaning itself.\n" +
                "- If no clean word-form hook exists, keep the mnemonic meaning-first and do not force a weak pun.\n" +
                "- The learner should be able to infer the meaning from the scene first, then use the mnemonic to recall the Spanish word form.\n\n" +
                "Avoid:\n" +
                "- written words, labels, captions, alphabet letters, logos, arrows, icons, signs, or UI symbols;\n" +
                "- tiny dots, vague glow, colored light, mood lighting, smoke, haze, rhythm, or atmosphere as the main clue;\n" +
                "- whole-room scenes, empty rooms, interior design views, unanchored outdoor scenes, full natural landscapes, full city views, disasters, explosions, battle, horror, gore, or large smoke clouds;\n" +
                "- template phrases such as \"represents the meaning,\" \"symbolizes,\" \"embodies,\" \"shows the word,\" or \"using the anchor as memory location.\"\n\n" +
                "Batch variety:\n" +
                "- Within the batch, try to use different cue nouns and different main actions.\n" +
                "- However, meaning accuracy and recall quality are more important than forced variety.\n" +
                "- Before final JSON, check for duplicated or overly similar cues. If two items feel similar, rewrite one with a different object and action.\n\n" +
                "Field-specific rules:\n" +
                "- visual_cue_en should be 12 to 24 words and must start naturally with \"At the {AnchorLabel}, ...\"\n" +
                "- visual_cue_en should describe only the visible scene, not explain the symbolism.\n" +
                "- visual_cue_ja should be fluent and natural Japanese, not a word-for-word translation.\n" +
                "- mnemonic_en should be 14 to 30 words.\n" +
                "- mnemonic_en should explain the retrieval path: meaning first, then optional Spanish word-form support.\n" +
                "- mnemonic_ja should express the same retrieval path in natural Japanese.\n" +
                "- image_prompt_en should be 6 to 14 words and name only the foreground proxy cue/action, with no room overview.\n" +
                "- image_prompt_ja should be the same foreground cue/action in natural Japanese.\n" +
                "- For nature/outdoor meanings, image_prompt_en must name the proxy material/object, not just the concept word. Good: \"cotton cloud mobile clipped to chair backrest, paper raindrops\". Bad: \"cloud floating over chair\".\n" +
                "- visual_objects must list every concrete foreground object used for meaning retrieval.\n" +
                "- For nature/outdoor meanings, visual_objects must list the proxy object and visible parts, not only the abstract natural phenomenon.\n" +
                "- If mnemonic_en uses a visible object as a word-form hook, that object must also be included in visual_objects.\n" +
                "- Do not include the anchor itself in visual_objects unless the anchor is also part of the foreground cue.\n" +
                "- Do not include abstract ideas, emotions, meanings, or invisible sound hints in visual_objects.\n\n" +
                "Before outputting JSON, silently check each item:\n\n" +
                "1. Can the visual scene retrieve the meaning without reading the Spanish word?\n" +
                "2. Does mnemonic_en avoid simply restating the answer?\n" +
                "3. Does mnemonic_en explain the same scene as visual_cue_en?\n" +
                "4. If mnemonic_en mentions a concrete sound-hint object, does it appear in visual_cue_en and visual_objects?\n" +
                "5. If the word-form hook feels artificial, remove it and keep a meaning-first mnemonic.\n" +
                "6. Is the scene realistic enough to fit on or near an ordinary room anchor?\n" +
                "7. If the meaning is nature/outdoor/large-scale, did you choose the best indoor proxy after rejecting weaker candidates?\n" +
                "8. Is image_prompt_en a concrete proxy-object prompt rather than a concept-only prompt?\n" +
                "9. Is Japanese natural and fluent?\n\n" +
                "\nOutput only valid JSON in this shape:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"the word\",\n" +
                "      \"anchor\": \"the assigned anchor_id\",\n" +
                "      \"visual_cue_en\": \"At the assigned AnchorLabel, concrete foreground overlay cue action.\",\n" +
                "      \"visual_cue_ja\": \"same scene in Japanese\",\n" +
                "      \"mnemonic_en\": \"short cue story that retrieves the meaning and optionally supports the Spanish word form\",\n" +
                "      \"mnemonic_ja\": \"same memory link in Japanese\",\n" +
                "      \"image_prompt_en\": \"foreground cue/action only\",\n" +
                "      \"image_prompt_ja\": \"foreground cue/action only in Japanese\",\n" +
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

        private static string BuildCompactMnemonicRetryPrompt(List<WordEntry> words, int globalOffset, int totalWords)
        {
            var assignmentLines = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var globalIndex = globalOffset + i;
                var wordNumber = globalIndex + 1;
                var anchor = RoomSpecCatalog.GetAssignmentAnchor(globalIndex, totalWords);
                var meaningJa = string.IsNullOrWhiteSpace(words[i].meaningJa) ? "" : "; meaningJa=" + words[i].meaningJa;
                assignmentLines.Append(wordNumber)
                    .Append(". word=").Append(words[i].word)
                    .Append("; meaning=").Append(words[i].meaning)
                    .Append(meaningJa)
                    .Append("; anchor_id=").Append(anchor.id)
                    .Append("; anchor_label=").Append(anchor.label)
                    .AppendLine();
            }

            return
                "Generate VR memory palace mnemonics from the complete DATA below.\n" +
                "DATA:\n" + assignmentLines +
                "\nReturn exactly " + words.Count + " items in one JSON object whose top-level key is items.\n" +
                "Use each listed anchor_id and anchor_label exactly. The visual scene must retrieve the meaning first.\n" +
                "visual_cue_en must start with \"At the {anchor_label},\" and describe the visible scene only.\n" +
                "For each word, silently draft 3 candidate mnemonics and output only the best one.\n" +
                "For nature/weather/sky/water/outdoor/place/travel/large-environment meanings, candidates must use indoor physical proxies: crafted model, cotton/paper/felt object, hanging mobile, contained effect, miniature diorama, or framed glimpse at the assigned anchor.\n" +
                "Reject candidates that show a real outdoor scene, use only the concept word, ignore the anchor, lack a physical proxy, or force a weak word-form pun.\n" +
                "mnemonic_en must explain the same visible scene: meaning first, then optional natural Spanish word-form support.\n" +
                "If a word-form hook needs a visible object, that object must appear in visual_cue_en and visual_objects.\n" +
                "If no clean word-form hook exists, keep the mnemonic meaning-first.\n" +
                "image_prompt_en must name the proxy object/action, not only a concept word such as cloud, waterfall, beach, or neighborhood.\n" +
                "visual_objects must list concrete foreground objects only, not anchors or abstract ideas.\n" +
                "Output only this JSON shape:\n" +
                "{\n" +
                "  \"items\": [\n" +
                "    {\n" +
                "      \"word\": \"the word\",\n" +
                "      \"anchor\": \"the assigned anchor_id\",\n" +
                "      \"visual_cue_en\": \"At the assigned AnchorLabel, concrete foreground cue action.\",\n" +
                "      \"visual_cue_ja\": \"same scene in Japanese\",\n" +
                "      \"mnemonic_en\": \"meaning-first retrieval path with optional Spanish word-form support\",\n" +
                "      \"mnemonic_ja\": \"same retrieval path in Japanese\",\n" +
                "      \"image_prompt_en\": \"foreground cue/action only\",\n" +
                "      \"image_prompt_ja\": \"foreground cue/action only in Japanese\",\n" +
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

        private static bool TryParseMnemonicEnvelope(string rawText, out GeneratedMnemonicEnvelope envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = "Ollama response text was empty.";
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

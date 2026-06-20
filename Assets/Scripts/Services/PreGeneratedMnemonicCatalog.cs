using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MemPalaceLLM
{
    public static class PreGeneratedMnemonicCatalog
    {
        private static readonly string[] Shapes = { "Sphere", "Cube", "Capsule", "Cylinder" };
        private static readonly string[] Colors =
        {
            "#7EC8E3", "#F4A261", "#E76F51", "#A8DADC", "#2A9D8F",
            "#FFD166", "#E9C46A", "#CCD5AE", "#457B9D", "#9B5DE5"
        };

        private static PreGeneratedMnemonicLibrary cachedLibrary;
        private static Dictionary<string, PreGeneratedMnemonicItem> cachedByKey;

        public sealed class CoverageItem
        {
            public string word;
            public string meaning;
            public string anchorId;
            public string anchorLabel;
            public string anchorType;
            public string lookupKey;
            public bool hit;
        }

        public sealed class CoverageReport
        {
            public int total;
            public int hits;
            public int misses;
            public float ratio;
            public List<CoverageItem> items = new();
        }

        [Serializable]
        public sealed class ReviewedMnemonicBatch
        {
            public string instructions;
            public List<ReviewedMnemonicItem> items = new();
        }

        [Serializable]
        public sealed class ReviewedMnemonicItem
        {
            public string id;
            public string word;
            public string meaning;
            public string anchorType;
            public string anchorLabel;
            public string mnemonicSource;
            public string visualCue;
            public string mainCueObject;
            public string associationPrompt;
            public string mnemonic;
            public string mnemonicMode;
            public bool hookAccepted;
            public int hookScore;
            public string hookReason;
            public string mnemonicHook;
            public string imagePrompt;
            public List<string> imagePromptCandidates = new();
            public CueBlueprintData cueBlueprint;
            public string objectShape;
            public string colorHex;
            public List<VisualObjectSpec> visualObjects = new();
        }

        [Serializable]
        private sealed class PreGeneratedMnemonicLibrary
        {
            public List<PreGeneratedMnemonicItem> items = new();
        }

        [Serializable]
        private sealed class PreGeneratedMnemonicItem
        {
            public string id;
            public string word;
            public string meaning;
            public string anchorType;
            public string mnemonicSource;
            public string visualCue;
            public string mainCueObject;
            public string associationPrompt;
            public string mnemonic;
            public string mnemonicMode;
            public bool hookAccepted;
            public int hookScore;
            public string hookReason;
            public string mnemonicHook;
            public string imagePrompt;
            public List<string> imagePromptCandidates = new();
            public CueBlueprintData cueBlueprint;
            public string objectShape;
            public string colorHex;
            public List<VisualObjectSpec> visualObjects = new();
        }

        public static int EntryCount => LoadLibrary().items?.Count ?? 0;

        public static void Reload()
        {
            cachedLibrary = null;
            cachedByKey = null;
        }

        public static CoverageReport BuildCoverageReport(List<WordEntry> words, List<AnchorDefinition> assignedAnchors)
        {
            var report = new CoverageReport();
            if (words == null || words.Count == 0)
            {
                return report;
            }

            var lookup = LoadLookup();
            report.total = words.Count;
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var anchor = assignedAnchors != null && i < assignedAnchors.Count && assignedAnchors[i] != null
                    ? assignedAnchors[i]
                    : RoomSpecCatalog.GetAssignmentAnchor(i, words.Count);
                var wordKey = NormalizeKey(word?.word);
                var anchorType = NormalizeAnchorType(anchor);
                var lookupKey = BuildLookupKey(wordKey, anchorType);
                var hit = !string.IsNullOrWhiteSpace(wordKey)
                          && !string.IsNullOrWhiteSpace(anchorType)
                          && lookup.ContainsKey(lookupKey);

                if (hit)
                {
                    report.hits++;
                }

                report.items.Add(new CoverageItem
                {
                    word = word?.word,
                    meaning = word?.meaning,
                    anchorId = anchor?.id,
                    anchorLabel = anchor?.label,
                    anchorType = anchorType,
                    lookupKey = lookupKey,
                    hit = hit
                });
            }

            report.misses = report.total - report.hits;
            report.ratio = report.total <= 0 ? 0f : (float)report.hits / report.total;
            return report;
        }

        public static CoverageReport BuildMatrixCoverageReport(List<WordEntry> words, IReadOnlyList<string> anchorTypes)
        {
            var report = new CoverageReport();
            if (words == null || words.Count == 0 || anchorTypes == null || anchorTypes.Count == 0)
            {
                return report;
            }

            var lookup = LoadLookup();
            for (int wordIndex = 0; wordIndex < words.Count; wordIndex++)
            {
                var word = words[wordIndex];
                var wordKey = NormalizeKey(word?.word);
                for (int anchorIndex = 0; anchorIndex < anchorTypes.Count; anchorIndex++)
                {
                    var anchorType = NormalizeAnchorType(string.Empty, anchorTypes[anchorIndex]);
                    var lookupKey = BuildLookupKey(wordKey, anchorType);
                    var hit = !string.IsNullOrWhiteSpace(wordKey)
                              && !string.IsNullOrWhiteSpace(anchorType)
                              && lookup.ContainsKey(lookupKey);

                    report.total++;
                    if (hit)
                    {
                        report.hits++;
                    }

                    report.items.Add(new CoverageItem
                    {
                        word = word?.word,
                        meaning = word?.meaning,
                        anchorId = anchorType,
                        anchorLabel = anchorType,
                        anchorType = anchorType,
                        lookupKey = lookupKey,
                        hit = hit
                    });
                }
            }

            report.misses = report.total - report.hits;
            report.ratio = report.total <= 0 ? 0f : (float)report.hits / report.total;
            return report;
        }

        public static List<string> BuildDiagnostics()
        {
            var issues = new List<string>();
            var library = LoadLibrary();
            if (library.items == null || library.items.Count == 0)
            {
                issues.Add("Catalog is empty or missing Resources/PreGeneratedMnemonics.json.");
                return issues;
            }

            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < library.items.Count; i++)
            {
                var item = library.items[i];
                var label = string.IsNullOrWhiteSpace(item?.id) ? $"item #{i + 1}" : item.id.Trim();
                if (item == null)
                {
                    issues.Add($"Entry {i + 1} is null.");
                    continue;
                }

                var wordKey = NormalizeKey(item.word);
                var anchorType = NormalizeAnchorType(string.Empty, item.anchorType);
                var lookupKey = BuildLookupKey(wordKey, anchorType);
                if (string.IsNullOrWhiteSpace(wordKey))
                {
                    issues.Add($"{label}: word is empty.");
                }

                if (string.IsNullOrWhiteSpace(anchorType))
                {
                    issues.Add($"{label}: anchorType is empty.");
                }

                if (!string.IsNullOrWhiteSpace(wordKey) && !string.IsNullOrWhiteSpace(anchorType))
                {
                    if (seenKeys.TryGetValue(lookupKey, out var firstIndex))
                    {
                        issues.Add($"{label}: duplicate word+anchorType key '{lookupKey}' first seen at item #{firstIndex + 1}.");
                    }
                    else
                    {
                        seenKeys[lookupKey] = i;
                    }
                }

                if (string.IsNullOrWhiteSpace(item.visualCue))
                {
                    issues.Add($"{label}: visualCue is empty.");
                }

                if (string.IsNullOrWhiteSpace(item.associationPrompt))
                {
                    issues.Add($"{label}: associationPrompt is empty.");
                }

                if (string.IsNullOrWhiteSpace(item.mnemonic))
                {
                    issues.Add($"{label}: mnemonic is empty.");
                }

                if (string.IsNullOrWhiteSpace(item.imagePrompt))
                {
                    issues.Add($"{label}: imagePrompt is empty.");
                }

                var mode = string.IsNullOrWhiteSpace(item.mnemonicMode) ? "STORY_ONLY" : item.mnemonicMode.Trim();
                if (!string.Equals(mode, "STORY_ONLY", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(mode, "HOOK_PLUS_STORY", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"{label}: mnemonicMode must be STORY_ONLY or HOOK_PLUS_STORY.");
                }

                if (item.imagePromptCandidates == null || item.imagePromptCandidates.Count < 4)
                {
                    issues.Add($"{label}: imagePromptCandidates should contain at least 4 prompts.");
                }

                if (item.hookAccepted && item.hookScore < 7)
                {
                    issues.Add($"{label}: hookAccepted is true but hookScore is below 7.");
                }
            }

            return issues;
        }

        public static bool TryParseReviewedBatch(string rawJson, out List<ReviewedMnemonicItem> items, out string error)
        {
            items = null;
            error = string.Empty;
            var json = ExtractJsonPayload(rawJson);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "No JSON object or array was found in the pasted text.";
                return false;
            }

            try
            {
                var batch = JsonUtility.FromJson<ReviewedMnemonicBatch>(json);
                if (batch?.items == null || batch.items.Count == 0)
                {
                    error = "Reviewed mnemonic JSON must contain an items array.";
                    return false;
                }

                items = batch.items;
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to parse reviewed mnemonic JSON: " + ex.Message;
                return false;
            }
        }

        public static bool UpsertReviewedItems(
            List<ReviewedMnemonicItem> reviewedItems,
            out int upsertedCount,
            out string catalogPath,
            out List<string> issues)
        {
            upsertedCount = 0;
            catalogPath = string.Empty;
            issues = new List<string>();
            if (reviewedItems == null || reviewedItems.Count == 0)
            {
                issues.Add("No reviewed mnemonic items were provided.");
                return false;
            }

            var library = LoadLibrary();
            library.items ??= new List<PreGeneratedMnemonicItem>();
            var existingIndexes = BuildItemIndex(library.items);

            for (int i = 0; i < reviewedItems.Count; i++)
            {
                var reviewed = reviewedItems[i];
                if (!TryConvertReviewedItem(reviewed, i, out var converted, out var issue))
                {
                    issues.Add(issue);
                    continue;
                }

                var key = BuildLookupKey(
                    NormalizeKey(converted.word),
                    NormalizeAnchorType(string.Empty, converted.anchorType));
                if (existingIndexes.TryGetValue(key, out var existingIndex)
                    && existingIndex >= 0
                    && existingIndex < library.items.Count)
                {
                    library.items[existingIndex] = converted;
                }
                else
                {
                    library.items.Add(converted);
                    existingIndexes[key] = library.items.Count - 1;
                }

                upsertedCount++;
            }

            if (upsertedCount == 0)
            {
                return false;
            }

            catalogPath = GetCatalogFilePath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(catalogPath));
                File.WriteAllText(catalogPath, JsonUtility.ToJson(library, true));
            }
            catch (Exception ex)
            {
                issues.Add("Failed to write PreGeneratedMnemonics.json: " + ex.Message);
                return false;
            }

            cachedLibrary = library;
            cachedByKey = null;
            return true;
        }

        public static bool TryCreateItem(WordEntry word, AnchorDefinition anchor, int itemIndex, out MnemonicItemData item)
        {
            item = null;
            if (word == null || anchor == null)
            {
                return false;
            }

            var wordKey = NormalizeKey(word.word);
            var anchorType = NormalizeAnchorType(anchor);
            if (string.IsNullOrWhiteSpace(wordKey) || string.IsNullOrWhiteSpace(anchorType))
            {
                return false;
            }

            var lookup = LoadLookup();
            if (!lookup.TryGetValue(BuildLookupKey(wordKey, anchorType), out var source))
            {
                return false;
            }

            item = new MnemonicItemData
            {
                word = word.word,
                meaning = string.IsNullOrWhiteSpace(word.meaning) ? source.meaning : word.meaning,
                anchorId = anchor.id,
                anchorLabel = anchor.label,
                anchorType = anchorType,
                mnemonicSource = string.IsNullOrWhiteSpace(source.mnemonicSource) ? "pre_generated" : source.mnemonicSource,
                visualCue = source.visualCue,
                mainCueObject = source.mainCueObject,
                associationPrompt = source.associationPrompt,
                mnemonic = source.mnemonic,
                mnemonicMode = string.IsNullOrWhiteSpace(source.mnemonicMode) ? "STORY_ONLY" : source.mnemonicMode,
                hookAccepted = source.hookAccepted,
                hookScore = source.hookScore,
                hookReason = source.hookReason,
                mnemonicHook = source.mnemonicHook,
                storyCue = string.Empty,
                imagePrompt = source.imagePrompt,
                imagePromptCandidates = source.imagePromptCandidates == null
                    ? new List<string>()
                    : new List<string>(source.imagePromptCandidates),
                cueBlueprint = CloneCueBlueprint(source.cueBlueprint),
                selectedImageCandidateIndex = -1,
                objectShape = string.IsNullOrWhiteSpace(source.objectShape) ? Shapes[itemIndex % Shapes.Length] : source.objectShape,
                colorHex = string.IsNullOrWhiteSpace(source.colorHex) ? Colors[itemIndex % Colors.Length] : source.colorHex,
                visualObjects = CloneVisualObjects(source.visualObjects)
            };

            return true;
        }

        private static CueBlueprintData CloneCueBlueprint(CueBlueprintData source)
        {
            if (source == null)
            {
                return null;
            }

            return new CueBlueprintData
            {
                targetMeaning = source.targetMeaning,
                visualSceneCore = source.visualSceneCore,
                mainObject = source.mainObject,
                anchorRelation = source.anchorRelation,
                relativeSize = source.relativeSize,
                mainActionOrState = source.mainActionOrState,
                visibleObjects = source.visibleObjects == null ? new List<string>() : new List<string>(source.visibleObjects),
                mnemonicHookNote = source.mnemonicHookNote,
                mnemonicMode = source.mnemonicMode
            };
        }

        public static string NormalizeAnchorType(AnchorDefinition anchor)
        {
            return anchor == null
                ? string.Empty
                : NormalizeAnchorType(anchor.id, anchor.label);
        }

        public static string NormalizeAnchorType(string anchorId, string anchorLabel)
        {
            var text = (Safe(anchorId) + " " + Safe(anchorLabel)).ToLowerInvariant();
            if (ContainsAny(text, "air conditioner", "air_conditioner", "aircon", "a/c", " ac "))
            {
                return "air_conditioner";
            }

            if (ContainsAny(text, "bookcase", "bookshelf"))
            {
                return "bookshelf";
            }

                if (ContainsAny(text, "wardrobe", "closet", "cabinet", "armario"))
            {
                return "wardrobe";
            }

            if (ContainsAny(text, "television", "tv"))
            {
                return "television";
            }

            if (ContainsAny(text, "dining table"))
            {
                return "table";
            }

            var knownTypes = new[]
            {
                "door", "bed", "desk", "computer", "window", "chair", "table", "sofa",
                "toilet", "stove", "sink", "counter", "fridge", "refrigerator", "lamp",
                "plant", "shelf"
            };
            for (int i = 0; i < knownTypes.Length; i++)
            {
                if (ContainsAny(text, knownTypes[i]))
                {
                    return knownTypes[i] == "refrigerator" ? "fridge" : knownTypes[i];
                }
            }

            return SanitizeKey(string.IsNullOrWhiteSpace(anchorLabel) ? anchorId : anchorLabel);
        }

        private static Dictionary<string, int> BuildItemIndex(List<PreGeneratedMnemonicItem> items)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            if (items == null)
            {
                return index;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var wordKey = NormalizeKey(item?.word);
                var anchorType = NormalizeAnchorType(string.Empty, item?.anchorType);
                if (string.IsNullOrWhiteSpace(wordKey) || string.IsNullOrWhiteSpace(anchorType))
                {
                    continue;
                }

                var lookupKey = BuildLookupKey(wordKey, anchorType);
                if (!index.ContainsKey(lookupKey))
                {
                    index[lookupKey] = i;
                }
            }

            return index;
        }

        private static bool TryConvertReviewedItem(
            ReviewedMnemonicItem reviewed,
            int index,
            out PreGeneratedMnemonicItem converted,
            out string issue)
        {
            converted = null;
            issue = string.Empty;
            var label = string.IsNullOrWhiteSpace(reviewed?.id) ? $"review item #{index + 1}" : reviewed.id.Trim();
            if (reviewed == null)
            {
                issue = $"Review item #{index + 1} is null.";
                return false;
            }

            var wordKey = NormalizeKey(reviewed.word);
            var anchorType = NormalizeAnchorType(string.Empty, reviewed.anchorType);
            if (string.IsNullOrWhiteSpace(wordKey))
            {
                issue = $"{label}: word is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(anchorType))
            {
                issue = $"{label}: anchorType is empty.";
                return false;
            }

            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(reviewed.visualCue))
            {
                missingFields.Add("visualCue");
            }

            if (string.IsNullOrWhiteSpace(reviewed.mainCueObject))
            {
                missingFields.Add("mainCueObject");
            }

            if (string.IsNullOrWhiteSpace(reviewed.associationPrompt))
            {
                missingFields.Add("associationPrompt");
            }

            if (string.IsNullOrWhiteSpace(reviewed.mnemonic))
            {
                missingFields.Add("mnemonic");
            }

            if (string.IsNullOrWhiteSpace(reviewed.imagePrompt))
            {
                missingFields.Add("imagePrompt");
            }

            if (missingFields.Count > 0)
            {
                issue = $"{label}: missing required field(s): {string.Join(", ", missingFields)}.";
                return false;
            }

            var mode = string.IsNullOrWhiteSpace(reviewed.mnemonicMode)
                ? "STORY_ONLY"
                : reviewed.mnemonicMode.Trim();
            if (!string.Equals(mode, "STORY_ONLY", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mode, "HOOK_PLUS_STORY", StringComparison.OrdinalIgnoreCase))
            {
                issue = $"{label}: mnemonicMode must be STORY_ONLY or HOOK_PLUS_STORY.";
                return false;
            }

            var cleanImagePromptCandidates = CleanStringList(reviewed.imagePromptCandidates);
            if (cleanImagePromptCandidates.Count < 4)
            {
                issue = $"{label}: imagePromptCandidates must contain at least 4 prompts.";
                return false;
            }

            if (reviewed.hookAccepted && reviewed.hookScore < 7)
            {
                issue = $"{label}: hookAccepted is true but hookScore is below 7.";
                return false;
            }

            converted = new PreGeneratedMnemonicItem
            {
                id = string.IsNullOrWhiteSpace(reviewed.id)
                    ? wordKey + "_" + anchorType + "_reviewed_v1"
                    : reviewed.id.Trim(),
                word = reviewed.word.Trim(),
                meaning = Safe(reviewed.meaning),
                anchorType = anchorType,
                mnemonicSource = string.IsNullOrWhiteSpace(reviewed.mnemonicSource)
                    ? "gpt_reviewed"
                    : reviewed.mnemonicSource.Trim(),
                visualCue = reviewed.visualCue.Trim(),
                mainCueObject = reviewed.mainCueObject.Trim(),
                associationPrompt = reviewed.associationPrompt.Trim(),
                mnemonic = reviewed.mnemonic.Trim(),
                mnemonicMode = mode.ToUpperInvariant(),
                hookAccepted = reviewed.hookAccepted,
                hookScore = reviewed.hookScore,
                hookReason = Safe(reviewed.hookReason),
                mnemonicHook = Safe(reviewed.mnemonicHook),
                imagePrompt = reviewed.imagePrompt.Trim(),
                imagePromptCandidates = cleanImagePromptCandidates,
                cueBlueprint = reviewed.cueBlueprint,
                objectShape = Safe(reviewed.objectShape),
                colorHex = Safe(reviewed.colorHex),
                visualObjects = reviewed.visualObjects == null
                    ? new List<VisualObjectSpec>()
                    : new List<VisualObjectSpec>(reviewed.visualObjects)
            };

            return true;
        }

        private static List<string> CleanStringList(List<string> source)
        {
            var result = new List<string>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(source[i]))
                {
                    result.Add(source[i].Trim());
                }
            }

            return result;
        }

        private static string ExtractJsonPayload(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return string.Empty;
            }

            var text = rawJson.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = text.IndexOf('\n');
                if (firstNewLine >= 0)
                {
                    text = text[(firstNewLine + 1)..];
                }

                var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fenceIndex >= 0)
                {
                    text = text[..fenceIndex];
                }

                text = text.Trim();
            }

            var firstObject = text.IndexOf('{');
            var firstArray = text.IndexOf('[');
            if (firstArray >= 0 && (firstObject < 0 || firstArray < firstObject))
            {
                var lastArray = text.LastIndexOf(']');
                return lastArray > firstArray
                    ? "{\"items\":" + text.Substring(firstArray, lastArray - firstArray + 1) + "}"
                    : string.Empty;
            }

            var lastObject = text.LastIndexOf('}');
            return firstObject >= 0 && lastObject > firstObject
                ? text.Substring(firstObject, lastObject - firstObject + 1)
                : string.Empty;
        }

        private static PreGeneratedMnemonicLibrary LoadLibrary()
        {
            if (cachedLibrary != null)
            {
                return cachedLibrary;
            }

            var catalogFilePath = GetCatalogFilePath();
            if (File.Exists(catalogFilePath))
            {
                try
                {
                    cachedLibrary = JsonUtility.FromJson<PreGeneratedMnemonicLibrary>(File.ReadAllText(catalogFilePath))
                                    ?? new PreGeneratedMnemonicLibrary();
                    cachedLibrary.items ??= new List<PreGeneratedMnemonicItem>();
                    return cachedLibrary;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Failed to parse PreGeneratedMnemonics.json from disk: " + ex.Message);
                }
            }

            var asset = Resources.Load<TextAsset>("PreGeneratedMnemonics");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                cachedLibrary = new PreGeneratedMnemonicLibrary();
                return cachedLibrary;
            }

            try
            {
                cachedLibrary = JsonUtility.FromJson<PreGeneratedMnemonicLibrary>(asset.text)
                                ?? new PreGeneratedMnemonicLibrary();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to parse PreGeneratedMnemonics.json: " + ex.Message);
                cachedLibrary = new PreGeneratedMnemonicLibrary();
            }

            cachedLibrary.items ??= new List<PreGeneratedMnemonicItem>();
            return cachedLibrary;
        }

        private static Dictionary<string, PreGeneratedMnemonicItem> LoadLookup()
        {
            if (cachedByKey != null)
            {
                return cachedByKey;
            }

            cachedByKey = new Dictionary<string, PreGeneratedMnemonicItem>(StringComparer.Ordinal);
            var library = LoadLibrary();
            if (library.items == null)
            {
                return cachedByKey;
            }

            for (int i = 0; i < library.items.Count; i++)
            {
                var item = library.items[i];
                var wordKey = NormalizeKey(item?.word);
                var anchorType = NormalizeAnchorType(string.Empty, item?.anchorType);
                if (string.IsNullOrWhiteSpace(wordKey) || string.IsNullOrWhiteSpace(anchorType))
                {
                    continue;
                }

                var key = BuildLookupKey(wordKey, anchorType);
                if (!cachedByKey.ContainsKey(key))
                {
                    cachedByKey[key] = item;
                }
            }

            return cachedByKey;
        }

        private static string GetCatalogFilePath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "Resources", "PreGeneratedMnemonics.json"));
        }

        private static List<VisualObjectSpec> CloneVisualObjects(List<VisualObjectSpec> source)
        {
            var clone = new List<VisualObjectSpec>();
            if (source == null)
            {
                return clone;
            }

            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null)
                {
                    continue;
                }

                clone.Add(new VisualObjectSpec
                {
                    label = item.label,
                    primitiveShape = item.primitiveShape,
                    colorHex = item.colorHex,
                    localPosition = item.localPosition,
                    scale = item.scale,
                    effect = item.effect
                });
            }

            return clone;
        }

        private static string BuildLookupKey(string wordKey, string anchorType)
        {
            return wordKey + "|" + SanitizeKey(anchorType);
        }

        private static string NormalizeKey(string value)
        {
            return SanitizeKey(value);
        }

        private static string SanitizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var sanitized = builder.ToString().Trim('_');
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            return sanitized;
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(text) || terms == null)
            {
                return false;
            }

            for (int i = 0; i < terms.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(terms[i])
                    && text.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Safe(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }
    }
}

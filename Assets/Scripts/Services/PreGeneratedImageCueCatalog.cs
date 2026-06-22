using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MemPalaceLLM
{
    public static class PreGeneratedImageCueCatalog
    {
        public const int RequiredImagesPerPair = 4;
        public static readonly string[] FormalAnchorTypes =
        {
            "door", "bed", "desk", "chair", "bathtub",
            "sofa", "wardrobe", "bookshelf", "air_conditioner", "television"
        };

        private static PreGeneratedImageCueLibrary cachedLibrary;
        private static Dictionary<string, PreGeneratedImageCueItem> cachedByKey;

        public sealed class LoadedImageCueSet
        {
            public string word;
            public string anchorType;
            public int primaryIndex;
            public List<LoadedImageCueVariant> variants = new();
        }

        public sealed class LoadedImageCueVariant
        {
            public int index;
            public string label;
            public string rawPrompt;
            public string fullPrompt;
            public string imagePath;
            public Texture2D texture;
            public int score;
            public bool pass;
            public string reason;
        }

        public sealed class GeneratedImageCueVariant
        {
            public string label;
            public string rawPrompt;
            public string fullPrompt;
            public Texture2D texture;
            public int score;
            public bool pass;
            public string reason;
        }

        public sealed class CoverageItem
        {
            public string word;
            public string meaning;
            public string anchorId;
            public string anchorLabel;
            public string anchorType;
            public string lookupKey;
            public int imageCount;
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
        private sealed class PreGeneratedImageCueLibrary
        {
            public List<PreGeneratedImageCueItem> items = new();
        }

        [Serializable]
        private sealed class PreGeneratedImageCueItem
        {
            public string id;
            public string word;
            public string meaning;
            public string anchorType;
            public int primaryIndex;
            public List<PreGeneratedImageCueVariant> images = new();
        }

        [Serializable]
        private sealed class PreGeneratedImageCueVariant
        {
            public string label;
            public string resourcePath;
            public string filePath;
            public string rawPrompt;
            public string fullPrompt;
            public int score;
            public bool pass;
            public string reason;
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
                var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor);
                var lookupKey = BuildLookupKey(wordKey, anchorType);
                var imageCount = 0;
                var hit = false;
                if (!string.IsNullOrWhiteSpace(wordKey)
                    && !string.IsNullOrWhiteSpace(anchorType)
                    && lookup.TryGetValue(lookupKey, out var item))
                {
                    imageCount = CountUsableImages(item);
                    hit = imageCount >= RequiredImagesPerPair;
                }

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
                    imageCount = imageCount,
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
                    var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, anchorTypes[anchorIndex]);
                    var lookupKey = BuildLookupKey(wordKey, anchorType);
                    var imageCount = 0;
                    var hit = false;
                    if (!string.IsNullOrWhiteSpace(wordKey)
                        && !string.IsNullOrWhiteSpace(anchorType)
                        && lookup.TryGetValue(lookupKey, out var item))
                    {
                        imageCount = CountUsableImages(item);
                        hit = imageCount >= RequiredImagesPerPair;
                    }

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
                        imageCount = imageCount,
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
                issues.Add("Image cue catalog is empty or missing Resources/PreGeneratedImageCueCatalog.json.");
                return issues;
            }

            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < library.items.Count; i++)
            {
                var item = library.items[i];
                var label = string.IsNullOrWhiteSpace(item?.id) ? $"image item #{i + 1}" : item.id.Trim();
                if (item == null)
                {
                    issues.Add($"Image cue entry {i + 1} is null.");
                    continue;
                }

                var wordKey = NormalizeKey(item.word);
                var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, item.anchorType);
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
                        issues.Add($"{label}: duplicate word+anchorType key '{lookupKey}' first seen at image item #{firstIndex + 1}.");
                    }
                    else
                    {
                        seenKeys[lookupKey] = i;
                    }
                }

                var imageCount = CountUsableImages(item);
                if (imageCount < RequiredImagesPerPair)
                {
                    issues.Add($"{label}: has {imageCount}/{RequiredImagesPerPair} usable image path(s).");
                }

                if (item.images != null)
                {
                    for (int j = 0; j < item.images.Count; j++)
                    {
                        var image = item.images[j];
                        if (image == null)
                        {
                            issues.Add($"{label}: image #{j + 1} is null.");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(image.resourcePath) && string.IsNullOrWhiteSpace(image.filePath))
                        {
                            issues.Add($"{label}: image #{j + 1} has neither resourcePath nor filePath.");
                        }
                    }
                }
            }

            return issues;
        }

        public static bool UpsertGeneratedImageCueSet(
            string word,
            string meaning,
            string anchorType,
            List<GeneratedImageCueVariant> variants,
            int primaryIndex,
            out string catalogPath,
            out string error)
        {
            catalogPath = string.Empty;
            error = string.Empty;
            var wordKey = NormalizeKey(word);
            var normalizedAnchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, anchorType);
            if (string.IsNullOrWhiteSpace(wordKey) || string.IsNullOrWhiteSpace(normalizedAnchorType))
            {
                error = "Word or anchorType is empty.";
                return false;
            }

            if (variants == null || variants.Count < RequiredImagesPerPair)
            {
                error = $"Need {RequiredImagesPerPair} image variants before writing a catalog entry.";
                return false;
            }

            var library = LoadLibrary();
            library.items ??= new List<PreGeneratedImageCueItem>();
            var key = BuildLookupKey(wordKey, normalizedAnchorType);
            PreGeneratedImageCueItem item = null;
            for (int i = 0; i < library.items.Count; i++)
            {
                var existing = library.items[i];
                if (existing == null)
                {
                    continue;
                }

                var existingKey = BuildLookupKey(
                    NormalizeKey(existing.word),
                    PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, existing.anchorType));
                if (string.Equals(existingKey, key, StringComparison.Ordinal))
                {
                    item = existing;
                    break;
                }
            }

            item ??= new PreGeneratedImageCueItem();
            item.id = wordKey + "_" + normalizedAnchorType + "_images_v1";
            item.word = string.IsNullOrWhiteSpace(word) ? wordKey : word.Trim();
            item.meaning = string.IsNullOrWhiteSpace(meaning) ? string.Empty : meaning.Trim();
            item.anchorType = normalizedAnchorType;
            item.primaryIndex = Mathf.Clamp(primaryIndex, 0, variants.Count - 1);
            item.images = new List<PreGeneratedImageCueVariant>();

            var folder = Path.Combine(
                Application.dataPath,
                "Resources",
                "PreGeneratedImageCues",
                wordKey,
                normalizedAnchorType);
            Directory.CreateDirectory(folder);

            for (int i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                if (variant == null || variant.texture == null)
                {
                    error = $"Generated variant #{i + 1} is empty.";
                    return false;
                }

                var label = string.IsNullOrWhiteSpace(variant.label) ? BuildLabel(i) : SanitizeFilePart(variant.label);
                var fileName = wordKey + "_" + normalizedAnchorType + "_" + label + ".png";
                var fullPath = Path.Combine(folder, fileName);
                try
                {
                    File.WriteAllBytes(fullPath, variant.texture.EncodeToPNG());
                }
                catch (Exception ex)
                {
                    error = "Failed to write image file: " + ex.Message;
                    return false;
                }

                var resourcePath = "PreGeneratedImageCues/" + wordKey + "/" + normalizedAnchorType + "/" + Path.GetFileNameWithoutExtension(fileName);
                item.images.Add(new PreGeneratedImageCueVariant
                {
                    label = label,
                    resourcePath = resourcePath,
                    filePath = string.Empty,
                    rawPrompt = variant.rawPrompt,
                    fullPrompt = variant.fullPrompt,
                    score = variant.score,
                    pass = variant.pass,
                    reason = string.IsNullOrWhiteSpace(variant.reason)
                        ? "Generated by Image Catalog Builder."
                        : variant.reason.Trim()
                });
            }

            if (!library.items.Contains(item))
            {
                library.items.Add(item);
            }

            catalogPath = GetCatalogFilePath();
            try
            {
                File.WriteAllText(catalogPath, JsonUtility.ToJson(library, true));
            }
            catch (Exception ex)
            {
                error = "Failed to write PreGeneratedImageCueCatalog.json: " + ex.Message;
                return false;
            }

            cachedLibrary = library;
            cachedByKey = null;
            return true;
        }

        public static bool TryLoadImageCueSet(MnemonicItemData item, out LoadedImageCueSet loadedSet, out string error)
        {
            loadedSet = null;
            error = string.Empty;
            if (item == null)
            {
                error = "Mnemonic item is null.";
                return false;
            }

            return TryLoadImageCueSet(item.word, item.meaning, item.anchorType, out loadedSet, out error);
        }

        public static bool TryLoadImageCueSet(string word, string meaning, string anchorType, out LoadedImageCueSet loadedSet, out string error)
        {
            loadedSet = null;
            error = string.Empty;
            var wordKey = NormalizeKey(word);
            var normalizedAnchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, anchorType);
            if (string.IsNullOrWhiteSpace(wordKey) || string.IsNullOrWhiteSpace(normalizedAnchorType))
            {
                error = "Word or anchorType is empty.";
                return false;
            }

            var lookup = LoadLookup();
            if (!lookup.TryGetValue(BuildLookupKey(wordKey, normalizedAnchorType), out var source))
            {
                error = $"No pre-generated image cue entry for {word} x {normalizedAnchorType}.";
                return false;
            }

            if (source.images == null || source.images.Count < RequiredImagesPerPair)
            {
                error = $"Image cue entry for {word} x {normalizedAnchorType} has {source.images?.Count ?? 0}/{RequiredImagesPerPair} image records.";
                return false;
            }

            var set = new LoadedImageCueSet
            {
                word = string.IsNullOrWhiteSpace(word) ? source.word : word,
                anchorType = normalizedAnchorType,
                primaryIndex = Mathf.Clamp(source.primaryIndex, 0, source.images.Count - 1)
            };

            for (int i = 0; i < source.images.Count; i++)
            {
                var sourceImage = source.images[i];
                if (sourceImage == null)
                {
                    continue;
                }

                if (!TryLoadTexture(sourceImage, out var texture, out var imagePath, out var imageError))
                {
                    error = $"{source.id}: failed to load image #{i + 1}: {imageError}";
                    DestroyLoadedTextures(set);
                    return false;
                }

                set.variants.Add(new LoadedImageCueVariant
                {
                    index = i,
                    label = string.IsNullOrWhiteSpace(sourceImage.label) ? BuildLabel(i) : sourceImage.label.Trim(),
                    rawPrompt = sourceImage.rawPrompt,
                    fullPrompt = sourceImage.fullPrompt,
                    imagePath = imagePath,
                    texture = texture,
                    score = sourceImage.score,
                    pass = sourceImage.pass,
                    reason = string.IsNullOrWhiteSpace(sourceImage.reason)
                        ? "Loaded from pre-generated image cue catalog."
                        : sourceImage.reason.Trim()
                });
            }

            if (set.variants.Count < RequiredImagesPerPair)
            {
                error = $"Image cue entry for {word} x {normalizedAnchorType} loaded {set.variants.Count}/{RequiredImagesPerPair} images.";
                DestroyLoadedTextures(set);
                return false;
            }

            loadedSet = set;
            return true;
        }

        private static bool TryLoadTexture(PreGeneratedImageCueVariant image, out Texture2D texture, out string imagePath, out string error)
        {
            texture = null;
            imagePath = string.Empty;
            error = string.Empty;

            if (!string.IsNullOrWhiteSpace(image.filePath))
            {
                var resolvedPath = ResolveFilePath(image.filePath.Trim());
                if (!File.Exists(resolvedPath))
                {
                    error = "filePath does not exist: " + resolvedPath;
                    return false;
                }

                try
                {
                    var bytes = File.ReadAllBytes(resolvedPath);
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(texture);
                        texture = null;
                        error = "Unity failed to decode image bytes.";
                        return false;
                    }

                    imagePath = resolvedPath;
                    return true;
                }
                catch (Exception ex)
                {
                    if (texture != null)
                    {
                        UnityEngine.Object.Destroy(texture);
                    }

                    texture = null;
                    error = ex.Message;
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(image.resourcePath))
            {
                var resourcePath = NormalizeResourcePath(image.resourcePath);
                var sourceTexture = Resources.Load<Texture2D>(resourcePath);
                if (sourceTexture == null)
                {
                    error = "resourcePath was not found: " + resourcePath;
                    return false;
                }

                texture = CreateReadableCopy(sourceTexture);
                imagePath = "Resources/" + resourcePath;
                return true;
            }

            error = "No resourcePath or filePath was provided.";
            return false;
        }

        private static Texture2D CreateReadableCopy(Texture2D source)
        {
            var previous = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            return copy;
        }

        private static void DestroyLoadedTextures(LoadedImageCueSet set)
        {
            if (set?.variants == null)
            {
                return;
            }

            for (int i = 0; i < set.variants.Count; i++)
            {
                var texture = set.variants[i]?.texture;
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        private static int CountUsableImages(PreGeneratedImageCueItem item)
        {
            if (item?.images == null)
            {
                return 0;
            }

            var count = 0;
            for (int i = 0; i < item.images.Count; i++)
            {
                var image = item.images[i];
                if (image != null
                    && (!string.IsNullOrWhiteSpace(image.resourcePath) || !string.IsNullOrWhiteSpace(image.filePath)))
                {
                    count++;
                }
            }

            return count;
        }

        private static PreGeneratedImageCueLibrary LoadLibrary()
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
                    cachedLibrary = JsonUtility.FromJson<PreGeneratedImageCueLibrary>(File.ReadAllText(catalogFilePath))
                                    ?? new PreGeneratedImageCueLibrary();
                    cachedLibrary.items ??= new List<PreGeneratedImageCueItem>();
                    return cachedLibrary;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Failed to parse PreGeneratedImageCueCatalog.json from disk: " + ex.Message);
                }
            }

            var asset = Resources.Load<TextAsset>("PreGeneratedImageCueCatalog");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                cachedLibrary = new PreGeneratedImageCueLibrary();
                return cachedLibrary;
            }

            try
            {
                cachedLibrary = JsonUtility.FromJson<PreGeneratedImageCueLibrary>(asset.text)
                                ?? new PreGeneratedImageCueLibrary();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to parse PreGeneratedImageCueCatalog.json: " + ex.Message);
                cachedLibrary = new PreGeneratedImageCueLibrary();
            }

            cachedLibrary.items ??= new List<PreGeneratedImageCueItem>();
            return cachedLibrary;
        }

        private static Dictionary<string, PreGeneratedImageCueItem> LoadLookup()
        {
            if (cachedByKey != null)
            {
                return cachedByKey;
            }

            cachedByKey = new Dictionary<string, PreGeneratedImageCueItem>(StringComparer.Ordinal);
            var library = LoadLibrary();
            if (library.items == null)
            {
                return cachedByKey;
            }

            for (int i = 0; i < library.items.Count; i++)
            {
                var item = library.items[i];
                var wordKey = NormalizeKey(item?.word);
                var anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(string.Empty, item?.anchorType);
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
            return Path.GetFullPath(Path.Combine(Application.dataPath, "Resources", "PreGeneratedImageCueCatalog.json"));
        }

        private static string ResolveFilePath(string filePath)
        {
            if (Path.IsPathRooted(filePath))
            {
                return Path.GetFullPath(filePath);
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", filePath));
        }

        private static string NormalizeResourcePath(string resourcePath)
        {
            var result = resourcePath.Trim().Replace('\\', '/');
            const string prefix = "Resources/";
            var prefixIndex = result.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex >= 0)
            {
                result = result[(prefixIndex + prefix.Length)..];
            }

            var extension = Path.GetExtension(result);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                result = result[..^extension.Length];
            }

            return result;
        }

        private static string BuildLookupKey(string wordKey, string anchorType)
        {
            return wordKey + "|" + NormalizeKey(anchorType);
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
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

        private static string SanitizeFilePart(string value)
        {
            var key = NormalizeKey(value);
            return string.IsNullOrWhiteSpace(key) ? "image" : key;
        }

        private static string BuildLabel(int index)
        {
            return index switch
            {
                0 => "A",
                1 => "B",
                2 => "C",
                3 => "D",
                _ => "Image " + (index + 1)
            };
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MemPalaceLLM
{
    public static class MockMnemonicGenerator
    {
        private static readonly string[] Shapes = { "Sphere", "Cube", "Capsule", "Cylinder" };
        private static readonly string[] Colors =
        {
            "#7EC8E3", "#F4A261", "#E76F51", "#A8DADC", "#2A9D8F",
            "#FFD166", "#E9C46A", "#CCD5AE", "#457B9D", "#9B5DE5"
        };

        public static List<MnemonicItemData> Generate(WordSetDefinition wordSet, DemoDataLibrary library)
        {
            var sampleSet = FindSampleSet(wordSet.setId, library);
            if (sampleSet != null && sampleSet.items.Count == wordSet.words.Count)
            {
                return MergeSampleSet(wordSet, sampleSet);
            }

            return GenerateFallback(wordSet.words);
        }

        public static List<MnemonicItemData> GenerateFallback(List<WordEntry> words)
        {
            var results = new List<MnemonicItemData>();

            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var anchor = RoomSpecCatalog.Anchors[i % RoomSpecCatalog.AnchorCount];
                var meaning = string.IsNullOrWhiteSpace(word.meaning) ? "the target meaning" : word.meaning.Trim();
                var cue = $"At the {anchor.label}, an exaggerated scene dramatizes '{word.meaning}' with bright motion and oversized props.";
                var association = $"oversized prop for {word.meaning} physically interacting with the {anchor.label}";
                var mnemonic = $"At the {anchor.label}, the cue grows into a small {meaning} moment with clear motion and oversized props. As the scene settles, {word.word} becomes the name attached to it.";

                results.Add(new MnemonicItemData
                {
                    word = word.word,
                    meaning = word.meaning,
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    anchorType = PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor),
                    mnemonicSource = "mock_fallback",
                    visualCue = cue,
                    associationPrompt = association,
                    mnemonic = mnemonic,
                    mnemonicMode = "STORY_ONLY",
                    hookAccepted = false,
                    hookScore = 0,
                    hookReason = "Fallback generator uses story-only mnemonics unless a curated hook is provided.",
                    mnemonicHook = string.Empty,
                    storyCue = string.Empty,
                    imagePrompt = association,
                    imagePromptCandidates = new List<string>
                    {
                        association + ", object-on-anchor close-up",
                        association + ", action-focused close-up",
                        association + ", unusual but physically possible relation",
                        association + ", simplest literal foreground cue"
                    },
                    cueBlueprint = new CueBlueprintData
                    {
                        targetMeaning = meaning,
                        visualSceneCore = association,
                        mainObject = "oversized meaning prop",
                        anchorRelation = "physically interacting with " + anchor.label,
                        relativeSize = "large foreground cue",
                        mainActionOrState = "bright motion",
                        visibleObjects = new List<string> { "oversized meaning prop" },
                        mnemonicHookNote = string.Empty,
                        mnemonicMode = "STORY_ONLY"
                    },
                    objectShape = Shapes[i % Shapes.Length],
                    colorHex = Colors[i % Colors.Length]
                });
            }

            return results;
        }

        private static SampleMnemonicSet FindSampleSet(string setId, DemoDataLibrary library)
        {
            for (int i = 0; i < library.sampleMnemonics.Count; i++)
            {
                if (library.sampleMnemonics[i].setId == setId)
                {
                    return library.sampleMnemonics[i];
                }
            }

            return null;
        }

        private static List<MnemonicItemData> MergeSampleSet(WordSetDefinition wordSet, SampleMnemonicSet sampleSet)
        {
            var merged = new List<MnemonicItemData>();

            for (int i = 0; i < sampleSet.items.Count; i++)
            {
                var sample = sampleSet.items[i];
                var word = wordSet.words[i];
                var anchor = RoomSpecCatalog.GetAnchor(sample.anchorId);
                var anchorType = string.IsNullOrWhiteSpace(sample.anchorType)
                    ? PreGeneratedMnemonicCatalog.NormalizeAnchorType(anchor)
                    : sample.anchorType;

                merged.Add(new MnemonicItemData
                {
                    word = word.word,
                    meaning = word.meaning,
                    anchorId = sample.anchorId,
                    anchorLabel = anchor.label,
                    anchorType = anchorType,
                    mnemonicSource = string.IsNullOrWhiteSpace(sample.mnemonicSource) ? "sample_data" : sample.mnemonicSource,
                    visualCue = sample.visualCue,
                    associationPrompt = string.IsNullOrWhiteSpace(sample.associationPrompt) ? sample.imagePrompt : sample.associationPrompt,
                    mnemonic = sample.mnemonic,
                    mnemonicMode = string.IsNullOrWhiteSpace(sample.mnemonicMode) ? "STORY_ONLY" : sample.mnemonicMode,
                    hookAccepted = sample.hookAccepted,
                    hookScore = sample.hookScore,
                    hookReason = sample.hookReason,
                    mnemonicHook = sample.mnemonicHook,
                    storyCue = string.Empty,
                    imagePrompt = sample.imagePrompt,
                    imagePromptCandidates = sample.imagePromptCandidates == null ? new List<string>() : new List<string>(sample.imagePromptCandidates),
                    cueBlueprint = sample.cueBlueprint,
                    objectShape = string.IsNullOrWhiteSpace(sample.objectShape) ? Shapes[i % Shapes.Length] : sample.objectShape,
                    colorHex = string.IsNullOrWhiteSpace(sample.colorHex) ? Colors[i % Colors.Length] : sample.colorHex
                });
            }

            return merged;
        }
    }
}

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
                var mnemonic = $"The Spanish word for {meaning} is {word.word}. Imagine the cue object has that name, so the name stays with the meaning.";

                results.Add(new MnemonicItemData
                {
                    word = word.word,
                    meaning = word.meaning,
                    meaningJa = word.meaningJa,
                    anchorId = anchor.id,
                    anchorLabel = anchor.label,
                    visualCue = cue,
                    visualCueJa = $"{anchor.label} で、「{(string.IsNullOrWhiteSpace(word.meaningJa) ? word.meaning : word.meaningJa)}」を表す印象的な場面を想像する。",
                    associationPrompt = association,
                    associationPromptJa = association,
                    mnemonic = mnemonic,
                    mnemonicJa = $"「{word.word}」の音や意味を {anchor.label} と結びつけて覚える。",
                    imagePrompt = association,
                    imagePromptJa = association,
                    imagePromptCandidates = new List<string>
                    {
                        association + ", object-on-anchor close-up",
                        association + ", action-focused close-up",
                        association + ", unusual but physically possible relation",
                        association + ", simplest literal foreground cue"
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

                merged.Add(new MnemonicItemData
                {
                    word = word.word,
                    meaning = word.meaning,
                    meaningJa = word.meaningJa,
                    anchorId = sample.anchorId,
                    anchorLabel = anchor.label,
                    visualCue = sample.visualCue,
                    visualCueJa = sample.visualCueJa,
                    associationPrompt = string.IsNullOrWhiteSpace(sample.associationPrompt) ? sample.imagePrompt : sample.associationPrompt,
                    associationPromptJa = string.IsNullOrWhiteSpace(sample.associationPromptJa)
                        ? (string.IsNullOrWhiteSpace(sample.associationPrompt) ? sample.imagePrompt : sample.associationPrompt)
                        : sample.associationPromptJa,
                    mnemonic = sample.mnemonic,
                    mnemonicJa = sample.mnemonicJa,
                    imagePrompt = sample.imagePrompt,
                    imagePromptJa = sample.imagePromptJa,
                    imagePromptCandidates = sample.imagePromptCandidates == null ? new List<string>() : new List<string>(sample.imagePromptCandidates),
                    objectShape = string.IsNullOrWhiteSpace(sample.objectShape) ? Shapes[i % Shapes.Length] : sample.objectShape,
                    colorHex = string.IsNullOrWhiteSpace(sample.colorHex) ? Colors[i % Colors.Length] : sample.colorHex
                });
            }

            return merged;
        }
    }
}

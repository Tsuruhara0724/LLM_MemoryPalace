using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MemPalaceLLM
{
    public static class MnemonicCueFrameRag
    {
        private const int MaxCasesPerWord = 3;
        private static CueFrameRagLibrary cachedLibrary;

        [Serializable]
        private sealed class CueFrameRagLibrary
        {
            public CueFrameRagCase[] cases;
        }

        [Serializable]
        private sealed class CueFrameRagCase
        {
            public string id;
            public string title;
            public int priority;
            public string[] meaning_keywords;
            public string[] word_keywords;
            public string[] anchor_keywords;
            public string[] good_patterns;
            public string[] bad_patterns;
            public string[] image_prompt_hints;
            public string[] visual_objects;
        }

        private sealed class ScoredCase
        {
            public CueFrameRagCase Item;
            public int Score;
        }

        public static string BuildBatchGuidance(List<WordEntry> words, int globalOffset, int totalWords)
        {
            if (words == null || words.Count == 0)
            {
                return BuildFailurePatternTable();
            }

            var builder = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                var globalIndex = globalOffset + i;
                var anchor = RoomSpecCatalog.GetAssignmentAnchor(globalIndex, totalWords);
                AppendGuidanceForWord(builder, words[i], globalIndex + 1, anchor.id, anchor.label);
            }

            if (builder.Length == 0)
            {
                return BuildFailurePatternTable();
            }

            return BuildFailurePatternTable() +
                "RETRIEVED CUE FRAME GUIDANCE (RAG):\n" +
                "Use these retrieved patterns as guidance and constraints. Do not copy examples verbatim unless they are exactly appropriate. Prefer meaning accuracy, anchor fit, and image stability over novelty.\n" +
                builder +
                "\n";
        }

        public static string BuildSingleGuidance(WordEntry word, int wordNumber, string anchorId, string anchorLabel)
        {
            var builder = new StringBuilder();
            AppendGuidanceForWord(builder, word, wordNumber, anchorId, anchorLabel);
            if (builder.Length == 0)
            {
                return BuildFailurePatternTable();
            }

            return BuildFailurePatternTable() +
                "RETRIEVED CUE FRAME GUIDANCE (RAG):\n" +
                "Use these retrieved patterns to avoid repeating known weak cue frames. Do not copy examples verbatim unless they fit the target word and anchor.\n" +
                builder +
                "\n";
        }

        private static string BuildFailurePatternTable()
        {
            return
                "FAILURE PATTERN TABLE:\n" +
                "Avoid these known weak mnemonic patterns and apply the listed fixes.\n" +
                "P1 Tautology:\n" +
                "Bad: \"The pillow reminds you of pillow.\"\n" +
                "Fix: Use a distinctive visible action or state, such as a pillow cushioning a door.\n" +
                "P2 Split-link:\n" +
                "Bad: The image shows a map, but the mnemonic says bar + rio without bar or river in the scene.\n" +
                "Fix: Add the hook objects to the visible scene and visual_objects, or remove the hook.\n" +
                "P3 Broad meaning drift:\n" +
                "Bad: suitcase + passport for airport, because it only suggests travel.\n" +
                "Fix: Use meaning-specific cues such as boarding pass, security tray, gate waiting, airline tag, or another cue that clearly suggests the target meaning.\n" +
                "P4 Meta explanation:\n" +
                "Bad: \"The visible cue retrieves the meaning first.\"\n" +
                "Fix: Write a natural learner-facing micro-story that connects the Spanish word form to the target meaning.\n" +
                "P5 Weak foreground:\n" +
                "Bad: a small object rests beside the anchor.\n" +
                "Fix: Use a novel but physically possible cue-anchor relation, such as wedged under, hanging from, clipped to, spilling from, wrapped around, cushioning, contained by, or balanced in a visible support.\n" +
                "P6 Unsafe association:\n" +
                "Bad: a poster of a drug trafficker to cue cartel/poster.\n" +
                "Fix: Use a neutral poster, tape, clip, frame, or notice-board display; never use drugs, crime, gambling, sexual content, weapons, horror, or gore.\n" +
                "P7 Hidden or swallowed cue:\n" +
                "Bad: a wallet tucked inside a chair pocket, so the cue disappears into the furniture.\n" +
                "Fix: Keep small cue objects fully exposed, visually separate from the anchor, and impossible to miss.\n" +
                "P8 Target-object display:\n" +
                "Bad: an airport model sits on a cabinet shelf, or a hallway model is displayed on an air conditioner.\n" +
                "Fix: Create a target-meaning event using ordinary objects in action, such as a security tray for airport or objects forming a narrow passage for hallway.\n" +
                "P9 Dead cue story:\n" +
                "Bad: \"The visible cue retrieves airport; repeat aeropuerto while mentally replaying the same scene.\"\n" +
                "Fix: Use a tiny learner-friendly memory scene, such as a natural sound hook or naming the existing cue object with the Spanish word.\n\n";
        }

        private static void AppendGuidanceForWord(StringBuilder builder, WordEntry word, int wordNumber, string anchorId, string anchorLabel)
        {
            if (builder == null || word == null)
            {
                return;
            }

            var matches = RetrieveCases(word, anchorId, anchorLabel);
            if (matches.Count == 0)
            {
                return;
            }

            builder.Append("- word ").Append(wordNumber)
                .Append(" ").Append(Safe(word.word))
                .Append(" @ ").Append(Safe(anchorLabel))
                .AppendLine(":");

            var affordance = BuildAnchorAffordance(anchorLabel);
            if (!string.IsNullOrWhiteSpace(affordance))
            {
                builder.Append("  Anchor affordances: ").Append(affordance).AppendLine();
            }

            var caseCount = Math.Min(MaxCasesPerWord, matches.Count);
            for (int i = 0; i < caseCount; i++)
            {
                var item = matches[i].Item;
                builder.Append("  Case: ").Append(Safe(item.title)).AppendLine();
                AppendArrayLine(builder, "    Good patterns", item.good_patterns, 2);
                AppendArrayLine(builder, "    Avoid", item.bad_patterns, 3);
                AppendArrayLine(builder, "    Image prompt hints", item.image_prompt_hints, 2);
                AppendArrayLine(builder, "    Visual objects", item.visual_objects, 4);
            }
        }

        private static List<ScoredCase> RetrieveCases(WordEntry word, string anchorId, string anchorLabel)
        {
            var library = LoadLibrary();
            var results = new List<ScoredCase>();
            if (library?.cases == null || word == null)
            {
                return results;
            }

            var wordText = (Safe(word.word) + " " + Safe(word.meaning) + " " + Safe(word.meaningJa)).ToLowerInvariant();
            var anchorText = (Safe(anchorId) + " " + Safe(anchorLabel)).ToLowerInvariant();

            for (int i = 0; i < library.cases.Length; i++)
            {
                var item = library.cases[i];
                var score = ScoreCase(item, wordText, anchorText);
                if (score > 0)
                {
                    results.Add(new ScoredCase { Item = item, Score = score });
                }
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return results;
        }

        private static int ScoreCase(CueFrameRagCase item, string wordText, string anchorText)
        {
            if (item == null)
            {
                return 0;
            }

            var score = Math.Max(0, item.priority);
            var matchedMeaning = CountMatches(item.meaning_keywords, wordText);
            var matchedWord = CountMatches(item.word_keywords, wordText);
            var matchedAnchor = CountMatches(item.anchor_keywords, anchorText);

            if (matchedMeaning == 0 && matchedWord == 0)
            {
                return 0;
            }

            score += matchedMeaning * 25;
            score += matchedWord * 35;
            score += matchedAnchor * 8;
            return score;
        }

        private static CueFrameRagLibrary LoadLibrary()
        {
            if (cachedLibrary != null)
            {
                return cachedLibrary;
            }

            var asset = Resources.Load<TextAsset>("MnemonicCueFrameRag");
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                cachedLibrary = new CueFrameRagLibrary { cases = Array.Empty<CueFrameRagCase>() };
                return cachedLibrary;
            }

            try
            {
                cachedLibrary = JsonUtility.FromJson<CueFrameRagLibrary>(asset.text)
                                ?? new CueFrameRagLibrary { cases = Array.Empty<CueFrameRagCase>() };
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to parse MnemonicCueFrameRag.json: " + ex.Message);
                cachedLibrary = new CueFrameRagLibrary { cases = Array.Empty<CueFrameRagCase>() };
            }

            if (cachedLibrary.cases == null)
            {
                cachedLibrary.cases = Array.Empty<CueFrameRagCase>();
            }

            return cachedLibrary;
        }

        private static int CountMatches(string[] keywords, string text)
        {
            if (keywords == null || string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var count = 0;
            for (int i = 0; i < keywords.Length; i++)
            {
                var keyword = keywords[i];
                if (!string.IsNullOrWhiteSpace(keyword)
                    && text.IndexOf(keyword.Trim().ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AppendArrayLine(StringBuilder builder, string label, string[] values, int maxValues)
        {
            if (builder == null || values == null || values.Length == 0)
            {
                return;
            }

            var appended = 0;
            builder.Append(label).Append(": ");
            for (int i = 0; i < values.Length && appended < maxValues; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                {
                    continue;
                }

                if (appended > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(values[i].Trim());
                appended++;
            }

            builder.AppendLine();
        }

        private static string BuildAnchorAffordance(string anchorLabel)
        {
            var anchor = Safe(anchorLabel).ToLowerInvariant();
            if (anchor.Contains("chair"))
            {
                return "seat, backrest, chair leg, under the chair; keep the chair recognizable.";
            }

            if (anchor.Contains("door"))
            {
                return "door frame, handle, floor directly in front of door, door surface; avoid replacing the door with the cue.";
            }

            if (anchor.Contains("desk"))
            {
                return "desktop surface, front edge, drawer handle, under desk.";
            }

            if (anchor.Contains("table"))
            {
                return "tabletop surface, table edge, table leg, under table.";
            }

            if (anchor.Contains("wardrobe"))
            {
                return "wardrobe door surface, handle, shelf, floor beside wardrobe.";
            }

            if (anchor.Contains("shelf") || anchor.Contains("bookshelf"))
            {
                return "shelf surface, shelf edge, between books, side panel.";
            }

            if (anchor.Contains("air conditioner"))
            {
                return "front panel, vent, wall directly below it, floor below it; keep the AC visible.";
            }

            if (anchor.Contains("window"))
            {
                return "window frame, sill, glass surface, curtain edge.";
            }

            return "surface, edge, handle, leg, or floor directly beside the anchor.";
        }

        private static string Safe(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }
    }
}

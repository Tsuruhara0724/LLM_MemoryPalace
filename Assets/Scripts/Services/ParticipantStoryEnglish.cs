using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MemPalaceLLM
{
    // Offline lexical matching, not semantic/POS disambiguation. Sentence assignment
    // remains participant-confirmed. Unknown forms fail closed instead of stemming.
    public static class ParticipantStoryEnglish
    {
        public const string StudyTextVersion = "english-span-es-v1";
        private static readonly Dictionary<string, Regex> Matchers = new(StringComparer.OrdinalIgnoreCase);
        private static string inflections;

        public static bool TryFindTarget(string text, string englishMeaning, out int start, out int length)
        {
            start = length = 0;
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(englishMeaning)) return false;
            var target = englishMeaning.Trim();
            if (target.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) target = target.Substring(3).Trim();
            if (target.Length == 0) return false;
            if (!Matchers.TryGetValue(target, out var matcher))
            {
                var forms = GetForms(target);
                forms.Sort((a, b) => b.Length.CompareTo(a.Length));
                var escaped = forms.ConvertAll(Regex.Escape);
                matcher = new Regex(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", escaped) + @")(?![\p{L}\p{N}_])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (Matchers.Count >= 256) Matchers.Clear();
                Matchers[target] = matcher;
            }
            foreach (Match match in matcher.Matches(text))
            {
                var end = match.Index + match.Length;
                // Do not match pieces of contractions, e.g. can in can't.
                // Preserve noun possessive 's with the visible token when adding a label.
                if (match.Index >= 2 && IsApostrophe(text[match.Index - 1]) && char.IsLetter(text[match.Index - 2])) continue;
                if (end + 1 < text.Length && IsApostrophe(text[end]) && char.IsLetter(text[end + 1]))
                {
                    if (char.ToLowerInvariant(text[end + 1]) != 's' ||
                        (end + 2 < text.Length && char.IsLetterOrDigit(text[end + 2]))) continue;
                    end += 2;
                }
                start = match.Index;
                length = end - start;
                return true;
            }
            return false;
        }

        public static string AnnotateFirst(string text, WordEntry word)
        {
            if (string.IsNullOrWhiteSpace(word?.word) ||
                !TryFindTarget(text, word.meaning, out var start, out var length)) return text ?? string.Empty;
            var spanish = word.word.Trim();
            var end = start + length;
            var suffix = text.Substring(end).TrimStart();
            // Finalization/preview/package copies must not duplicate an existing label.
            if (suffix.StartsWith("(", StringComparison.Ordinal))
            {
                var close = suffix.IndexOf(')');
                if (close > 0 && string.Equals(suffix.Substring(1, close - 1).Trim(), spanish, StringComparison.OrdinalIgnoreCase)) return text;
            }
            return text.Insert(end, " (" + spanish + ")");
        }

        public static List<string> AnnotateAssignedSentences(IReadOnlyList<string> sentences,
            IReadOnlyList<int> assignments, IReadOnlyList<WordEntry> words)
        {
            var result = new List<string>(sentences.Count);
            var annotated = new HashSet<int>();
            for (var i = 0; i < sentences.Count; i++)
            {
                var sentence = sentences[i];
                var wordIndex = i < assignments.Count ? assignments[i] : -1;
                if (wordIndex >= 0 && wordIndex < words.Count && !annotated.Contains(wordIndex) &&
                    TryFindTarget(sentence, words[wordIndex]?.meaning, out _, out _))
                {
                    sentence = AnnotateFirst(sentence, words[wordIndex]);
                    annotated.Add(wordIndex);
                }
                result.Add(sentence);
            }
            return result;
        }

        public static string JoinAssignedSentences(IReadOnlyList<string> sentences, IReadOnlyList<int> assignments, int wordIndex)
        {
            var result = new StringBuilder();
            for (var i = 0; i < sentences.Count && i < assignments.Count; i++)
            {
                if (assignments[i] != wordIndex) continue;
                if (result.Length > 0) result.Append(' ');
                result.Append(sentences[i].Trim());
            }
            return result.ToString().Trim();
        }

        public static StorySessionData BuildStudyStory(string original, IReadOnlyList<string> sentences,
            IReadOnlyList<int> assignments, IReadOnlyList<WordEntry> words, IReadOnlyList<int> wordOrder)
        {
            var annotated = AnnotateAssignedSentences(sentences, assignments, words);
            var story = new StorySessionData
            {
                fullStory = string.Join(" ", annotated).Trim(),
                originalParticipantStory = original,
                storyTextFormatVersion = StudyTextVersion,
                storySource = "participant_written_story",
                storyProvider = "Participant",
                storyModel = "none",
                generatedAtUtc = DateTime.UtcNow.ToString("o")
            };
            for (var i = 0; i < wordOrder.Count; i++)
            {
                var wordIndex = wordOrder[i];
                var word = words[wordIndex];
                story.orderedItems.Add(new WordImageItemData
                {
                    word = word.word,
                    meaning = word.meaning,
                    storyOrder = i + 1,
                    storySegment = JoinAssignedSentences(annotated, assignments, wordIndex),
                    imageResourcePath = WordImageCatalog.BuildResourcePath(word.word)
                });
            }
            return story;
        }

        private static List<string> GetForms(string target)
        {
            var forms = new List<string> { target };
            // Phrase glosses retain exact matching; no speculative phrase rewriting.
            if (!Regex.IsMatch(target, "^[A-Za-z]{2,}$")) return forms;
            if (inflections == null)
            {
                var data = Resources.Load<TextAsset>("EnglishInflections");
                if (data == null)
                {
                    Debug.LogWarning("English inflection resource missing; participant stories use exact English matching only.");
                    inflections = string.Empty;
                }
                else
                {
                    try
                    {
                        using var input = new MemoryStream(data.bytes);
                        using var gzip = new GZipStream(input, CompressionMode.Decompress);
                        using var reader = new StreamReader(gzip, Encoding.UTF8);
                        inflections = reader.ReadToEnd();
                    }
                    catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
                    {
                        Debug.LogWarning("English inflection resource unreadable; participant stories use exact English matching only.");
                        inflections = string.Empty;
                    }
                }
            }
            var prefix = "\n" + target.ToLowerInvariant() + "\t";
            var lineStart = inflections.IndexOf(prefix, StringComparison.Ordinal);
            if (lineStart < 0) return forms;
            lineStart += prefix.Length;
            var lineEnd = inflections.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = inflections.Length;
            forms.AddRange(inflections.Substring(lineStart, lineEnd - lineStart).Split(','));
            return forms;
        }

        private static bool IsApostrophe(char value) => value == '\'' || value == '\u2019';
    }
}

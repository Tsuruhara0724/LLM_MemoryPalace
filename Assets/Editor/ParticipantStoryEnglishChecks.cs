using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MemPalaceLLM.Editor
{
    public static class ParticipantStoryEnglishChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Tools/Memory Palace/Check Participant Story English")]
        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run these isolated checks outside Play Mode.");
            checks = 0;
            Expect(Resources.Load<TextAsset>("EnglishInflections") != null, "Offline inflection data is bundled.");
            Expect(Resources.Load<TextAsset>("EnglishInflectionsNotice") != null, "Upstream copyright notices are bundled.");
            Forms("walk", "walk walks walked walking");
            Forms("carry", "carry carries carried carrying");
            Forms("try", "try tries tried trying");
            Forms("stop", "stop stops stopped stopping");
            Forms("hug", "hug hugs hugged hugging");
            Forms("glue", "glue glues glued gluing glueing");
            Forms("forget", "forget forgets forgot forgotten forgetting");
            Forms("run", "run runs ran running");
            Forms("write", "write writes wrote written writing");
            Forms("take", "take takes took taken taking");
            Forms("bird", "bird birds");
            Forms("box", "box boxes");
            Forms("baby", "baby babies");
            Forms("child", "child children");
            Forms("person", "person people persons");
            Forms("foot", "foot feet");
            Forms("mouse", "mouse mice");
            Forms("good", "good better best");
            Forms("bad", "bad worse worst");
            Forms("happy", "happy happier happiest");
            Forms("big", "big bigger biggest");
            foreach (var pair in new[] { ("noise", "noisy"), ("hunger", "hungry"), ("help", "helpful"),
                ("forget", "forgetful"), ("forget", "unforgotten"), ("art", "cart"), ("bird", "blackbird"),
                ("run", "runway"), ("forget", "olvidar"), ("can", "can't"), ("help", "helper") })
                Expect(!ParticipantStoryEnglish.TryFindTarget(pair.Item2, pair.Item1, out _, out _), "No broad stemming/substring/Spanish match: " + pair);
            Expect(ParticipantStoryEnglish.TryFindTarget("I forgot it.", "to forget", out _, out _), "Infinitive gloss prefixes are supported.");
            Expect(ParticipantStoryEnglish.TryFindTarget("Look after the bag.", "look after", out _, out _), "Exact multiword meanings still work.");
            Expect(!ParticipantStoryEnglish.TryFindTarget("Looked after the bag.", "look after", out _, out _), "Uncertain phrase inflections are not guessed.");
            Expect(!ParticipantStoryEnglish.TryFindTarget(null, "forget", out _, out _), "Empty input is safe.");

            var forget = new WordEntry { word = "olvidar", meaning = "forget" };
            var bird = new WordEntry { word = "pájaro", meaning = "bird" };
            const string repeated = "I FORGOT the date, then forgot the address.";
            var annotated = ParticipantStoryEnglish.AnnotateFirst(repeated, forget);
            Expect(annotated == "I FORGOT (olvidar) the date, then forgot the address.", "Annotate the first actual surface form, preserving case and other occurrences.");
            Expect(ParticipantStoryEnglish.AnnotateFirst(annotated, forget) == annotated, "Annotation is idempotent.");
            Expect(ParticipantStoryEnglish.AnnotateFirst("I forgot (OLVIDAR) it.", forget) == "I forgot (OLVIDAR) it.", "Existing case-insensitive Spanish labels are not duplicated.");
            Expect(ParticipantStoryEnglish.AnnotateFirst("He said 'forgot'.", forget) == "He said 'forgot (olvidar)'.", "Quoted English words remain matchable.");
            Expect(ParticipantStoryEnglish.AnnotateFirst("A child's toy.", new WordEntry { word = "niño", meaning = "child" }) == "A child's (niño) toy.", "A possessive suffix stays with the matched surface form.");
            CheckControllerAndPackage(forget, bird);
            Debug.Log("PARTICIPANT STORY ENGLISH CHECK PASSED: " + checks + "; offline EditMode only, no network/TTS/XR calls.");
        }

        private static void CheckControllerAndPackage(WordEntry forget, WordEntry bird)
        {
            var root = new GameObject("Participant story check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var controller = root.AddComponent<MemoryPalaceExperimentController>();
                // Target-list order deliberately differs from narrative order.
                var words = new List<WordEntry> { bird, forget };
                const string original = "  I FORGOT the date.\n  Birds crossed the sky.\nI forgot the address.  ";
                Set(controller, "activeWordSet", new WordSetDefinition { words = words });
                Set(controller, "participantFullStoryText", original);
                Call(controller, "RefreshParticipantStorySentencesFromFullText");
                var sentences = (List<string>)Get(controller, "participantStorySentences");
                var assignments = (List<int>)Get(controller, "participantStorySentenceWordIndexes");
                Expect(assignments.Count == 3 && assignments[0] == 1 && assignments[1] == 0 && assignments[2] == 1, "Split infers regular plurals and irregular past forms.");
                Expect((int)Call(controller, "InferParticipantStoryWordIndex", "Birds forgot it.") == -1, "Multiple matching targets remain unassigned for user confirmation.");
                Expect((int)Call(controller, "InferParticipantStoryWordIndex", "olvidar pájaro") == -1, "Spanish input is not a new input/recognition mode.");
                var arguments = new object[] { null, null, null };
                Expect((bool)Call(controller, "TryBuildParticipantStorySegments", arguments), "Recognized inflections satisfy the final story validation.");
                var order = (List<int>)arguments[0];
                Expect(order.Count == 2 && order[0] == 1 && order[1] == 0, "Route order remains narrative order, not target-list order.");
                var story = ParticipantStoryEnglish.BuildStudyStory(original, sentences, assignments, words, order);
                Expect(story.fullStory == "I FORGOT (olvidar) the date. Birds (pájaro) crossed the sky. I forgot the address.", "Learning text preserves sentence order and labels each target only once.");
                Expect(story.originalParticipantStory == original && (string)Get(controller, "participantFullStoryText") == original, "Exact raw input is preserved independently of study text.");
                Expect(sentences[0] == "I FORGOT the date." && sentences[1] == "Birds crossed the sky.", "Split English sentences are never rewritten in place.");
                Expect(story.orderedItems[0].storySegment == "I FORGOT (olvidar) the date. I forgot the address.", "Grouped route segments share the annotated study sentences without duplicate labels.");
                Expect(story.orderedItems[0].word == "olvidar" && story.orderedItems[1].word == "pájaro", "Spanish target identities are preserved.");
                var restored = JsonUtility.FromJson<StorySessionData>(JsonUtility.ToJson(story));
                Expect(restored.fullStory == story.fullStory && restored.originalParticipantStory == original && restored.storyTextFormatVersion == ParticipantStoryEnglish.StudyTextVersion, "Export JSON preserves original, study text and format version.");
                var legacy = JsonUtility.FromJson<StorySessionData>("{\"fullStory\":\"Legacy English story.\",\"storySource\":\"participant_written_story\"}");
                Expect(legacy.fullStory == "Legacy English story." && string.IsNullOrEmpty(legacy.originalParticipantStory), "Old story JSON remains readable.");
                Set(controller, "currentStory", story);
                var items = story.orderedItems.ConvertAll(item => new MnemonicItemData { word = item.word, meaning = item.meaning, mnemonic = item.storySegment });
                var package = (StorySessionData)Call(controller, "BuildVRSessionPackageStory", items);
                Expect(package.fullStory == story.fullStory && package.originalParticipantStory == original && package.storyTextFormatVersion == story.storyTextFormatVersion, "VR package preserves original input and annotated learning text.");
                Expect(package.orderedItems[0].storySegment == items[0].mnemonic, "VR package segments use the annotated mnemonic text.");
                Expect((string)Call(controller, "ResolveVoiceStorySegment", items[0]) == items[0].mnemonic, "Narration/subtitle resolution receives the same annotated story segment.");
                var unassigned = ParticipantStoryEnglish.AnnotateAssignedSentences(sentences, new List<int> { -1, -1, -1 }, words);
                Expect(string.Join(" ", unassigned) == string.Join(" ", sentences), "Unassigned sentences are not automatically tagged.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Forms(string lemma, string forms)
        {
            foreach (var form in forms.Split(' '))
            {
                var text = "Here: " + form + ".";
                Expect(ParticipantStoryEnglish.TryFindTarget(text, lemma, out var start, out var length) &&
                    text.Substring(start, length) == form, "Known English inflection: " + lemma + " -> " + form);
            }
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, PrivateInstance).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        private static object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}

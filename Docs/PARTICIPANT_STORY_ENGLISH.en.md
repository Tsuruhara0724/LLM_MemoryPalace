# Participant Stories: English Inflections and Spanish Annotations

[中文](PARTICIPANT_STORY_ENGLISH.md) | English


Participants write English only. They do not need to enter Spanish or force English words into their base forms. LLM story prompts are unchanged by this feature.

## Workflow

1. Write a complete English story and select **Split Story Into Sentences**.
2. The system infers targets from the English lemma or known inflections. Sentences matching multiple targets remain Unassigned for participant review. Each sentence still needs an assignment, and every target needs a matching English sentence.
3. The lower preview shows learning text, for example `I forgot (olvidar) the key.` The input box and original split sentences remain unchanged.
4. Confirm the story, then continue to furniture assignment and VR Study. Learning text, route segments, narration / subtitle parsing, and new session packages use the same annotated text.

Each target is annotated once at its first actual match in an assigned sentence. English forms such as `forgot` and `children`, capitalization, sentence content, and sentence order are retained. Repeated preview or finalization must not stack annotations. Re-Split or reassignment rebuilds the preview from the original English, not from already annotated text.

## Recognition and limits

- Supports regular and irregular verb forms in the offline table, including `walked/walking`, `carried`, `forgot/forgotten/forgetting`, and `ran/running`.
- Supports listed regular / irregular plurals and comparative / superlative forms, such as `birds`, `children`, `people`, and `better/best`.
- Matches word boundaries: `art` does not match inside `cart`. It does not guess through fuzzy spelling or stem truncation. `hungry` is not treated as `hunger`, nor `helpful` as `help`.
- This is not full semantic analysis. Homographs and part-of-speech ambiguity need manual review; the system does not determine whether the story uses the correct meaning.
- Unlisted forms retain exact-lemma matching. Rare variants are not guessed. Multiword meanings such as `look after` currently use exact phrase matching. The infinitive marker `to` in `to forget` may be omitted.
- Spanish input is not an additional recognition channel. `olvidar` is not accepted as the English `forget`.

## Text, exports, and compatibility

`StorySessionData.originalParticipantStory` preserves the original input, including whitespace and line breaks. `fullStory` is the learning version in original sentence order, with whitespace normalized by the existing Split flow. `orderedItems[*].storySegment` and `MnemonicItemData.mnemonic` contain learning route segments. Existing target-grouped route rules are unchanged.

`storyTextFormatVersion` is `english-span-es-v1`. These two additional fields are retained in VR packages and experiment JSON. Older data without them still loads. Old packages are not silently rewritten; reconfirm the story and export again to use annotations. Quest requires an APK containing the updated code and Resource data. No headset test was performed for this feature.

## Offline data and rebuilding

The table uses the [AGID format and notices](https://wordlist.aspell.net/agid-readme/) and a pinned [source snapshot](https://raw.githubusercontent.com/chadsowald/agid-irony-parser/6814807058dee1b9694dac547f768cd7428474c2/AgidIronyParser.Tests/infl.txt). Conversion retains lowercase entries with recognized parts of speech, excludes question-marked forms and rare / speculative forms at level >= 2, merges by lemma, and compresses a UTF-8 table. No third-party parser code was copied.

- `Assets/Resources/EnglishInflections.bytes`: a derived table of 39,738 lemmas, compressed to 276,879 bytes. It decompresses on first use and caches matchers only for meanings actually used.
- `Assets/Resources/EnglishInflectionsNotice.txt`: full upstream notices and snapshot provenance.
- `Tools/Build-EnglishInflections.ps1`: mechanical conversion with source SHA256 verification.

To rebuild, save the pinned snapshot as `.utmp/agid-source/infl.txt` and the official notice page as `.utmp/agid-source/readme.html`, then run `./Tools/Build-EnglishInflections.ps1` from the project directory. Runtime matching needs no Python, additional server, or network connection.

## Checks

Outside Play Mode, run **Tools > Memory Palace > Check Participant Story English**. It checks regular / irregular forms, false-positive protection, first-occurrence annotation, original-text preservation, automatic / ambiguous classification, route order, JSON round trips, older formats, VR packages, and narration parsing. It makes no network requests, plays no audio, and initializes no XR. Engineering checks do not establish learning effectiveness.

The isolated Unity fixture passed **109 checks**, and runtime compilation passed. The original feature log is in the ignored `.utmp/participant-story-english-final.log`. Review of the existing Azure mixed-language SSML wrapper confirmed that it matches Spanish target tokens independently of annotation direction, so no service change was needed for `English (Spanish)`. No live speech request or Quest hardware test was performed.

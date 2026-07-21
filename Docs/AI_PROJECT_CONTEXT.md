# AI Project Context - LLM Memory Palace

Last verified: 2026-07-20

## 1. Current development state

- Active branch: `codex/story-only-memory-palace`
- Current commit before this feature batch: `c82c307` (`Add unlimited local TTS support`)
- Unity version: `6000.3.12f1`
- Current design label: `LLM Story + User Furniture Assignment vs Participant Story + User Furniture Assignment vs Furnished Room Baseline`
- The redesign is active through `IsStoryOnlyRedesignEnabled() => true` in both the controller and LLM service.
- The active branch is synchronized with its remote but has not been merged into `main`.

Maintenance rule: update this document whenever story schema, word-image handling, experiment stages, study duration, room format, assessment flow, export fields, or default models change.

## 2. Research design

This is a Unity Desktop/VR experiment for Spanish vocabulary learning in a memory palace.

Current intended participant protocol has three conditions:

1. Select a room and a set of Spanish target words.
2. `LLM Story + User Furniture Assignment`: after room creation, start LLM generation of one continuous story in the background while the participant enters the PC room and assigns each word to furniture from the 2 x 4 card. HMD Study unlocks only after both the story and all assignments are ready.
3. `Participant Story + User Furniture Assignment`: no LLM call. The participant writes one complete continuous story first, splits it into sentences, classifies each sentence to a target word, then uses the same PC furniture assignment. Study retains TTS, subtitles, replay, and segmented route progress.
4. `Furnished Room Baseline`: after room creation, enter the same furnished HMD room directly with no story, target words, word pictures, narration, or furniture-word assignment during Study.
5. Let the participant enter the room and study independently for 20 minutes.
6. After Study in either story condition, optionally reveal every assigned furniture word-picture UI simultaneously in the same room. The participant controls entry and finish; there is no countdown.
7. Run the finalized post-study assessment and export research data.

The former `Self-Chosen Pictures` condition has been replaced by the participant-written-story condition plus a furnished-room baseline. Both story conditions use participant-defined spatial mapping.

The 20-minute duration is a protocol requirement. The runtime currently displays elapsed study time but does not enforce a hard 20-minute minimum or automatic transition. Until that is implemented, the researcher must control timing externally.

## 3. Current runtime flow

The active UI flow is:

```text
Setup
-> optional Room Builder
-> LLM background Story + PC Furniture Assignment
OR
-> Participant Story Authoring -> PC Furniture Assignment
-> Story Study Room
OR
-> Furnished Baseline Study
-> optional All-Picture Display in the Study Room
-> Final Test A/B as applicable
-> Questionnaire
-> Result / JSON + CSV export
```

The Mid Test has been removed. Story conditions run a spatial-anchor final block followed by a word meaning/image block. The furnished-room baseline skips the spatial-anchor block and runs the same Spanish-word-to-English-meaning final block before the questionnaire.

## 4. Core files

```text
Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs
Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
Assets/Scripts/Services/OllamaLlmService.cs
Assets/Scripts/Services/WordImageCatalog.cs
Assets/Scripts/Services/ElevenLabsTextToSpeechService.cs
Assets/Scripts/Data/ExperimentModels.cs
Assets/Scripts/Data/RoomSpecModels.cs
Assets/Resources/MemPalaceDemoData.json
Assets/Resources/MemPalaceRoomSpec.json
Assets/Resources/ExampleRooms/example_room.json
Assets/Resources/WordImages/
```

Responsibilities:

- `MemoryPalaceBootstrap`: creates the experiment controller after scene load.
- `MemoryPalaceExperimentController`: Setup UI, room builder, background/participant story flow, PC furniture-word assignment, study room, Desktop/VR interaction, snapshots, recognition tests, questionnaire, and export.
- `OllamaLlmService.GenerateStory`: runs the causal-plan and final-story passes through local Ollama.
- `OllamaLlmService.GenerateGeminiStory`: runs the same plan, repair, parsing, and validation pipeline through the Gemini API.
- `WordImageCatalog`: loads `Resources/WordImages/{word}` and falls back to `_placeholder`.
- `ElevenLabsTextToSpeechService`: prefers the local OpenAI-compatible Chatterbox/Kokoro WAV endpoint, then falls back to ElevenLabs `eleven_multilingual_v2` and official Gemini Flash Preview TTS. It decodes local PCM WAV, ElevenLabs MP3, or Gemini 24 kHz mono PCM into a Unity `AudioClip` while preserving route callbacks and replay controls.
- `ExperimentModels`: story, word-image, response, questionnaire, and export models.
- `RoomSpecModels`: room shell, furniture anchors, resource loading, and fallback room.

Known engineering debt: `MemoryPalaceExperimentController.cs` is still a very large mixed-responsibility class, and much of the old mnemonic/Stable Diffusion/catalog pipeline remains in source behind the story-only switch.

## 5. Story generation

Entry point:

```text
OllamaLlmService.GenerateStory
```

Local default connection:

- Endpoint: `http://localhost:11434/api/generate`
- Story model: `gemma3:12b`
- Request format: Ollama JSON mode, non-streaming
- Temperature: `0.7`
- Token budget: `1800`

Online option:

- Provider: Gemini API
- Default model: `gemini-2.5-flash`
- Key lookup on Windows desktop: user-level `GEMINI_API_KEY`, then `GOOGLE_API_KEY`, then locally saved Setup value
- The same two-pass causal plan and final-story validation are used for both providers.

The model returns:

```json
{
  "fullStory": "one continuous English story",
  "items": [
    { "word": "estrella", "storyOrder": 1 }
  ]
}
```

Rules and guards:

- One concrete premise, consistent setting, and satisfying ending.
- Not a room tour, packing list, shopping list, scavenger hunt, or row of isolated object scenes.
- Every selected word must appear as exact `English meaning (Spanish word)` text.
- The LLM may choose the most natural narrative order.
- The active story generation path no longer receives automatic furniture anchors; assigned furniture and anchor names must not leak into the story.
- Repeated mentions do not create extra route items.
- Malformed JSON can be recovered from `fullStory`.
- Missing target annotations are repaired when possible.
- Story order can be recovered from first occurrence in `fullStory`.
- Fragmented-object-scene heuristics can reject low-coherence output.

Minor plan-link paraphrases are canonicalized rather than rejected. If the first final story fails parsing/quality checks or omits a target annotation, the same provider receives one constrained rewrite request. A remaining failure returns to Setup with an explicit error; the runtime does not open an empty Preview or append isolated dream-like repair sentences.

Legacy per-word mnemonic generation, cue-blueprint generation, Stable Diffusion cue generation, and A-D image reranking remain disabled. Gemini is active only as an online provider for the continuous-story pipeline.

### Local unlimited TTS

- Primary backend: the stable PyPI Chatterbox Multilingual release on CUDA, exposed locally as `POST /v1/audio/speech`.
- Low-resource alternative: Kokoro-82M through the same endpoint contract.
- Default Unity base URL: `http://127.0.0.1:8880/v1`; override with `LOCAL_TTS_ENDPOINT`, `LOCAL_TTS_MODEL`, and `LOCAL_TTS_VOICE`.
- Setup/start scripts live under `Tools/LocalTtsServer/`. Environments, model downloads, and generated WAV caches live under ignored `.local-tts/`.
- The local server caches identical requests. Unity tries the local provider first and retains ElevenLabs/Gemini as automatic fallbacks.
- A missing non-serialized speech service is recreated automatically after Unity domain reload, so Play Mode script recompilation no longer leaves the Study route with `No system text-to-speech service`.
- For Quest package transfer, PC-written packages store `sourcePcHost` and rewrite loopback Local TTS endpoints to the PC LAN IP when possible. This lets an ADB-pushed package still call the PC local TTS server over Wi-Fi.

## 6. Word images

Runtime lookup:

```text
Resources.Load<Texture2D>("WordImages/{lower-case Spanish word}")
```

Current coverage as of 2026-07-02:

- 26/26 distinct words across the preset sets
- 12/12 words in `formal_12_pool`
- `_placeholder.png` remains the missing-file fallback

Most images are color PNGs from OpenMoji, licensed CC BY-SA 4.0. Six files (`alfombra`, `barco`, `biblioteca`, `camino`, `campana`, and `castillo`) were later replaced locally and currently need their source/license metadata restored. Exact status is tracked in `Assets/Resources/WordImages/ATTRIBUTION.md`.

Temporary semantic approximations that may deserve replacement after pilot review:

- `biblioteca`: stack of books
- `camino`: motorway/road symbol
- `mercado`: convenience-store symbol
- `vecino`: person raising a hand

Replacing a picture requires keeping the same Spanish filename and updating the attribution document.

Important behavior: `_placeholder.png` currently counts as a ready texture, so `AreAllCurrentImageCuesReady()` does not distinguish real assets from placeholders. Pre-run QA must visually confirm that no selected word uses the placeholder.

## 7. Word material

Resource: `Assets/Resources/MemPalaceDemoData.json`

Preset sets:

- `alpha`: 8 words
- `beta`: 8 words
- `formal_12_pool`: 12 words
- `advanced_pool`: currently duplicates the formal 12-word pool

Formal runs sample 8 distinct words from `formal_12_pool`.

Current formal pool:

```text
estrella, espejo, castillo, máscara, vela, tambor,
nube, campana, linterna, flor, corona, barco
```

## 8. Room system

- Default resource room: `Assets/Resources/MemPalaceRoomSpec.json`
- Example room: `Assets/Resources/ExampleRooms/example_room.json`
- The example L-shaped room has 14 fixed furniture anchors.
- Setup can load the default room, load the example room, or open the grid room builder.
- The legacy text-description room generator remains available under an advanced toggle.
- The story is generated without furniture names. Word-to-anchor assignment is spatial metadata applied after story construction.

## 9. Study and assessment

Study UI currently provides:

- Full continuous story.
- Per-word Spanish word, English meaning, local image, story beat, and anchor label.
- Desktop free movement and click inspection.
- Optional OpenXR/VR study runtime.
- Optional guided voice route: anchor instruction -> wait for the correct nearby image to be inspected -> play that story segment -> continue to the next anchor.
- Word-image markers are proximity-gated: only the current route marker is revealed at roughly 2.2 m and its detail UI remains available to roughly 2.6 m, with automatic look-to-inspect and foreground rendering to prevent room-geometry clipping.
- VR HMD users see the currently spoken guide or story segment as a subtitle in the world-space study panel; the existing replay control is unchanged.
- Story-segment subtitles wait for participant confirmation through the `>` advance button. Walking instructions still advance automatically after the participant reaches and looks toward the next anchor.
- The first spoken Study pass is sequential and cannot be skipped. After it completes, Desktop and VR expose one thin whole-route bar split into one segment per word/story section. Selecting a segment restarts its anchor guide from the beginning; arbitrary within-audio seeking is intentionally disabled. Replay Voice, Restart Route, and automatic advancement stay synchronized with the route bar.
- PC furniture selection before Study in both story conditions: click an actual furniture model, then choose from a 2 x 4 word card; one word per furniture and one furniture per word, with doors and windows excluded.
- An optional post-Study all-picture room display in both story conditions. It reveals every furniture marker at once, has no countdown, and can be entered/finished or skipped by the participant.
- The furnished-room baseline keeps furniture anchors visible but omits all word assignment, word pictures, story segments, subtitles, and voice route during Study.
- Elapsed study time.
- Snapshot capture for the final spatial-anchor recognition block in story conditions.

Intended study duration: 20 minutes of independent room exploration.

Current assessment code:

- There is no Mid Test.
- Story conditions unlock the final test after all current words have stored 3D scene snapshots.
- `Final Test A - Spatial Anchor` asks the participant to choose the correct stored 3D room-view snapshot from three images.
- `Final Test B - Word Meaning + Image` asks the participant to choose the fixed local word image and English meaning that match the target Spanish word.
- The furnished-room baseline skips the spatial-anchor block and runs the same Spanish-word-to-English-meaning final block before the questionnaire.
- Questionnaire contains NASA-TLX-style 0-20 scales and vividness/helpfulness/trust 1-7 scales.

## 10. Export

Output directory:

```text
ExperimentExports/
```

Formats:

- JSON session record
- CSV recall/recognition summary

The export includes participant/session identifiers, room metadata, provider/model metadata, condition labels, story workflow, story readiness, furniture-word assignment readiness, HMD-entry readiness, LLM story attempt/cancel/error status, participant story-authoring duration, furniture-assignment duration, Study duration, whether the optional all-picture display was entered, its duration, viewed/memorized counts, the complete `StorySessionData`, per-word story order and assigned furniture metadata, snapshot-test responses, questionnaire values, and interaction logs. The CSV recognition summary repeats the session-level condition/workflow/readiness fields on every row for easier analysis.

The current redesign still needs a fresh end-to-end exported test session before formal use.

## 11. Verification status

Verified on 2026-07-20:

- Static flow checks confirm condition 1 enters PC furniture assignment immediately after starting background LLM story generation.
- Static flow checks confirm condition 2 uses a participant-written whole story, sentence-to-word classification, then the same furniture assignment and HMD Study gate as condition 1.
- Static flow checks confirm condition 3 enters the furnished HMD room directly without story, words, word pictures, voice route, or furniture-word assignment.
- Static flow checks confirm the Mid Test is removed from the active UI flow.
- Static flow checks confirm Quest can load an ADB-pushed `session_package_latest.json` from app persistent files before optional HTTP fetching.
- Static flow checks confirm PC-written Quest packages include a source PC host and rewrite loopback Local TTS endpoints for HMD use.
- Static checks confirm the active story-generation calls no longer pass automatic anchor assignments into `GenerateStory` / `GenerateGeminiStory`.
- Static checks confirm PC and VR Study-entry controls use the same `CanEnterStudyAfterAssignment()` gate.
- The open Unity editor successfully rebuilt `Assembly-CSharp.dll` through Bee/Tundra after the redesign changes. The latest direct csc check shows build success with six pre-existing unused-field warnings and no C# errors.
- Unity batchmode still cannot run while this project is open in the editor, but the open editor's compile log confirms C# compilation for the current source state.
- A direct `gemini-2.5-flash` API request using the configured user-level key returned valid JSON. `gemini-3.5-flash` returned HTTP 503 in three consecutive attempts, so 2.5 Flash remains the verified default.
- All 26 word-image files are valid readable PNGs.
- All preset/formal word keys have matching image filenames.
- OpenXR Play Mode without a connected headset reports `XR_ERROR_FORM_FACTOR_UNAVAILABLE`; the new VR progress-bar and all-picture interactions compile but still require headset validation.
- Android/Quest package loading, Local TTS reachability, ElevenLabs audio decoding, and completion callbacks still require an on-device network/audio test with the intended Quest Pro.
- There are no automated Unity tests.
- No post-redesign `ExperimentExports` artifact was present at verification time.

## 12. Immediate priorities

1. Run complete Desktop sessions for all three conditions through export and inspect JSON/CSV.
2. Run all three protocols on the intended Quest Pro, including ADB package transfer, segmented route jumping after the first pass, subtitles, PC furniture assignment, the furnished-room baseline, and all-picture display where applicable.
3. Pilot-review the 26 downloaded word images and replace ambiguous ones while preserving filenames and attribution.
4. Decide whether the application should enforce the 20-minute study window or only display a timer.
5. Confirm the final assessment timing after the 20-minute room period.
6. Add lightweight tests for story JSON repair/order recovery and export shape.
7. Remove or isolate the disabled mnemonic/Stable Diffusion pipeline after the new design stabilizes.

## 13. Fast search commands

```powershell
rg -n "GenerateStory|BuildStoryPrompt|TryBuildStorySession" Assets/Scripts/Services/OllamaLlmService.cs
rg -n "DrawGenerationView|DrawStudyView|BeginSnapshotTest|BuildExportPayload" Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
rg -n "WordImageCatalog|WordImages" Assets/Scripts Assets/Resources Docs
rg -n "StorySessionData|WordImageItemData|ExperimentSessionExport" Assets/Scripts/Data/ExperimentModels.cs
```

# AI Project Context - LLM Memory Palace

Last verified: 2026-07-08

## 1. Current development state

- Active branch: `codex/story-only-memory-palace`
- Current commit before this feature batch: `356e616` (`Add guided VR study media and causal stories`)
- Unity version: `6000.3.12f1`
- Current design label: `LLM Story vs Self-Chosen Pictures + Local Word Images`
- The redesign is active through `IsStoryOnlyRedesignEnabled() => true` in both the controller and LLM service.
- The active branch is synchronized with its remote but has not been merged into `main`.

Maintenance rule: update this document whenever story schema, word-image handling, experiment stages, study duration, room format, assessment flow, export fields, or default models change.

## 2. Research design

This is a Unity Desktop/VR experiment for Spanish vocabulary learning in a memory palace.

Current intended participant protocol has two conditions:

1. Select a room and a set of Spanish target words.
2. `LLM Story`: generate one continuous English story with Ollama or Gemini and assign its fixed local word pictures to stable furniture anchors.
3. `Self-Chosen Pictures`: let the participant pair every fixed local word picture with a different selectable furniture marker before Study; no LLM story or generated narration is used.
4. Let the participant enter the room and study independently for 20 minutes.
5. After Study, optionally reveal every assigned furniture word-picture UI simultaneously in the same room. The participant controls entry and finish; there is no countdown.
6. Run the finalized post-study assessment and export research data.

The legacy `Self Generated` label has been replaced in the active UI by `Self-Chosen Pictures`. The participant chooses spatial word-picture pairings rather than generating new images.

The 20-minute duration is a protocol requirement. The runtime currently displays elapsed study time but does not enforce a hard 20-minute minimum or automatic transition. Until that is implemented, the researcher must control timing externally.

## 3. Current runtime flow

The active UI flow is:

```text
Setup
-> optional Room Builder
-> LLM Story Preview OR Self-Chosen Furniture Assignment
-> Study Room
-> existing Mid Image Test
-> optional All-Picture Display in the Study Room
-> existing Final Image Test
-> Questionnaire
-> Result / JSON + CSV export
```

The mid/final snapshot-recognition stages are inherited from the earlier prototype. Keep them available until the final dependent variables and post-20-minute assessment protocol are confirmed.

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
- `MemoryPalaceExperimentController`: Setup UI, room builder, story preview, study room, Desktop/VR interaction, snapshots, recognition tests, questionnaire, and export.
- `OllamaLlmService.GenerateStory`: runs the causal-plan and final-story passes through local Ollama.
- `OllamaLlmService.GenerateGeminiStory`: runs the same plan, repair, parsing, and validation pipeline through the Gemini API.
- `WordImageCatalog`: loads `Resources/WordImages/{word}` and falls back to `_placeholder`.
- `ElevenLabsTextToSpeechService`: calls ElevenLabs `eleven_multilingual_v2` first, then automatically falls back to free Gemini Flash Preview TTS when ElevenLabs reports an authorization/quota failure. It decodes ElevenLabs MP3 or Gemini 24 kHz mono PCM into a Unity `AudioClip` while preserving the same route callbacks and replay controls.
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
- Assigned furniture and anchor names must not leak into the story.
- Repeated mentions do not create extra route items.
- Malformed JSON can be recovered from `fullStory`.
- Missing target annotations are repaired when possible.
- Story order can be recovered from first occurrence in `fullStory`.
- Fragmented-object-scene heuristics can reject low-coherence output.

Minor plan-link paraphrases are canonicalized rather than rejected. If the first final story fails parsing/quality checks or omits a target annotation, the same provider receives one constrained rewrite request. A remaining failure returns to Setup with an explicit error; the runtime does not open an empty Preview or append isolated dream-like repair sentences.

Legacy per-word mnemonic generation, cue-blueprint generation, Stable Diffusion cue generation, and A-D image reranking remain disabled. Gemini is active only as an online provider for the continuous-story pipeline.

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
- Word-image markers are proximity-gated: only the current route marker is revealed at roughly 8 m and its detail UI remains available to roughly 8.5 m, with automatic look-to-inspect and foreground rendering to prevent room-geometry clipping.
- VR HMD users see the currently spoken guide or story segment as a subtitle in the world-space study panel; the existing replay control is unchanged.
- The first spoken Study pass is sequential and cannot be skipped. After it completes, Desktop and VR expose one thin whole-route bar split into one segment per word/story section. Selecting a segment restarts its anchor guide from the beginning; arbitrary within-audio seeking is intentionally disabled. Replay Voice, Restart Route, and automatic advancement stay synchronized with the route bar.
- `Self-Chosen Pictures` furniture selection before Study, with one word picture per furniture and one furniture per word; doors and windows are excluded.
- An optional post-Study all-picture room display in both conditions. It reveals every furniture marker at once, has no countdown, and can be entered/finished or skipped by the participant.
- Elapsed study time.
- Snapshot capture for the legacy recognition tests.

Intended study duration: 20 minutes of independent room exploration.

Existing assessment code:

- Mid test unlocks after at least 3 snapshots.
- Final test unlocks after all current words have snapshots.
- Each question asks the participant to choose the correct stored snapshot from three images.
- Final-test feedback can return the camera to the correct anchor.
- Questionnaire contains NASA-TLX-style 0-20 scales and vividness/helpfulness/trust 1-7 scales.

Decision still required: confirm whether the inherited mid/final image-recognition tasks are the final assessment after the 20-minute study period.

## 10. Export

Output directory:

```text
ExperimentExports/
```

Formats:

- JSON session record
- CSV recall/recognition summary

The export includes participant/session identifiers, room metadata, provider/model metadata, self-choice duration, Study duration, whether the optional all-picture display was entered, its duration, viewed/memorized counts, the complete `StorySessionData`, word entries, snapshot-test responses, questionnaire values, and interaction logs.

The current redesign still needs a fresh end-to-end exported test session before formal use.

## 11. Verification status

Verified on 2026-07-08:

- Active source compiles in the open Unity project after an earlier fixed `TryParseStoryEnvelope` error.
- The ElevenLabs voice-route implementation compiles in Unity and preserves the existing route, subtitle, replay, and completion-callback behavior.
- Unity successfully rebuilt and reloaded `Assembly-CSharp.dll` after the new condition, display, progress, Gemini, and export changes with no C# errors in the latest compile log.
- A direct `gemini-2.5-flash` API request using the configured user-level key returned valid JSON. `gemini-3.5-flash` returned HTTP 503 in three consecutive attempts, so 2.5 Flash remains the verified default.
- All 26 word-image files are valid readable PNGs.
- All preset/formal word keys have matching image filenames.
- OpenXR Play Mode without a connected headset reports `XR_ERROR_FORM_FACTOR_UNAVAILABLE`; the new VR progress-bar and all-picture interactions compile but still require headset validation.
- Android/Quest ElevenLabs audio decoding and completion still require an on-device network/audio test with a valid API key.
- There are no automated Unity tests.
- No post-redesign `ExperimentExports` artifact was present at verification time.

## 12. Immediate priorities

1. Run complete Desktop sessions for both conditions through export and inspect JSON/CSV.
2. Run both protocols on the intended OpenXR headset, including segmented route jumping after the first pass, subtitles, self-choice assignment, and all-picture display.
3. Pilot-review the 26 downloaded word images and replace ambiguous ones while preserving filenames and attribution.
4. Decide whether the application should enforce the 20-minute study window or only display a timer.
5. Confirm the final assessment after the 20-minute room period.
6. Add lightweight tests for story JSON repair/order recovery and export shape.
7. Remove or isolate the disabled mnemonic/Stable Diffusion pipeline after the new design stabilizes.

## 13. Fast search commands

```powershell
rg -n "GenerateStory|BuildStoryPrompt|TryBuildStorySession" Assets/Scripts/Services/OllamaLlmService.cs
rg -n "DrawGenerationView|DrawStudyView|BeginSnapshotTest|BuildExportPayload" Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
rg -n "WordImageCatalog|WordImages" Assets/Scripts Assets/Resources Docs
rg -n "StorySessionData|WordImageItemData|ExperimentSessionExport" Assets/Scripts/Data/ExperimentModels.cs
```

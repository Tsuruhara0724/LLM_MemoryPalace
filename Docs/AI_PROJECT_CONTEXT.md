# AI Project Context - LLM Memory Palace

Last verified: 2026-07-02

## 1. Current development state

- Active branch: `codex/story-only-memory-palace`
- Current commit at verification: `b75b1e2` (`Redesign experiment around continuous word stories`)
- Unity version: `6000.3.12f1`
- Current design label: `Continuous Story + Local Word Images`
- The redesign is active through `IsStoryOnlyRedesignEnabled() => true` in both the controller and LLM service.
- The active branch is synchronized with its remote but has not been merged into `main`.

Maintenance rule: update this document whenever story schema, word-image handling, experiment stages, study duration, room format, assessment flow, export fields, or default models change.

## 2. Research design

This is a Unity Desktop/VR experiment for Spanish vocabulary learning in a memory palace.

Current intended participant protocol:

1. Select a room and a set of Spanish target words.
2. Generate one continuous English story containing all selected words.
3. Place each word's fixed local picture at one stable room anchor.
4. Let the participant enter the room and study independently for 20 minutes.
5. Run the finalized post-study assessment and export research data.

There is no `Self Generated` comparison condition in the intended design. The old enum, data fields, and legacy methods still exist for compatibility, but the Setup UI exposes only the continuous-story flow and `BeginSelfAuthoring()` redirects to story generation.

The 20-minute duration is a protocol requirement. The runtime currently displays elapsed study time but does not enforce a hard 20-minute minimum or automatic transition. Until that is implemented, the researcher must control timing externally.

## 3. Current runtime flow

The active UI flow is:

```text
Setup
-> optional Room Builder
-> Continuous Story Preview
-> Study Room
-> existing Mid Image Test
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
- `OllamaLlmService.GenerateStory`: first requests a structured causal plan, then asks a second local Ollama pass to audit/repair the plan and write the final story; it parses and repairs JSON, validates story structure, and assigns story items to anchors.
- `WordImageCatalog`: loads `Resources/WordImages/{word}` and falls back to `_placeholder`.
- `ElevenLabsTextToSpeechService`: calls ElevenLabs `eleven_multilingual_v2`, decodes the returned MP3 into a Unity `AudioClip`, and preserves asynchronous completion callbacks for route progression.
- `ExperimentModels`: story, word-image, response, questionnaire, and export models.
- `RoomSpecModels`: room shell, furniture anchors, resource loading, and fallback room.

Known engineering debt: `MemoryPalaceExperimentController.cs` is still a very large mixed-responsibility class, and much of the old mnemonic/Stable Diffusion/catalog pipeline remains in source behind the story-only switch.

## 5. Story generation

Entry point:

```text
OllamaLlmService.GenerateStory
```

Default connection:

- Endpoint: `http://localhost:11434/api/generate`
- Story model: `gemma3:12b`
- Request format: Ollama JSON mode, non-streaming
- Temperature: `0.7`
- Token budget: `1800`

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

If either local Ollama pass fails or the final story fails validation, generation stops with an explicit error. The legacy fallback builder remains in code for compatibility but is no longer presented as a successful story.

Legacy per-word mnemonic generation, Gemini mnemonic generation, cue-blueprint generation, Stable Diffusion cue generation, and A-D image reranking are disabled for the active story-only design, although their code and data fields have not yet been removed.

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
- Word-image markers are proximity-gated: only the current route marker is revealed near its anchor, with automatic look-to-inspect and foreground rendering to prevent room-geometry clipping.
- VR HMD users see the currently spoken guide or story segment as a subtitle in the world-space study panel; the existing replay control is unchanged.
- Desktop and VR controls to replay the current utterance or restart the spoken route.
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

The export includes participant/session identifiers, room metadata, provider/model metadata, study duration, viewed/memorized counts, the complete `StorySessionData`, word entries, snapshot-test responses, questionnaire values, and interaction logs.

The current redesign still needs a fresh end-to-end exported test session before formal use.

## 11. Verification status

Verified on 2026-07-02:

- Active source compiles in the open Unity project after an earlier fixed `TryParseStoryEnvelope` error.
- The ElevenLabs voice-route implementation compiles in Unity and preserves the existing route, subtitle, replay, and completion-callback behavior.
- Unity successfully reloaded `Assembly-CSharp.dll` and subsequently entered Play Mode.
- All 26 word-image files are valid readable PNGs.
- All preset/formal word keys have matching image filenames.
- OpenXR Play Mode without a connected headset reports `XR_ERROR_FORM_FACTOR_UNAVAILABLE`; VR hardware flow is not yet verified.
- Android/Quest ElevenLabs audio decoding and completion still require an on-device network/audio test with a valid API key.
- There are no automated Unity tests.
- No post-redesign `ExperimentExports` artifact was present at verification time.

## 12. Immediate priorities

1. Pilot-review the 26 downloaded word images and replace ambiguous ones while preserving filenames and attribution.
2. Decide whether the application should enforce the 20-minute study window or only display a timer.
3. Confirm the final assessment after the 20-minute room period.
4. Run one complete Desktop session through export and inspect JSON/CSV.
5. Run the same protocol on the intended OpenXR headset.
6. Add lightweight tests for story JSON repair/order recovery and export shape.
7. Remove or isolate the disabled mnemonic/Stable Diffusion pipeline after the new design stabilizes.

## 13. Fast search commands

```powershell
rg -n "GenerateStory|BuildStoryPrompt|TryBuildStorySession" Assets/Scripts/Services/OllamaLlmService.cs
rg -n "DrawGenerationView|DrawStudyView|BeginSnapshotTest|BuildExportPayload" Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
rg -n "WordImageCatalog|WordImages" Assets/Scripts Assets/Resources Docs
rg -n "StorySessionData|WordImageItemData|ExperimentSessionExport" Assets/Scripts/Data/ExperimentModels.cs
```

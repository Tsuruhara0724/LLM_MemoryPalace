# AI Project Context - LLM Memory Palace

Last verified: 2026-06-20

Current development base:
- Branch: `restore/baseline-5bf3069`
- Baseline before current change: `4acd327`, tag `cue-story-microstory-20260605`
- Current save label: `Association Image Cue + dynamic Mnemonic`
- Meaning of current state: Japanese translation fields were removed; mnemonic/image prompts are English-only; image cue generation prepares four single-pass A-D image choices. The image catalog builder can skip vision scoring for fast bulk pre-generation; text generation now separates machine-facing Association Image Cue from the final learner-facing Mnemonic through an internal `cue_blueprint`. Mnemonic dynamically becomes either `HOOK_PLUS_STORY` or `STORY_ONLY`.
- Failed checkpoint saved for reference: remote branch/tag `checkpoint/failed-halfdone-before-progress-20260605` / `failed-halfdone-before-progress-20260605`, commit `a9912ef`. It contains a half-finished cue-blueprint attempt. Do not restore it blindly.

Maintenance rule:
- Update this file whenever prompt design, data fields, experiment flow, export format, room format, default models, or major TODO status changes.
- Also update `Docs/MANUAL_REBUILD_GUIDE.md` when a change affects how a human would manually rebuild the project.
- Keep this document compact but exact. Prefer file, class, function, and field names over long explanation.

## 1. Project Summary

This is a Unity VR/Desktop experiment app for Spanish vocabulary learning in a memory palace. The system assigns Spanish words to fixed room anchors, uses a pre-generated catalog or a selected text LLM provider to generate image-focused association cues and one final learner-facing mnemonic, generates multiple image candidates through Stable Diffusion from the image cue fields only, then uses an Ollama vision model to score and select the clearest anchor-plus-cue image. After the first selected image is displayed, the app continues preparing additional selected image sets in the background to hide regeneration latency. Participants study in the room, capture memory snapshots, complete mid/final image-choice tests, answer a questionnaire, and export JSON/CSV research data.

## 2. Research Intent

The project combines:
- Memory palace: each vocabulary item is placed at a fixed room anchor.
- Keyword/mnemonic method: Mnemonic should be the final learner-facing study text. It uses a hook only when the hook is strong enough to beat a story-only mnemonic.
- If no strong hook exists, Mnemonic is a single story-only paragraph anchored in the room. Do not force weak spelling overlap or weak sound-alikes into the learner text.
- Text-to-image support: drawable association scenes become visual cue images.
- Experiment workflow: compare `LLM Generated` and `Self Generated` conditions.

Paper-derived design principles:
- Do not use the full teaching mnemonic as the image prompt.
- Split generated fields by role:
  - `visualCue`: what the learner sees at the anchor.
  - `associationPrompt`: the core drawable visual relationship for image generation.
  - `imagePrompt`: a shorter foreground-focused prompt.
  - `imagePromptCandidates`: 4 diverse composition variants.
  - `mnemonic`: final learner-facing text. `HOOK_PLUS_STORY` has two short paragraphs; `STORY_ONLY` has one story paragraph.
  - `mnemonicMode`, `hookAccepted`, `hookScore`, `hookReason`, `mnemonicHook`: internal/export metadata for hook quality.
  - `storyCue`: legacy compatibility field; no longer generated or displayed.
- Image quality should prioritize clarity, simplicity, familiar objects, target-meaning specificity, anchor interaction, a novel but physically possible relation, and no irrelevant whole-room overview.
- Runtime image generation prepares four A-D image choices. Each choice uses one prompt candidate and one Stable Diffusion call, then receives one vision score; B-D are prepared after A is shown.
- User-facing UI hides `visualCue` / Visual Cue Scene. It shows Association Image Cue and Mnemonic only. Internally these map to `associationPrompt` and `mnemonic`.
- The current prompt/data schema is English-only. Japanese translation fields were removed from models, resources, UI display, and exports.

Important current-state note:
- Call 1 now outputs an internal `cue_blueprint`; Call 2 receives a compact scene blueprint summary so the image cue and final mnemonic share one memory event without copying image-prompt wording.
- Cue blueprint is a future direction, not current code.

## 3. Repository Map

Core scripts:

```text
Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs
Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
Assets/Scripts/Services/OllamaLlmService.cs
Assets/Scripts/Services/MnemonicCueFrameRag.cs
Assets/Scripts/Services/PreGeneratedMnemonicCatalog.cs
Assets/Scripts/Services/MockMnemonicGenerator.cs
Assets/Scripts/Data/ExperimentModels.cs
Assets/Scripts/Data/RoomSpecModels.cs
```

Responsibilities:
- `MemoryPalaceBootstrap.cs`
  - Auto-creates `MemoryPalaceExperimentController` after scene load.
- `MemoryPalaceExperimentController.cs`
  - Main controller. Handles UI, state machine, room builder, study room, image generation, vision selection, snapshot tests, questionnaire, export, and VR panel.
- `OllamaLlmService.cs`
  - Ollama calls, Gemini mnemonic calls, prompt construction, JSON parsing/repair, room generation, mnemonic generation.
- `MnemonicCueFrameRag.cs`
  - Lightweight rule/example retrieval for live Ollama generation. It is not embedding/vector RAG.
- `PreGeneratedMnemonicCatalog.cs`
  - Loads curated word x anchorType mnemonic items from `Resources/PreGeneratedMnemonics.json` and returns room-anchor-specific `MnemonicItemData`.
- `MockMnemonicGenerator.cs`
  - Fallback/sample mnemonic generation when Ollama is unavailable.
- `ExperimentModels.cs`
  - Experiment state models, mnemonic item model, visual object model, response models, export models.
- `RoomSpecModels.cs`
  - Room spec, anchor spec, resource loading, fallback room.

Resources:

```text
Assets/Resources/MemPalaceDemoData.json
Assets/Resources/MemPalaceRoomSpec.json
Assets/Resources/MnemonicCueFrameRag.json
Assets/Resources/PreGeneratedMnemonics.json
Assets/Resources/PreGeneratedImageCueCatalog.json
```

Outputs:

```text
ExperimentExports/
ExperimentExports/GeneratedMnemonicImages/<session>/
```

Do not commit generated artifacts:

```text
Demo01.apk
Demo01_BackUpThisFolder_ButDontShipItWithYourGame/
MemPalaceLLM_BurstDebugInformation_DoNotShip/
.codex_tmp/
.utmp/
```

## 4. Runtime Flow

Experiment stages are defined in `ExperimentModels.cs`:

```text
Setup -> RoomBuilder -> Generation -> SelfAuthoring -> Study -> Recall -> Questionnaire -> Result
```

Controller flow:
- `Awake`
  - Loads demo data, room spec, and initial session state.
- `OnGUI`
  - Draws UI for the current stage.
- Setup
  - Participant ID, condition, Ollama endpoint/model, image endpoint, vision model, word set, optional room builder.
- RoomBuilder
  - Edits room shell and anchor furniture.
  - Room shells normalize to raised walls plus generated ceiling slabs; builder previews hide ceilings so top-down editing stays readable.
- Generation
  - Preferentially looks up pre-generated word x furniture mnemonic items through `PreGeneratedMnemonicCatalog`.
  - Missing catalog combinations default to a deterministic local story-only fallback so Gemini/Ollama quota or outages do not block a session.
  - The selected live mnemonic provider, Ollama Local or Gemini Online, can optionally improve missing combinations before the local fallback is used.
  - Optional random anchor assignment can be used before catalog lookup and live generation.
  - Displays generated preview and allows regeneration.
  - Loads pre-generated image cue result pools before Study. Entering the room is blocked until every mnemonic item has a prepared image cue. Slow SD generation is a researcher-only fallback for preparing missing catalog assets.
- SelfAuthoring
  - User manually writes anchors, visual cues, and mnemonics, then pre-generates image cues before entering Study.
- Study
  - User enters the room, inspects mnemonic objects, reviews already prepared image cues, optionally cycles A-D choices, and captures memory snapshots.
- Recall
  - Mid/final image-choice tests using stored snapshots.
- Questionnaire
  - Workload and subjective quality ratings.
- Result
  - JSON/CSV export.

Important defaults in `MemoryPalaceExperimentController.cs`:
- `MidTestTriggerCount = 3`
- `RandomAdvancedWordCount = 8`
- `RequiredImagePromptCandidateCount = 4`
- `BufferedImageCueResultCount = 4`
- `ollamaBaseUrl = "http://localhost:11434/api/generate"`
- `ollamaModel = "qwen3:8b"`
- `geminiModel = "gemini-2.5-flash"`
- `geminiApiKey` is runtime-only; if blank, the app reads `GEMINI_API_KEY` then `GOOGLE_API_KEY`.
- `imageGenerationEndpoint = "http://127.0.0.1:7860/sdapi/v1/txt2img"`
- `imageCueValidationModel = "gemma3:12b"`
- `usePreGeneratedImageCueCatalog = true`
- `allowRuntimeImageCueGenerationForMissing = false`

Formal material plan:
- `Assets/Resources/MemPalaceDemoData.json` includes `formal_12_pool`.
- Formal runs should sample 8 words from the 12-word pool.
- The mnemonic text catalog target is 12 words x 10 fixed anchor types = 120 reviewed word-anchor entries.
- The image asset target is 12 words x 10 fixed anchor types x 4 reviewed images = 480 images.
- Fixed anchor types are defined by `PreGeneratedImageCueCatalog.FormalAnchorTypes`: `door`, `bed`, `desk`, `chair`, `table`, `sofa`, `wardrobe`, `bookshelf`, `air_conditioner`, `television`.
- When `usePreGeneratedImageCueCatalog` is enabled, mnemonic anchor assignment is constrained to those formal anchor types so runtime sessions do not sample image-catalog-missing anchors.

## 5. Mnemonic Generation Architecture

Current LLM condition can use a pre-generated catalog before live LLM generation. Catalog items are keyed by target word plus normalized `anchorType`, then stamped with the concrete room anchor id/label at runtime. If a word-anchorType combination is missing and fallback is enabled, the selected live provider fills only the missing items. Live generation still uses two LLM calls: Call 1 generates machine-facing image cue fields. Call 2 generates the final learner-facing Mnemonic plus hook quality metadata.

As of 2026-06-18, missing catalog pairs no longer hard-fail the session when Gemini is rate-limited or unavailable. The default path is:
1. Use pre-generated catalog hits.
2. If `allowLiveLlmForMissingPreGenerated` is enabled, try the selected live provider for missing pairs.
3. If live generation is disabled, incomplete, or fails, fill missing pairs with local `STORY_ONLY` mnemonic items when `useLocalFallbackForMissingPreGenerated` is enabled.

The local fallback is intentionally conservative: it creates one anchored story paragraph with `mnemonicSource = "local_story_fallback"` and does not force a weak mnemonic hook. It is lower quality than curated/Gemini output, but preferable to blocking the experiment or presenting empty mnemonic items.

Live mnemonic providers:
- `LlmProviderMode.OllamaLocal`: local Ollama endpoint/model, no cloud dependency.
- `LlmProviderMode.GeminiOnline`: Gemini REST `generateContent`, intended as the quality-first online provider for mnemonic text/cue packages.
- Free Gemini keys should default to `gemini-2.5-flash` or `gemini-2.5-flash-lite`; `gemini-2.5-pro` is exposed as a paid/billing-oriented option.
- Gemini requests are sent one word at a time and retry on transient 429/503/high-demand errors; Pro requests can fall back to Flash, and Flash/Pro requests can fall back to Flash-Lite for the same request.
- Gemini API keys must not be committed. Use the runtime password field, `GEMINI_API_KEY`, or `GOOGLE_API_KEY`.
- Setup UI includes `Test Gemini Connection`, a tiny JSON-only request that verifies the key/model before mnemonic generation.
- Room generation, guided furniture placement, custom furniture interpretation, and image cue validation still use the existing Ollama paths.

### Pre-generated Catalog

Entry:
- `PreGeneratedMnemonicCatalog.TryCreateItem`

Resource:
- `Assets/Resources/PreGeneratedMnemonics.json`
- The formal pool avoids target words that duplicate furniture anchors; `armario=wardrobe` was replaced with `zapato=shoe`.

Key fields:
- `word`
- `anchorType`
- `mnemonicSource`
- `visualCue`
- `associationPrompt`
- `mnemonic`
- `mnemonicMode`
- `imagePromptCandidates`

Controller options:
- `preferPreGeneratedMnemonics`
- `allowLiveLlmForMissingPreGenerated`
- `useLocalFallbackForMissingPreGenerated`
- `randomizeMnemonicAnchors`
- `providerMode`
- `geminiModel`

Catalog QA tools in Setup:
- `Check Catalog Coverage` builds the current word-set x assigned-anchor preview and reports pre-generated hits/misses.
- `Check 12 x 10 Mnemonic Matrix` reports the full formal 120-pair text catalog coverage.
- If randomized anchors are enabled, coverage uses a fixed seed preview so the QA button does not consume the real generation RNG.
- `Validate Catalog` checks `PreGeneratedMnemonics.json` for empty required fields, duplicate `word|anchorType` keys, illegal `mnemonicMode`, too few image prompt candidates, and inconsistent hook metadata.
- `Show Mnemonic Catalog Review Builder` exports missing or full 12 x 10 draft JSON for manual GPT-5.5 review, copies it to the clipboard, accepts pasted reviewed JSON, and upserts valid items into `Assets/Resources/PreGeneratedMnemonics.json`.
- `PreGeneratedMnemonicCatalog.TryParseReviewedBatch` accepts a strict `{ "items": [...] }` wrapper or a raw JSON array, and strips Markdown fences when pasted output includes them.
- `PreGeneratedMnemonicCatalog.UpsertReviewedItems` writes to disk first, then runtime lookup reloads from disk before falling back to `Resources.Load`.

Export metadata:
- `ExperimentSessionExport.preGeneratedMnemonicCount`
- `ExperimentSessionExport.liveGeneratedMnemonicCount`
- `ExperimentSessionExport.localFallbackMnemonicCount`
- `ExperimentSessionExport.usedLocalFallback`
- `ExportWordEntry.anchorType`
- `ExportWordEntry.mnemonicSource`
- `ExportWordEntry.cueBlueprint`
- `ExportWordEntry.imageCueResults`
- `ImageCueResultExport.innerCandidates`

### Call 1: Image Scene / Visual Cue

Entry:
- `OllamaLlmService.GenerateMnemonicChunk`

Prompt builders:
- `BuildVisualCuePrompt`
- Single-item regeneration: `BuildSingleVisualCueRegenerationPrompt`

Output fields:

```text
cue_blueprint
visual_cue_en
association_prompt_en
image_prompt_en
image_prompt_candidates_en
visual_objects
```

Call 1 rules:
- Build `cue_blueprint` first and derive the machine-facing cue fields from it.
- Use meaning and anchor only.
- Do not use Spanish pronunciation, spelling, cognates, puns, or sound similarity.
- Generate a meaning-first and image-stable visual scene.
- Build a target-meaning event, not a target-object display.
- The cue should physically interact with the assigned anchor.
- For nature/place/large-environment nouns, use indoor proxy events instead of full outdoor scenes.
- `association_prompt_en`, `image_prompt_en`, and `image_prompt_candidates_en` must avoid process words such as candidate, version, best-of-four, selected image, story, mnemonic, learner, remember, and Spanish word.
- Do not output `mnemonic_en` or `story_cue_en`.
- Generate exactly 4 diverse image prompt candidates.

### Call 2: Dynamic Mnemonic

Prompt builders:
- `BuildMnemonicLinkPrompt`
- `BuildSingleMnemonicLinkPrompt`

Inputs:
- word
- meaning
- assigned anchor
- No Association Image Cue / visual cue / image prompt input.

Output fields:

```text
mnemonic_en
mnemonic_mode
hook_judge.accepted
hook_judge.score
hook_judge.reason
hook_judge.best_hook
```

Call 2 rules:
- `mnemonic_en` is the only learner-facing study text.
- First generate possible hooks, then judge whether the best hook adds real value beyond a story-only mnemonic.
- Accept a hook only when it creates a memorable intermediate cue, phrase, image, or action that helps retrieve the Spanish word form.
- Reject weak hooks based only on partial spelling overlap, weak sound-alikes, circular explanation, forced puns, or generic relation wording.
- `mnemonic_mode = HOOK_PLUS_STORY` only when `hook_judge.accepted = true` and `hook_judge.score >= 7`.
- `HOOK_PLUS_STORY`: exactly two short paragraphs. Paragraph 1 explains the accepted hook; paragraph 2 tells an anchor-based story.
- `STORY_ONLY`: exactly one short anchor-based story paragraph. Do not mention that no hook was found.
- Example rejection: `isla -> isl -> island` must use `STORY_ONLY`.
- Example acceptance: `carretera -> carry the road` may use `HOOK_PLUS_STORY`.
- Do not write camera framing, image prompt language, or separate story fields.
- Do not mention or assume generated images, visual cue props, association prompts, image prompts, or 3D proxy props.
- Avoid meta phrases: `visible cue`, `points to`, `retrieves`, `bind syllables`, `action rhythm`, `same scene`, `links to`, `is associated with`.
- Do not merely say "repeat the word."

Merge:
- `MergeGeneratedMnemonicFields`
- `BuildMnemonicItemData`
- `NormalizeImagePromptCandidates`

## 6. Image Generation, Vision Selection, And Latency Hiding

Main function:
- `PreGeneratedImageCueCatalog.TryLoadImageCueSet`
- `MemoryPalaceExperimentController.GenerateMnemonicImageCueRoutine`
- `MemoryPalaceExperimentController.PrepareImageCuesBeforeStudyRoutine`

Pipeline:
0. Pre-study image cue gate
   - Step 2 shows `Image Cue Preparation`.
   - Formal mode uses `PreGeneratedImageCueCatalog` and loads A-D image choices from `Resources/PreGeneratedImageCueCatalog.json`.
   - `Load Missing Image Cues From Catalog` runs `PrepareImageCuesBeforeStudyRoutine`.
   - `Reload All Image Cues From Catalog` clears existing image cue pools and reloads them.
   - If `allowRuntimeImageCueGenerationForMissing` is false, a missing catalog image blocks Study instead of running SD.
   - If researcher mode is enabled, missing catalog images may fall back to `GenerateMnemonicImageCueRoutine`.
   - `EnterStudyRoom` refuses to enter Study until `AreAllCurrentImageCuesReady()` is true.
   - Self-authoring edits clear the affected word's generated image cue so stale images cannot survive after text changes.
   - Setup includes `Check Image Catalog Coverage`, `Check 12 x 10 Image Matrix`, and `Validate Image Catalog`.
   - Setup also includes `Image Catalog Builder`.
     - `Generate Next Missing Pair` creates one missing word-anchor image entry.
     - `Generate Missing Matrix` iterates all missing formal 12 x 10 entries until complete or cancelled.
     - Builder output PNGs go to `Assets/Resources/PreGeneratedImageCues/<word>/<anchorType>/`.
     - Builder writes/updates `Assets/Resources/PreGeneratedImageCueCatalog.json`.
     - Each saved entry contains 4 A-D reviewed image choices generated through the same SD + vision reranking pipeline.
1. Displayed-result pool
   - `BufferedImageCueResultCount = 4`.
   - The UI-visible results are A/B/C/D.
   - Each of A/B/C/D is produced by one Stable Diffusion call from one prompt candidate.
   - Runtime/manual generation can score each image with the VLM; Image Catalog Builder defaults to fast mode and skips scoring for bulk pre-generation.
   - A is displayed as soon as it is ready; B/C/D continue in the same coroutine while the UI remains responsive.
   - Desktop UI arrows call `TryCycleDisplayedImageCueCandidate` to switch among prepared A-D results.
   - `Regenerate Image Cue Set` clears the old pool and starts a fresh A-D run.
2. Single-pass image generation
   - `GenerateBestImageCueVariantRoutine`
   - Selects the prompt candidate matching the A/B/C/D slot.
   - Generates one raw image for that displayed result.
   - Scores that image with the vision judge.
   - Keeps the generated image even if the score flags issues, so human review can decide whether to regenerate the pair.
   - Stores the single scored attempt under `ExportWordEntry.imageCueResults.innerCandidates` for backward-compatible export shape.
3. `BuildMnemonicImagePromptCandidates`
   - Uses `imagePromptCandidates`, `imagePrompt`, `associationPrompt`, and `visualCue`.
   - Re-wraps each raw prompt as one machine-facing single-image layout before Stable Diffusion sees it.
   - Adds camera-safe layout variants without sending builder/process words to the image model:
     - `object-on-anchor`
     - `action-focused`
     - `novel-possible`
     - `literal-simple`
4. Stable Diffusion txt2img
   - Request class: `StableDiffusionTxt2ImgRequest`
   - 512 x 512
   - 28 steps
   - CFG 8.5
   - sampler `DPM++ 2M`
   - `batch_size = 1`, `n_iter = 1`, `tiling = false`, `do_not_save_grid = true`.
   - Final prompt is logged as `FINAL_IMAGE_PROMPT_SENT_TO_MODEL`.
   - Negative prompt rejects split-screen, collage, contact sheets, grids, image sequences, side-by-side views, and multi-panel layouts.
5. Ollama Vision validation
   - `ValidateImageCueSubjectsRoutine`
   - Prompt builder: `BuildImageCueValidationPrompt`
   - Default model: `gemma3:12b`
   - Rejects collage/split-screen/contact-sheet output via `single_continuous_image` and `no_split_screen_or_collage`.
6. Scoring
   - `ScoreImageCueValidation`
   - Rewards anchor visible, cue visible, focus, meaning specificity, foreground clarity, anchor interaction, simplicity, familiar objects, novel possible relation, and no room overview.
7. Selection and storage
   - Passing candidate beats non-passing candidate.
   - Within the same pass state, higher score wins.
   - Current displayed texture is saved to:
     - `mnemonicImageCues`
     - `imageCuePath`
     - `selectedImagePrompt`
     - `selectedImageCandidateIndex`
     - `imageSelectionReason`
   - The A-D pool is stored in:
     - `imageCueCandidateResults`
     - `displayedImageCueCandidateIndexes`
     - `ImageCueCandidateResult`
   - `CaptureMemorySnapshotRoutine` captures whichever A-D result is currently displayed.

Known behavior:
- If vision validation stops after one usable image exists, the system may save the best available image and record the failure reason.
- VR displays image cue preview/status and provides Previous/Next image cue buttons for prepared A-D choices.
- Image generation remains a known unstable area, so it is now front-loaded before Study instead of being part of normal study-room time.

## 7. RAG Status

The project has a RAG-like helper, but it is not vector retrieval.

Code:
- `MnemonicCueFrameRag.BuildBatchGuidance`
- `BuildSingleGuidance`
- `RetrieveCases`
- `ScoreCase`
- `BuildAnchorAffordance`
- `LoadLibrary`

Resource:
- `Assets/Resources/MnemonicCueFrameRag.json`

Separate from this lightweight prompt RAG, the project now also has a curated pre-generated mnemonic catalog:
- Code: `PreGeneratedMnemonicCatalog`
- Resource: `Assets/Resources/PreGeneratedMnemonics.json`
- Lookup key: normalized `word` plus normalized `anchorType`
- Purpose: improve accuracy and consistency for known word-furniture combinations without forcing live LLM generation.
- Gemini can be used as a quality-first live provider to create or regenerate missing mnemonic packages, but the preferred experiment path is still to pre-generate and freeze accepted catalog entries.

Built-in failure checklist:
- P1 Tautology
- P2 Split-link
- P3 Broad meaning drift
- P4 Meta explanation
- P5 Weak foreground
- P6 Unsafe association
- P7 Hidden or swallowed cue
- P8 Target-object display
- P9 Dead cue story

When adding a new recurring failure pattern:
1. Add concise general guidance to `MnemonicCueFrameRag.cs` if it is broadly useful.
2. Add retrieval cases to `MnemonicCueFrameRag.json` if examples help.
3. Add prompt rules in `OllamaLlmService` only if generation should change.
4. Add runtime guardrails only for narrow, repeated, high-confidence failures.

## 8. Data Model Change Map

When adding a mnemonic-related field:
1. Add it to `SampleMnemonicItem`, `MnemonicItemData`, and `ExportWordEntry` in `ExperimentModels.cs`.
2. Add it to `GeneratedMnemonicItem` in `OllamaLlmService.cs`.
3. Map it in `BuildMnemonicItemData`.
4. Preserve it in `MockMnemonicGenerator` if needed.
5. Display it in preview/study UI if user-facing.
6. Export it in `BuildExportPayload`.
7. If it affects image generation, include it in `BuildMnemonicImagePromptCandidates` or `BuildImageCueValidationPrompt`.
8. Update both docs.

When changing export:
1. Update `ExperimentModels.cs`.
2. Update `BuildExportPayload`.
3. Update `BuildRecallCsv`.
4. Confirm old export analysis scripts if any.

When adding a word set:
1. Edit `Assets/Resources/MemPalaceDemoData.json`.
2. Use fields `word`, `meaning`.
3. Add `sampleMnemonics` only if curated sample data is needed.

When changing room format:
1. Update `RoomSpecModels.cs`.
2. Update `Assets/Resources/MemPalaceRoomSpec.json`.
3. Update `RoomSpecCatalog.CreateFallbackRoom`.
4. Update room builder and save/load logic if the field is editable.

## 9. Prompt Change Map

Visual cue prompt:
- `OllamaLlmService.BuildVisualCuePrompt`
- `BuildSingleVisualCueRegenerationPrompt`
- `BuildCompactVisualCueRetryPrompt`

Dynamic Mnemonic prompt:
- `BuildMnemonicLinkPrompt`
- `BuildSingleMnemonicLinkPrompt`
- Also check fallback/hardcoded guardrail text in `MemoryPalaceExperimentController.cs` if learner-facing mnemonic behavior changes.

Image candidate logic:
- Generated fields are in `OllamaLlmService`.
- Runtime candidate composition is in:
  - `BuildMnemonicImagePromptCandidates`
  - `BuildImagePromptCandidateLabel`
  - `BuildImagePromptCandidateComposition`
  - `GenerateBestImageCueVariantRoutine`
  - `BuildFinalStableDiffusionPrompt`
  - `BuildImageCueResultLabel`
  - `BuildImageCueCandidateDisplaySummary`
  - `TryCycleDisplayedImageCueCandidate`

Vision judge:
- `BuildImageCueValidationPrompt`
- `ImageCueValidationResult`
- `ScoreImageCueValidation`
- Failure summary and retry guidance helpers.

Important principle:
- Do not keep stacking rules forever.
- If `visualCue`, `associationPrompt`, and `mnemonic` start copying each other again, change the generation structure instead of adding only another sentence to the prompt.
- Image generation must not read from `mnemonic`; it should use `imagePromptCandidates`, `imagePrompt`, `associationPrompt`, and only then `visualCue` as a fallback.

## 10. Cue Blueprint Architecture

Problem to solve:
- Fields can collapse into repetition:
  - `visualCue` becomes target object on anchor.
  - `associationPrompt` becomes a shorter duplicate.
  - `mnemonic` forces a weak hook instead of using story-only mode.

Current structure:

```text
Word + Meaning + Anchor
-> internal cue_blueprint
-> Association/Image prompt branch
-> Dynamic Mnemonic branch
-> validation/regeneration
```

`cue_blueprint` fields:
- `targetMeaning`
- `visualSceneCore`
- `mainObject`
- `anchorRelation`
- `relativeSize`
- `mainActionOrState`
- `visibleObjects`
- `mnemonicHookNote`
- `mnemonicMode`

Rule:
- Association Prompt paints the cue.
- Mnemonic is the final learner-facing text. If the hook is strong, it teaches the hook then tells the story; otherwise it is a clean story-only mnemonic.
- All branches can come from the blueprint, but none should copy the others.

Implementation:
- `OllamaLlmService.GeneratedCueBlueprint`
- `CueBlueprintData`
- `MnemonicItemData.cueBlueprint`
- `ExportWordEntry.cueBlueprint`

## 11. Known Issues

High priority:
- Dynamic Mnemonic may still accept borderline hooks unless hook scoring stays strict.
- Hard words may still need story-only mnemonics because no natural word-form hook exists.
- Some visual cues may still become target-object displays.
- Stable Diffusion may produce room overview images, miss the anchor, or miss the cue.
- VLM judge can choose the best available image, but cannot guarantee a truly good image.
- Field separation can still degrade if a model ignores the blueprint, but Call 1/Call 2 now have a structural bridge instead of only prompt rules.

Medium priority:
- `MemoryPalaceExperimentController.cs` is too large and mixes UI, flow, room, image, export, and VR logic.
- No automated tests protect JSON parsing, prompt schema, export shape, or image score logic.
- RAG is keyword/example retrieval, not semantic retrieval.
- Japanese translation fields are intentionally removed. Keep prompt/data/export changes English-only unless a future experiment explicitly reintroduces multilingual display.

Low priority:
- Untracked build artifacts exist in the workspace.
- `Demo_Runbook.md` may lag behind the real current flow.

## 12. TODO

Prompt/mnemonic:
- Continue expanding curated word-form hook examples for hard words after pilot review:
  - `aeropuerto`
  - `pasillo`
  - `cascada`
  - `cartera`
  - `desierto`
  - `barrio`
- Current prompt examples explicitly reject weak hooks such as `isla -> isl -> island`, `aeropuerto -> airport`, forced `cartera -> car tear a`, and distracting `barrio -> bar/rio`.
- Cue blueprint architecture is now implemented in Call 1 / Call 2 and exported; keep reducing prompt-rule pileup by moving future constraints into structured fields.

Image:
- Single-pass image paths, scores, and validation JSON are exported through `imageCueResults`.
- Desktop and VR can switch among prepared A-D reviewer image choices; selected result metadata is exported.
- VR world-space panel has Previous Image / Next Image buttons for A-D reviewer choices.
- Vision prompt rejects tiny cue/anchor details, showroom/floor-plan views, and whole-room overviews.
- Continue separating `associationPrompt` and `imagePrompt`.

Experiment:
- Finalize study protocol and dependent variables.
- Confirm export fields for thesis/statistical analysis.
- Add participant/session metadata if needed.
- Verify distractor generation for mid/final tests.

Engineering:
- Add lightweight tests for JSON parse/repair, score calculation, and export shape.
- Eventually split the large controller into services:
  - UI
  - RoomBuilder
  - StudyFlow
  - ImageCueGeneration
  - Export
- Check `.gitignore` coverage for generated build artifacts.

## 13. Git Notes

Safe baseline:
- `4acd327 cue story改成学习者小故事`
- tag `cue-story-microstory-20260605`

Previous notable commits:
- `597a8f5 mnemonic大致正确，图片生成仍然有问题`
- `23be6db 加完rag以及违禁词语禁止，分层cue scene和cue story前`
- `0a88a96 Save image cue validation checkpoint`

Failed checkpoint:
- `a9912ef 失败。半成品状态。交进度前`
- Contains half-finished cue blueprint changes.
- Reference only.

Before committing:
- Run `git status -sb`.
- Stage only intended source/docs/resource files.
- Do not stage APK, build backups, debug folders, or temp folders.
- For prompt commits, include the research/design reason in the commit message.

## 14. Fast Search Commands

```powershell
rg -n "BuildVisualCuePrompt|BuildMnemonicLinkPrompt|BuildSingleMnemonicLinkPrompt" Assets/Scripts/Services/OllamaLlmService.cs
rg -n "GenerateMnemonicImageCueRoutine|ValidateImageCueSubjectsRoutine|ScoreImageCueValidation" Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
rg -n "BuildExportPayload|WriteExportFiles|BuildRecallCsv" Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
rg -n "MnemonicItemData|ExportWordEntry|ExperimentSessionExport" Assets/Scripts/Data/ExperimentModels.cs
rg -n "BuildBatchGuidance|P[0-9]|RetrieveCases" Assets/Scripts/Services/MnemonicCueFrameRag.cs
rg -n "PreGeneratedMnemonicCatalog|PreGeneratedMnemonics|mnemonicSource|anchorType" Assets/Scripts Docs Assets/Resources
```

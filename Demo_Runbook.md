# Memory Palace Experiment Runbook

Last verified: 2026-07-08

## Experiment design

- Setup exposes two conditions: `LLM Story` and `Self-Chosen Pictures`.
- The participant enters the memory-palace room and studies independently for 20 minutes.
- In `LLM Story`, Ollama or Gemini creates one continuous English story containing all selected Spanish target words.
- In `Self-Chosen Pictures`, the participant assigns each fixed local word picture to a different furniture marker before Study; no generated story or narration is used.
- Furniture anchors define the spatial route. Furniture names must not influence the story text.
- Word pictures are fixed local files under `Assets/Resources/WordImages/` and are not generated during a session.
- Optional system speech guides the participant through the ordered story route one anchor at a time.

The current runtime still contains mid/final snapshot-recognition screens from the earlier prototype. The 20-minute study duration is the protocol target; the application does not yet enforce a hard 20-minute lock.

## Quick start

1. Open the project with Unity `6000.3.12f1`.
2. Wait for scripts and word images to import.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Press Play. `MemoryPalaceBootstrap` creates the experiment UI automatically.
5. Enter the participant ID.
6. Use the default room, load the example room, or open Room Builder.
7. Select a preset word set. Formal runs sample 8 distinct words from the 12-word formal pool.
8. Select `LLM Story` or `Self-Chosen Pictures`.
9. For `LLM Story`, select Ollama Local or Gemini Online, confirm its settings, generate the story, and review its per-word beats.
10. For `Self-Chosen Pictures`, enter the assignment room, select furniture, browse word pictures, assign every word to a different furniture item, and finish the assignment.
11. Leave `Use guided voice route` enabled for narrated LLM sessions unless the session is intentionally silent.
12. Enter the study room and begin the 20-minute independent study period.

Recommended Ollama settings:

- Endpoint: `http://localhost:11434/api/generate`
- Model: `gemma3:12b`

Recommended Gemini setting:

- Model: `gemini-2.5-flash` (verified with the Gemini API free tier on 2026-07-08)
- Key source on Windows desktop: user-level `GEMINI_API_KEY`, with Setup input as a fallback

Minor causal-plan wording differences are normalized automatically. If the first story omits a target or fails quality checks, the selected LLM receives one repair request; the app starts Study only after the repaired story passes validation. A remaining failure returns to Setup instead of opening an empty Preview.

## Participant study period

- Duration: 20 minutes.
- The participant navigates the room independently.
- The voice first says which anchor to approach and which word image will be there.
- All word-image markers stay hidden until the participant comes within roughly 8 metres of the current route anchor; only that anchor's image appears, and its detail UI remains available to roughly 8.5 metres.
- Looking toward the revealed image counts as inspection and starts that word's story segment.
- When the segment finishes, the voice automatically guides the participant to the next anchor.
- The revealed image is offset toward the viewer and rendered as foreground study UI to avoid intersecting or disappearing behind room geometry.
- In an active VR HMD, the world-space study panel displays the text currently being spoken as a subtitle. Use the existing Replay Voice button when repetition is needed.
- A thin `VOICE` progress bar is always visible directly below the top banner in Desktop Study and at the top of the VR study panel. It activates when an utterance is loaded; select or drag it to seek backward or forward, and Replay Voice restarts the loaded utterance.
- Selecting an image shows its Spanish word, English meaning, and story beat.
- The complete continuous story remains available in the study UI.
- The researcher should record the true start/end time until an enforced timer is implemented.

Desktop controls:

- Right mouse drag: look around
- WASD: move
- Q / E: move down / up
- Left click: inspect a word image
- Shift: move faster
- `Replay Voice`: repeat the current anchor guide or story segment
- `Restart Route`: return to the first spoken anchor guide

VR study mode can be enabled in Setup. A connected OpenXR headset is required for VR validation.

After Study is complete, both conditions offer an optional `Show All Pictures In The Room` step before the final test. It reveals every assigned furniture word-picture UI simultaneously in the existing room. There is no countdown; the participant chooses when to enter and when to finish. Skipping is also allowed.

## Word images

The project currently contains images for all 26 distinct preset words, including all 12 words in `formal_12_pool`.

- Naming: `Assets/Resources/WordImages/{spanish_word}.png`
- Missing-image fallback: `_placeholder.png`
- Source and license record: `Assets/Resources/WordImages/ATTRIBUTION.md`

Replacing a picture requires only replacing the same-named PNG. Keep its license record up to date.

## Existing assessment and export flow

The current prototype can capture word-image snapshots, run mid/final three-choice image recognition, collect questionnaire ratings, and export research data. These screens remain available while the final 20-minute protocol and dependent variables are being finalized.

Exports are written to `ExperimentExports/`:

- `session_<participant>_<timestamp>.json`
- `session_<participant>_<timestamp>.csv`

## Pre-run checklist

- Unity Console has no C# compile errors.
- All selected words display their real image rather than `_placeholder.png`.
- Ollama connection and model are available.
- For Gemini runs, `gemini-2.5-flash` passes `Test Gemini` and the key is available from Setup or user-level `GEMINI_API_KEY`.
- Speech uses ElevenLabs first when its key has quota. If ElevenLabs reports an authorization/quota failure, the runtime automatically uses the configured Gemini key with free `gemini-2.5-flash-preview-tts` for the same subtitle, completion callback, route order, progress, seek, and replay flow.
- Press `Test ElevenLabs Voice` in Setup and confirm audible speech before entering the room. The voice route can start with either a ready ElevenLabs configuration or a Gemini API key.
- A valid ElevenLabs Voice ID is entered; the default is the voice used by the current ElevenLabs API example.
- The device has internet access to `api.elevenlabs.io` and audio output is audible.
- The generated story contains each selected `meaning (Spanish word)` exactly once as a route item.
- The room has enough distinct anchors for the selected words.
- Desktop or VR navigation works on the study machine.
- A 20-minute timing method is ready.
- A complete test session can be exported before recruiting participants.
- The optional all-picture room display can be entered, finished, and skipped, and its choice/duration appears in the JSON export.

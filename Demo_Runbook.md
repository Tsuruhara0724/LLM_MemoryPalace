# Memory Palace Experiment Runbook

Last verified: 2026-07-08

## Experiment design

- Setup exposes three conditions: `LLM Story + Own Order`, `Write Story + Own Order`, and `Empty Room`.
- The participant enters the memory-palace room and studies independently for 20 minutes.
- In `LLM Story + Own Order`, Ollama or the official Gemini API creates one continuous English story. The participant then defines only the spatial word-to-furniture mapping; the story order is unchanged.
- In `Write Story + Own Order`, the participant writes eight connected segments that form one complete story, then defines the same one-to-one furniture mapping. The existing TTS, subtitles, replay, and route progress are retained.
- In `Empty Room`, the participant enters only the room shell: no furniture, target words, pictures, story, subtitles, or voice route.
- Furniture anchors define the spatial route. Furniture names must not influence the story text.
- Word pictures are fixed local files under `Assets/Resources/WordImages/` and are not generated during a session.
- Optional speech guides the participant through the ordered story route one anchor at a time. Local Chatterbox is preferred when running; ElevenLabs and official Gemini TTS remain fallbacks.

The current runtime still contains mid/final snapshot-recognition screens from the earlier prototype. The 20-minute study duration is the protocol target; the application does not yet enforce a hard 20-minute lock.

## Quick start

1. Open the project with Unity `6000.3.12f1`.
2. Wait for scripts and word images to import.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Press Play. `MemoryPalaceBootstrap` creates the experiment UI automatically.
5. Enter the participant ID.
6. Use the default room, load the example room, or open Room Builder.
7. Select a preset word set. Formal runs sample 8 distinct words from the 12-word formal pool.
8. Select one of the three conditions.
9. For `LLM Story + Own Order`, generate and review the story; for `Write Story + Own Order`, write all eight connected story segments.
10. In either story condition, enter the PC assignment room, click the actual furniture models, and choose one unused word from the selected furniture's 2-column x 4-row card.
11. For `Empty Room`, enter the room directly; picture-dependent tests and the all-picture display are skipped.
12. Leave `Use guided voice route` enabled for either narrated story condition unless the session is intentionally silent.
13. Enter the study room and begin the 20-minute independent study period.

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
- The first Study pass is locked to the original spoken order and has no route-jump control. After every story segment has completed once, a thin segmented `ROUTE` bar appears below the Desktop top banner and at the top of the VR panel. Each segment represents one word/story section; selecting a segment restarts that section from its anchor guide, never from the middle. Replay Voice, Restart Route, and automatic advancement update the same whole-route position.
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

After Study is complete, both story conditions offer an optional `Show All Pictures In The Room` step before the final test. The empty-room baseline does not offer this step.

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
- Speech uses local Chatterbox first when its endpoint is enabled. If local speech is unavailable, the runtime tries ElevenLabs and then the configured official Gemini key with `gemini-2.5-flash-preview-tts`, preserving subtitles, completion callbacks, route order, segmented progress, and replay.
- For unlimited local speech, run `Tools/LocalTtsServer/Setup-Chatterbox.cmd` once and `Start-Chatterbox.cmd` before Unity. The default endpoint is `http://127.0.0.1:8880/v1`; generated WAV files are cached under the ignored `.local-tts/` directory.
- Press `Test Configured Voice` in Setup and confirm audible speech before entering the room. The route prefers local Chatterbox, then falls back to ElevenLabs and official Gemini TTS.
- A valid ElevenLabs Voice ID is entered; the default is the voice used by the current ElevenLabs API example.
- The device has internet access to `api.elevenlabs.io` and audio output is audible.
- The generated story contains each selected `meaning (Spanish word)` exactly once as a route item.
- The room has enough distinct anchors for the selected words.
- Desktop or VR navigation works on the study machine.
- A 20-minute timing method is ready.
- A complete test session can be exported before recruiting participants.
- The optional all-picture room display can be entered, finished, and skipped, and its choice/duration appears in the JSON export.

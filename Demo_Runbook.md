# Memory Palace Experiment Runbook

Last verified: 2026-07-31

The active protocol is the four-condition 2 × 2 design described in
`EXPERIMENT_FLOW_2X2.md`.

## Conditions

1. Self-room × Self-story
2. Self-room × LLM-story
3. Default-room × Self-story
4. Default-room × LLM-story

All four conditions use the same vocabulary, furniture-word mapping, HMD study,
immediate post-test, questionnaire, and export implementation. They vary only the
room source and story source.

## Quick start

1. Open the project in Unity `6000.3.12f1`.
2. Open `Assets/Scenes/SampleScene.unity` and enter Play mode.
3. Enter the participant ID and select one of the four conditions.
4. Confirm the formal word pool. Formal sessions shuffle the 32-word candidate
   pool with a recorded seed and screen until eight unknown learning words are found.
5. Start the session.
6. For a self-room condition, create the participant's familiar room. For a
   default-room condition, let the participant explore the fixed room using WASD
   and right-mouse look.
7. Complete the adaptive Spanish-to-English pre-test. It gives no feedback and
   continues until eight unknown words are found or all 32 candidates are exhausted.
8. For a self-story condition, write one continuous story and classify its
   sentences to the target words. For an LLM-story condition, wait for three full
   stories and select one.
9. Map every word to one unique furniture item on PC.
10. Wait for all TTS clips to finish preparing.
11. Put on the HMD and complete the ten-minute memory-palace study.
12. Complete the immediate Spanish-to-English post-test and questionnaire.
13. Export the JSON and CSV files.

## Meta Quest Pro VR check

For Quest Link testing, connect the headset and enter the Meta Quest Link environment
before pressing Play. If Play starts first, the Oculus OpenXR runtime can return
`XR_ERROR_FORM_FACTOR_UNAVAILABLE`; stop Play, connect Link, and start Play again.

At the VR ready gate, verify all of the following before handing the headset to a
participant:

- A fixed object remains stable when the head turns or tilts; head translation is 1:1.
- The blue left-controller proxy and orange right-controller proxy follow the two
  Touch Pro controllers in position and rotation.
- The left stick moves, the right stick snap-turns, and either trigger operates the ray.
- Pressing A or X starts learning even when the controller ray is not over the button.

Stop immediately if the world follows the head, head motion appears reversed, or either
controller remains at the origin. Do not continue a study session with incorrect tracking.

## Timing

| Phase | Protocol target | Runtime behavior |
|---|---:|---|
| Room creation/exploration | 10 min | timer shown; researcher may end early |
| Spanish pre-test | 5 min | target shown; continues until 8 unknown words are found |
| Story writing/generation and choice | 10 min | timer shown |
| Furniture-word mapping | 3 min | timer shown; all mappings required |
| Audio generation | 5–8 min expected | waits for actual TTS completion |
| HMD study | 10 min | post-test locked until 10 min |
| Immediate post-test | 5 min | hard timeout |
| Questionnaire | 5 min | timer shown |

Actual phase durations are written to the JSON export.

## LLM and speech setup

Recommended Ollama settings:

- Endpoint: `http://localhost:11434/api/generate`
- Model: `gemma3:12b`

Recommended Gemini story model:

- `gemini-2.5-flash`

The LLM is used only in conditions 2 and 4. Each LLM condition generates three
complete story candidates using the existing causal-plan and story-writing prompts.
The participant's selected candidate is preserved unchanged for the later route.

All 32 Spanish word-pronunciation clips are generated in advance with Azure's
`es-ES-ElviraNeural` voice and bundled under `Resources/WordAudio`. Study playback
never synthesizes Spanish words at runtime. Start
`Tools/LocalTtsServer/Start-Azure.cmd` before a session so the PC-local Azure proxy
can synthesize English guides and stories; it retains the Azure key only in the
terminal process and marks formal Spanish words in an English sentence as `es-ES`.
Test the prepared word files and configured sentence voice before the session.

## Assessment and export

The adaptive pre-test screens the candidate pool to select eight unknown words. The
post-test begins only after the ten-minute HMD phase. CSV assessment rows use the
post-test phase IDs; pre-test exports retain only the screened count and random seed.

Exports are written to `ExperimentExports/`:

- `session_<participant>_<timestamp>.json`
- `session_<participant>_<timestamp>.csv`

The JSON contains condition factors, actual phase durations, pre-test screened count
and random seed, the final eight learning items, all three LLM candidates when
applicable, the chosen candidate index, final story, furniture mapping, post-test
responses, questionnaire, and event log. Individual candidate answers are not exported.

## Pre-run checklist

- Unity Console has no C# compile errors.
- The room has at least eight eligible furniture anchors.
- Every selected word has a usable image under `Assets/Resources/WordImages/`.
- Ollama or Gemini works before an LLM-story session.
- `Assets/Resources/WordAudio/` contains one playable WAV for every formal-pool word.
- `Tools/LocalTtsServer/Start-Azure.cmd` is running and `http://127.0.0.1:8880/health` reports `backend: azure` and `status: ok`.
- Desktop room navigation and HMD navigation both work.
- A complete dry run reaches the questionnaire and produces both export files.
- The exported condition, `roomSource`, `storySource`, pre-test screened count and
  seed, final eight items, post-test rows, and selected LLM candidate index are correct.

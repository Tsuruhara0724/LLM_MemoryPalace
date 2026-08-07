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
4. Confirm the formal word pool. Formal sessions sample eight distinct words from
   the 12-word pool.
5. Start the session.
6. For a self-room condition, create the participant's familiar room. For a
   default-room condition, let the participant explore the fixed room using WASD
   and right-mouse look.
7. Complete the Spanish-to-English pre-test. It gives no feedback and ends after
   five minutes.
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
| Spanish pre-test | 5 min | hard timeout |
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

Local Chatterbox/Kokoro speech is preferred when enabled. ElevenLabs and Gemini TTS
remain fallbacks. Test the configured voice before the session.

## Assessment and export

The pre-test and immediate post-test both measure Spanish-to-English meaning recall.
The post-test begins only after the ten-minute HMD phase. CSV rows use phase IDs
`pre_test` and `final_word_meaning`.

Exports are written to `ExperimentExports/`:

- `session_<participant>_<timestamp>.json`
- `session_<participant>_<timestamp>.csv`

The JSON contains condition factors, actual phase durations, pre-test responses,
all three LLM candidates when applicable, the chosen candidate index, final story,
furniture mapping, post-test responses, questionnaire, and event log.

## Pre-run checklist

- Unity Console has no C# compile errors.
- The room has at least eight eligible furniture anchors.
- Every selected word has a usable image under `Assets/Resources/WordImages/`.
- Ollama or Gemini works before an LLM-story session.
- The configured TTS voice is audible and its service is available.
- Desktop room navigation and HMD navigation both work.
- A complete dry run reaches the questionnaire and produces both export files.
- The exported condition, `roomSource`, `storySource`, pre-test rows, post-test rows,
  and selected LLM candidate index are correct.

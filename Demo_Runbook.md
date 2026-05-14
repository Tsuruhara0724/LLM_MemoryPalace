# Memory Palace Demo Runbook

## Quick start

1. Open Unity and wait for scripts to finish compiling.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Press `Play`.
4. The runtime bootstrap will create the full experiment UI automatically.

## Room source

- The study room is now loaded from `Assets/Resources/MemPalaceRoomSpec.json`
- Think of it as a `SceneFoundry-lite` offline room specification:
  - room metadata
  - camera poses
  - environment primitives
  - anchor furniture
- The setup screen can now call Ollama to generate a new room spec from a layout type and text description.
- `Open Room Builder` lets you add, select, move, rotate, scale, delete, and save furniture anchors before the mnemonic stage.

## Demo path for tomorrow

### Option A: Local Ollama path
- Keep `Condition` on `LLM Generated`
- Enter `Ollama Endpoint`
- Enter `Model`
- Optional: choose `Studio / 1K / 1DK / 1LDK`, edit the room description, and click `Generate Room From Description`
- Optional: click `Open Room Builder` to adjust furniture anchors, then `Use This Room`
- Pick `Set Alpha`
- Or click `Random Advanced 8 Words` to probe how well generated scenes fit harder vocabulary
- Click `Generate Mnemonics and Preview Session`
- Click `Enter Study Room`
- Use `right mouse drag` + `WASD`
- Click mnemonic objects to open the detail panel
- Click `Capture Memory Snapshot` for at least 3 words
- Click `Start Mid Test`
- Pick the matching image from 1 correct + 2 distractors
- Return to the room, capture the remaining words, then start the final test
- After each final answer, the camera jumps back to the correct anchor
- Fill questionnaire sliders
- Click `Finish Session and Export`

Recommended defaults on this machine:
- Endpoint: `http://localhost:11434/api/generate`
- Model: `qwen3:8b` or `gemma3:12b`

### Option B: Self-generated condition
- Switch `Condition` to `Self Generated`
- Click `Open Self-Authoring Workspace`
- Optionally click `Auto-Fill Starter Drafts`
- Edit anchors, cues, and mnemonics
- Click `Enter Study Room`

## Controls

- `Right mouse drag`: look around
- `WASD`: move
- `Q / E`: move down / up
- `Left click`: inspect mnemonic object
- `Shift`: move faster

## Export location

Exports are written to:

`<project root>/ExperimentExports/`

Files created:
- `session_<participant>_<timestamp>.json`
- `session_<participant>_<timestamp>.csv`

## Notes

- The current room and UI are generated entirely at runtime.
- The room geometry is no longer hardcoded in the controller; it is driven by the offline room spec JSON.
- Generated scenes and mnemonic links are now shown bilingually in English and Japanese.
- Generated mnemonic items can now include small Unity primitive visual props, so the study scene is no longer text-only.
- The study flow is now `setup -> preview -> study + snapshot capture -> mid image test -> final image test -> questionnaire -> export`.
- A non-blocking Burst warning may still appear in the Unity log; the experiment scripts themselves compiled successfully in the editor log.

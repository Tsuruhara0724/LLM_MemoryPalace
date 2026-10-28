# VR click-to-learn

Updated: 2026-10-28

## Participant flow

1. Follow the existing guide to the next assigned furniture. The existing approximately 2.2 m proximity and facing checks still highlight it, but do not reveal a word/image or start its narration.
2. Point the right controller at the **furniture model** and press a trigger. Selection respects distance, the first-pass word order, and solid occluders; the hidden word-image marker is not the click target.
3. The selected word opens a fixed-position learning card containing the word, meaning, image, story, pronunciation, replay, snapshot, and Next controls. It cannot switch to another furniture because the participant looks away.
4. Joystick translation and snap turning pause during the card. Native head orientation/position tracking stays enabled. Amplified physical walking also pauses; natural leaning/head motion stays 1:1. No camera teleport or forced rotation is applied.
5. A (right) / X (left) captures the selected furniture view. The existing clean-snapshot renderer disables all scene canvases, including the new learning card, before capturing the room.
6. Next remains disabled until the current word/story narration finishes. Then press the trigger or the **Next furniture** button to close the card, restore furniture search, and begin the next guide. Recenter the sticks before moving again. Snapshot requirements for finishing the whole study are unchanged.

No-voice/debug mode and audio failures expose **Back to room**, preventing a soft lock. Tracking loss temporarily hides the card and restores the same selected target when tracking returns. Restart, route-segment navigation, study exit, and runtime cleanup also clear the focus state.

## View and UI

“Fixed view” means a stable learning target and paused artificial locomotion, **not freezing the headset's tracked camera**. The participant can still naturally turn or lean. Disabling orientation/position tracking can itself cause discomfort; see [Meta's head tracking guidance](https://developers.meta.com/vr/design/head-best-practices/).

The learning card uses a top-level **Screen Space - Camera** canvas with an explicit foreground UI shader (`ZTest Always`). It is not attached to the furniture, has no occlusion-position solver, and is not depth-clipped by room geometry. Existing global HUD panels are hidden while the card is open to avoid duplicate words/subtitles. The research room stays visible around the card.

Headset stereo readability and comfort still require observation in the participant setup; passing code/state checks is not evidence of reduced simulator sickness.

## Implementation

- `Assets/Scripts/Runtime/MemoryPalaceExperimentController.VrStudyFocus.cs`: click eligibility, focus lifecycle, input press edges, navigation lock/recenter, actual-furniture raycast, and screen-space learning card.
- `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs`: selection, narration, locomotion, HUD/pointer integration, stage exit, and cleanup hooks.
- `Assets/Resources/Shaders/VrStudyOverlay.shader`: depth-independent learning graphics, including font texture support and stereo instance macros.
- `Assets/Editor/VrStudyFocusChecks.cs`: isolated EditMode checks; no Play Mode or XR initialization.

Desktop auto-inspection, Builder, Writing, Matching, story prompts, word/anchor assignments, TTS backend, scene tuning, vignette modes, and session-package schema are not changed by this interaction revision. Two runtime interaction events (`vr_study_focus_enter`, `vr_study_focus_exit`) record entry/exit without adding a new experimental condition.

The earlier `ParticipantGuides/VR_Learning_Guide_EN_JA.png` shows the old gaze-to-learn flow. Do not use its FIND/LISTEN panels to describe the new click-to-learn interaction without updating that participant handout.

## Verification

2026-10-28: 54 click-to-learn checks, 109 participant-story checks, and 19 Builder shortcut checks passed in the isolated Unity project. The click-to-learn checks include a D3D11 offscreen render with a nearer opaque occluder and an actual call to the clean-snapshot renderer: the learning card remains readable over scene geometry, is omitted from the saved-view texture, and is restored afterwards. Layout preview: `.utmp/vr-click-study-preview.png`. This is an editor fixture, not a headset/production-room screenshot. No Quest run was performed.

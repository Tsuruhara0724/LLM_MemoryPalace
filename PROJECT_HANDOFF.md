# Project Handoff: LLM Memory Palace

Last updated: 2026-05-15

This document is the restart point for a new Codex/Claude/API session after chat history is lost. Read this first, then inspect the files mentioned below before editing.

## Current Repository State

- Workspace: `C:\E\UnityProjects\Naist\LLM_MemoryPalace\MemPalaceLLM`
- GitHub remote: `https://github.com/Tsuruhara0724/LLM_MemoryPalace.git`
- Branch: `main`
- Working tree at handoff time: clean
- Recent commits:
  - `c473f24 Set generated L-shape room as default`
  - `01abe65 Add room-scale VR touch interaction`
  - `91da708 Fix VR input compile errors`
  - `5bf3069 Save Unity memory palace baseline`

## Research/System Goal

The project is an experimental Unity system for studying LLM-supported memory palace construction.

Core research framing:

- The system should not merely match words to existing objects.
- It should assign each learning item to a stable room anchor, then use an LLM to generate an overlay cue scene and a short memory story at that anchor.
- `anchor` and `mnemonic cue` are separate layers.
- Anchor examples: desk, window, shelf, fridge, door.
- Cue examples: imagined/overlaid scene, virtual object, small action, story gesture.
- The research question is roughly: can an LLM act as a mnemonic scaffold for automatically proposing location-image-story mappings in a fixed/generated space, lowering memory palace construction burden while preserving memory performance?

Current desired participant experience:

- Desktop/PC side is used for setup, room creation/preview, LLM generation, and experiment preparation.
- VR/Quest side is used for the in-room study phase.
- In VR, movement should be physical room-scale walking, not joystick locomotion.
- In VR, interaction should be near-hand touch with mnemonic markers/cue props, not ray interaction.

## High-Level Flow

Existing experiment flow:

```text
Setup
Room Builder / Preview
Mnemonic Generation or Self Authoring
Study Room
Mid Image-Choice Test
Final Image-Choice Test
Questionnaire
Export
```

Desktop flow currently works in Unity Editor. The next major task is to support Quest standalone APK receiving a generated session package over the local network, so users do not need Meta Link or repeated APK rebuilds after every room/word change.

## Important Files

- `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs`
  - Main runtime controller and UI.
  - Contains setup UI, room builder, generation flow, study flow, tests, export, VR runtime, and many helper methods.
  - This file is large and somewhat "god class" style. Make minimal, well-scoped edits.

- `Assets/Scripts/Data/ExperimentModels.cs`
  - Serializable data models for experiment stage/condition, word sets, mnemonic items, questionnaire, responses, exports.
  - `MnemonicItemData` is the generated/authored mnemonic item used by study room.

- `Assets/Scripts/Data/RoomSpecModels.cs`
  - Serializable room model.
  - `RoomSpecDefinition` contains metadata, cameras, environment primitives, and anchors.
  - `RoomSpecCatalog.CurrentRoom` is the active room.
  - `RoomSpecCatalog.SetCurrentRoom(room)` replaces the active room.
  - `RoomSpecCatalog.ReloadResourceRoom()` reloads `Assets/Resources/MemPalaceRoomSpec.json`.

- `Assets/Scripts/Services/OllamaLlmService.cs`
  - Local Ollama generation service.
  - Existing endpoint default used by UI: `http://localhost:11434/api/generate`.
  - Current model field has used values like `qwen3:8b`.

- `Assets/Scripts/Services/MockMnemonicGenerator.cs`
  - Fallback/mock mnemonic generation.

- `Assets/Resources/MemPalaceRoomSpec.json`
  - Current default room resource.
  - As of commit `c473f24`, this is the saved generated L-shape room.
  - Current room name: `Guided L-Shape Memory Room`.
  - Parsed check at handoff time: 39 environment primitives, 13 anchors.

- `Assets/Resources/MemPalaceDemoData.json`
  - Demo word sets and sample mnemonic data.
  - Currently changed toward Spanish medium noun vocabulary, with English and Japanese definitions.

- `Demo_Runbook.md`
  - Older runbook for desktop demo path.
  - Some wording may be stale, e.g. "Random Advanced"; current UI should be Spanish nouns.

- `Packages/manifest.json`
  - Uses Unity Input System, URP, UGUI, XR modules.
  - Before Quest standalone build, verify XR Plug-in/OpenXR/Android settings in Unity Editor, because package manifest alone does not prove Quest build is fully configured.

## Current Default Room

The user saved a generated room:

```text
GeneratedRooms/room_20260515_120949.json
```

That JSON has been copied into:

```text
Assets/Resources/MemPalaceRoomSpec.json
```

So project startup and `Reload Default Room` should now load the generated L-shape room by default.

## Current Vocabulary Direction

The user wanted to replace advanced English vocabulary with Spanish medium-difficulty concrete nouns, because abstract advanced English words caused overly abstract mnemonic generation.

Definitions should remain bilingual:

- English meaning
- Japanese meaning

Target examples from earlier work:

- `mochila`, `espejo`, `puente`, `maleta`, `moneda`, `cuchara`, `mercado`, `biblioteca`
- `ventana`, `alfombra`, `llave`, `reloj`, `cuaderno`, `vecino`, `castillo`, `camino`

If editing word sets, keep words concrete enough that mnemonic cues are visual and object/action-focused.

## Current LLM/Mnemonic Design Direction

The user was unhappy with generated images and mnemonics that missed the memory point. The intended design is:

- Word does not search for a matching existing object.
- Word is assigned to an anchor.
- LLM then creates a vivid overlay cue and story at that anchor.
- `Scene / Image Scene` should provide background spatial staging.
- `Cue Story / Memory Link` should define the foreground memory point.
- Image generation should use the scene as background and the cue story as the foreground focal object/action.
- Avoid whole-room, interior-design, empty-window, generic architecture images.

Prompts were already adjusted in `MemoryPalaceExperimentController.cs` to emphasize close-up foreground mnemonic detail. If future image quality is poor, inspect methods around image prompt building, especially text similar to:

```text
foreground mnemonic detail dominates the frame
no room overview
Cue Story the clear foreground focus
```

## Current VR Behavior

VR Study runtime has been added inside `MemoryPalaceExperimentController.cs`.

Current intended behavior:

- Desktop setup remains unchanged.
- After entering Study Room with VR mode enabled, XR head pose controls camera pose.
- Movement is room-scale physical walking only.
- No joystick locomotion.
- No controller ray selection.
- Left/right XR hand positions drive transparent touch proxies.
- `Physics.OverlapSphere` near each hand detects `StudyInteractable`.
- Touching a mnemonic marker or cue prop selects it.
- Pressing `A` / `Grip` while touching captures/replaces a memory snapshot.

Relevant methods to inspect:

- `SetupVrStudyRuntime`
- `HandleVrStudyRuntime`
- `UpdateVrHeadPose`
- `HandleVrTouchInteraction`
- `BuildVrTouchHands`
- `CreateVrTouchProxy`
- `UpdateVrTouchHand`
- `FindNearestVrTouchedInteractable`
- `CheckVrTouchCandidates`
- `UpdateVrTouchProxyColor`
- `SelectStudyItem`

Known compile issue already fixed:

- `InputDevice` and `CommonUsages` were ambiguous between Input System and XR.
- Current code explicitly uses `UnityEngine.XR.InputDevice`, `UnityEngine.XR.InputDevices`, `UnityEngine.XR.CommonUsages`, and `UnityEngine.XR.InputFeatureUsage<bool>` where needed.

## Quest/Hardware Context

The user uses Meta Quest Pro.

Important history:

- Initial USB-C Link had `USB-C disabled / water/debris` warning.
- Factory reset and updates were painful.
- Eventually cable connection worked.
- Horizon app had account/pairing issues, especially with a China-region phone.
- User managed to log in using another account/device.
- User now wants standalone Quest operation for participant testing, not Meta Link.

Development decision:

- Continue using Unity Editor + Meta Link for fast development when convenient.
- But participant testing should use Quest standalone APK and wireless/local session package transfer.

## Why Standalone Needs A Session Package

If using Meta Link/Editor, PC memory state is available directly.

If building Quest standalone APK, the APK cannot see the Editor's current room/mnemonic state unless it is packaged or downloaded.

Rebuilding the APK every time is too slow. Therefore the next architecture should be:

```text
PC Editor:
Build/edit room -> Generate mnemonic items -> Export/host session package JSON

Quest APK:
Download/import session package JSON -> Set active room -> Set currentItems -> Enter VR Study
```

The user prefers this over rebuilding.

## Next Major Task: Wireless Session Package Workflow

Implement minimal PC-to-Quest package import/export.

Do not redesign the whole app. Add a small data bridge.

### Recommended Data Model

Add a serializable class, likely in `ExperimentModels.cs` or a new data file:

```csharp
[Serializable]
public class VrSessionPackage
{
    public string packageVersion;
    public string exportedAtUtc;
    public string sessionId;
    public string participantId;
    public ExperimentCondition condition;
    public string wordSetId;
    public string wordSetName;
    public RoomSpecDefinition roomSpec;
    public List<MnemonicItemData> mnemonicItems = new();
}
```

Unity `JsonUtility` supports serializable classes and lists when wrapped inside a class. It should work here because `RoomSpecDefinition` and `MnemonicItemData` are already serializable.

### PC/Desktop Side

Add UI controls, probably on Setup or Generation screen:

- `Export VR Session Package`
- optionally a text field showing output path

Export should:

- Validate `RoomSpecCatalog.CurrentRoom != null`
- Validate `currentItems.Count > 0`
- Create `VRSessionPackages/`
- Write `session_package_latest.json`
- Optionally write timestamped copy like `session_package_yyyyMMdd_HHmmss.json`
- Include current room and current mnemonic items.

Expected output path:

```text
<project root>/VRSessionPackages/session_package_latest.json
```

Manual hosting for first milestone:

```text
cd C:\E\UnityProjects\Naist\LLM_MemoryPalace\MemPalaceLLM\VRSessionPackages
python -m http.server 7777
```

Quest URL:

```text
http://<PC_LOCAL_IP>:7777/session_package_latest.json
```

Do not implement a built-in HTTP server in the first pass unless necessary. External Python server is enough to validate the architecture.

### Quest/Standalone Side

Add UI controls:

- Package URL text field
- `Download VR Session Package`
- status message showing loaded room name and item count

Download routine should:

- Use `UnityWebRequest.Get(url)`
- Parse JSON into `VrSessionPackage`
- Validate `roomSpec` and `mnemonicItems`
- Call `RoomSpecCatalog.SetCurrentRoom(package.roomSpec)`
- Set `currentItems = EnsureMnemonicDefaults(package.mnemonicItems)`
- Call `ResetSessionState()` where appropriate, but be careful not to clear `currentItems` after assigning them.
- Reassign anchors only if necessary. Ideally package items should already reference package room anchors.
- Allow `EnterStudyRoom()` after package load.

Potential method names:

- `ExportCurrentVrSessionPackage()`
- `DownloadVrSessionPackageRoutine(string url)`
- `ApplyVrSessionPackage(VrSessionPackage package)`

### Important Caution

`ResetSessionState()` currently may clear state including `currentItems` depending on implementation. Inspect before calling. If it clears `currentItems`, reset first, then assign package items.

### First Milestone Acceptance Criteria

On PC Editor:

1. Build/choose default room.
2. Generate mnemonic items.
3. Click `Export VR Session Package`.
4. Confirm JSON exists under `VRSessionPackages/session_package_latest.json`.

On Quest APK:

1. Launch app standalone.
2. Enter package URL.
3. Click download.
4. App reports room and item count.
5. Enter Study Room.
6. See reconstructed room and mnemonic markers.
7. Physical walking and near-hand touch work.

## Later Optional Task: Built-In PC Host

After URL import/export works, optional improvement:

- Add PC-side lightweight HTTP server inside Unity Editor/Windows build.
- Button: `Host Current VR Package`.
- Show local IPs and URL.

This is not first milestone. Avoid overcomplicating until the Quest import works.

## Current Desktop Demo Usage

Rough Editor path:

1. Open Unity project.
2. Press Play.
3. Setup participant/condition.
4. Default room should now be L-shape room.
5. Generate Spanish noun mnemonics with local Ollama.
6. Enter Study Room.
7. Desktop fallback controls:
   - right mouse drag: look
   - WASD: move
   - Q/E: vertical
   - left click: inspect mnemonic object
8. VR mode can be enabled in setup with `Use VR study runtime after entering the room`.

For local LLM:

- Endpoint: `http://localhost:11434/api/generate`
- Model examples: `qwen3:8b`, possibly `gemma3:12b`

Image cue generation:

- UI has Stable Diffusion WebUI endpoint field, default seen earlier: `http://127.0.0.1:7860/sdapi/v1/txt2img`
- Image checkpoint field optional.
- Image quality has been problematic; keep mnemonic foreground focus strong.

## Unity/Build Notes

Known limitations at handoff:

- Terminal did not have `dotnet` available, so command-line C# build was not possible.
- Use Unity Editor Console as source of truth for compile errors.
- After C# edits, let Unity recompile and check Console.
- If targeting Quest standalone, verify Android Build Support, OpenXR/Meta XR setup, and Quest runtime settings in Unity. Do not assume this is complete from scripts alone.

Package manifest currently includes:

- `com.unity.inputsystem`
- `com.unity.render-pipelines.universal`
- `com.unity.ugui`
- Unity XR modules

But explicit OpenXR/XR Plug-in package/settings should be checked in the Editor before standalone Quest build.

## Git/Workflow Rules For Future Agent

- The repo may be clean at handoff, but always run `git status --short` before edits.
- Do not revert user changes.
- Use small commits after stable milestones.
- Existing remote is configured; pushing may require approval in Codex environment.
- Recent commits are safe restore points.

Suggested next commit names:

- `Add VR session package model`
- `Export VR session package`
- `Import VR session package from URL`

## Immediate Next Steps For A New Agent

1. Run:

```powershell
git status --short
git log --oneline -5
```

2. Inspect:

```text
Assets/Scripts/Data/ExperimentModels.cs
Assets/Scripts/Data/RoomSpecModels.cs
Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
```

3. Implement `VrSessionPackage`.

4. Add PC export button and method.

5. Add URL download/import UI and coroutine.

6. Test in Unity Editor first by exporting and importing from `http://localhost:7777/session_package_latest.json` or a local file-equivalent if convenient.

7. Then build Quest APK and test download from PC local IP.

## A Good Prompt To Continue Work

If handing this to another coding agent, use:

```text
Read PROJECT_HANDOFF.md first. Then implement the first milestone of the wireless Quest standalone workflow: add a serializable VR session package containing RoomSpecDefinition and List<MnemonicItemData>, add PC-side export to VRSessionPackages/session_package_latest.json, and add Quest/runtime UI to download a package from URL using UnityWebRequest, apply RoomSpecCatalog.SetCurrentRoom(package.roomSpec), set currentItems, and allow entering Study Room. Keep existing desktop workflow, LLM generation, default room, and VR room-scale touch behavior unchanged.
```


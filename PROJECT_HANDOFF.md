# Project Handoff: LLM Memory Palace

Last updated: 2026-05-15

This document is the restart point for a new Codex/Claude/API session after chat history is lost. Read this first, then inspect the files mentioned below before editing.

## Latest Session Summary

This handoff supersedes part of the older "next major task" section below.

What was completed in the latest session:

- Implemented the first-pass wireless Quest session package workflow in code.
- Added `VrSessionPackage` model in `Assets/Scripts/Data/ExperimentModels.cs`.
- Added PC-side export UI and logic in `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs`.
- Added runtime/Quest-side URL download UI and package import logic in `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs`.
- Added a safe `ResetSessionState(bool stopCoroutines = true)` overload so package import can reset session state without killing the currently running download coroutine.
- Default package URL in code now points to the USB/ADB reverse test path:
  - `http://127.0.0.1:7777/session_package_latest.json`
  - The setup UI also has quick buttons for `Use USB Test URL` and `Use Campus PC URL`.

Current blocker:

- Android toolchain is repaired through a user-writable toolchain folder and batch APK builds now succeed.
- A Quest Pro was detected by ADB after developer mode was enabled, and the first APK install succeeded.
- Unity/ADB later restarted the ADB server, so the headset is currently `unauthorized` again until the user accepts `Allow USB debugging` in the headset.
- After authorization, reinstall `Builds\MemPalaceLLM.apk`, restore `adb reverse tcp:7777 tcp:7777`, launch `jp.naist.MemPalace`, and test package download/import.

Very important collaboration notes:

- The user prefers communication in Chinese.
- Do not revert the Unity-generated settings/assets unless you understand why they changed.
- The working tree is dirty now; see the updated repository state below.

## Current Priority

The current highest-priority task is no longer "implement session package export/import" because that code is already in place.

The current priority is:

1. Re-authorize Quest Pro USB debugging if ADB shows `unauthorized`.
2. Install the latest verified VR APK from `Builds\MemPalaceLLM.apk`.
3. Restore `adb reverse tcp:7777 tcp:7777` for the USB package-download demo path.
4. Launch the APK and test downloading/importing `VRSessionPackages/session_package_latest.json`.

## Current Repository State

- Workspace: `C:\E\UnityProjects\Naist\LLM_MemoryPalace\MemPalaceLLM`
- GitHub remote: `https://github.com/Tsuruhara0724/LLM_MemoryPalace.git`
- Branch: `main`
- Working tree is currently dirty. Important modified/untracked paths include:
  - `Assets/Scripts/Data/ExperimentModels.cs`
  - `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs`
  - `ProjectSettings/ProjectSettings.asset`
  - `ProjectSettings/EditorBuildSettings.asset`
  - `Packages/manifest.json`
  - `Packages/packages-lock.json`
  - `Assets/XR/`
  - `Assets/Settings/Build Profiles/`
  - `VRSessionPackages/`
  - several Unity-generated URP/XR/settings assets
- Recent commits:
  - `c473f24 Set generated L-shape room as default`
  - `01abe65 Add room-scale VR touch interaction`
  - `91da708 Fix VR input compile errors`
  - `5bf3069 Save Unity memory palace baseline`

## Exact Code Changes Already Made

These are the key code entry points already added and should not be re-implemented from scratch:

- `Assets/Scripts/Data/ExperimentModels.cs`
  - `VrSessionPackage` at around line 110

- `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs`
  - default URL field:
    - `private string vrSessionPackageUrl = VrSessionPackageAdbReverseUrl;`
  - package URL constants:
    - `VrSessionPackageAdbReverseUrl = "http://127.0.0.1:7777/session_package_latest.json"`
    - `VrSessionPackageCampusPcUrl = "http://163.221.38.241:7777/session_package_latest.json"`
  - Setup screen package UI around line 760+
  - Generation screen export UI around line 2050+
  - methods:
    - `ExportCurrentVrSessionPackage()`
    - `BeginVrSessionPackageDownload()`
    - `DownloadVrSessionPackageRoutine(string url)`
    - `ApplyVrSessionPackage(VrSessionPackage package)`
    - `BuildWordSetFromPackage(VrSessionPackage package)`
  - `ResetSessionState(bool stopCoroutines = true)` overload

Current exported package files already present:

- `VRSessionPackages/session_package_latest.json`
- `VRSessionPackages/session_package_20260515_143819.json`
- `VRSessionPackages/session_package_20260515_144803.json`

## Current Unity / Quest Build State

Unity version:

- `6000.3.12f1`

What happened in this session:

- Android Build Support initially looked partially installed/broken in Unity Hub.
- Unity's AndroidPlayer install under Program Files still lacks embedded `OpenJDK`, `SDK`, and `NDK`, but Unity Hub cached module ZIPs were extracted into:
  - `C:\Users\cheny\AppData\Local\UnityAndroidToolchains\6000.3.12f1`
- Batch build uses `Assets/Editor/CodexAndroidBuild.cs` to point Unity at that external JDK/SDK/NDK/Gradle toolchain.
- `Builds\MemPalaceLLM.apk` was built successfully.
- The final checked APK manifest includes:
  - `android.hardware.vr.headtracking`
  - `com.oculus.intent.category.VR`
  - `com.oculus.supportedDevices = quest|quest2|cambria|eureka|quest3s`
- `VRSessionPackages` is being served by a Python HTTP server on port `7777`; for current USB testing use:
  - `adb reverse tcp:7777 tcp:7777`
  - `http://127.0.0.1:7777/session_package_latest.json`
- Quest wireless network and PC Ethernet are on different campus subnets right now, so the old direct URL `http://163.221.38.241:7777/...` may not be reachable from the headset without routing/firewall changes.
- Quest Pro ADB was working as `device`, then Unity restarted ADB and it returned to `unauthorized`; the user needs to accept the USB debugging prompt again.

Current verified filesystem state of the Unity Android module:

```text
C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer
  Apk/
  Bee/
  Data/
  Documentation/
  Source/
  Tools/
  Variations/
  AndroidPlayerBuildProgram.exe
  ...
  Tools/gradle exists
  OpenJDK missing
  SDK missing
  NDK missing
```

There is also a temporary repair-download directory from an aborted automated recovery attempt:

```text
%TEMP%\unity_android_fix_6000.3.12f1
```

Observed files there include:

- `openjdk.zip`
- `sdktools.zip`
- `buildtools.zip`
- `cmdline.zip`
- `platform34.zip`
- `ndk.zip` (likely incomplete because the scripted recovery was user-aborted)

Do not assume those ZIPs are complete or correct without checking sizes/hashes.

## Current Recommended Next Steps For The Next Agent

Start here, not from scratch.

1. Read this handoff and confirm the working tree with:

```powershell
git status --short
```

2. Verify the current Unity Android toolchain filesystem state:

```powershell
Get-ChildItem "C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer"
```

3. Repair the missing `OpenJDK`, `SDK`, and `NDK` directories under the Unity AndroidPlayer install.
   - Likely options:
     - reinstall the missing submodules correctly via Unity Hub if possible
     - or manually download/extract the exact module payloads into the expected paths
   - The previous scripted attempt was interrupted by the user and should be treated as incomplete.

4. Re-open Unity and confirm in `Edit -> Preferences -> External Tools` that these are valid:

```text
C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK
C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK
C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK
```

5. In Unity:
   - `Build Profiles -> Android`
   - `Add Build Profile`
   - `Switch Platform`
   - verify `Assets/Scenes/SampleScene.unity` is in the scene list
   - verify `Player Settings` values for Android
   - verify `XR Plug-in Management -> Android -> OpenXR`

6. Then build/install to Quest and test the package workflow:
   - export package from Editor
   - host `VRSessionPackages` with:

```powershell
cd C:\E\UnityProjects\Naist\LLM_MemoryPalace\MemPalaceLLM\VRSessionPackages
python -m http.server 7777
```

   - run Quest standalone app
   - download `http://<PC_IP>:7777/session_package_latest.json`
   - enter study room from loaded package

## Important Caveat About The Older Sections Below

The older sections below still contain useful project background, but they are stale in two ways:

- They describe the wireless session package flow as not yet implemented. It has now been implemented in code.
- They say the working tree was clean. It is no longer clean.

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

2. Inspect code changes already made:

```text
Assets/Scripts/Data/ExperimentModels.cs
Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
```

3. Verify Unity Android toolchain filesystem state:

```text
C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer
```

Specifically confirm whether these exist:

```text
OpenJDK
SDK
NDK
Tools\gradle
```

4. Repair missing Android submodules (`OpenJDK`, `SDK`, `NDK`) under the Unity install.

5. Re-open Unity and verify `Edit -> Preferences -> External Tools` no longer reports missing JDK/SDK/NDK paths.

6. In Unity, verify Android build configuration:
   - Build Profiles
   - Player Settings
   - XR Plug-in Management / OpenXR
   - Scene list

7. Build/install Quest APK and test the already-implemented package flow using `VRSessionPackages/session_package_latest.json`.

## A Good Prompt To Continue Work

If handing this to another coding agent, use:

```text
Read PROJECT_HANDOFF.md first. The VR session package workflow is already implemented in code. Continue from the current blocker: Unity 6000.3.12f1 recognizes the Android platform again, but `OpenJDK`, `SDK`, and `NDK` are still missing under `C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer`, so Unity External Tools reports invalid paths and Quest build cannot proceed. Repair the Android submodules, verify External Tools paths, then build/install a Quest standalone APK and test downloading `VRSessionPackages/session_package_latest.json` into the headset app. Keep the existing desktop workflow, LLM generation, default room, and VR room-scale touch behavior unchanged.
```

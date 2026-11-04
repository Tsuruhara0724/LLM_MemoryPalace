# Developer Guide

(DEVELOPER_GUIDE.md) | English


## 1. Project overview

This is a Unity desktop / VR vocabulary-learning experiment. The PC handles room preparation, vocabulary screening, story writing or selection, and furniture matching. Quest handles spatial learning and subsequent assessments. The PC also supports development checks without a headset.

The current experiment has two factors:

| Setup condition | Room source | Story source |
| --- | --- | --- |
| `1 · Self room / Self story` | Participant-created familiar room | Participant-written English story |
| `2 · Self room / LLM story` | Participant-created familiar room | Participant selects a generated story |
| `3 · Example room / Self story` | Preset `example_room.json` | Participant-written English story |
| `4 · Example room / LLM story` | Preset `example_room.json` | Participant selects a generated story |

Older documents describe three conditions, baseline conditions, or 20-minute learning. Do not use them as instructions for this version. Current time targets are 10 minutes for the room, 5 for screening, 10 for the story, 3 for matching, and 10 for learning. These are not universal forced transitions. Screening may continue beyond five minutes until eight unknown words are found. Actual durations are recorded; researchers must manage timing according to the approved protocol.

## 2. Requirements and first launch

### Requirements

- Windows workflows are supplied as PowerShell and `.cmd` scripts.
- Unity Hub and **Unity 6000.3.12f1**; `ProjectSettings/ProjectVersion.txt` is the reference.
- Git is unnecessary for a ZIP handoff. It is needed only for authorized repository access. Preserve Unity `.meta` files; do not copy just the `.cs` files.
- Python **3.12** and the Python Launcher (`py`) for the Azure proxy environment. Local web services use the Python standard library.
- For Quest builds: install **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK** for the same Unity Editor version. Prepare a Quest with USB debugging enabled.

`Packages/manifest.json` and `packages-lock.json` pin dependencies, including URP, Input System, UGUI, XR Management, and OpenXR. Initial import requires access to the package registries. Do not casually upgrade the Editor or packages during a study.

### Optional Editor Burst JIT workaround

Some Windows security policies block dynamically generated Burst Editor JIT DLLs. Only if this occurs, use **Tools > Memory Palace > Compatibility > Disable Editor Burst JIT**. To enable it again, explicitly select **Enable Editor Burst JIT** in the same menu.

Both actions request confirmation. They change JIT in the current Editor and save a preference shared by Unity Editors under the same OS user, potentially affecting other projects. **Quest / Android Player Burst AOT settings are not changed.** Opening the project or reloading scripts does not change, enable, or restore this preference automatically. An existing disabled setting remains disabled.

### Obtaining the source and opening the scene

Use a reviewed `MemoryPalace-Source-*.zip` exported by the maintainer through `Tools/Export-CollaborationPackage.cmd`. Extract it into a new development directory. It excludes Git history, credentials, participant data, and research assets not approved for redistribution. Do not request or compress the maintainer's entire workspace.

Only collaborators authorized to access research data should use the full Git repository. **History still contains older participant data**; a cleaned current directory does not mean that history is anonymized:

```powershell
git clone <repository-url>
```

In Unity Hub, select **Add project from disk** and choose the directory containing `Assets/`, `Packages/`, and `ProjectSettings/`. If you received the outer workspace, the Unity project is its `MemPalaceLLM/` subdirectory.

Open `Assets/Scenes/SampleScene.unity`, the enabled entry scene in the current build settings. Wait for import and compilation, confirm that Console has no compilation errors, then enter Play Mode.

The scene controller is `MemoryPalaceExperimentController` on `MemoryPalaceExperiment`. `MemoryPalaceBootstrap` creates a missing controller after scene load. Do not add a second controller while troubleshooting.

The reviewed ZIP excludes commercial furniture, word-image PNGs, prepared WordAudio, and personal API configuration. It uses procedural furniture and blank images for development. It is not visually equivalent to the research machine and cannot run a complete formal learning session until materials are restored. See [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.en.md).

## 3. Installation and service startup

### Install the Azure proxy once

From the project root, double-click `Tools/LocalTtsServer/Setup-Azure.cmd`, or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\LocalTtsServer\Setup-Azure.ps1
```

This creates a Python 3.12 environment at `.local-tts/azure-venv/` and installs `fastapi` and `uvicorn`. Installation requires network access. If it fails, check `py -3.12 --version` and pip connectivity before reinstalling Unity.

### After each reboot

Run `Tools/Start-ProjectServices.cmd`, or:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1
```

The launcher reuses compatible running services and starts missing ones. It does not enter Play Mode, export a session package, or change firewall rules. Azure TTS opens a separate window: enter your key using hidden input, enter the resource region, and leave that window running.

| Service | Address / port | Required for |
| --- | --- | --- |
| Azure Speech proxy | `http://127.0.0.1:8880/health` | Story and guide narration |
| Quest session-package HTTP server | `http://127.0.0.1:8765/session_package_latest.json` | Quest fetching a package over Wi-Fi; USB transfer does not require it |
| Delayed-test web application | `http://127.0.0.1:8766/` | Follow-up testing, not a prerequisite for entering VR |
| Ollama | `http://127.0.0.1:11434/api/version` | Local models and diagnostics; not required by the current cloud story mode |
| ADB | Local port `5037` | Quest USB transfer, installation, and result retrieval |

The launcher also attempts to start Ollama. A missing Ollama installation does not mean cloud story generation is unavailable. Before the first package export, the package server may request a package; this does not prevent PC session preparation.

To inspect status without starting services:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1 -StatusOnly
```

A healthy TTS response must include both `status: ok` and `backend: azure`. This confirms proxy responsiveness, not an active Azure subscription, available credit, or synthesis permission. In Unity's Researcher settings, use **Test Configured Voice** for an actual synthesis check.

See [STARTUP.md](STARTUP.en.md) for individual commands. The standalone package server and delayed-test scripts both originally default to `8765`; when running both, pass `-Port 8766` to the delayed-test script. Stable Diffusion is not required by the current learning flow.

### Stopping services

- Azure TTS: press `Ctrl+C` in its window, then close it. The launcher does not persist the entered key. Ending the process does not revoke the Azure credential itself.
- Manually started web or package servers: press `Ctrl+C` in their terminals.
- Background services started by the launcher: record the printed PID, verify its command line, then use `Stop-Process -Id <confirmed-pid>`. Do not terminate all Python, PowerShell, or Unity processes.
- If Ollama runs through its desktop application, exit through its tray menu. ADB may be shared by other Android projects and need not be stopped each time.

Background logs are in `.utmp/project-services/`; Azure errors appear in its own window. No Windows startup task is installed by these scripts.

## 4. API configuration and credentials

### Story API

The UI labels are **Claude Haiku** and **GPT_Luna**. The service retains the historical filename `OllamaLlmService.cs`, but requests can use configured cloud endpoints. Its name does not imply that a local model must be running.

The following local Resources configuration files are supported and should remain Git-ignored. Supply your own endpoint, available model, and credentials.

Fields for `Assets/Resources/AnthropicLocalConfig.json`:

```json
{
  "apiKey": "<your-key>",
  "baseUrl": "<your-messages-endpoint>",
  "model": "<your-model>",
  "authMode": "x-api-key"
}
```

Use `x-api-key` or `bearer` according to your service. Local environment overrides are `ANTHROPIC_BASE_URL`, `ANTHROPIC_MODEL`, `ANTHROPIC_AUTH_TOKEN`, and `ANTHROPIC_API_KEY`; AUTH_TOKEN takes precedence over API_KEY.

Fields for `Assets/Resources/OpenAiLocalConfig.json`:

```json
{
  "apiKey": "<your-key>",
  "baseUrl": "<your-responses-endpoint>",
  "model": "<your-model>",
  "reasoningEffort": "low"
}
```

Overrides are `OPENAI_BASE_URL`, `OPENAI_MODEL`, and `OPENAI_API_KEY`. Launch Unity from a process that already has the variables. Changes in an external terminal do not automatically reach an already-running Editor.

**Resources is not a credential vault.** JSON files there can be included in builds; `.gitignore` does not stop Unity from embedding them in an APK. Use your own local configuration and review build inputs before sharing executables. Do not distribute an APK containing long-lived provider keys. Public or multi-device deployment needs researcher-controlled server-side forwarding and access control; the current local configuration does not provide that protection.

### Azure Speech

`Start-Azure.ps1` reads `AZURE_SPEECH_KEY` and `AZURE_SPEECH_REGION`, prompting if missing. It does not permanently save the entered key. Do not put keys in scripts, scenes, screenshots, or documentation. The region must match the Speech resource; `japaneast` is only a default.

The Azure key stays in the PC proxy and is not sent to Unity or Quest. The PC TTS base address is `http://127.0.0.1:8880/v1`; Quest needs `http://<PC-IP>:8880/v1`, not the headset's localhost.

The launcher binds TTS and the package server for LAN access. Use a trusted research network; do not forward these ports to the public internet. Speech caches in `.local-tts/azure-audio-cache/` may contain synthesized session content and must also be excluded from sharing.

## 5. Study flow and protocol constraints

1. **Setup:** enter Participant ID and select one of the four conditions. The formal vocabulary entry is **Formal 32-Word Pool**.
2. **Room:** Self room opens Builder. Example room uses `Assets/Resources/ExampleRooms/example_room.json` for PC familiarization. Builder requires at least **nine distinct valid furniture types**, not nine copies of one type. The learning target remains eight words.
3. **Spanish Pre-test:** screen the 32-word pool to find eight words unknown to the participant. Candidate order and the random seed are recorded. Do not bypass screening when fewer than eight unknown words are available.
4. **Story:** for Self story, write a complete English story, select **Split Story Into Sentences**, and check each sentence's target assignment. After editing, use **Refresh Sentence Split**. For LLM story, generate candidates and let the participant select one; furniture positions do not determine the text.
5. **Assign Words:** click an actual furniture model on PC, then select a word from the 2 × 4 cards. Each word maps to one furniture item and vice versa. Used words cannot be assigned twice. Use **Clear current assignment** to revise a mapping. Continue with **Finish Assignment And Enter VR Study**.
6. **VR Study:** complete Ready / **Start learning**, follow the route, click furniture to learn, and capture scene views as described below.
7. **Assessment and results:** complete the configured spatial-anchor, word-image, and meaning assessments, then save JSON / CSV. If using a later Google Form, link it with the same Session ID, not participant names.

Time targets do not guarantee enforced minimum durations in every stage. Do not bypass readiness checks, the first-pass route, narration completion, or snapshot requirements for convenience. Do not silently change scoring, word count, condition enum values, export fields, or version labels. Protocol changes require updated documentation, regression checks, and a frozen revision.

### Participant stories remain English-only

The offline inflection table supports common regular and irregular verbs, plurals, and comparative forms. After Split, the learning text can annotate the actual English form, for example `forgot (olvidar)`. Participants need not type Spanish. Ambiguous matches remain **Unassigned** for manual review; this is not a full semantic parser.

`originalParticipantStory` preserves the original input. Learning uses annotated `fullStory`, `storySegment`, and mnemonic text. Do not overwrite the original with the display version. Older packages still load but do not gain annotations automatically; re-Split and re-export them from PC. See [PARTICIPANT_STORY_ENGLISH.md](PARTICIPANT_STORY_ENGLISH.en.md).

### VR uses click-to-learn, not automatic proximity popups

- Approaching and facing the correct furniture still highlights it. Highlighting alone opens no card and starts no narration.
- Point the right-hand ray at the furniture model and press the trigger. Distance, route order, and occlusion checks must pass before the fixed-screen learning card opens.
- While learning, joystick translation, snap turning, and amplified physical walking pause. Native head position and orientation tracking remain active; the camera is not forcibly frozen in space.
- The left stick moves; horizontal input on the right stick produces 30-degree snap turns. Recenter the sticks after closing a card before movement resumes.
- Right-hand `A` / left-hand `X` captures a scene view with the learning UI hidden.
- The first-pass route cannot be skipped. **Next furniture** unlocks after narration. **Finish learning** requires route completion and a snapshot for every target.

See [VR_CLICK_TO_LEARN.md](VR_CLICK_TO_LEARN.md) for fallback, tracking-loss, and replay behavior. `Docs/ParticipantGuides/VR_Learning_Guide_EN_JA.png` describes the old automatic / gaze interaction and is not a current participant handout.

The fixed-screen card is an interaction change, not evidence of reduced simulator sickness. Researchers remain responsible for headset fitting, safe boundaries, and stopping on discomfort.

## 6. PC / Quest workflow and data transfer

### Prepare on PC, learn on Quest

1. Complete room preparation, screening, story selection, and furniture mapping on PC.
2. In Researcher settings, select **Write Quest Session Package** to create `VRSessionPackages/session_package_latest.json`.
3. Quest loads a local USB-transferred file or a Wi-Fi URL. Packages contain research content and are not public demo assets.
4. Before each participant, verify Participant ID, Session ID, room, story, and assignments. Do not reuse the previous cached package accidentally.

For USB transfer, enable USB debugging on Quest and confirm that `adb devices` lists an authorized device. `adb` must be on the terminal's PATH; Unity's SDK `platform-tools` directory can be added locally. Then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Push-VRSessionPackageToQuest.ps1
```

The script reads the Android package ID from Project Settings and copies the file to `/sdcard/Android/data/<package-id>/files/session_package_latest.json`. Install and launch the application at least once beforehand. After transfer, launch or restart it so Setup reads the local package. Use `-Device <serial>` for multiple devices and `-PackageId <id>` to specify the application explicitly.

For Wi-Fi, ensure PC and Quest can reach each other. Use the launcher's `http://<PC-IP>:8765/session_package_latest.json` address and allow only necessary ports on a trusted private network. Re-export in Unity after PC IP or TTS configuration changes. A successful PC health check does not establish Quest connectivity.

**Session packages update content and configuration, not C#, shaders, or built scenes.** Rebuild and reinstall the APK after application changes. Pushing JSON alone does not install new interaction code.

### Retrieving results

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Pull-QuestResultsToPC.ps1
```

The default destination is `QuestResultImports/<timestamp>/`, containing `ExperimentExports` and available `MemoryPalaceSessionData` screenshots. Verify and back up JSON, CSV, and screenshots under the research data policy before cleaning the device. The script does not implement a backup policy for you.

Editor JSON / CSV exports are in the project-root `ExperimentExports/`. PC builds place them in `ExperimentExports/` one level above their data directory. Android exports to `Application.persistentDataPath/ExperimentExports/`; snapshots are under the same persistentDataPath at `MemoryPalaceSessionData/<session>/SpatialSnapshots/`.

### Delayed testing

After service startup, open `http://127.0.0.1:8766/` and use the exact Session ID. Participant ID resolves to that participant's latest exported session, so verify the selected record. The current sequence is: first word test (typed English meanings only) → original study story → original furniture snapshot / Spanish-word matching (three choices) → second word test (typed English meanings only). Neither word test has an English multiple-choice round. No scores or correctness feedback appear until completion. The first test is saved before revealing the story; matching and post-review scores are stored separately and never replace baseline delayed-memory scores. Schema 3 marks recognition as not administered, not as a zero score; older result files remain unchanged.

The current protocol requires the original `storySession.fullStory`, furniture assignments, and `SpatialSnapshots` images. Missing materials block startup rather than producing replacement stimuli. On another computer, use `-SnapshotsDir` with the original session-folder structure. Restart Python after backend changes; use `-Port 8767` if keeping an older service running. Do not reload during a test: saved checkpoints remain on disk, but active attempts cannot resume after a page reload or server restart.

Researchers can run early or repeated tests, each saved separately. An available Start button does not establish that seven days have elapsed. Retrieved Quest files default to `QuestResultImports`, whereas the web application reads `ExperimentExports` by default. Select the correct source explicitly; the standalone launcher supports `-ExportsDir`. Do not assume retrieval makes files discoverable automatically. See [DelayedTestWeb/README.md](../Tools/DelayedTestWeb/README.md).

## 7. Directory and code map

| Location | Responsibility |
| --- | --- |
| `Assets/Scenes/SampleScene.unity` | Entry scene, saved Inspector parameters, editable Canvas |
| `Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs` | Controller entry after scene load |
| `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs` | Stages, Builder, screening, stories, matching, learning, assessments, exports |
| `MemoryPalaceExperimentController.SceneUi.cs` / `.SceneVrUi.cs` | Scene Canvas binding and display; same Runtime directory |
| `MemoryPalaceExperimentController.VrStudyFocus.cs` | Furniture selection, fixed learning card, learning-state locomotion gating |
| `MemoryPalaceExperimentController.VrComfort.cs`, `VrPeripheralBlur*` | Vignette, peripheral blur, rendering integration |
| `Assets/Scripts/Services/MnemonicStoryPrompts.cs` | Runtime prompt construction |
| `Assets/Scripts/Services/OllamaLlmService.cs` | Story requests, parsing, validation, repair, backend routing |
| `Assets/Scripts/Services/ParticipantStoryEnglish.cs` | English inflections and Spanish annotations |
| `Assets/Scripts/Services/ElevenLabsTextToSpeechService.cs` | Speech adapter, prepared word clips, Azure PC proxy |
| `Assets/Scripts/Data/ExperimentModels.cs`, `RoomSpecModels.cs` | Condition enums, session / export structures, rooms, anchors |
| `Assets/Resources/` | Vocabulary, default / example rooms, research media, furniture, shaders, inflection table |
| `Assets/Editor/` | UI baking, asset tools, headset-free checks; excluded from Player code |
| `Tools/` | PC services, speech, Quest transfer, delayed testing, data-table scripts |
| `Docs/` | Handoff instructions, feature contracts, prompt design material |

The controller still has several responsibilities. Refactoring must preserve serialized fields, scene references, partial-class integration, and data contracts. A text search finding no caller is not enough to delete code used through serialization, Resources, reflection, or menus.

## 8. Common adjustments

### UI

Select `EditableSceneUI` in Hierarchy. Use **Memory Palace Scene UI View > Stage Preview** to adjust RectTransforms, layouts, fonts, and colors. Binding depends on unique GameObject names. Change appearance without casually renaming controls or introducing duplicates. Check with **Validate Unique Control Names** in Inspector or **Tools > Memory Palace > Validate Editable Scene UI**.

Older HMD templates are under `EditableWorldSpaceUiTemplates`. The new click-to-learn card is generated at runtime in `.VrStudyFocus.cs`; editing the old Study Panel alone does not update it.

**Tools > Memory Palace > Bake Legacy UI Into SampleScene** replaces the entire `EditableSceneUI`, overwriting manual layout changes. Run it only when rebuilding intentionally and after backing up the scene. See [EditableSceneUI_README.md](../Assets/Scenes/EditableSceneUI_README.md).

### Movement, view height, and vignette

Select the controller on the scene's `MemoryPalaceExperiment` object:

- **VR Physical Walking > VR Physical Walking Gain:** physical walking amplification.
- **VR Physical Walking > VR Joystick Move Speed (m/s):** controller translation speed, independent of walking gain and PC keyboard speed.
- **VR View Height (Temporary Tuning) > VR View Height Offset (m):** negative values lower the view; positive values raise it. Head and hand tracking remain active. This is temporarily exposed for researcher tuning; do not hide it again without confirmation.
- **VR Comfort Vignette > Vignette Mode:** switch Opaque / Blurred modes. **Blur: Edge Darkness** controls blackening over peripheral blur; zero adds no black.
- **Self Room Physical Build Area:** Builder bounds and virtual / physical proportions. This is not walking gain and does not replace Quest's system safety boundary.

Play Mode changes usually do not persist. Record desired values, stop Play, apply them to the scene Inspector, and save. Do not overwrite researcher-tuned scene values with script defaults.

### Furniture scale and rooms

Open the relevant prefab in `Assets/Resources/FurniturePrefabs/` and adjust its root Transform Scale. The filename must match the room anchor's `modelKey`, such as `bed` or `bathtub`. Runtime preserves root rotation and scale; it does not normalize every furniture item to the same proportions. Conventions and baseline values are in [FurniturePrefabs/README.md](../Assets/Resources/FurniturePrefabs/README.md).

Keep the original formal room before editing. Anchor IDs, modelKey values, and furniture changes affect matching and older packages; verify exports as well as appearance. In Builder, select a misplaced item and press `B` to delete, `R` to rotate, or `Ctrl+Z` to undo. Dropping below nine distinct valid types locks completion again.

Builder controls are `WASD` to move, hold the middle mouse button to rotate, mouse wheel or `Q` / `E` to zoom, and `Esc` to cancel or deselect. PC room familiarization / matching uses `WASD`, hold the right mouse button to look, and left-click furniture. Do not confuse the two rotation bindings.

### Vocabulary, images, word audio, and prompts

- Vocabulary: `Assets/Resources/MemPalaceDemoData.json`. Preserve Spanish accents, meanings, and selection rules.
- Images: `Assets/Resources/WordImages/{word}.png`, named after the lowercase Spanish word. Missing images use a placeholder. Update `ATTRIBUTION.md` after replacements.
- Word audio: `Assets/Resources/WordAudio/`. `Tools/LocalTtsServer/Generate-FormalWordAudio.ps1` calls the speech service and generates assets; review parameters and costs before running it.
- Inflections: `EnglishInflections.bytes`, rebuilt with `Tools/Build-EnglishInflections.ps1`. Preserve `EnglishInflectionsNotice.txt`. Runtime needs neither Python nor a network connection for matching.
- Prompts: edit `MnemonicStoryPrompts.cs` to affect Unity. `Docs/MnemonicStoryPromptV2/` contains shareable templates, schemas, and the full framework; Unity does not read them at runtime.

The current pipeline is planning, writing, and at most one repair. The full MSF-2.1 design's four-blueprint selection and independent Audit are not integrated runtime capabilities. Prompt changes require compatibility checks, preserved order / output contracts and budgets, and updated version notes.

The collaboration ZIP deliberately excludes all word-image PNGs and WordAudio. Blank images are for development only. Formal Spanish words use `localOnly` audio: missing files cause an error, not automatic cloud synthesis. Before a complete session, restore authorized images and restore approved audio or generate it through your own Azure account. See the [asset inventory](THIRD_PARTY_ASSETS.en.md).

## 9. Checks, builds, and handoff

### Headset-free regression checks

After successful compilation, use these Unity menus outside Play Mode:

- **Tools > Memory Palace > Run Project Checks:** runs furniture, prompt, participant-story, Builder, and VR click-to-learn offline checks.
- **Tools > Memory Palace > Check Furniture Prefab Fallback**
- **Tools > Memory Palace > Check Story Prompt Compatibility**
- **Tools > Memory Palace > Check Participant Story English**
- **Tools > Memory Palace > Check Builder Delete Shortcut**
- **Tools > Memory Palace > Check VR Click-to-Learn**
- **Tools > Memory Palace > Check VR Comfort Vignette**
- **Tools > Memory Palace > Validate Editable Scene UI**

Read Console results; invoking a menu is not evidence of success. `RunBatch` entry points may exit the Editor and should not be invoked in a normal editing session. Some rendering checks require a graphics device and cannot run with `-nographics`.

Run the delayed-test service's unit tests from the project root:

```powershell
python -m unittest discover -s Tools/DelayedTestWeb/tests -p "test_*.py"
```

Use an explicit test ID for PC smoke tests: Builder placement / rotation / deletion / undo, screening, story Split, unique eight-word matching, learning-card entry / exit, narration completion, snapshots, full assessment, and JSON / CSV export. Online generation and TTS may incur charges. Use your own test account, not unrestricted replay of participant data.

Headset-free checks cover logic and some rendering, not Quest frame rate, tracking, controller compatibility, or discomfort. This guide does not claim headset acceptance testing for the current version or require headset testing after every documentation change.

### Building

In Unity **File > Build Profiles**, choose Windows or Android. Quest uses Android; confirm `SampleScene` as the entry scene. Preserve committed XR / OpenXR and rendering settings, and resolve Console / Build errors first. Label development and formal study builds separately. Before collection, record the commit, APK, device, and parameter versions.

Inspect Resources API configuration before building: ignored files can still be embedded. Do not give unauthorized recipients builds containing long-lived keys. Reinstall after code, shader, or scene changes; use session JSON only for content changes.

### Exporting collaboration source

In the maintainer's workspace, double-click `Tools/Export-CollaborationPackage.cmd`, or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Export-CollaborationPackage.ps1
```

Output: `Builds/Collaboration/MemoryPalace-Source-YYYYMMDD-HHmmss.zip`. Share the reviewed ZIP, not the entire workspace or `.git`. Export filters a copy; it does not remove formal assets from the maintainer's workspace.

Run `Tools/Check-CollaborationPackage.ps1` afterwards. By default it checks the latest ZIP's allowed paths, SHA256 hashes, Unity `.meta` files, and relative Markdown links. `SOURCE_MANIFEST.json` records file hashes and `SHARING_NOTES.md` explains development limitations. Automated scanning is not a guarantee against leaks; manually review new files before distribution.

The package excludes commercial or unverified visual assets, all word-image PNGs, prepared WordAudio, personal configuration, participant data, caches, and Git history. It retains code, scenes, settings, development fallbacks, and required notices. It is development source, not a complete study installation. See [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.en.md) for restoration and licensing gaps, and [PROJECT_CONTEXT.md](PROJECT_CONTEXT.en.md) for the current implementation.

Full repository history still contains older participant data. Grant private Git access only after confirming research-data authorization. A cleaned latest revision is not permission to publish the entire history.

### Sharing checklist

- Include code, required Project Settings, Packages, approved assets, and documentation. Exclude `Library`, `Temp`, logs, caches, and personal IDE folders.
- Exclude `.git`, credential JSON / environment files, APK signing material, speech caches, session packages, results, original stories, and screenshots. Deleting current files does not remove them from history.
- Commercial **Furniture Mega Pack** source is excluded. Collaborators must confirm applicable licensing before obtaining and importing it; do not bundle local source simply to make the package complete.
- Missing Mesh / Material dependencies can use procedural furniture for development. This is not equivalent to the formal research stimulus.
- Only 15 of 47 tracked word-image PNGs are covered by the old OpenMoji table; 32 are not. The old six-file list is incomplete, and existing records do not independently verify that files were never replaced. The ZIP excludes every PNG and its `.meta`, including `_placeholder.png`, and uses a programmatic blank image.
- The 32 prepared WordAudio WAVs are also excluded. Confirm their generation source and applicable service terms, then restore or regenerate them before a complete session. Do not collect participant data with an incomplete material set.
- Preserve OpenMoji attribution, source and CC BY-SA 4.0 links, and the inflection table's upstream notice. Do not describe the whole project as unrestricted commercial material.
- Open the source in a new directory and check fallbacks, missing-configuration messages, and startup instructions. Separately verify authorized materials, images, audio, and versions on the study machine.

## 10. Troubleshooting

| Symptom | Check first |
| --- | --- |
| Slow Play entry; restarting the Editor helps temporarily | Console and timed Editor.log stages, script / asset refresh, OpenXR initialization, Meta Quest Link, and the active XR runtime. Record reproducible steps before changing settings; deleting Library or disabling XR is not a diagnosis. |
| Furniture is missing, pink, or simplified | Authorized source assets, missing Mesh / Material, URP compatibility, and matching modelKey values. A fallback does not mean formal materials were installed correctly. |
| Builder Done is unavailable | At least nine distinct valid furniture types within bounds are required, not nine learning words. |
| Participant story targets are not detected | Split / Refresh, English meaning, actual inflection, and sentence assignment. Resolve ambiguity manually; do not require Spanish input. |
| Online generation fails | Selected backend, local configuration, model access, endpoint, network, and limits. A stopped Ollama service does not explain every cloud error. |
| TTS health is OK but no speech is heard | Use Test Configured Voice. Check Azure subscription status, credit, region / key, proxy errors, and system volume. Health does not test synthesis permission. |
| Quest cannot fetch packages or speech | LAN IP rather than localhost, mutual reachability, firewall, server binding, exported URL, and USB / adb authorization and package ID as applicable. |
| Port 8765 is occupied | Separate package and delayed-test servers; use 8766 for the latter. Identify the owner before stopping anything. |
| No learning UI appears near furniture | Point the right-hand ray and trigger-click the correct route target; check distance and occlusion. Highlighting does not open the card automatically. |
| Next / Finish learning is unavailable | Narration completion, first-pass route completion, required snapshots, and the card's return / error controls. |
| Quest still uses old interactions | Rebuild and install the APK, not just the JSON package. Confirm that the installed and launched package IDs match. |
| Results cannot be found | Complete assessment export; check project-root exports in Editor, persistentDataPath on Android, and timestamped QuestResultImports after retrieval. |

For new issues, record the Unity version / commit, PC or Quest, steps, stage, expected and actual behavior, and sanitized logs. Never attach credentials or identifiable participant information.

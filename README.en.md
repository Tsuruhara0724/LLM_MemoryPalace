# LLM Memory Palace

[中文](README.md) | English


A Unity desktop / VR project for Spanish vocabulary-learning research. Participants create or familiarize themselves with a room, write or select an English story, assign eight target words to furniture, and complete VR learning and assessments.

The current design is **Self room / Example room × Self story / LLM story**, a 2 × 2 experiment. The implementation and developer guide take precedence over older three-condition or 20-minute protocols.

## Getting started

1. Install Unity Hub and **Unity 6000.3.12f1**. Quest builds also require Android Build Support, SDK & NDK Tools, and OpenJDK for the same Editor version.
2. Extract the reviewed `MemoryPalace-Source-*.zip`. In Unity Hub, use **Add project from disk** and select the directory containing `Assets`, `Packages`, and `ProjectSettings`. Full Git access is restricted to collaborators authorized to access research data.
3. Open `Assets/Scenes/SampleScene.unity`. Wait for package restoration, asset import, and script compilation before entering Play Mode.
4. Before using Azure speech for the first time, run `Tools/LocalTtsServer/Setup-Azure.cmd`. After each reboot, run `Tools/Start-ProjectServices.cmd` and enter your own key and resource region in the separate TTS window.

Desktop UI and Builder development do not require every service. LLM conditions need your own story API configuration; speech needs the Azure proxy. Quest network transfer and delayed testing use separate services.

## Documentation

- [Developer guide](Docs/DEVELOPER_GUIDE.en.md): installation, configuration, study flow, code map, testing, builds, and handoff.
- [Maintenance index](Docs/PROJECT_CONTEXT.en.md).
- [Third-party assets and sharing restrictions](Docs/THIRD_PARTY_ASSETS.en.md).
- [Starting services after a reboot](Docs/STARTUP.en.md).
- [Click-to-learn VR interaction](Docs/VR_CLICK_TO_LEARN.md).
- [English inflections and Spanish annotations in participant stories](Docs/PARTICIPANT_STORY_ENGLISH.en.md).
- [Editable scene UI](Assets/Scenes/EditableSceneUI_README.md).
- [Runtime prompts versus the full story framework](Docs/MnemonicStoryPromptV2/README.md).
- [Delayed-test web application](Tools/DelayedTestWeb/README.md).

## Sharing

Double-click `Tools/Export-CollaborationPackage.cmd`, or run from the project root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Export-CollaborationPackage.ps1
```

The output is `Builds/Collaboration/MemoryPalace-Source-YYYYMMDD-HHmmss.zip`. Share the reviewed ZIP, not the entire workspace or `.git`. Full repository history still contains older participant data and is not an anonymized source distribution.

The source package excludes credentials, participant data, Git history, commercial or unverified visual assets, all word-image PNGs, and prepared WordAudio. Procedural furniture and blank images support development only; they are not equivalent research stimuli. Before running a complete study, restore authorized, frozen materials and restore or generate word audio as described in the [asset inventory](Docs/THIRD_PARTY_ASSETS.en.md).

Of 47 tracked word-image PNGs, only 15 are covered by the existing OpenMoji attribution table; 32 are not. The older six-file list of missing sources is incomplete. An attribution record does not independently establish that an image has never been replaced. Retain all required licenses, sources, and upstream notices.

Do not commit API keys, key-bearing APKs, participant packages, results, original stories, or screenshots. `.gitignore` helps prevent accidental commits; it does not replace a sharing review.

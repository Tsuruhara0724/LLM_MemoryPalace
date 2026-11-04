# Maintenance Index

[中文](PROJECT_CONTEXT.md) | English


Reviewed: 2026-10-28. Start with the [developer guide](DEVELOPER_GUIDE.en.md). See [STARTUP.md](STARTUP.en.md) for services and [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.en.md) for material restrictions.

## Current implementation

- Unity 6000.3.12f1; entry scene: `Assets/Scenes/SampleScene.unity`.
- Four conditions: Self / Example room × Self / LLM story. Three-condition, baseline, and 20-minute descriptions belong to earlier designs.
- Self-built rooms require at least nine distinct valid furniture types; learning uses eight unknown words. Stage timers are targets, not universal automatic transitions.
- Participant stories are English-only. An offline inflection table recognizes English variants; after Split, learning text annotates the matching English form with Spanish. Original input is preserved; participants are not required to type Spanish.
- LLM stories use MSF-2.1 planner / writer / repair through `OllamaLlmService.GenerateStory`. The historical class name does not restrict the backend to local Ollama.
- VR highlights nearby correct furniture, but a click is required to open the learning card. Virtual translation, turning, and amplified walking pause during learning; native head tracking remains active. See [VR_CLICK_TO_LEARN.md](VR_CLICK_TO_LEARN.md).

## Code map

| File / directory | Responsibility |
| --- | --- |
| `Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs` | Scene startup and creation of a missing controller |
| `MemoryPalaceExperimentController.cs` and partial files | Stages, Building, Writing, Matching, learning, assessment, exports, desktop / VR UI |
| `Assets/Scripts/Services/OllamaLlmService.cs` | Model requests, story planning / repair, parsing, and retained room / furniture interfaces |
| `Assets/Scripts/Services/MnemonicStoryPrompts.cs` | Runtime story prompt policy |
| `Assets/Scripts/Services/ParticipantStoryEnglish.cs` | Inflection matching and learning annotations |
| `Assets/Scripts/Services/ElevenLabsTextToSpeechService.cs` | Prepared word audio and Azure proxy narration; historical name retained |
| `Assets/Scripts/Services/FurniturePrefabAvailability.cs` | Pre-instantiation Mesh / Material checks and procedural fallback eligibility |
| `Assets/Scripts/Data/` | Session, story, result, and room models |
| `Assets/Resources/` | Vocabulary, example rooms, inflection data, shaders, research materials |
| `Assets/Editor/` | Offline checks, build tooling, asset utilities |
| `Tools/` | Service startup, audio generation, device transfer, delayed testing, source export |

Short controller filenames refer to `Assets/Scripts/Runtime/`; other paths are given in full.

## Maintenance boundaries

Scene events, Resources paths, string-based reflection in Editor checks, and serialized fields can all create dependencies. An IDE's zero-reference result alone is not enough to justify deletion. Image generation, catalog import, and shared anchor normalization remain where active offline tools still use them.

The controller is still large. Building, Story, and Assessment are candidates for separation, but stage-transition and result-compatibility tests should come first. Do not rewrite the study flow as part of a handoff cleanup. Procedural furniture, blank images, and synthetic data are development materials, not interchangeable with formal research stimuli.

Before committing, run `Tools/Memory Palace/Run Project Checks` and `git diff --check`. These are engineering checks, not verification of model quality, human learning outcomes, or headset comfort.

Update this index and the developer guide when changing the protocol, model, vocabulary, resource paths, or data fields. Older design documents remain research records, not current operating instructions.

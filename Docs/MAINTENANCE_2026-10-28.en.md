# Collaboration Handoff Cleanup — 2026-10-28

[中文](MAINTENANCE_2026-10-28.md) | English


## Version boundary

Pre-cleanup checkpoint: `2d32807`, tagged `checkpoint/2026-10-28-vr-click-to-learn`. The cleanup is a separate revision and does not rewrite history. Removed code and templates can be recovered from that checkpoint.

## Changes

- Removed 45 unreachable or uncalled private methods, four old DTOs, and three constants from the story service. Four old public entry points retain their signatures and disabled-feature messages. Active planner / writer / repair, parsing, and provider paths remain.
- Shortened two permanently disabled controller coroutines; removed four exclusive helpers and six nonserialized fields that were assigned but never read.
- Removed unused `MnemonicCueFrameRag` code / JSON, the old system speech bridge, and Unity welcome templates. Approximately 3,200 lines of old / template C# were removed, excluding newly added checks and utilities.
- Added Mesh / Material checks before furniture instantiation. Invalid resources use the existing procedural fallback. Valid prefab root scale and rotation, scene parameters, and the current study flow are unchanged.
- Replaced automatic Editor Burst preference changes with explicit compatibility menus.
- Added README, developer guide, maintenance index, asset inventory, and allowlist-based export / verification scripts. Older study documents are marked historical and excluded from the handoff package.
- Stopped tracking the latest session package and Sketchfab import installer while retaining their local files. Ignore rules prevent accidental future commits. Older history may still contain participant data and is not an anonymized distribution.

Image catalogs, shared anchor normalization, and room / furniture generation remain where research tools still call them. The controller still needs further separation of responsibilities; that requires additional transition and result-compatibility tests. This cleanup does not claim a theoretically minimal project.

## Verification and limits

- Runtime Roslyn semantic checks passed. Exported source restored Unity packages, imported, and compiled in a new directory.
- Passed 78 furniture resource / missing-entry / blank-image checks, 38 prompt compatibility checks, 109 English inflection / annotation checks, 19 Builder checks, and 54 VR click-to-learn checks: 298 offline assertions in total. VR checks include D3D11 occlusion and actual snapshot rendering.
- The new-directory, graphics-free run passed the same nonrendering suites, with a 51-check VR subset, without commercial assets or private configuration from the original workspace.
- Four delayed-test Python unit tests, PowerShell parsing, and `git diff --check` passed.
- Package checks verify per-file SHA256, exclusions, Unity `.meta` files, and relative Markdown links. The package excludes Git history, personal configuration, participant outputs, media assets, and caches.

No online model generation, real Azure synthesis, complete formal study, or Quest hardware test was performed. EditMode checks did not construct all 14 procedural furniture models. The missing-prefab controller branch and read-only availability checks were verified; final appearance and collision still need checking with the intended materials. Blank images and procedural furniture are development-only; formal learning requires authorized, frozen materials and prepared word audio.

These results describe the cleanup revision. Subsequent English documentation changes do not alter runtime code or constitute a new hardware test.

See the [developer guide](DEVELOPER_GUIDE.en.md) for check entry points and the [asset inventory](THIRD_PARTY_ASSETS.en.md) for material gaps.

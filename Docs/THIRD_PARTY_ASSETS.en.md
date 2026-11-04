# Third-Party Assets and Handoff Restrictions

[中文](THIRD_PARTY_ASSETS.md) | English


Reviewed: 2026-10-28. This inventory records source evidence and gaps found in the repository; it is not a licensing or legal guarantee. Names such as `free`, `CC0`, and `dontuse` establish neither permission nor runtime usage.

Do not overwrite or replace formal research images, audio, furniture, or prefab proportions while preparing a source handoff. The development package's procedural furniture and blank images are not formal research materials and must not be used directly for data collection.

## Available licensing records

| Path | Repository evidence | Handoff requirement |
| --- | --- | --- |
| `Assets/Resources/EnglishInflections.bytes`, `EnglishInflectionsNotice.txt` | AGID source, snapshot, SHA256, conversion notes, and complete upstream notices are recorded in the notice file | Keep the data and full notice together, including author, WordNet, and other upstream notices |
| `Assets/Resources/FurniturePrefabs/bookshelf_cc0/` | `license.txt` names Crew Froebel and states CC-BY-4.0, with a Sketchfab source | Do not infer CC0 from the folder name or model title; preserve the file and required attribution |
| The following 15 existing images in `Assets/Resources/WordImages/` | `ATTRIBUTION.md` lists OpenMoji, CC BY-SA 4.0, and the source version | Preserve attribution, sources, and license information; the old table is not independent verification that images were never replaced |

Existing files covered by the OpenMoji table; all have the extension `.png`:

```text
cuaderno cuchara espejo estrella llave maleta mercado mochila
moneda nube puente reloj vecino vela ventana
```

The older table also lists `flor`, `corona`, `linterna`, `máscara`, and `tambor`, but those PNGs were not in the tracked directory at review time. Do not infer current coverage from that table alone.

## Commercial furniture dependencies

`Assets/Furniture Mega Pack/` and `Assets/Furniture Mega Pack.meta` are Git-ignored. Do not include locally installed commercial source in a collaboration archive without confirming permission. Collaborators must verify licensing for their intended collaboration arrangement and obtain the matching assets.

The following eight tracked prefabs have Mesh, Material, or nested-prefab GUID references into that local directory:

```text
Assets/Resources/FurniturePrefabs/bathtub.prefab
Assets/Resources/FurniturePrefabs/bed.prefab
Assets/Resources/FurniturePrefabs/bookshelf.prefab
Assets/Resources/FurniturePrefabs/chair.prefab
Assets/Resources/FurniturePrefabs/desk.prefab
Assets/Resources/FurniturePrefabs/sofa.prefab
Assets/Resources/FurniturePrefabs/toilet.prefab
Assets/Resources/FurniturePrefabs/wardrobe.prefab
```

Referenced assets include BathTub03, Bed10 (including Cushion27), Closet16, Chair06, Table49, Sofa31, Toilet07, Closet07, and their materials. A clean clone may load a prefab file while its Mesh / Material references remain missing. File existence is not a sufficient availability check.

Exclude these eight `.prefab` files and their `.meta` files from the development package and use procedural furniture. Reproducing the formal appearance requires the matching asset version and original GUID relationships; importing a same-named FBX may not repair references. Do not reset tuned prefab root scale or rotation to repair missing dependencies.

## Furniture with unverified source or license records

Sufficient source, author, and license records were not found alongside these directories or files. Exclude them from the distributed development package, retain them in the original workspace, and add them separately only after authorization is confirmed:

```text
Assets/Arts/
Assets/Resources/FurniturePrefabs/modern-room-lcd-3d-model/
Assets/Resources/FurniturePrefabs/pbr-floor-lamp-free-download/
Assets/Resources/FurniturePrefabs/rhyzome-plant/
Assets/Resources/FurniturePrefabs/window_source/
Assets/Resources/FurniturePrefabs/dontuseAir_conditioner.fbx
Assets/Resources/FurniturePrefabs/dontuseDoor.fbx
Assets/Resources/FurniturePrefabs/dontuse_Window_1.fbx
```

Exclude the corresponding `.meta` files as well. Do not remove a model from the export while retaining a broken material dependency chain.

These files are directly or indirectly referenced by the following six runtime prefabs. Exclude these prefabs and their `.meta` files from the development package too, allowing procedural fallbacks:

```text
Assets/Resources/FurniturePrefabs/air_conditioner.prefab
Assets/Resources/FurniturePrefabs/door.prefab
Assets/Resources/FurniturePrefabs/lamp.prefab
Assets/Resources/FurniturePrefabs/plant.prefab
Assets/Resources/FurniturePrefabs/television.prefab
Assets/Resources/FurniturePrefabs/window.prefab
```

The `dontuse*.fbx` files do have prefab references. Their names are not grounds for deleting them from the formal workspace. Identically named textures under `Assets/Arts/` and `window_source/` can have different GUIDs and are not necessarily interchangeable.

`GeneratedMaterials/` belongs to the procedural bathtub generator, not the commercial pack. `FurniturePrefabGenerator` can generate a simple project bathtub when its prefab is missing. That result is not the same research stimulus as the formal bathtub.

## Gaps in word-image records

The older `Assets/Resources/WordImages/ATTRIBUTION.md` explicitly flags these six filenames as missing source information:

```text
alfombra.png barco.png biblioteca.png camino.png campana.png castillo.png
```

Only `alfombra.png`, `biblioteca.png`, and `camino.png` were still tracked at review time; the other three files were absent. This is not a complete review list: many later additions also lack records.

Of 47 tracked PNGs, these 32 are not covered by the existing source table; all have the extension `.png`:

```text
_placeholder abrazo alfombra ayuda biblioteca burbuja búsqueda camino
cerrojo columpio cremallera enchufe espera grieta hambre huella huevo
juego lodo logro olvidar paraguas pegamento pájaro relámpago risa
rodilla rueda ruido sombra techo valla
```

A PNG does not establish its own license. Records should include the author, original page, license, acquisition date, local modifications, and permission to redistribute source assets. A missing license record does not mean an asset has no copyright.

The conservative development-package policy is to exclude every PNG and corresponding `.meta` under `Assets/Resources/WordImages/`, while retaining README and ATTRIBUTION. When `WordImageCatalog` cannot load an image, the controller already calls `CreateBlankPlaceholder()`. The unverified `_placeholder.png` is therefore unnecessary in the package. The programmatic blank image is for development only.

## Prepared image cues and UI backgrounds

No per-file source, generation-account terms, or permission records were found for `Assets/Resources/PreGeneratedImageCues/`. Being stored in the project does not establish redistribution rights. Exclude that directory and its `.meta`, plus `Assets/Resources/PreGeneratedImageCueCatalog.json` and its `.meta`, from the development source package.

Older image-cue management tools still use these resources. Do not delete them from the research workspace merely because the default formal flow does not use them. Those tools will report missing materials in the development package.

`Assets/Resources/HandDrawnStudyUI/README.md` describes the uses of `paper_panel.png` and `wood_desk_background.png` but contains no source or license evidence. The conservative handoff also excludes those PNGs and their `.meta` files. Collaborators can supply approved decorative backgrounds. Missing decoration is distinct from missing formal word images; check layout readability in a clean copy.

## Prepared speech

The 32 WAVs under `Assets/Resources/WordAudio/` are prepared formal vocabulary pronunciations, not disposable caches. `Tools/LocalTtsServer/Generate-FormalWordAudio.ps1` explicitly writes Azure Speech output there, but the repository lacks per-file provenance and generation-account terms.

Before distribution, confirm their generation source and applicable service terms. If WAVs are excluded, exclude their `.meta` files and tell collaborators to restore or regenerate them. Formal words use `localOnly` playback: missing prepared clips produce an error rather than automatic dynamic cloud TTS. Removing these WAVs does not leave a complete runnable study.

## Local content excluded from source packages

Collaborators should not inherit these local files and directories:

```text
.git/
Library/  Temp/  Obj/  Logs/  UserSettings/  Build/  Builds/
.utmp/  .local-tts/  .vs/  .idea/  .vscode/
ExperimentExports/  GeneratedRooms/  Demos/  VRSessionPackages/
Assets/Resources/AnthropicLocalConfig.json
Assets/Resources/AnthropicLocalConfig.json.meta
Assets/Resources/OpenAiLocalConfig.json
Assets/Resources/OpenAiLocalConfig.json.meta
SketchfabForUnity-v1.2.1.unitypackage
```

Other local debugging and historical repair caches are also excluded. `VRSessionPackages/session_package_latest.json` was tracked before cleanup. Removing it from the current revision does not remove it from older commits or checkpoints. Export reviewed files through the allowlist; do not compress the entire project or copy `.git` as an anonymized handoff.

The root Sketchfab `.unitypackage` is an approximately 0.98 MB import installer, not a runtime dependency of scenes, code, or the package manifest. Excluding it does not remove already imported resources; the earlier checkpoint can recover the tracked installer.

Git ignore rules do not make builds safe. Unity embeds Resources configuration in applications. Do not include real API keys in Quest APKs supplied to unauthorized recipients, and inspect scene fields before distribution. No key values or participant stories are recorded in this inventory.

## What the clean development package supports

Retain project code, scenes, UI, shaders, room / demo vocabulary, `Packages/manifest.json`, `Packages/packages-lock.json`, and `ProjectSettings/`. Use the Editor version in `ProjectSettings/ProjectVersion.txt` (6000.3.12f1 at review time). Initial opening requires package restoration; Quest builds additionally require the matching Android Build Support, SDK/NDK, and OpenJDK.

The controller checks missing or invalid furniture through `FurniturePrefabAvailability` before instantiation, then uses existing procedural fallback code. Valid local prefab proportions are not changed. Excluding all 14 furniture prefabs provides development fallbacks, not equivalent formal visuals.

Missing word images use a programmatic blank placeholder. This supports UI, Building, Writing, Matching, and VR interaction development, not a complete research material set.

Story generation, dynamic narration, and cross-device learning still require each collaborator's model configuration, service accounts, LAN connectivity, and formal materials. Static review alone cannot guarantee those services. See [STARTUP.md](STARTUP.en.md). Initial Azure proxy setup uses Python 3.12 and `Tools/LocalTtsServer/Setup-Azure.cmd`; afterwards run `Tools/Start-ProjectServices.cmd` as needed. The maintainer must separately confirm account and subscription validity.

Restore the source package in a new directory, compile, and check a development demo without relying on commercial assets installed in the original workspace. A formal study additionally requires authorized and frozen assets, prepared word audio, model settings, and TTS settings.

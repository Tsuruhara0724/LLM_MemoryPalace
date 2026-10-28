# 第三方素材与交接边界

核对日期：2026-10-28。本文只记录仓库内能找到的来源证据及缺口，不构成许可或法律保证。文件名、目录名中的 `free`、`CC0` 或 `dontuse` 都不等于授权证明，也不等于运行时未使用。

整理协作源码包时应保留本机正式研究素材，不覆盖、不替换原工作区中的图片、音频、家具或 Prefab 比例。开发包使用的程序家具和空白词图不属于正式研究材料，不能直接用于收集实验数据。

## 能找到的许可记录

| 路径 | 仓库内证据 | 交接要求 |
| --- | --- | --- |
| `Assets/Resources/EnglishInflections.bytes`、`EnglishInflectionsNotice.txt` | AGID 数据来源、快照、SHA256、转换说明及上游完整声明记录在 `EnglishInflectionsNotice.txt` | 数据和完整声明一起保留；不删作者、WordNet 或其他上游声明 |
| `Assets/Resources/FurniturePrefabs/bookshelf_cc0/` | `license.txt` 标注 Crew Froebel，实际许可字段为 CC-BY-4.0，附 Sketchfab 来源 | 不应按目录名或模型标题当作 CC0；保留该文件及要求的署名 |
| `Assets/Resources/WordImages/` 中下列 15 张现存图片 | `ATTRIBUTION.md` 将其列为 OpenMoji、CC BY-SA 4.0，并给出来源版本 | 保留来源、署名和许可声明；表格是既有记录，不是对图片内容未曾被替换的独立鉴定 |

上述有 OpenMoji 记录的现存文件，扩展名均为 `.png`：

```text
cuaderno cuchara espejo estrella llave maleta mercado mochila
moneda nube puente reloj vecino vela ventana
```

旧表中还列出 `flor`、`corona`、`linterna`、`máscara`、`tambor`，但本次核对的 Git 跟踪目录里没有这些 PNG；不要由旧表推断目前的材料覆盖率。

## 商业家具依赖：需要协作者另行取得

`Assets/Furniture Mega Pack/` 及 `Assets/Furniture Mega Pack.meta` 已被 `.gitignore` 排除。不能把本机已安装的商业源素材直接塞进协作源码包；协作者须自行确认适用于其协作方式的许可，并安装对应资源。

以下 8 个已跟踪 Prefab 的 Mesh、Material 或嵌套 Prefab GUID 引用指向该本机目录：

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

具体来源分别为 BathTub03、Bed10（含 Cushion27）、Closet16、Chair06、Table49、Sofa31、Toilet07、Closet07 及其材质。干净 clone 中虽然可能仍能加载这些 Prefab 文件，其 Mesh/Material 也可能是空引用，不能只用“Prefab 文件存在”判断可用。

开发包应排除这 8 个 `.prefab` 以及各自的 `.meta`，使用程序家具。若需复现正式画面，必须取得同版资源并保留原 GUID 对应关系；仅导入同名 FBX 不保证修复引用。不要为了修复空引用而重置已调好的 Prefab 根节点缩放和旋转。

## 尚未找到来源或许可记录的家具文件

以下根目录或文件的旁边未发现足够的来源、作者和许可说明。应从对外开发源码包中排除，并在原工作区保留；确认授权后再单独补充：

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

每个被排除的文件或目录都应连同对应 `.meta` 一起从导出副本排除，不能只删模型、保留断开的材质链。

这些文件仍被下列 6 个运行时 Prefab 直接或间接引用，因此导出开发包时也排除这些 `.prefab` 和各自 `.meta`，以便使用程序家具：

```text
Assets/Resources/FurniturePrefabs/air_conditioner.prefab
Assets/Resources/FurniturePrefabs/door.prefab
Assets/Resources/FurniturePrefabs/lamp.prefab
Assets/Resources/FurniturePrefabs/plant.prefab
Assets/Resources/FurniturePrefabs/television.prefab
Assets/Resources/FurniturePrefabs/window.prefab
```

`dontuse*.fbx` 当前确实有 Prefab 引用，不能按名字直接从正式工作区删除。`Assets/Arts/` 与 `window_source/` 下有同名纹理，但不同 GUID 的资源不一定可以直接互删。

`GeneratedMaterials/` 是程序浴缸生成器使用的材质目录，不应混同为商业素材；`FurniturePrefabGenerator` 可在没有浴缸 Prefab 时生成本项目的简单浴缸。该生成结果与本机正式浴缸并非相同研究刺激。

## 词图的记录缺口

旧 `Assets/Resources/WordImages/ATTRIBUTION.md` 明确列出 6 个来源待补的文件名：

```text
alfombra.png barco.png biblioteca.png camino.png campana.png castillo.png
```

本次其中只有 `alfombra.png`、`biblioteca.png`、`camino.png` 仍在 Git 跟踪目录内；另外 3 个文件目前不存在。这个旧清单不是完整的待核对清单：后来新增的许多词图也没有相应记录。

当前 47 张已跟踪 PNG 中，下列 32 张未被现有来源表覆盖；扩展名均为 `.png`：

```text
_placeholder abrazo alfombra ayuda biblioteca burbuja búsqueda camino
cerrojo columpio cremallera enchufe espera grieta hambre huella huevo
juego lodo logro olvidar paraguas pegamento pájaro relámpago risa
rodilla rueda ruido sombra techo valla
```

不能从 PNG 文件本身推断其许可。补充记录至少要包含作者、原始页面、许可、取得时间、当地修改及是否允许源码形式再分发。不要把“没有找到许可记录”写成“没有版权”。

最保守且容易维护的开发包做法是排除 `Assets/Resources/WordImages/` 下所有 PNG 及对应 `.meta`，保留 README 和 ATTRIBUTION。运行时 `WordImageCatalog` 加载失败后，主控制器已有 `CreateBlankPlaceholder()` 分支，因此不需要连来源未明的 `_placeholder.png` 一起复制。这个空白占位图只用于开发。

## 预生成图像 cue 和 UI 背景

`Assets/Resources/PreGeneratedImageCues/` 未发现配套的逐文件来源、生成账户使用条件或授权记录。不能把这批图片仅因存放在项目目录里，就视为来源已确认的可再分发资产。开发源码包应排除整个目录及其 `.meta`，以及指向它的 `Assets/Resources/PreGeneratedImageCueCatalog.json` 和对应 `.meta`。这些资源仍由旧版图像 cue 管理功能使用，不应因为默认正式流程不用，就直接从本机研究工作区删除；相关旧功能在开发包中会显示素材缺失。

`Assets/Resources/HandDrawnStudyUI/README.md` 只说明 `paper_panel.png`、`wood_desk_background.png` 的用途，没有来源或许可证明。最严格的源码交接也应排除这两张 PNG 及对应 `.meta`，由协作者自行提供确认可用的装饰背景。缺少装饰图与缺少正式词图是不同问题，应在干净副本验证界面布局仍然清楚。

## 预生成语音

`Assets/Resources/WordAudio/` 下的 32 个 WAV 是正式词汇的预生成朗读文件，不是可随意清理的临时缓存。`Tools/LocalTtsServer/Generate-FormalWordAudio.ps1` 明确把 Azure Speech 生成结果写入此目录，但仓库里没有逐文件来源或生成账户使用条件记录。

如对外分发，维护者需确认这些音频的生成来源及适用的服务使用条件。若排除 WAV，也要排除相应 `.meta`，并告知协作者需自行补回或生成：正式单词的 `localOnly` 朗读在缺少预生成文件时会报错，不会自动改用动态云端 TTS。不能声称删掉这些 WAV 后仍可完整运行实验。

## 源码包不能包含的本地内容

以下内容不是协作者运行项目应直接继承的数据：

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

其他本地调试目录和历史修复缓存也不随包分发。`VRSessionPackages/session_package_latest.json` 在整理前曾被 Git 跟踪；即使从当前分支移除，原有提交历史和检查点仍可能包含它。不要把历史仓库本身当作已匿名化的交接包。应使用当前审核后的文件白名单导出源码包，而不是压缩整个项目文件夹或复制 `.git`。

根目录 Sketchfab `.unitypackage` 约 0.98 MB，是导入安装包，场景、代码和 Package manifest 都不以它作为运行时加载目标。它不应跟随开发包；若从当前版本移除，可通过先前检查点找回，不影响现有已导入资源。

忽略本地配置的 Git 规则不等于构建安全：位于 `Resources` 下的配置会被 Unity 打进应用。不要在交付给未授权人员的 Quest APK 中嵌入真实 API 密钥；公开分享前也要检查场景序列化字段。本文没有读取或记录任何密钥值和参与者故事。

## 干净开发包能运行到什么程度

保留 `Assets` 中项目代码、场景、UI、Shaders、房间/演示词表，及 `Packages/manifest.json`、`Packages/packages-lock.json`、`ProjectSettings/`。使用 `ProjectSettings/ProjectVersion.txt` 中的 Unity 版本（本次为 6000.3.12f1），首次打开需要联网恢复 Unity 包；Quest 构建还需该版本的 Android Build Support、SDK/NDK 和 OpenJDK。

家具方面，主控制器在 Prefab 缺失或通过 `FurniturePrefabAvailability` 判断不可用时，应退回已有程序家具；检查发生在实例化之前，不改变本机完整 Prefab 的比例。所有 14 个家具 Prefab 从导出副本排除后，代码层面有对应 fallback，但正式画面不再等价。

词图方面，缺图时已有程序生成的空白占位图。这样可以做界面、Building、Writing、Matching 和 VR 交互开发，不代表研究刺激已齐全。

完整的故事生成、动态朗读和跨设备学习仍依赖协作者自己的模型配置、服务账户、局域网可达性及正式材料；不能仅凭静态审查保证可用。启动步骤见 `STARTUP.md`。Azure 代理首次安装需要 Python 3.12，执行 `Tools/LocalTtsServer/Setup-Azure.cmd`；之后按需运行 `Tools/Start-ProjectServices.cmd`。学生订阅或其他服务账户是否有效必须由维护者另行确认。

完成分享包后，应在新的目录中恢复包、编译并运行一次开发演示；不要用原工作区中已安装的商业资源来代替这项检查。正式实验另需还原经授权并冻结的素材版本、预生成词音频、模型和 TTS 设置。

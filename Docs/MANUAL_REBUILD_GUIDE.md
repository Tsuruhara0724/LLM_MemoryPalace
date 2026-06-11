# 手工重建教程 - LLM Memory Palace

最后核对日期：2026-06-11

用途：
- 让项目所有者不借助 AI，也能理解并手工重新搭建当前项目。
- 本文按“从零到可运行”的顺序写。
- 如果以后架构或运行方式变化，必须同步更新。

## 1. 你最终要做出的东西

目标是一个 Unity 记忆宫殿实验 app。

最终功能：
1. 输入 participant ID。
2. 选择 `LLM Generated` 或 `Self Generated`。
3. 选择或随机抽取西语词汇。
4. 使用或编辑一个带家具 anchor 的房间。
5. LLM 条件下，用 Ollama 生成 visual cue、association prompt、image prompt、Cue Story。
6. Self 条件下，让用户手动写 cue 和 story。
7. 进入 study room，点击每个 anchor 的 mnemonic object 查看信息。
8. 用 Stable Diffusion 生成 image cue，每个显示结果都由 4 张原始候选经 Ollama Vision 自检打分后选出。
9. 第一个最优结果 A 完成后立刻展示，同时后台继续准备 B/C/D 三个最优结果，用来隐藏等待时间。
10. 捕获 memory snapshot。
11. 完成 mid/final image-choice test。
12. 填问卷。
13. 导出 JSON/CSV。

## 2. 工具和环境

推荐：
- Unity `6000.3.12f1`
- Git
- Ollama
- Ollama text model: `qwen3:8b`
- Ollama vision model: `gemma3:12b`
- Stable Diffusion WebUI 或兼容 txt2img API

默认 endpoint：

```text
Ollama: http://localhost:11434/api/generate
Stable Diffusion: http://127.0.0.1:7860/sdapi/v1/txt2img
```

Unity package：
- URP
- Input System
- UGUI
- XR Management
- OpenXR
- AI Navigation
- Test Framework

## 3. 新建项目和目录

新建 Unity project：

```text
MemPalaceLLM
```

建立目录：

```text
Assets/Scripts/Data
Assets/Scripts/Runtime
Assets/Scripts/Services
Assets/Resources
Docs
ExperimentExports
GeneratedRooms
VRSessionPackages
```

不需要手动在 scene 里挂 controller。项目使用 runtime bootstrap 自动创建 controller。

## 4. 第一步：写数据模型

创建：

```text
Assets/Scripts/Data/ExperimentModels.cs
```

定义 stage：

```text
Setup
RoomBuilder
Generation
SelfAuthoring
Study
Recall
Questionnaire
Result
```

定义 condition：

```text
LlmGenerated
SelfGenerated
```

定义 provider：

```text
OllamaLocal
```

定义词表数据：
- `DemoDataLibrary`
  - `wordSets`
  - `sampleMnemonics`
- `WordSetDefinition`
  - `setId`
  - `displayName`
  - `description`
  - `words`
- `WordEntry`
  - `word`
  - `meaning`

定义 mnemonic 数据：
- `SampleMnemonicItem`
- `MnemonicItemData`

这两个类至少包含：

```text
word
meaning
anchorId
anchorLabel
visualCue
associationPrompt
mnemonic
imagePrompt
imagePromptCandidates
selectedImagePrompt
selectedImageCandidateIndex
imageSelectionReason
imageCuePath
objectShape
colorHex
visualObjects
```

定义 `VisualObjectSpec`：

```text
label
primitiveShape
colorHex
localPosition
scale
effect
```

定义实验响应和导出：
- `RecallResponse`
- `SnapshotTestResponse`
- `QuestionnaireResponse`
- `InteractionLog`
- `ExportWordEntry`
- `ExperimentSessionExport`

原则：
- 只要某个字段需要出现在最终实验数据里，就要进入 `ExportWordEntry` 或 `ExperimentSessionExport`。

## 5. 第二步：写房间模型

创建：

```text
Assets/Scripts/Data/RoomSpecModels.cs
```

定义：
- `CameraPoseDefinition`
  - `position`
  - `eulerAngles`
- `RoomPrimitiveDefinition`
  - `id`
  - `label`
  - `primitiveShape`
  - `colorHex`
  - `position`
  - `scale`
  - `rotationEuler`
  - `showLabel`
  - `labelHeight`
- `AnchorDefinition`
  - `id`
  - `label`
  - `primitiveShape`
  - `colorHex`
  - `position`
  - `scale`
  - `rotationEuler`
  - `mnemonicOffset`
  - `labelHeight`
  - `modelParts`
- `RoomSpecDefinition`
  - `roomId`
  - `roomName`
  - `generatedBy`
  - `sourcePrompt`
  - `summary`
  - `overviewCamera`
  - `studyCamera`
  - `environmentPrimitives`
  - `anchors`

再写 `RoomSpecCatalog`：
- `CurrentRoom`
- `Anchors`
- `AnchorCount`
- `RoomName`
- `SetCurrentRoom`
- `ReloadResourceRoom`
- `GetAnchor`
- `GetAssignmentAnchor`
- `EnsureDefaults`
- `CreateFallbackRoom`

加载规则：
- 优先从 `Resources.Load<TextAsset>("MemPalaceRoomSpec")` 读取。
- 如果没有资源或资源无效，就用 `CreateFallbackRoom`。

## 6. 第三步：准备资源 JSON

创建：

```text
Assets/Resources/MemPalaceDemoData.json
```

最小例子：

```json
{
  "wordSets": [
    {
      "setId": "alpha",
      "displayName": "Set Alpha",
      "description": "Spanish noun set.",
      "words": [
        { "word": "mochila", "meaning": "backpack" }
      ]
    }
  ],
  "sampleMnemonics": []
}
```

创建：

```text
Assets/Resources/MemPalaceRoomSpec.json
```

最小例子：

```json
{
  "roomId": "room_001",
  "roomName": "Memory Room",
  "generatedBy": "manual",
  "sourcePrompt": "",
  "summary": "",
  "overviewCamera": {
    "position": { "x": 0, "y": 5, "z": -6 },
    "eulerAngles": { "x": 55, "y": 0, "z": 0 }
  },
  "studyCamera": {
    "position": { "x": 0, "y": 1.6, "z": -3 },
    "eulerAngles": { "x": 0, "y": 0, "z": 0 }
  },
  "environmentPrimitives": [],
  "anchors": []
}
```

创建：

```text
Assets/Resources/MnemonicCueFrameRag.json
```

最小例子：

```json
{ "cases": [] }
```

注意：
- Unity `JsonUtility` 对字段名很严格。
- JSON 字段名必须和 C# public field 一样。
- `List<T>` 在 JSON 里就是 array。

## 7. 第四步：写 Bootstrap

创建：

```text
Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs
```

逻辑：
1. 加 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`。
2. 场景加载后检查是否已有 `MemoryPalaceExperimentController`。
3. 如果没有，就新建 GameObject。
4. `DontDestroyOnLoad`。
5. `AddComponent<MemoryPalaceExperimentController>()`。

这样只要打开一个空 scene，Play 后 app 就会启动。

## 8. 第五步：写主 Controller

创建：

```text
Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
```

先写状态字段：

```text
stage
condition
participantId
ollamaBaseUrl
ollamaModel
imageGenerationEndpoint
imageCueValidationModel
activeWordSet
currentItems
viewedWords
memorizedWords
memorySnapshots
snapshotTestResponses
questionnaire
interactionLogs
```

关键常量：

```text
MidTestTriggerCount = 3
RequiredImagePromptCandidateCount = 4
BufferedImageCueResultCount = 4
AdvancedPoolSetId = "advanced_pool"
```

先实现这些生命周期：
- `Awake`
- `Update`
- `OnGUI`

然后按 stage 写 UI：
1. `DrawSetupView`
2. `DrawRoomBuilderView`
3. `DrawGenerationView`
4. `DrawSelfAuthoringView`
5. `DrawStudyView`
6. `DrawRecallView`
7. `DrawQuestionnaireView`
8. `DrawResultView`

建议顺序：
- 先做 desktop IMGUI。
- 等实验流程能跑通，再做 VR panel。

## 9. 第六步：渲染房间和 anchor

在 controller 里写一个函数，例如：

```text
BuildStudyRoom()
```

它应该：
1. 清空旧 room root。
2. 遍历 `RoomSpecCatalog.CurrentRoom.environmentPrimitives`。
3. 用 Unity primitive 创建地板、墙、家具外壳。
4. 遍历 `RoomSpecCatalog.CurrentRoom.anchors`。
5. 创建 anchor 物体、collider、label。
6. 为 anchor 加 `RoomAnchorInteractable`。
7. 如果已有 `MnemonicItemData`，在 anchor 附近生成 mnemonic object。
8. mnemonic object 上挂 `StudyInteractable`。

`StudyInteractable` 至少要保存：

```text
MnemonicItemData Data
```

点击 mnemonic object 后：
- 设置 `selectedStudyItem`。
- 在 UI 或 VR panel 显示 visual cue、association prompt、Cue Story、图片 cue。

## 10. 第七步：写 Mock Generator

创建：

```text
Assets/Scripts/Services/MockMnemonicGenerator.cs
```

用途：
- 没有 Ollama 时也能跑流程。
- sample mnemonic 存在时用 sample。
- 否则根据 word/meaning/anchor 生成简单 fallback。

每个 fallback item 至少填：
- `word`
- `meaning`
- `anchorId`
- `anchorLabel`
- `visualCue`
- `associationPrompt`
- `mnemonic`
- `imagePrompt`
- `imagePromptCandidates`
- `visualObjects`

## 11. 第八步：写 Ollama Service

创建：

```text
Assets/Scripts/Services/OllamaLlmService.cs
```

请求类：
- `OllamaGenerateRequest`
- `OllamaGenerateResponse`
- `OllamaRequestOptions`

生成结果类：
- `GeneratedMnemonicEnvelope`
- `GeneratedMnemonicItem`

公开方法：
- `GenerateMnemonics`
- `RegenerateMnemonicItem`
- `GenerateRoomSpec`
- `GenerateGuidedFurnitureLayout`

核心是 `GenerateMnemonics`。

它应按 chunk 处理 words：
1. 取 3 个词一组。
2. 调用 Call 1 生成 visual cue 和 image prompts。
3. 对齐 word。
4. 调用 Call 2 生成 Cue Story。
5. 对齐 word。
6. `MergeVisualCueAndCueStory`。
7. `BuildMnemonicItemData`。

## 12. 第九步：写 Prompt

Call 1 prompt 函数：

```text
BuildVisualCuePrompt
```

Call 1 输入：
- word
- meaning
- assigned anchor id/label
- RAG guidance

Call 1 输出：

```text
visual_cue_en
association_prompt_en
image_prompt_en
image_prompt_candidates_en
visual_objects
```

Call 1 规则：
- 只生成画面，不生成 mnemonic。
- meaning-first。
- 不用西语读音、拼写、pun。
- cue 和 anchor 必须物理交互。
- 避免 target object display。
- 避免 whole room overview。
- 避免文字、标签、logo、箭头、危险内容。
- 生成 4 个 image prompt candidates。

Call 2 prompt 函数：

```text
BuildCueStoryPrompt
BuildSingleCueStoryPrompt
```

Call 2 输入：
- word
- meaning
- visual cue
- association prompt
- visual objects

Call 2 输出：

```text
mnemonic_en
```

Call 2 规则：
- Cue Story 是 learner-friendly micro-story。
- 帮助记 meaning 和 Spanish word form。
- 西语词在 `mnemonic_en` 出现一次。
- 1 到 2 句，18 到 45 words。
- 不引入新物体。
- 不复述 image scene。
- 不说 repeat the word。

推荐 few-shot：

```text
word: playa
meaning: beach
mnemonic_en: "The Spanish word for beach is playa. Imagine you are happy to play at the beach, so the sound links to play."
```

## 13. 第十步：写 RAG Helper

创建：

```text
Assets/Scripts/Services/MnemonicCueFrameRag.cs
```

它不是向量 RAG，只是轻量 keyword retrieval。

需要实现：
- 读取 `Resources/MnemonicCueFrameRag.json`。
- 根据 word/meaning/anchor keywords 给 case 打分。
- 把 top cases 加进 prompt。
- 永远加一个 failure checklist。
- 根据 anchor label 给 affordance 提示：
  - door handle
  - shelf surface
  - chair seat
  - air conditioner vent
  - cabinet top
  - 等。

## 14. 第十一步：图片生成、自检选图和 latency hiding

在 controller 中实现：

```text
GenerateMnemonicImageCueRoutine
```

核心概念：
- `RequiredImagePromptCandidateCount = 4`：一次 best-of-4 里生成 4 张原始图。
- `BufferedImageCueResultCount = 4`：为用户准备 A/B/C/D 四个可切换结果。
- A、B、C、D 每一个都不是原始图，而是各自从 4 张原始图里经 vision 打分选出的最优图。
- 这叫 asynchronous speculative pre-generation / latency hiding：先显示 A，再后台准备 B/C/D。

步骤：
1. `GenerateMnemonicImageCueRoutine` 清空旧图片池。
2. 外层循环 4 次，准备 A/B/C/D。
3. 每次外层循环调用 `GenerateBestImageCueVariantRoutine`。
4. `GenerateBestImageCueVariantRoutine` 调 `BuildMnemonicImagePromptCandidates`。
   - 从 `imagePromptCandidates` 开始。
   - 不够时用 `imagePrompt`、`associationPrompt`、`visualCue` 补。
   - 给每个 candidate 加构图说明。
5. 对 4 个原始 candidate 分别调 Stable Diffusion txt2img。
6. 把 base64 image 转成 `Texture2D`。
7. 调 `ValidateImageCueSubjectsRoutine`。
8. vision prompt 用 `BuildImageCueValidationPrompt`。
9. 解析 vision JSON。
10. 用 `ScoreImageCueValidation` 打分。
11. 保存 pass 且分数最高的原始图作为当前显示结果。
12. A 完成时立即显示；B/C/D 继续在后台生成和评分。
13. Desktop UI 用左右按钮切换已经准备好的 A/B/C/D。
14. 再点 Regenerate 时，不是切到下一张原始图，而是重新开始一整轮新的 A/B/C/D。

Stable Diffusion request 建议：

```text
width = 512
height = 512
steps = 28
cfg_scale = 8.5
sampler_name = "DPM++ 2M"
```

Vision validation JSON 需要：

```text
pass
anchor_visible
cue_visible
focus_ok
meaning_specific
foreground_clear
anchor_interaction
simple_scene
familiar_objects
novel_possible_relation
no_room_overview
caption
reason
missing_or_wrong
```

选择规则：
- pass 候选胜过 non-pass。
- pass 状态相同，score 高的胜出。
- 如果 vision 中途失败，只能保存 best available，并记录原因。

需要的数据结构：
- `ImageCueCandidateResult`
- `imageCueCandidateResults`
- `displayedImageCueCandidateIndexes`

需要的辅助函数：
- `GenerateBestImageCueVariantRoutine`
- `TryCycleDisplayedImageCueCandidate`
- `TryDisplayImageCueCandidate`
- `BuildImageCueResultLabel`
- `BuildImageCueCandidateDisplaySummary`
- `ClearImageCueCandidatePool`

截图规则：
- `CaptureMemorySnapshotRoutine` 不需要额外改。
- 它读取当前显示的 `mnemonicImageCues[item.word]`，所以会截下用户当前切到的 A/B/C/D。

## 15. 第十二步：Study 和 Snapshot Test

Study 阶段要做：
- 进入房间。
- 点击每个 mnemonic object。
- 显示 cue 信息。
- 可生成 image cue。第一个最优结果先显示，后续最优结果后台准备。
- Desktop UI 可左右切换已准备好的 A/B/C/D。
- 可捕获 memory snapshot。

需要函数：
- `CaptureMemorySnapshotRoutine`
- `CaptureCurrentCameraSnapshot`
- `BeginSnapshotTest`
- `DrawRecallView`

mid test：
- 至少 3 个 snapshot 后解锁。

final test：
- mid test 完成。
- 所有 item 都有 snapshot。

每题：
- 1 个正确 snapshot。
- 2 个 distractor。
- 记录到 `SnapshotTestResponse`。

## 16. 第十三步：问卷和导出

问卷字段在 `QuestionnaireResponse`。

实现：
- `DrawQuestionnaireView`
- `FinalizeAndExport`
- `BuildExportPayload`
- `WriteExportFiles`
- `BuildRecallCsv`

导出位置：

```text
ExperimentExports/session_<participant>_<timestamp>.json
ExperimentExports/session_<participant>_<timestamp>.csv
```

JSON 需要包含：
- participant/session
- word set
- room info
- condition
- LLM model
- study duration
- viewed/memorized count
- mid/final test score
- questionnaire
- 每个 item 的 cue、prompt、mnemonic、selected image info
- interaction logs

CSV 可以简化，但至少要能用于统计。

## 17. 第十四步：Room Builder

最小可用版本：
- 显示当前 room。
- 添加 anchor。
- 移动、缩放、旋转、删除 anchor。
- 保存到 `MemPalaceRoomSpec.json`。

当前项目更复杂，还包含：
- guided floor plan
- furniture placement suggestion
- wall/floor primitive editing
- shell repair
- overlap resolution
- top-down preview

建议最后再重建复杂 builder，不要一开始就做。

## 18. 第十五步：VR 支持

可以先完成 desktop 版本，再做 VR。

VR 需要：
- runtime camera/head pose
- pointer ray
- world-space study panel
- button interactables
- image preview
- selected item display

按钮：
- Generate Image Cue
- Capture Memory Snapshot
- Start Mid Test
- Start Final Test

Desktop 控制也要保留：
- right mouse drag 看方向
- WASD 移动
- Q/E 上下
- Shift 加速
- left click 选择 mnemonic object

## 19. 推荐重建顺序

按这个顺序最稳：

1. 数据模型。
2. 房间模型和 fallback room。
3. Bootstrap。
4. Controller 的 Setup/Study 最小 UI。
5. 读取 `MemPalaceDemoData.json`。
6. 渲染 room primitives 和 anchors。
7. Mock mnemonic generation。
8. Study item 选择和 label。
9. Snapshot capture。
10. Recall/snapshot test。
11. Export。
12. Ollama mnemonic generation。
13. Call 1 / Call 2 prompt 分离。
14. Stable Diffusion image generation。
15. Ollama Vision validation 和 best-of-4 selection。
16. Latency hiding：A 先显示，B/C/D 后台准备，UI 可切换。
17. Room builder。
18. VR panel/pointer。
19. Prompt polish、RAG、guardrails。

不要从 VR 或完整 room builder 开始。它们很大，会掩盖核心实验流程。

## 20. 检查清单

每次重建或大改后检查：
- Unity 没有 compile error。
- Play 后自动创建 `MemoryPalaceExperiment`。
- Setup UI 出现。
- 词表能加载。
- Room JSON 缺失时 fallback room 能出现。
- LLM Generated 能调用 Ollama 并解析 JSON。
- Self Generated 不依赖 Ollama 也能跑。
- Study room 显示 anchor 和 mnemonic object。
- 点击 item 能看到 visual cue、association prompt、Cue Story。
- 图片生成能产生 A/B/C/D 四个显示结果。
- 每个显示结果都来自一轮 4 张原始图的 vision best-of-4 选择。
- A 完成后能先显示，B/C/D 能继续在后台准备。
- 左右按钮能切换当前已经准备好的显示结果。
- vision model 能选择候选并记录 reason。
- snapshot capture 正常。
- mid/final test 正常记录。
- questionnaire 能完成。
- JSON/CSV 能写入。

## 21. 常见错误

Ollama JSON 解析失败：
- request 设置 `format = "json"`。
- prompt schema 要简单。
- 加 JSON repair pass。
- 输出字段名必须和 C# 一致。

三个字段互相复制：
- 强化 Call 1/Call 2 分离。
- 不让 Cue Story 改写 image prompt。
- 必要时引入内部 `cue_blueprint`。

图片只出现房间或只出现 cue：
- prompt 明确 anchor 和 cue 都必须可见。
- vision prompt 严格 reject。
- 生成多个 foreground candidate。

旧日语字段误留：
- 当前项目是英语-only。
- 不要再加入 `meaningJa`、`visualCueJa`、`associationPromptJa`、`mnemonicJa`、`imagePromptJa`。
- 资源 JSON、模型、prompt schema、UI、导出字段都保持英文。

Git 误提交 build：
- 先看 `git status -sb`。
- 只 stage 明确需要的文件。
- 不提交 APK、build backup、Burst debug folder。

## 22. 修改位置速查

改 prompt：
- `Assets/Scripts/Services/OllamaLlmService.cs`
- `BuildVisualCuePrompt`
- `BuildCueStoryPrompt`
- `BuildSingleCueStoryPrompt`

改数据字段：
- `Assets/Scripts/Data/ExperimentModels.cs`
- `OllamaLlmService.GeneratedMnemonicItem`
- `BuildMnemonicItemData`
- controller UI
- export mapping

改图片自检标准：
- `BuildImageCueValidationPrompt`
- `ImageCueValidationResult`
- `ScoreImageCueValidation`

改 latency hiding / A-B-C-D 切换：
- `GenerateMnemonicImageCueRoutine`
- `GenerateBestImageCueVariantRoutine`
- `ImageCueCandidateResult`
- `imageCueCandidateResults`
- `displayedImageCueCandidateIndexes`
- `TryCycleDisplayedImageCueCandidate`
- `TryDisplayImageCueCandidate`

改词表：
- `Assets/Resources/MemPalaceDemoData.json`

改 RAG：
- `Assets/Resources/MnemonicCueFrameRag.json`
- `Assets/Scripts/Services/MnemonicCueFrameRag.cs`

改模型默认值：
- `MemoryPalaceExperimentController.cs`
- `ollamaBaseUrl`
- `ollamaModel`
- `imageGenerationEndpoint`
- `imageCueValidationModel`

改导出：
- `ExperimentModels.cs`
- `BuildExportPayload`
- `BuildRecallCsv`

改房间格式：
- `RoomSpecModels.cs`
- `MemPalaceRoomSpec.json`
- room builder 保存/加载逻辑。

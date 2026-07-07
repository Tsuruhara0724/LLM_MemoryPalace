# 手工重建指南 - LLM Memory Palace

最后核对：2026-07-08

## 1. 当前目标

重建一个 Unity Desktop/VR 西班牙语词汇记忆宫殿实验：

1. 研究者选择房间和词表。
2. `LLM Story` 条件使用 Ollama 或 Gemini 为全部目标词生成一个连续英文故事，并把固定本地词图分配到家具锚点。
3. `Self-Chosen Pictures` 条件不生成故事；参与者进入分配房间，自行为每个家具选择一个单词和对应本地图片。
4. 参与者进入房间，自主学习 20 分钟。
5. 完成最终确定的测验、问卷和 JSON/CSV 导出。

当前实验有 `LLM Story` 与 `Self-Chosen Pictures` 两个条件。后者只让参与者选择“家具—单词—固定本地图”的配对，不调用 Stable Diffusion 生成图片。

## 2. 环境

- Unity `6000.3.12f1`
- Universal Render Pipeline
- Input System
- UGUI
- XR Management + OpenXR
- Ollama，默认 `http://localhost:11434/api/generate`
- 默认故事模型 `gemma3:12b`
- 在线 Gemini 默认模型 `gemini-2.5-flash`；Windows 桌面端依次读取用户级 `GEMINI_API_KEY`、`GOOGLE_API_KEY` 和 Setup 本地保存值
- 故事先生成结构化因果计划，再生成正文；程序会强制检查“前一节点的结果”与“后一节点的需求”完全衔接，不合格时直接报错，不展示无逻辑的备用故事。
- 学习阶段的单词图片在距离当前锚点约 `8 m` 时出现，并在约 `8.5 m` 范围内保留详情 UI。

## 3. Unity 工程结构

```text
Assets/Scenes/SampleScene.unity
Assets/Scripts/Data/ExperimentModels.cs
Assets/Scripts/Data/RoomSpecModels.cs
Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs
Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs
Assets/Scripts/Services/OllamaLlmService.cs
Assets/Scripts/Services/WordImageCatalog.cs
Assets/Scripts/Services/ElevenLabsTextToSpeechService.cs
Assets/Resources/MemPalaceDemoData.json
Assets/Resources/MemPalaceRoomSpec.json
Assets/Resources/ExampleRooms/example_room.json
Assets/Resources/WordImages/
```

场景不需要手工挂主控制器。`MemoryPalaceBootstrap` 在场景加载后自动创建 `MemoryPalaceExperimentController`。

## 4. 数据模型

核心模型位于 `ExperimentModels.cs`：

- `WordEntry`: `word`, `meaning`
- `WordImageItemData`: 单词、含义、锚点、故事顺序、故事片段、图片路径
- `StorySessionData`: 完整故事、来源、模型、时间和有序词项
- `SnapshotTestResponse`: 旧版图片识别测验记录
- `QuestionnaireResponse`: NASA-TLX 风格和 1-7 主观评分
- `ExperimentSessionExport`: 完整会话导出，包括自选配对耗时、是否进入全部照片展示及其展示时长

旧的 `MnemonicItemData` 和图片生成字段仍保留，以兼容大控制器中的历史代码；新功能不要继续依赖这些旧字段。

## 5. 词表

编辑 `Assets/Resources/MemPalaceDemoData.json`：

```json
{
  "setId": "example",
  "displayName": "Example Set",
  "description": "Eight Spanish nouns",
  "words": [
    { "word": "estrella", "meaning": "star" }
  ]
}
```

正式材料从 `formal_12_pool` 中无重复抽取 8 个词。

## 6. 本地词图

每个词保存一张：

```text
Assets/Resources/WordImages/{lower-case Spanish word}.png
```

`WordImageCatalog` 使用 `Resources.Load<Texture2D>("WordImages/{word}")` 加载。缺图时使用 `_placeholder.png`。

替换图片时：

1. 保持文件名与西语 `word` 完全一致。
2. 使用清楚、单义、少干扰的图片。
3. 不要在图片中直接写答案。
4. 更新 `Assets/Resources/WordImages/ATTRIBUTION.md`。
5. 在 Unity 中确认实际显示效果。

## 7. 连续故事生成

入口：`OllamaLlmService.GenerateStory`。

当前使用两阶段本地生成：第一阶段只输出每个词的 `need -> action -> result` 因果计划并检查字段与词集完整性；第二阶段先修复计划中不符合物理常识的连接，再依据计划写成连续故事。两阶段都使用本地 Ollama，不消耗 ElevenLabs 额度。

LLM 输出：

```json
{
  "fullStory": "continuous English story",
  "items": [
    { "word": "estrella", "storyOrder": 1 }
  ]
}
```

故事要求：

- 一个明确问题或目标、逐步升级的冲突、转折和结尾；
- 目标物必须参与行动并产生后果，而不是只承担环境描写；
- 每个目标词都必须同时具备“前因、具体动作、可观察后果”；前一事件为它创造使用理由，它的动作再推动下一事件；
- 禁止仅用“于是、因此、随后”等连接词把无关动作伪装成因果关系；删除任一目标词事件后，故事因果链应当无法保持完整；
- 故事中段以后不能放弃最初问题，转入与主线无关的游行、舞蹈、庆典或奇观；
- 至少 80% 的句子必须表现人物主动行动、决定、反应或纠错；目标词不能仅以远景、光影、回声、等待、悬挂、倒影等状态出现；
- 通常每句只引入一个新目标词，并在同一句中交代使用理由、人物对它采取的动作以及立即产生的结果；
- 环境气氛描写最多作为一个开场句，大部分句子应包含行动、决定、反应或结果；
- 整体语气应明亮、日常、温暖，可以有喜剧性意外，但避免恐怖、梦境逻辑、诡异拟人和现实扭曲；
- 不是购物清单、房间导览或孤立物体画面；
- 每个目标词以 `English meaning (Spanish word)` 出现；
- 不出现家具或锚点名称；
- 每个目标词只建立一个路线项；
- 允许模型选择最自然的故事顺序。

解析器会修复部分缺词和坏 JSON，并能从完整故事恢复词序。任一 Ollama 阶段失败或最终故事未通过质量检查时，程序会停止并显示错误，不再把旧的本地 fallback 当作可用故事展示。

## 8. 房间与锚点

- 默认房间：`MemPalaceRoomSpec.json`
- 示例房间：`ExampleRooms/example_room.json`
- 也可以通过 Grid Room Builder 创建房间。
- 房间必须有足够多、容易区分且可到达的家具锚点。
- 家具只定义空间路线，不参与故事文本生成。

## 9. 学习阶段

当前设计要求参与者进入房间后自主学习 20 分钟。

学习 UI 显示：

- 完整连续故事；
- 当前词的西语、英语含义、图片、故事片段和锚点；
- 已浏览数量、截图数量和经过时间。

房间中的单词图片默认全部隐藏。参与者进入当前语音路线锚点约 8 米范围时，只显示该锚点的图片；详情 UI 在约 8.5 米内保留。看向图片后自动视为已查看。图片会向观察者方向偏移并以前景 UI 方式渲染，避免与墙面或家具穿模。未启用语音路线时，则只显示参与者附近最近的一个图片。

目前程序只显示经过时间，没有强制锁定 20 分钟。实现硬计时之前，研究者需要外部计时并记录真实开始/结束时间。

语音路线由 `ElevenLabsTextToSpeechService` 和主控制器中的 `VoiceRoutePhase` 状态机完成：

1. 播放下一个家具锚点和目标词图片的引导语。
2. 等待参与者靠近并实际查看正确图片。
3. 播放该词对应的 `StorySessionData.orderedItems[].storySegment`。
4. 收到播放完成回调后进入下一个锚点。

Windows 编辑器、桌面版和 Android/Quest 均调用 ElevenLabs `eleven_multilingual_v2`。Setup 中输入 API Key 和 Voice ID；桌面环境也可用 `ELEVENLABS_API_KEY`、`ELEVENLABS_VOICE_ID`。API Key 只保存在运行时内存中，不应写入项目或提交到版本库。服务使用 Stability `0.42`、Similarity `0.78`、Style `0.20`、Speed `0.96`，以减少系统语音的机械感，同时保留路线推进所需的播放完成回调。

VR HMD 模式会在 world-space 学习面板显示当前正在播放的引导语或故事片段字幕。桌面和 VR 学习画面顶部都有细进度条，可在当前已加载语句内任意前后拖动；`Replay Voice` 会直接从头重播该语句。

## 10. 测验、问卷和导出

旧原型仍提供 mid/final 三选一截图识别测验。这部分是否作为 20 分钟学习后的正式测验仍需确认。

两个条件在 Study 结束、进入 final test 之前都提供可选的“查看全部照片”。它不是新页面，而是在原房间中同时显示每个已分配家具上的单词+图片 UI；不倒计时，参与者自行选择进入、结束或跳过。选择与实际展示时长会写入 JSON 导出。

问卷包括：

- Mental Demand, Physical Demand, Temporal Demand
- Performance, Effort, Frustration
- Vividness, Helpfulness, Trust
- 自由备注

导出目录：`ExperimentExports/`。必须同时检查 JSON 和 CSV。

## 11. 最小验证顺序

1. Unity Console 无 C# 错误。
2. 所有目标词都加载真实图片，不使用 `_placeholder.png`。
3. Ollama 能返回合法连续故事。
4. 故事包含全部目标词且不泄漏家具名称。
5. 所有图片锚点都能在房间中找到和点击。
6. 完成一次 20 分钟计时测试。
7. 完成预定测验和问卷。
8. 成功导出并人工核对 JSON/CSV。
9. 若使用 VR，在目标头显上重复全流程。
10. 验证语音引导、到达判定、小节播放、自动下一站、重播和路线重启。

## 12. 当前技术债

- 主控制器过大，混合 UI、房间、学习、测验、VR 和导出。
- 旧 mnemonic、Gemini、Stable Diffusion 和 A-D 图片候选代码尚未清除。
- 没有自动化测试保护故事解析和导出格式。
- 20 分钟学习窗口尚未由程序强制。

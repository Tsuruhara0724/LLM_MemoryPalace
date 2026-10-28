# 项目维护索引

核对日期：2026-10-28。日常交接以 [共同开发者指南](DEVELOPER_GUIDE.md) 为入口；服务操作见 [STARTUP.md](STARTUP.md)，素材限制见 [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.md)。

## 当前实现

- Unity 6000.3.12f1；入口为 `Assets/Scenes/SampleScene.unity`。
- 当前研究条件是 Self / Example room × Self / LLM story 的四条件设计。旧三条件、baseline 和 20 分钟说明属于历史方案。
- 自建房间至少放置 9 种家具，正式学习选取 8 个未知词；阶段计时是目标提示，不是统一的自动跳转。
- 自写故事使用英文，离线词形表识别英语变形；Split 后的学习文本在对应英语词形后标记西班牙语。保留原始故事，不改成要求参与者输入西班牙语。
- LLM 故事采用 MSF-2.1 planner / writer / repair。运行入口是 `OllamaLlmService.GenerateStory`；类名是历史兼容名称，不代表当前只能使用本地 Ollama。
- VR 靠近正确家具后保留高亮，须点击家具才打开学习卡。学习期间暂停虚拟平移、转向及放大的步行增益，始终保留真实头部追踪。流程见 [VR_CLICK_TO_LEARN.md](VR_CLICK_TO_LEARN.md)。

## 代码定位

| 文件 / 目录 | 职责 |
| --- | --- |
| `Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs` | 场景启动与缺失控制器补建 |
| `MemoryPalaceExperimentController.cs` 及其 partial 文件 | 阶段、Building、Writing、Matching、学习、测试、导出及桌面 / VR UI |
| `Assets/Scripts/Services/OllamaLlmService.cs` | 模型请求、故事规划与修复、响应解析；仍保留有效的房间 / 家具接口 |
| `Assets/Scripts/Services/MnemonicStoryPrompts.cs` | 当前故事提示词策略 |
| `Assets/Scripts/Services/ParticipantStoryEnglish.cs` | 英文词形匹配与学习标注 |
| `Assets/Scripts/Services/ElevenLabsTextToSpeechService.cs` | 预生成词音频与 Azure 本地代理的朗读接口；类名保留兼容 |
| `Assets/Scripts/Services/FurniturePrefabAvailability.cs` | 实例化前检查家具 Mesh / Material；失效时使用程序家具 |
| `Assets/Scripts/Data/` | 会话、故事、研究结果与房间数据模型 |
| `Assets/Resources/` | 词表、示例房间、离线词形表、Shaders 及研究材料 |
| `Assets/Editor/` | 离线工程检查、构建入口及资源工具 |
| `Tools/` | 服务启动、词音频生成、设备传包、延迟测试网页、协作包导出 |

表中的短文件名位于 `Assets/Scripts/Runtime/`；其他路径按表内完整位置。

## 维护边界

Unity 的场景事件、Resources 路径、Editor 检查中的字符串反射及序列化字段都可能形成依赖，不能仅凭 IDE 的“零引用”删除。仍被有效离线目录工具使用的图像生成、目录导入和共享 anchor 规范化代码暂时保留。

控制器仍偏大。下一轮可按 Building / Story / Assessment 拆分职责，但应先建立阶段切换和结果兼容性测试，不在交接整理时顺便重写实验流程。程序家具、空白词图和模拟数据仅支持开发，不可与正式研究刺激混用。

提交前运行 `Tools/Memory Palace/Run Project Checks`，并检查 `git diff --check`。这些是离线工程检查，不代表模型输出质量、人类学习效果或 Quest 真机舒适度已验证。

维护者修改协议、模型、词表、资源路径或数据字段时，应同时更新本索引和开发者指南；旧设计说明保留为研究记录，不再作为当前运行手册。

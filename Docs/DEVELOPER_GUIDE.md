# 共同开发者指南

源码核对日期：2026-10-28。本文描述当前实现，不替代研究方案、伦理审批或设备安全规程。开始正式收集前，应由研究负责人冻结代码版本、素材、模型配置和实验参数。

## 1. 项目做什么

这是一个 Unity 桌面 / VR 词汇学习实验。PC 负责房间、词汇预筛、自写 / 选择故事、家具匹配；Quest 负责空间学习及后续测验。PC 也可以用于无头显的开发检查。

当前分组由两个因素组成：

| Setup 条件 | 房间来源 | 故事来源 |
| --- | --- | --- |
| `1 · Self room / Self story` | 参与者创建熟悉房间 | 参与者写英文故事 |
| `2 · Self room / LLM story` | 参与者创建熟悉房间 | 从生成故事中选择 |
| `3 · Example room / Self story` | 预设 `example_room.json` | 参与者写英文故事 |
| `4 · Example room / LLM story` | 预设 `example_room.json` | 从生成故事中选择 |

旧设计稿可能包含三条件、无家具 baseline 或 20 分钟学习描述，不应据此操作当前版本。当前计时目标为房间 10 分钟、预筛 5 分钟、故事 10 分钟、匹配 3 分钟、学习 10 分钟；它们不是统一的强制到时跳转。预筛可以超过 5 分钟，直至找到 8 个未知词；房间熟悉和学习等阶段的实际时长会记录，研究者仍需按获批方案控制时长。

## 2. 环境与首次打开

### 基础环境

- Windows 开发路径已提供 PowerShell / `.cmd` 脚本。
- Unity Hub，Unity **6000.3.12f1**，以 `ProjectSettings/ProjectVersion.txt` 为准。
- ZIP 交接无需 Git；经授权访问完整仓库时才需要 Git。保留 Unity `.meta` 文件，不要只复制 `.cs`。
- Python **3.12** 和 Python Launcher（`py`），用于建立 Azure 代理环境；本地网页服务使用 Python 标准库。
- 若构建 Quest：在 Unity Hub 为同一 Editor 安装 **Android Build Support**、**Android SDK & NDK Tools**、**OpenJDK**，并准备可用于 USB 调试的 Quest。

包由 `Packages/manifest.json` 与 `packages-lock.json` 固定，包括 URP、Input System、UGUI、XR Management 和 OpenXR。首次导入需要包源网络访问；研究期间不要顺手升级包或 Editor。

### 可选：Editor Burst JIT 兼容设置

部分 Windows 机器的安全策略会阻止 Burst 动态生成的 Editor JIT DLL。只有遇到该问题时，才使用 **Tools > Memory Palace > Compatibility > Disable Editor Burst JIT**；需要恢复编译时，手动选择同目录下的 **Enable Editor Burst JIT**。

这两个操作都会先询问确认：它们改变当前 Editor 的 JIT 状态，并保存当前操作系统用户共享的 Editor 偏好，可能影响该用户的其他 Unity 项目；**不修改 Quest / Android Player 的 Burst AOT 设置**。打开项目或脚本重载不再自动改动该偏好，也不会自动启用或恢复。原机器已有的关闭状态会保留，不需要为了协作重新开启。

### 获取源码与场景入口

推荐使用维护者通过 `Tools/Export-CollaborationPackage.cmd` 导出的 `MemoryPalace-Source-*.zip`，解压到新的开发目录。包不含 Git 历史、密钥、参与者数据和未获准分发的研究素材。不要直接索取或压缩维护者的整个工作区。

只有获得研究数据访问授权的协作者才使用完整 Git 仓库；**历史中仍含旧参与者数据**，不能把仓库当前目录已清理理解为历史已匿名化：

```powershell
git clone <repository-url>
```

在 Unity Hub 选择 **Add project from disk**，指向实际包含 `Assets/`、`Packages/`、`ProjectSettings/` 的项目目录。若取到的是外层工作目录，项目位于其 `MemPalaceLLM/` 子目录。

打开 `Assets/Scenes/SampleScene.unity`。这是当前 Build Settings 中启用的入口场景。等待导入与脚本编译完成，检查 Console 没有编译错误，再按 Play。

场景主控制器是 `MemoryPalaceExperiment` 上的 `MemoryPalaceExperimentController`。`MemoryPalaceBootstrap` 会在场景载入后补建缺失的控制器，不要为排错再手工增加第二个。

审核后的 ZIP 不含商业家具、词图 PNG、预生成 WordAudio 或个人 API 配置，使用程序家具和空白图像占位。它可用于界面和交互开发，不能直接视为与研究机器相同的刺激材料，也不能在未补齐材料时完整执行正式学习。具体缺项见 [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.md)。

## 3. 第一次安装与每次启动

### 安装 Azure 代理环境：只需首次执行

从项目根目录双击 `Tools/LocalTtsServer/Setup-Azure.cmd`，或运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\LocalTtsServer\Setup-Azure.ps1
```

脚本在 `.local-tts/azure-venv/` 创建 Python 3.12 虚拟环境，安装 `fastapi` 和 `uvicorn`。安装阶段需要联网；失败先检查 `py -3.12 --version` 和 pip 网络，不必重新安装 Unity。

### 每次开机

运行 `Tools/Start-ProjectServices.cmd`。也可以从项目根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1
```

脚本会复用已正确运行的接口，启动缺少的服务；不自动按 Play、不导出会话包，也不改防火墙。Azure TTS 会打开单独窗口，依提示输入密钥（隐藏输入）和资源区域，保持窗口运行。

| 服务 | 地址 / 端口 | 什么时候需要 |
| --- | --- | --- |
| Azure Speech 代理 | `http://127.0.0.1:8880/health` | 故事及引导语音 |
| Quest 会话包 HTTP | `http://127.0.0.1:8765/session_package_latest.json` | Quest 通过 Wi-Fi 取会话包；USB 传包可不依赖它 |
| 延迟测验网页 | `http://127.0.0.1:8766/` | 后续延迟测验，不是进入 VR 的前提 |
| Ollama | `http://127.0.0.1:11434/api/version` | 本地模型 / 诊断；当前云端故事模式不依赖它 |
| ADB | 本机 `5037` | Quest USB 传包、安装和回收结果 |

一键脚本也尝试启动 Ollama。未安装 Ollama 的报错不等于云端故事不可用。首次还没有会话包时，包服务会提示先导出，这不妨碍在 PC 准备新会话。

只检查、不启动服务：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1 -StatusOnly
```

正常 TTS 健康响应需同时有 `status: ok` 和 `backend: azure`；这只证明代理响应，并不验证 Azure 订阅、余额或合成权限。进入 Unity 的 Researcher settings，用 **Test Configured Voice** 做一次真实语音检查。

单独启动命令见 [STARTUP.md](STARTUP.md)。独立包服务和独立延迟网页原来都默认 `8765`；同时运行时请给延迟网页指定 `-Port 8766`，不要让两者争用端口。Stable Diffusion 不属于当前学习流程的必需服务。

### 停止服务

- Azure TTS：在它自己的窗口按 `Ctrl+C`，再关闭窗口；本次输入的密钥随进程结束失效于本机内存，不会由启动脚本写入项目。
- 手动启动的网页 / 包服务：在对应终端按 `Ctrl+C`。
- 一键脚本后台启动的服务：记录启动时打印的 PID，确认对应命令行后用 `Stop-Process -Id <confirmed-pid>` 停止。不要批量结束全部 Python、PowerShell 或 Unity 进程。
- Ollama 若由桌面应用启动，可从它自己的托盘退出；ADB 可能被其他 Android 项目共用，不必每次关闭。

后台日志在 `.utmp/project-services/`；Azure 错误在独立 TTS 窗口。脚本没有安装 Windows 开机自启动任务。

## 4. API 配置与保密

### 故事 API

前端名称为 **Claude Haiku**、**GPT_Luna**。服务类保留历史文件名 `OllamaLlmService.cs`，但当前请求可走配置的云端接口；不要仅据类名判断必须启动本地模型。

项目支持以下本地 Resources 配置文件；它们应保持 Git 忽略。使用自己的服务地址和可用模型，不要复制他人的密钥。

`Assets/Resources/AnthropicLocalConfig.json` 的字段：

```json
{
  "apiKey": "<your-key>",
  "baseUrl": "<your-messages-endpoint>",
  "model": "<your-model>",
  "authMode": "x-api-key"
}
```

`authMode` 根据服务使用 `x-api-key` 或 `bearer`。支持本机环境变量覆盖：`ANTHROPIC_BASE_URL`、`ANTHROPIC_MODEL`、`ANTHROPIC_AUTH_TOKEN` 或 `ANTHROPIC_API_KEY`，其中 AUTH_TOKEN 优先。

`Assets/Resources/OpenAiLocalConfig.json` 的字段：

```json
{
  "apiKey": "<your-key>",
  "baseUrl": "<your-responses-endpoint>",
  "model": "<your-model>",
  "reasoningEffort": "low"
}
```

支持 `OPENAI_BASE_URL`、`OPENAI_MODEL`、`OPENAI_API_KEY` 覆盖。Unity 需要从已带环境变量的进程启动；不要假设外部终端刚修改的变量会自动进入已打开的 Editor。

**Resources 不等于密钥保险箱。** 其中的 JSON 会进入构建资源，`.gitignore` 不会阻止 Unity 将它打进 APK。共同开发者应使用自己的本地配置；分享可执行包前必须检查构建输入，不能发布含长期提供方密钥的 APK。公开 / 多设备部署应改为研究者控制的服务端转发与访问控制，当前本地开发配置不提供这一安全保证。

### Azure Speech

`Start-Azure.ps1` 读取 `AZURE_SPEECH_KEY`、`AZURE_SPEECH_REGION`，缺少时在窗口询问。启动器本身不永久保存密钥；不要把密钥写到脚本、场景、截图或 README。区域必须和 Speech 资源一致，`japaneast` 只是默认值。

Azure 密钥保留在 PC 代理中，不传给 Unity 或 Quest。PC 默认语音基础地址是 `http://127.0.0.1:8880/v1`；Quest 要用 `http://<PC-IP>:8880/v1`，不是头显自己的 localhost。

一键启动为 TTS 和包服务监听局域网地址。只在可信研究网络使用；不要端口映射到公网。语音缓存位于 `.local-tts/azure-audio-cache/`，可能含会话文本的合成内容，分享时也应排除。

## 5. 研究流程与不能随意改变的约束

1. **Setup**：输入 Participant ID，选择四条件之一。正式词表入口为 **Formal 32-Word Pool**。
2. **房间阶段**：Self room 进入 Builder；Example room 使用 `Assets/Resources/ExampleRooms/example_room.json` 并在 PC 熟悉房间。当前 Builder 至少需要 **9 种不同的有效家具**，不是同一家具的 9 份复制。学习目标仍为 8 词。
3. **Spanish Pre-test**：从 32 词池筛出 8 个该参与者未知的词。候选顺序与随机种子会记录；不足 8 个不能直接跳过预筛进入研究。
4. **故事阶段**：Self story 写完整英文故事，按 **Split Story Into Sentences**，逐句检查目标词分类；修改后用 **Refresh Sentence Split**。LLM story 模式生成候选并由参与者选择，不由家具位置决定文本。
5. **Assign Words**：在 PC 点击实际家具模型，再从 2 × 4 词卡选择词。一个词对应一个家具，一个家具对应一个词；已使用词不能重复分配。可 **Clear current assignment** 后重配。完成后使用 **Finish Assignment And Enter VR Study**。
6. **VR Study**：完成 Ready / **Start learning** 后按路线找家具、点击学习、拍摄场景视图，见下节。
7. **测验与结果**：完成配置的空间锚点、词图、词义测验，保存 JSON / CSV。Google Form 在之后填写时使用同一个 Session ID 关联，不依靠姓名拼接数据。

当前显示的计时目标不保证每阶段强制足时。不要为“让流程更顺”跳过就绪检查、首次路线、朗读完成或截图要求，也不要悄悄改计分、词数、条件枚举值、导出字段和版本标签。研究流程变更需要同步说明、回归测试和版本冻结。

### 自写故事：仍然只输入英语

本地变形表支持常见规则 / 不规则动词、复数、比较级等。Split 后可在实际英文表面词后补充对应西语，例如 `forgot (olvidar)`；参与者不需要手写西语。歧义匹配会留下 **Unassigned**，仍需人工确认，不是完整语义分析器。

`originalParticipantStory` 保留原始输入；学习用 `fullStory`、`storySegment` 和 mnemonic 使用标注后文本。不要用显示版本覆盖原始故事。旧会话包能载入，但不会自动获得新标注，需要从 PC 重新 Split / 导出。详情见 [PARTICIPANT_STORY_ENGLISH.md](PARTICIPANT_STORY_ENGLISH.md)。

### 当前 VR 是点击学习，不是靠近后自动弹出

- 靠近并面向正确家具仍高亮；高亮本身不自动弹卡或开始朗读。
- 用右手射线指向家具模型并扣扳机，满足距离、顺序和无遮挡条件后打开屏幕固定的学习卡。
- 学习期间暂停摇杆位移、snap turn 和放大的实体行走，保留原生头部位置 / 朝向跟踪；不会将相机强制锁死在空间中。
- 左摇杆移动；右摇杆左右为 30° snap turn。退出卡片后摇杆回中才恢复移动。
- 右手 `A` / 左手 `X` 拍摄场景视图，截图会隐藏学习 UI。
- 首轮路线不能跳过；朗读结束才开放 **Next furniture**。完成路线且所有学习目标都有截图后，才可 **Finish learning**。

具体回退、跟踪丢失与重播行为见 [VR_CLICK_TO_LEARN.md](VR_CLICK_TO_LEARN.md)。`Docs/ParticipantGuides/VR_Learning_Guide_EN_JA.png` 仍描绘旧的自动 / 注视学习交互，不要直接作为本版参与者说明。

屏幕固定卡片是交互设计变更，尚不能据此宣称已降低晕动症。头显佩戴、空间边界和不适退出仍需研究者按安全规程管理。

## 6. PC 与 Quest 的分工、传包和回收

### PC 准备会话，Quest 执行学习

1. 在 PC 完成房间、预筛、故事和家具分配，确认就绪。
2. 在 Researcher settings 中使用 **Write Quest Session Package**，生成 `VRSessionPackages/session_package_latest.json`。
3. Quest 使用 USB 本地文件或 Wi-Fi URL 取包。会话包包含研究内容，不是可公开的演示素材。
4. 开始新参与者前确认 Participant ID、Session ID、房间、故事和分配正确，避免使用上一次缓存包。

USB 传包前，Quest 需允许 USB debugging，`adb devices` 应显示已授权设备；`adb` 需要在当前终端 PATH。可将 Unity 所带 SDK 的 `platform-tools` 加入本机 PATH。然后运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Push-VRSessionPackageToQuest.ps1
```

脚本从 Project Settings 读取 Android package ID，将包复制到 `/sdcard/Android/data/<package-id>/files/session_package_latest.json`。首次应已安装并启动过应用；传完后启动 / 重启 Quest 应用，让 Setup 读取本地包。多个设备时使用 `-Device <serial>`，不能确定包名时用 `-PackageId <id>`。

Wi-Fi 取包时，PC 和 Quest 要互通，使用启动器打印的 `http://<PC-IP>:8765/session_package_latest.json`。允许可信私有网络的必要端口即可。PC IP 或 TTS 配置变化后要在 Unity 重新导出；PC 本机健康接口成功不代表 Quest 可访问。

**会话包只更换内容与配置，不更新 C#、shader 或场景构建。** 程序修改后需要重新构建 / 安装 APK；仅推 JSON 不会带上新交互代码。

### 结果回收

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Pull-QuestResultsToPC.ps1
```

默认回收到 `QuestResultImports/<timestamp>/`，包含 `ExperimentExports` 和可用的 `MemoryPalaceSessionData` 截图目录。确认 JSON / CSV 与截图都已回收并按研究规则备份后，再清理设备；脚本不会替你完成数据备份策略。

PC Editor 的 JSON / CSV 在项目根目录 `ExperimentExports/`；PC 构建在其数据目录的上一级 `ExperimentExports/`。Android 在应用的 `Application.persistentDataPath/ExperimentExports/`；截图位于同一 persistentDataPath 下的 `MemoryPalaceSessionData/<session>/SpatialSnapshots/`。

### 延迟测验

一键启动后访问 `http://127.0.0.1:8766/`，使用精确 Session ID；若输入 Participant ID，网页选择其最新导出会话，务必核对。它先测试手输英语释义，再做三选一，不在第一轮提前显示答案。

网页允许研究者处理提前 / 重复测验，每次另存结果，所以“网页可以开始”不等于已满足七天时间。原始 Quest 结果默认在 `QuestResultImports`，而网页默认读取 `ExperimentExports`：请明确选择正确来源（独立启动支持 `-ExportsDir`），不要假设回收后网页会自动发现结果。详情见 [DelayedTestWeb/README.md](../Tools/DelayedTestWeb/README.md)。

## 7. 目录和代码入口

| 位置 | 职责 |
| --- | --- |
| `Assets/Scenes/SampleScene.unity` | 入口场景、已保存的 Inspector 参数、可编辑 Canvas |
| `Assets/Scripts/Runtime/MemoryPalaceBootstrap.cs` | 场景载入后的控制器入口 |
| `Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs` | 实验阶段、Builder、预筛、故事、匹配、学习、测验、导出 |
| `MemoryPalaceExperimentController.SceneUi.cs` / `.SceneVrUi.cs` | 场景 Canvas 的显示和绑定；文件位于同一 Runtime 目录 |
| `MemoryPalaceExperimentController.VrStudyFocus.cs` | 点击家具、固定学习卡、学习中移动门控 |
| `MemoryPalaceExperimentController.VrComfort.cs`、`VrPeripheralBlur*` | 暗角 / 外围模糊及渲染接入 |
| `Assets/Scripts/Services/MnemonicStoryPrompts.cs` | 实际运行时 Prompt 构造 |
| `Assets/Scripts/Services/OllamaLlmService.cs` | 故事请求、解析、检查、修复与后端路由 |
| `Assets/Scripts/Services/ParticipantStoryEnglish.cs` | 英文变形识别与西语标注 |
| `Assets/Scripts/Services/ElevenLabsTextToSpeechService.cs` | 现有语音适配层，含预制词音频和 Azure PC 代理路径 |
| `Assets/Scripts/Data/ExperimentModels.cs`、`RoomSpecModels.cs` | 条件枚举、会话 / 导出结构、房间和锚点 |
| `Assets/Resources/` | 词表、默认 / 示例房间、词图、词音频、家具、shader、离线变形表 |
| `Assets/Editor/` | UI 烘焙、素材工具和无头显检查菜单；不进入 Player |
| `Tools/` | PC 服务、TTS、Quest 传输、延迟测验与数据表构建脚本 |
| `Docs/` | 交接说明、功能契约和 Prompt 设计资料 |

主控制器仍承担多项职责。拆分时优先保持序列化字段、场景引用、partial 类和数据契约；不能只凭文本搜索“没有调用”就删除 Unity 通过序列化、Resources、反射或菜单使用的代码。

## 8. 常用修改位置

### UI

在 Hierarchy 选择 `EditableSceneUI`，使用 **Memory Palace Scene UI View > Stage Preview**，调整 RectTransform、布局、字体、颜色等。控件依赖唯一 GameObject 名称绑定：改外观可以，不要随意改名或制造重名。使用 Inspector 的 **Validate Unique Control Names** 或 **Tools > Memory Palace > Validate Editable Scene UI** 检查。

HMD 旧模板在 `EditableWorldSpaceUiTemplates`；新的点击学习卡在 `.VrStudyFocus.cs` 运行时生成，不能只调整旧 Study Panel 就认为新卡已更新。

**Tools > Memory Palace > Bake Legacy UI Into SampleScene** 会完整替换 `EditableSceneUI`，覆盖手动布局。只有确认需要重建并已备份场景时才运行。详情见 [EditableSceneUI_README.md](../Assets/Scenes/EditableSceneUI_README.md)。

### 移动、视角和暗角

选择场景 `MemoryPalaceExperiment` 的控制器：

- **VR Physical Walking > VR Physical Walking Gain**：实体行走放大倍率。
- **VR Physical Walking > VR Joystick Move Speed (m/s)**：手柄平移速度，与行走倍率及 PC 键盘速度独立。
- **VR View Height (Temporary Tuning) > VR View Height Offset (m)**：负值降低视角、正值升高；保留头 / 手跟踪。此项暂时开放给研究者调节，不应未经确认重新隐藏。
- **VR Comfort Vignette > Vignette Mode**：切换 Opaque / Blurred 模式；**Blur: Edge Darkness / 黑边强度** 控制模糊外围叠黑程度，0 不叠黑。
- **Self Room Physical Build Area**：建房边界与虚实比例，仅是 Builder 范围配置，不等同于走路倍率，也不替代 Quest 系统安全边界。

Play 中的调试值通常不会自动保留；记录满意数值，Stop 后写回场景 Inspector 并保存。不要用脚本默认值覆盖场景中研究者已调好的参数。

### 家具比例与房间

在 `Assets/Resources/FurniturePrefabs/` 打开相应 prefab，修改根 Transform 的 Scale；名称需匹配房间 anchor 的 `modelKey`，如 `bed`、`bathtub`。运行时保留 prefab 根旋转 / 缩放，不会自动将所有家具改成相同比例。朝向约定和基线数值见 [FurniturePrefabs/README.md](../Assets/Resources/FurniturePrefabs/README.md)。

修改正式房间前保留原稿。锚点 ID、modelKey 与家具变动会影响匹配和旧会话包，不能只看画面而不验证导出。Builder 选中误放家具后按 `B` 删除，`R` 旋转，`Ctrl+Z` 撤销；删到少于 9 种有效家具会重新锁定完成。

Builder 视图用 `WASD` 移动、按住鼠标中键旋转、滚轮缩放（`Q` / `E` 也可缩放）、`Esc` 取消或取消选择。PC 熟悉 / 匹配房间则用 `WASD` 移动、按住右键转视角、左键点家具。两个视图的鼠标旋转按键不同，不要混用说明。

### 词表、图像、词音频和 Prompt

- 词表：`Assets/Resources/MemPalaceDemoData.json`，保留西语重音、词义及研究选择规则。
- 图像：`Assets/Resources/WordImages/{word}.png`；文件名与小写西语词一致，缺图会显示 `_placeholder`。每次换图更新 `ATTRIBUTION.md`。
- 词音频：`Assets/Resources/WordAudio/`；维护工具为 `Tools/LocalTtsServer/Generate-FormalWordAudio.ps1`，会调用语音服务并生成资源，需先审查参数和费用。
- 英文变形资源：`EnglishInflections.bytes`，可用 `Tools/Build-EnglishInflections.ps1` 重建；保留 `EnglishInflectionsNotice.txt` 许可说明，运行时不需要 Python 或联网。
- Prompt：改 `MnemonicStoryPrompts.cs` 才影响 Unity。`Docs/MnemonicStoryPromptV2/` 是可分享模板 / schema 和完整框架，运行时不直接读取这些文件。

当前运行管线为规划、成文和至多一次修复；MSF-2.1 完整设计中的四蓝图筛选与独立 Audit 不应当作已接入能力。改 Prompt 要跑兼容性检查，保留词序 / 输出契约与当前预算，并同步说明版本。

协作 ZIP 刻意排除了全部词图 PNG 与 WordAudio。运行时的空白图只用于开发；正式西语词的 `localOnly` 朗读缺文件时会报错，不会自动换成动态云端 TTS。完整学习前，按 [素材清单](THIRD_PARTY_ASSETS.md) 补回授权图像，并从自己的 Azure 账户重新生成词音频或补回已确认可用的音频版本。

## 9. 检查、构建与交接

### 无头显回归

编译无误后，在非 Play 状态使用 Unity 菜单：

- **Tools > Memory Palace > Run Project Checks**：一次运行家具回退、Prompt、自写故事、Builder 和 VR 点击学习的离线检查。
- **Tools > Memory Palace > Check Furniture Prefab Fallback**
- **Tools > Memory Palace > Check Story Prompt Compatibility**
- **Tools > Memory Palace > Check Participant Story English**
- **Tools > Memory Palace > Check Builder Delete Shortcut**
- **Tools > Memory Palace > Check VR Click-to-Learn**
- **Tools > Memory Palace > Check VR Comfort Vignette**
- **Tools > Memory Palace > Validate Editable Scene UI**

检查 Console 的结果，而不是以“菜单点过了”判断成功。源码中的 `RunBatch` 入口可能退出 Editor，不要在日常编辑窗口直接调用批处理入口。某些渲染检查需要图形设备，不适合 `-nographics` 环境。

延迟测验服务有独立单元测试，可在项目根目录执行：

```powershell
python -m unittest discover -s Tools/DelayedTestWeb/tests -p "test_*.py"
```

再用明确的测试 ID 做 PC 冒烟检查：Builder 放 / 旋转 / 删除 / 撤销、预筛、故事 Split、8 词唯一匹配、学习卡进入 / 退出、朗读完成、截图、完整测验和 JSON / CSV 导出。联网故事与 TTS 会产生请求 / 费用，应使用自己的测试账户，不能用正式参与者数据随意回放。

无头显检查能验证逻辑和部分渲染，但不能证明 Quest 帧率、追踪、手柄兼容或不适程度。当前说明不宣称已完成本版本 Quest 实机验收，也不要求每次文档修改都重跑头显研究。

### 构建

Unity **File > Build Profiles** 中选择 Windows 或 Android；Quest 使用 Android，确认入口为 `SampleScene`。沿用已提交的 XR / OpenXR 和渲染设置，先处理 Console / Build 输出中的错误。开发构建与正式研究构建分别标记，正式收集前记录 commit、APK、设备和参数版本。

构建前排查 Resources 内的 API 配置：忽略的文件仍可被打包。不要将含长期密钥的构建交给无权限第三方。代码 / shader / 场景变化后重新安装程序；仅修改房间故事时才使用 JSON 会话包。

### 导出协作源码包

在维护者工作区双击 `Tools/Export-CollaborationPackage.cmd`，或从项目根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Export-CollaborationPackage.ps1
```

输出为 `Builds/Collaboration/MemoryPalace-Source-YYYYMMDD-HHmmss.zip`。只分享这个审核后的 ZIP，不分享工作区的完整压缩包或 `.git`。导出在副本中筛选，不以删掉本机正式素材的方式制作交接版本。

导出后运行 `Tools/Check-CollaborationPackage.ps1`，默认检查最新 ZIP 的文件白名单、SHA256、Unity `.meta` 与文档相对链接。包内 `SOURCE_MANIFEST.json` 保留逐文件摘要，`SHARING_NOTES.md` 说明开发版限制。自动扫描不是无泄露保证；发布前仍需人工核对新增文件。

包不含商业 / 来源不明视觉素材、全部词图 PNG、预生成 WordAudio、个人配置、参与者数据、缓存或 Git 历史；保留源码、场景、配置、开发用程序家具 / 空白图像占位和必须的第三方许可说明。它是开发演示源码，不是正式研究刺激齐全的安装包。完整材料的恢复步骤和许可缺口见 [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.md)，当前系统说明见 [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md)。

完整仓库历史仍含旧参与者数据。需要 Git 级协作时，应由维护者确认研究数据访问权限后私下授权；不能把清理过的最新提交当成可以公开整个历史的许可。

### 源码分享前的清单

- 只交接源代码、必需 Project Settings、Packages、允许分发的资源和文档；不复制 `Library`、`Temp`、Logs、缓存和个人 IDE 目录。
- 排除 `.git` 目录、密钥 JSON / 环境文件、APK 签名材料、TTS 缓存、会话包、参与者结果、原始故事和截图。不要复制 Git 历史；删除当前文件不等于清除了历史内容。
- 商业 **Furniture Mega Pack** 源资源不在可共享源码范围内。共同开发者应自行获得适用许可并导入；本机原资源不能仅为“包更完整”就压缩给对方。
- 缺 Mesh / Material 的家具可回退到程序模型，便于开发；该外观不等价于正式研究素材，不能混用后仍声称实验刺激一致。
- 当前 47 张已跟踪词图 PNG 中，只有 15 张被旧 OpenMoji 来源表覆盖，另外 32 张未覆盖；旧的“6 张来源待补”清单并不完整。已记录也不等于本次独立鉴定图片未被替换。开发 ZIP 因此排除全部 PNG 及其 `.meta`，包括来源未明的 `_placeholder.png`，使用程序空白占位；详细文件清单与补证要求见 [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.md)。
- 32 个预生成 WordAudio WAV 也不进入开发 ZIP；维护者需确认服务生成来源及使用条件，协作者在完整学习前补回或通过自己的账户重新生成，不要把缺词音频的开发包用于参与者收集。
- OpenMoji 的 CC BY-SA 4.0 署名、来源和许可链接，及英文变形表第三方 notice 必须保留；不能将整个项目一概声明为无附带条件可商用。
- 在新目录做一次干净源码打开检查，确认 fallback、配置缺失提示和服务说明有效。正式研究机器另做授权素材 / 词图 / 语音 / 版本核对。

## 10. 常见问题

| 现象 | 先检查 |
| --- | --- |
| Play 很久才进入，重开 Editor 暂时变快 | Console、Editor.log 的耗时阶段、脚本 / 资源刷新、OpenXR 初始化、Meta Quest Link / 当前 XR runtime。不要直接删 Library 或关闭全局 XR 设置当作已定位原因；更改前记录可复现步骤。 |
| 家具缺失、粉色或形状简化 | 是否安装所需且有许可的素材、Mesh / Material 是否丢失、URP 材质兼容、modelKey 是否一致；程序 fallback 不代表正式素材已正确安装。 |
| Builder 不能 Done | 是否已放置至少 9 种有效家具，是否超出建房范围；不是需要 9 个学习单词。 |
| 自写故事没有自动认词 | 先 Split / Refresh，核对英语释义、实际词形和分类；歧义项手动指定，不要求参与者输入西语。 |
| 在线生成不可用 | 当前选择的后端、自己的本地 API 配置、模型权限与端点、网络和限额；Ollama 未启动不解释所有云端错误。 |
| TTS /health 正常却无声 | 真正运行一次 Test Configured Voice；检查 Azure 订阅是否启用、余额、区域 / key、代理错误和系统音量；健康响应不测实际合成权限。 |
| Quest 无法取包 / 语音 | LAN IP 而非 localhost、同网可达、PC 防火墙、服务绑定、最新导出的 URL；USB 模式检查 adb 授权及 package ID。 |
| 端口 8765 已占用 | 是否把独立延迟网页和包服务放在同一端口；延迟网页改用 8766。先确认占用进程，不自动结束未知程序。 |
| 靠近家具后没有学习 UI | 当前设计要求右手射线点家具并扣扳机；核对正确路线目标、距离与遮挡。高亮并不自动打开。 |
| Next / Finish learning 不可用 | 是否朗读完成、首轮路线完成、全部目标场景截图已拍；需要时查看卡片的返回 / 错误提示。 |
| Quest 仍是旧交互 | 是否只推了会话 JSON 而没重新构建 / 安装 APK；同时核对包名，避免安装和启动的是不同应用。 |
| 找不到结果 | 是否完成测验导出；Editor 在项目根目录，Android 在应用 persistentDataPath；回收后看带时间戳的 QuestResultImports，而不是旧 ExperimentExports。 |

新增问题时记录：Unity / commit、PC 或 Quest、步骤、发生阶段、预期与实际、脱敏日志。不要在 issue 或共享截图里附密钥或参与者可识别信息。

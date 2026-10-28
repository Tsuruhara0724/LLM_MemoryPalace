# LLM Memory Palace

用于西班牙语词汇学习研究的 Unity 桌面 / VR 项目。参与者创建或熟悉房间，阅读自写或生成的英文故事，将 8 个目标词分配给家具，再进入 VR 学习和测验。

当前条件为 **Self room / Example room × Self story / LLM story** 的 2 × 2 设计。环境和流程以本仓库代码及下列开发指南为准；旧研究稿中的三条件或 20 分钟流程不代表当前实现。

## 从这里开始

1. 安装 Unity Hub 和 **Unity 6000.3.12f1**。Quest 构建还需要该版本的 Android Build Support、SDK & NDK Tools、OpenJDK。
2. 解压审核后的 `MemoryPalace-Source-*.zip`，在 Unity Hub 用 **Add project from disk** 选择包含 `Assets`、`Packages`、`ProjectSettings` 的目录。只有得到研究数据访问授权的协作者才使用完整 Git 仓库。
3. 打开 `Assets/Scenes/SampleScene.unity`，等包恢复和导入结束，再进入 Play Mode。
4. 第一次使用 Azure 语音，先运行 `Tools/LocalTtsServer/Setup-Azure.cmd`；之后开机运行 `Tools/Start-ProjectServices.cmd`，在独立 TTS 窗口输入自己的密钥和区域。

仅开发桌面 UI / Builder 不必启动全部服务。LLM 条件需要自己的故事 API 配置，语音需要 Azure 代理，Quest 网络传包和延迟测验各有独立服务，详见指南。

## 交接文档

- [共同开发者指南](Docs/DEVELOPER_GUIDE.md)：安装、配置、研究流程、开发入口、测试、构建和交接检查。
- [当前项目上下文](Docs/PROJECT_CONTEXT.md)。
- [第三方素材与交接边界](Docs/THIRD_PARTY_ASSETS.md)。
- [开机后的服务启动方法](Docs/STARTUP.md)。
- [VR 点击家具进入学习](Docs/VR_CLICK_TO_LEARN.md)。
- [自写英文故事的变形识别与西语标注](Docs/PARTICIPANT_STORY_ENGLISH.md)。
- [可编辑场景 UI](Assets/Scenes/EditableSceneUI_README.md)。
- [故事 Prompt 运行时与完整框架的区别](Docs/MnemonicStoryPromptV2/README.md)。
- [延迟测验网页](Tools/DelayedTestWeb/README.md)。

## 分享边界

双击 `Tools/Export-CollaborationPackage.cmd`，或从项目根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Export-CollaborationPackage.ps1
```

生成 `Builds/Collaboration/MemoryPalace-Source-YYYYMMDD-HHmmss.zip`。对外只分享这个审核后的 ZIP，不压缩整个工作区或复制 `.git`。完整仓库历史仍含旧参与者数据，只能向获授权的协作者开放，不能当作已匿名化源码包。

源码包排除密钥、参与者数据、Git 历史、商业 / 来源不明视觉资源、全部词图 PNG 和预生成 WordAudio；使用程序家具和空白图像占位，仅供开发演示，不与正式研究视觉等价。完整学习还需按 [素材清单](Docs/THIRD_PARTY_ASSETS.md) 补回经授权且冻结的素材，并补回或自行生成词音频。

素材审计发现：47 张已跟踪词图 PNG 中，只有 15 张被旧 OpenMoji 来源表覆盖，另有 32 张未覆盖；旧“6 张来源待补”清单并不完整。即使已有记录，也不等于已独立核实图片未被替换。许可证、来源和上游声明仍须保留。

不要提交 API 密钥、含密钥的 APK、参与者会话包、结果、故事原文或截图。`.gitignore` 只帮助防止误提交，不替代分享前检查。

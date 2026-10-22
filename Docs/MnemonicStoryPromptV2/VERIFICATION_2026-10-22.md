# MSF-2.1 集成检查 — 2026-10-22

## 范围与结论

本次是 Prompt-only 策略适配，不是学习效果实验，也不是完整四蓝图/Audit 管线部署。八个模板与用户提供的 ZIP 一致（忽略换行格式）；占位符集合不变；三个 Schema 和示例文件未修改，序列化版本继续为 `MSF-2.0`。

Unity 原有 Planner/Writer/Repair 改为共用 `MnemonicStoryPrompts.cs`。模型、温度、输出 token 上限、网络调用次数、160–210 词预算及实验数据结构保持不变。真实运行路径不读取 Docs 模板。

## 编译与离线检查

- 使用 Unity 6000.3.12f1 自带编译器及项目现有编译响应文件，编译完整 Runtime 和 Editor 脚本：零编译错误；仅保留原有控制器未使用字段警告。
- 主项目编辑器保持打开。另建忽略目录中的最小 Unity 检查项目，使用本次实际源文件执行真实 `JsonUtility` 与现有故事解析器，而非模拟 JSON 实现。
- 最后一轮 **56 项检查通过**：其中 38 项检查 Prompt 规则、输入转义、唯一数据边界、fallback、旧 JSON 兼容及目标句保留；18 项检查下面的实际生成修复样本。
- `git diff --check` 通过。

检查中发现并修正一处兼容问题：合法的首句若没有旧规则里的 `must/need/...` 关键词，会被替换为固定的“到达安全门”故事片段。现在完整单目标句不再仅因缺少这些关键词被替换；其他分段逻辑保持不变。

复查入口：Unity 菜单 `Tools > Memory Palace > Check Story Prompt Compatibility`。批处理入口为 `MemPalaceLLM.Editor.MnemonicStoryPromptChecks.RunBatch`；不要对已经在编辑器打开的同一项目再次启动 batchmode。

## 单个实际生成探针

使用项目现有 Claude 配置，请求与响应报告模型均为 `claude-haiku-4-5-20251001`。

输入：`pegamento/glue, risa/laughter, hambre/hunger, logro/achievement, ayuda/help, ruido/noise, lodo/mud, olvidar/forget`。

| 阶段 | 参数 | 结果 |
|---|---|---|
| Planner | temperature 0.28，max tokens 1600 | 8 个条目；7 个连接字段按现有运行时规则规范化；这不证明连接语义合理 |
| Writer（动词标签规则加强前） | 0.48，2000 | 189 词、8 句、8/8 精确覆盖；仍有语法与衔接问题，不能按语义合格放行 |
| Repair（规则加强后） | 0.38，2200 | 接收人工列出的错误后，生成 199 词、8 句、8/8 精确覆盖的修复样本 |
| Unity 本地解析 | 实际 `TryParseAndBuildStoryResponse` | 接受修复结果；8 个完整目标句在分段后保留，无新增空间分配 |

人工指出的错误包括：`choose not to olvidar (forget)` 的英语动词位置错误、朋友已经出现却再次被叫来、泥土来源和清理对象不清。相应地，运行时 Prompt 进一步明确“先用自然英文表达动作，再将西班牙语短语作为该动作的标签”，并区分真正忘记与刻意忽略。

这里的人工反馈 **不是** 当前运行时能自动检测语义错误的证据。最终 Writer 提示词没有另外进行大规模、重复或对照生成测试。

### 修复样本（保留模型原文）

> You notice the poster has a large rip down the middle, so you apply pegamento (glue) carefully along both edges and press them together. As it dries, your friend stops by and their risa (laughter) at the obvious glue lines reminds you the repair alone won't look polished enough. You realize you need energy to solve this, so you pause to eat lunch, and the food quiets your hambre (hunger) while clearing your mind. Feeling restored, you successfully build a simple frame from materials at home—a real logro (achievement) that hides the damaged edges and transforms the poster into something finished. Your friend helps you carry it to the gallery, offering ayuda (help) when you need it most. At the venue, you wait between the ruido (noise) of construction workers to position your poster on the wall in a quiet moment. You step outside briefly and notice lodo (mud) caked on your shoes from the journey, so you wipe them clean on the grass before returning. Inside, you admire the hung poster and forget the rough moments of the repair, an example of olvidar (forget), choosing instead to focus on how professional it looks, ready for the opening.

这不是“优质助记已验证”的示范：外出清鞋仍显绕路，噪声与挂画之间的联系较弱，结尾忘记不愉快经历的表达也不等于已证明能帮助提取西班牙语词形。它只验证了请求、输出覆盖、一次受限修复与 Unity 分段兼容。

## 可复现记录与限制

本机原始请求、响应、参数、模型报告、Prompt SHA-256、耗时、人工反馈及表面检查报告保存在忽略目录 `.utmp/msf21-runtime-smoke/`；不含请求认证头。修复记录位于其 `manual-repair-check/` 子目录。Unity 检查日志位于 `.utmp/msf21-unity-*.log`。

- 没有新增参与者实验、记忆成绩保证、家具映射或发音谐音阶段。
- 确定性表面检查和字段规范化不能验证真实词义、因果连接或语法质量。
- 单个样本与人工修复不能证明新策略优于旧策略，也不能代表自动运行的稳定通过率。
- 正式 Immediate Recall、Effort 与 Delayed Retention 测量维持原设计；生成故事仍应由研究者按词义、语法和事件衔接进行必要审核。

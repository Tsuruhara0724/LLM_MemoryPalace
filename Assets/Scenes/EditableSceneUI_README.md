# 可编辑 Scene UI

`SampleScene` 中的实验前端现在由两个 Scene 根节点保存：

- `EditableSceneUI`：桌面端 Canvas、顶部状态栏、11 个实验阶段 Panel 和覆盖层。
- `EditableWorldSpaceUiTemplates`：HMD Ready、学习面板、Persistent HUD、单词图 HUD 和 Recall Panel 模板。

## 调整界面

1. 在 Hierarchy 选择 `EditableSceneUI`。
2. 在 `Memory Palace Scene UI View` Inspector 的 **Stage Preview** 中选择要编辑的阶段。
3. 调整子对象的 `RectTransform`、`Layout Group`、`Text`、`Image`、`Button`、`InputField`、`Slider` 或 `ScrollRect`。
4. HMD 界面可用 Inspector 的 **World-space / HMD Template Preview** 按钮单独显示和定位。
5. 保存 `SampleScene`。

控件通过唯一 GameObject 名称与实验逻辑绑定。可以改位置、尺寸、锚点、字体、颜色、图片和布局组件，但不要修改控件 GameObject 名称。Inspector 中的 **Validate Unique Control Names** 可检查重名。

## 安全回退

- Play 中可按顶部 `Legacy UI (safety)` 切回旧的代码绘制界面。
- 也可以在 `MemoryPalaceExperimentController` Inspector 中关闭 `Use Editable Scene UI`。
- `Tools > Memory Palace > Rebuild Editable Scene UI` 会完整重建层级，但会覆盖手动 UI 布局，仅在确实需要重置时使用。
- Git 回退点：`pre-scene-ui-migration-20260831`。

实验阶段切换、房间验证、JSON 存档、LLM、图像、TTS、XR 输入、测试计分和数据导出仍由脚本负责；Scene 对象负责可视前端与交互控件。

# 从原 UI 烘焙出的 Canvas

`SampleScene` 中的 Canvas 不是一套重新设计的界面。编辑器烘焙器按照原
`MemoryPalaceExperimentController.OnGUI` 的颜色、尺寸、文字顺序和阶段结构创建一次 UGUI 控件，
然后把结果保存在场景中。

- `EditableSceneUI`：桌面端 Canvas、原界面的 11 个阶段和覆盖层。
- `EditableWorldSpaceUiTemplates`：HMD Ready、Study Panel、Persistent HUD、Word Image HUD 和 Recall Panel。

## 调整界面

1. 在 Hierarchy 选择 `EditableSceneUI`。
2. 在 `Memory Palace Scene UI View` Inspector 的 **Stage Preview** 中选择需要编辑的阶段。
3. 调整子对象的 `RectTransform`、`Layout Group`、`Text`、`Image`、`Button`、`InputField`、`Slider` 或 `ScrollRect`。
4. HMD 界面可通过 **World-space / HMD Template Preview** 单独显示和定位。
5. 保存 `SampleScene`。

控件通过唯一 GameObject 名称与原实验逻辑绑定。可以改位置、尺寸、锚点、字体、颜色、图片和布局组件，
但不要修改控件 GameObject 名称。Inspector 中的 **Validate Unique Control Names** 可检查重名。

## 重新烘焙

`Tools > Memory Palace > Bake Legacy UI Into SampleScene` 会再次读取旧 UI 规格并完整替换
`EditableSceneUI`。它不会在脚本刷新时自动运行，以免覆盖你手动调整后的 Canvas。

## 安全回退

- `MemoryPalaceExperimentController` Inspector 中关闭 `Use Editable Scene UI` 可使用旧 `OnGUI` 界面。
- Git 回退点：`pre-scene-ui-migration-20260831`。

实验阶段切换、房间验证、JSON、LLM、图像、TTS、VR 输入、测试计分和数据导出仍由原脚本负责；
Canvas 只保存可编辑的显示与交互控件。

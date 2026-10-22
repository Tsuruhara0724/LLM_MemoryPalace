# VR 移动视野：黑色遮罩与外围模糊＋黑边

## 在 Inspector 选择

1. 打开 `Assets/Scenes/SampleScene.unity`。
2. 在 Hierarchy 选中 `MemoryPalaceExperiment`。
3. 在 `Memory Palace Experiment Controller` 组件中找到 **VR Comfort Vignette**。
4. 在 **Vignette Mode** 下选择：
   - **Opaque Vignette (Original) / 黑色遮罩（原方案）**：默认选项，原来的黑色遮罩。
   - **Blurred Vignette / 外围模糊＋黑边**：保持中央清晰，外围逐渐模糊并变黑；过渡区仍保留环境轮廓，最外侧默认全黑，不叠加原方案的遮罩 Canvas。

建议在停止 Play 后选择并保存 Scene，设置才会随下一次运行/构建保留。Play 中也可以即时切换对比，但普通 Play Mode 的 Inspector 修改不会自动保存到 Scene。

## 新方案的参数

- **Blur: Clear Radius**：完整清晰区域的半径，相对单眼画面高度的一半；越大，保留的清晰视野越多。
- **Blur: Transition Width**：清晰区到模糊区的渐变宽度；越大，过渡越柔和。
- **Blur: Radius**：外围高斯模糊的采样半径，单位为四分之一分辨率像素；只影响模糊模式。
- **Blur: Edge Darkness / 黑边强度**：默认 `1`，最外侧全黑；`0` 为原来的纯模糊，中间值减弱黑边。与模糊共用柔和的径向过渡，中央清晰区不变。

参考图中的镜片畸变、分屏镜像和色散不是这个选项要添加的效果；这里只实现外围模糊与渐变黑边。

## 原方案不变的部分

- 原有 `BuildVrComfortVignette` 和 `CreateVrComfortVignetteTexture` 实现保留；黑色遮罩仍使用原纹理、`0.18–0.34` 的径向范围及原渐入速度。
- 两种模式沿用现有触发条件：仅摇杆平移时激活；松开平移立即清除。物理走动、转头和单独转向不新增触发。
- 不叠加两种效果；切回黑色遮罩会关闭模糊。
- 未改变行走增益、实验阶段、词汇/故事、TTS、XR 自动初始化设置或已有会话包结构。

这是两种可选择的视觉表现，不表示已验证任一种能降低晕动或提高记忆成绩。正式实验应固定并记录所使用的选项。

## 渲染与构建

PC 和 Mobile 的 URP renderer 都已安装 `VrPeripheralBlurRendererFeature`。只有指定相机上的新模式处于移动状态时，才加入两个低分辨率高斯模糊 pass 和一个径向合成 pass；黑边直接在现有合成 pass 内完成，不新增 pass。新模式在静止时保留稳定的相机中间色缓冲，但不执行模糊计算，以避免 Mobile 渲染缩放在摇杆开始/停止时改变中心清晰度；原黑色方案不要求这份额外缓冲。松开摇杆会同时清除模糊与黑边。

新 pass 使用本项目 Unity 6 / URP 17.3 的 RenderGraph。临时纹理保留 XR 纹理数组的维度/切片，Shader 使用 `TEXTURE2D_X` 和每眼索引，避免把桌面镜像当作单眼图像。RTHandle 采样同时考虑当前视口缩放，适配 Mobile 配置的 `renderScale=0.8`，不修改这个全局比例。

Quest 需要重新构建并安装包含新 Shader 和 renderer feature 的 APK。旧 APK 不会仅凭会话包更新获得此功能；旧会话包也不会覆盖 Inspector 选择的模式。

## 检查

Unity 菜单：`Tools > Memory Palace > Check VR Comfort Vignette`。在非 Play 状态运行，检查默认模式、黑边强度的默认值/传递/边界、切换、松开后的清除、旧纹理保留，以及 PC/Mobile 两套 renderer 的 Shader 引用。

工程验证采用独立的最小 Unity 项目和棋盘格测试画面，以避免改变主项目的 XR 配置。实际 RenderGraph 渲染应确认：中心像素保留、外围渐变到黑色、黑边强度为零时仍有模糊且不变黑、停止后恢复原始图像。此验证不等于 Quest 双眼画面、Android GPU 兼容性或目标刷新率测试；按用户要求，本次不进行 Quest 实机验证。

### 2026-10-22 加黑边前的历史验证记录

- 完整 Runtime/Editor C# 编译通过；仅有原控制器已有的未使用字段警告。
- 22 项状态/引用检查通过，包括默认模式、原纹理保留、模式切回、停止清除和两套 renderer 的 Shader 引用。
- 独立项目使用 Unity 6000.3.12f1、URP 17.3、D3D11、真实 `SingleCameraRequest` 和 RenderGraph 渲染 512×512 棋盘格。
- PC 与 Mobile 配置下，移动前后中心区域平均像素误差均为 **0**，停止后的全画面平均误差均为 **0**；外围平均红通道变化分别约为 **0.344 / 0.260**，平均外围亮度变化小于 **0.2%**。
- Mobile 测试使用原有 `renderScale=0.8`。测试揭示了按输入切换中间缓冲造成的重采样跳变，因此最终实现让新模式保持缓冲稳定；原模式不增加该需求。
- 新模式的“中心不变”是相对于新模式已启用、静止时的清晰基线；切换模式本身可能改变 URP 的中间缓冲路径。未对 Quest 的真实双眼、Android 图形 API 或目标帧率作出验证承诺。

本机渲染结果位于忽略目录 `.utmp/vr-vignette-check/.utmp/results/`，通过日志为 `.utmp/vr-vignette-gpu-stable.log`。最小检查项目的 Unity Search 数据库还报告过索引异常；它不在模糊渲染调用栈中，渲染对比仍已完成。没有借此修改主项目的 Search、XR 或包版本设置。

### 2026-10-22 渐变黑边验证记录

- 完整 Runtime C# 编译通过；独立 Unity 项目重新编译 Runtime/Editor，并通过全部 **25 项**状态/引用检查。
- 在相同 D3D11 RenderGraph 棋盘格测试中，PC 和 Mobile（原 `renderScale=0.8`）的中央 RGB 平均误差均为 **0**，停止后的全画面 RGB 平均误差均为 **0**。
- 黑边强度 `1` 时，最外围的平均亮度均为 **0**；`0.5` 时约为纯模糊亮度的一半；`0` 时仍保留外围模糊和原有亮度。
- 渐变区与径向平滑变黑的预期结果一致，RGB 平均误差分别约为 **0.0011 / 0.0013**（0–1 范围）。已目视检查生成的黑边测试图。
- 原方案的遮罩构建和纹理生成方法与 Git 基线一致；没有新增全屏 pass，没有修改 XR 配置或进行 Quest 实机验证。

本次结果位于 `.utmp/vr-vignette-check/.utmp/results-dark/`，通过日志为 `.utmp/vr-vignette-dark.log`。

主要文件：

- `Assets/Scripts/Runtime/MemoryPalaceExperimentController.VrComfort.cs`：Inspector 与模式切换。
- `Assets/Scripts/Runtime/VrPeripheralBlur.cs`：相机上的瞬时效果状态。
- `Assets/Scripts/Runtime/VrPeripheralBlurRendererFeature.cs`：RenderGraph pass。
- `Assets/Resources/Shaders/VrPeripheralBlur.shader`：外围模糊与合成。

RenderGraph 集成参考本机 URP 17.3 的 `BlitAndSwapColorRendererFeature` 示例；离线渲染检查使用 Unity 官方的 [SingleCameraRequest / SubmitRenderRequest](https://docs.unity.com/en-us/engine/6000.0/manual/cameras/urp/multiple/user-render-requests) 接口。

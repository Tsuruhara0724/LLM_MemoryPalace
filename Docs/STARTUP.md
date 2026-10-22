# 开机后启动项目服务

从资源管理器双击 `Tools/Start-ProjectServices.cmd`。脚本会复用已经运行且接口正确的服务，启动缺少的服务，并显示状态。无需每次运行 Setup 安装脚本。

| 服务 | 地址 | 用途 |
|---|---|---|
| Azure Speech 代理 | `http://127.0.0.1:8880/health` | 英文故事、引导语和混合语言句子的语音合成 |
| Quest 会话包服务 | `http://127.0.0.1:8765/session_package_latest.json` | Quest 通过网络读取 PC 导出的会话包 |
| 延迟测试网页 | `http://127.0.0.1:8766/` | 在浏览器进行一周后词汇测试 |
| Ollama | `http://127.0.0.1:11434/api/version` | 本地模型和诊断；目前正式故事后端为配置好的云端服务 |
| ADB | `127.0.0.1:5037` | Quest USB 会话包传输与结果回收；设备需连接并授权 |

Quest 会话包服务与延迟测试网页原有脚本都默认用 `8765`。一键脚本为延迟测试网页指定 `8766`，避免冲突，不改变原有独立脚本的默认值。

## Azure 密钥输入

一键脚本会打开标题为 `Memory Palace - Azure TTS - enter key here` 的 PowerShell 窗口：

1. 在 `Azure Speech key (input is hidden)` 输入密钥，按回车；输入内容不会显示。
2. 输入 Azure Speech 资源对应的区域。只有资源确实位于 `japaneast` 时，才直接回车接受这个默认值。
3. 看到 Uvicorn 启动后，访问 `http://127.0.0.1:8880/health`。必须同时显示 `status: ok`、`backend: azure`。

密钥只留在该次进程内，不写入项目、Unity 设置或 Git。关闭窗口或关机后需要重新输入。健康接口只证明代理已配置并响应，实际语音能否生成还需要一次合成请求验证。

保留此 TTS 窗口。其余服务在后台运行，一键脚本的状态窗口可以关闭。再次运行脚本会复用已正常运行的服务；若端口被别的程序占用，会显示冲突，不强制终止已有程序。

## PowerShell 启动及状态检查

从项目根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1
```

只查看状态，不启动任何服务：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1 -StatusOnly
```

TTS 窗口已经单独打开时，也可以先启动其余服务：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1 -SkipTts
```

## 单独启动

```powershell
# Azure TTS。单独脚本默认只监听本机；Quest 使用时先设置监听地址。
$env:LOCAL_TTS_HOST = '0.0.0.0'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\LocalTtsServer\Start-Azure.ps1

# Quest 会话包（保持此终端运行）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-VRSessionServer.ps1

# 延迟测试网页，使用另一端口（保持此终端运行）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\DelayedTestWeb\Start-DelayedTestWeb.ps1 -Port 8766

# Ollama，仅当没有自动运行时（保持此终端运行）
ollama serve
```

## Quest 和 Unity

一键脚本会显示当前 PC 的网络地址。Quest 使用 `http://<PC-IP>:8765/session_package_latest.json` 获取会话包，TTS 基础地址使用 `http://<PC-IP>:8880/v1`。PC 与 Quest 需要互相可达；本机接口通过不代表已经通过头显网络或音频实机测试。网络地址变化后，应在 Unity 重新导出会话包。

Unity Editor 或已有构建需照常打开。脚本不会自动进入 Play Mode、导出/覆盖会话包，也不会修改实验条件或防火墙设置。

Claude Haiku / GPT_Luna 的 API 配置不需要启动本地服务。当前实验中已禁用的 Stable Diffusion 图像生成不需要启动；Kokoro / Chatterbox 是替代 TTS，不应与 Azure 同时抢占 `8880`。

## 日志

一键脚本启动的后台服务日志位于 `.utmp/project-services/`。文件名含服务名和启动时间。Azure TTS 的启动/错误信息显示在它自己的窗口里。重启电脑后，重新双击启动脚本即可；本次没有安装 Windows 开机自启动任务。

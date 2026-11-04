# Starting Project Services After a Reboot

[中文](STARTUP.md) | English


Double-click `Tools/Start-ProjectServices.cmd` in File Explorer. The launcher reuses compatible running services, starts missing ones, and displays status. Setup scripts are not needed after every reboot.

| Service | Address | Purpose |
| --- | --- | --- |
| Azure Speech proxy | `http://127.0.0.1:8880/health` | English stories, guides, and mixed-language narration |
| Quest session-package server | `http://127.0.0.1:8765/session_package_latest.json` | Quest fetching the PC-exported session package |
| Delayed-test web application | `http://127.0.0.1:8766/` | Browser-based follow-up vocabulary testing |
| Ollama | `http://127.0.0.1:11434/api/version` | Local models and diagnostics; current formal stories use the configured cloud backend |
| ADB | `127.0.0.1:5037` | Quest USB transfer and result retrieval; device connection and authorization required |

The standalone package-server and delayed-test scripts both default to `8765`. The combined launcher assigns `8766` to delayed testing without changing their individual defaults.

## Azure credentials

The launcher opens a PowerShell window titled `Memory Palace - Azure TTS - enter key here`:

1. At `Azure Speech key (input is hidden)`, enter the key and press Enter. Input is not displayed.
2. Enter the Speech resource's region. Accept the `japaneast` default only if the resource is actually in that region.
3. After Uvicorn starts, open `http://127.0.0.1:8880/health`. The response must contain both `status: ok` and `backend: azure`.

The entered key is held only for that process, not written to the project, Unity settings, or Git by the launcher. Enter it again after closing the window or rebooting. Health confirms that the proxy is configured and responsive; actual synthesis still needs a test request.

Keep the TTS window open. Other services run in the background, and the launcher's status window can be closed. Rerunning the launcher reuses healthy services. An occupied port is reported as a conflict; unknown processes are not forcibly stopped.

## PowerShell startup and status

From the project root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1
```

Inspect status without starting services:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1 -StatusOnly
```

If TTS already has its own running window, start the remaining services with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-ProjectServices.ps1 -SkipTts
```

## Starting services individually

```powershell
# Azure TTS. The standalone default is localhost; set the bind address for Quest.
$env:LOCAL_TTS_HOST = '0.0.0.0'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\LocalTtsServer\Start-Azure.ps1

# Quest session packages; keep this terminal running.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Start-VRSessionServer.ps1

# Delayed testing on a separate port; keep this terminal running.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\DelayedTestWeb\Start-DelayedTestWeb.ps1 -Port 8766

# Ollama, only if it is not already running; keep this terminal running.
ollama serve
```

## Quest and Unity

The launcher displays the PC's network address. Quest uses `http://<PC-IP>:8765/session_package_latest.json` for session packages and `http://<PC-IP>:8880/v1` as the TTS base address. PC and Quest must be mutually reachable. A local health check is not a headset connectivity or audio test. Re-export the session package in Unity after the PC address changes.

Open Unity Editor or the installed build normally. The launcher does not enter Play Mode, export or overwrite packages, alter experimental conditions, or change firewall rules.

Claude Haiku / GPT_Luna API configuration does not require a local service. Stable Diffusion generation is disabled in the current study flow and need not run. Kokoro / Chatterbox are alternative TTS backends; do not run them on port `8880` alongside Azure.

## Logs

Background service logs are in `.utmp/project-services/`, named by service and startup time. Azure startup and errors appear in its own window. After a reboot, rerun the launcher. No Windows automatic startup task is installed.

param(
    [switch]$StatusOnly,
    [switch]$SkipTts
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$LogRoot = Join-Path $ProjectRoot '.utmp\project-services'
$Summary = [System.Collections.Generic.List[object]]::new()

function Test-LocalPort([int]$Port) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        return $client.ConnectAsync('127.0.0.1', $Port).Wait(300) -and $client.Connected
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Get-LocalJson([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
        # Unity's exported session packages can begin with a UTF-8 BOM.
        $json = [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
        return $json.TrimStart([char]0xFEFF) | ConvertFrom-Json
    }
    catch { return $null }
}

function Add-Status([string]$Service, [string]$State, [string]$Address) {
    $Summary.Add([pscustomobject]@{ Service = $Service; State = $State; Address = $Address })
}

function Start-Background([string]$Name, [string]$Executable, [string[]]$Arguments) {
    New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments `
        -WorkingDirectory $ProjectRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $LogRoot "$Name-$stamp.out.log") `
        -RedirectStandardError (Join-Path $LogRoot "$Name-$stamp.err.log")
    Write-Host "Started $Name (PID $($process.Id))."
}

function Ensure-LocalService(
    [string]$Name, [int]$Port, [string]$Url, [scriptblock]$Probe, [scriptblock]$Launch
) {
    if (& $Probe) { Add-Status $Name 'READY' $Url; return }
    if (Test-LocalPort $Port) {
        Add-Status $Name 'PORT CONFLICT / WRONG SERVICE' $Url
        return
    }
    if ($StatusOnly) { Add-Status $Name 'STOPPED' $Url; return }
    try {
        & $Launch
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 400
            if (& $Probe) { Add-Status $Name 'READY' $Url; return }
        } while ([DateTime]::UtcNow -lt $deadline)
        Add-Status $Name 'NOT READY - CHECK LOGS' $Url
    }
    catch { Add-Status $Name ('FAILED: ' + $_.Exception.Message) $Url }
}

$Python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $Python) {
    $venvPython = Join-Path $ProjectRoot '.local-tts\azure-venv\Scripts\python.exe'
    $PythonPath = if (Test-Path -LiteralPath $venvPython) { $venvPython } else { '' }
}
else { $PythonPath = $Python.Source }

Ensure-LocalService 'Ollama' 11434 'http://127.0.0.1:11434/api/version' {
    $health = Get-LocalJson 'http://127.0.0.1:11434/api/version'
    return $null -ne $health -and -not [string]::IsNullOrWhiteSpace($health.version)
} {
    $ollama = Get-Command ollama -ErrorAction Stop
    Start-Background 'ollama' $ollama.Source @('serve')
}

Ensure-LocalService 'Quest session packages' 8765 'http://127.0.0.1:8765/session_package_latest.json' {
    $package = Get-LocalJson 'http://127.0.0.1:8765/session_package_latest.json'
    return $null -ne $package -and $null -ne $package.packageVersion -and $null -ne $package.roomSpec
} {
    if (-not $PythonPath) { throw 'Python was not found.' }
    $packageRoot = Join-Path $ProjectRoot 'VRSessionPackages'
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'session_package_latest.json'))) {
        throw 'Export a Quest session package from Unity first.'
    }
    Start-Background 'quest-packages' $PythonPath @(
        '-u', '-m', 'http.server', '8765', '--bind', '0.0.0.0', '--directory', ('"' + $packageRoot + '"')
    )
}

Ensure-LocalService 'Delayed test website' 8766 'http://127.0.0.1:8766/' {
    $health = Get-LocalJson 'http://127.0.0.1:8766/api/health'
    return $null -ne $health -and $health.ok -eq $true -and $health.service -eq 'one-week-delayed-test'
} {
    if (-not $PythonPath) { throw 'Python was not found.' }
    $serverPath = Join-Path $PSScriptRoot 'DelayedTestWeb\server.py'
    Start-Background 'delayed-test' $PythonPath @('-u', ('"' + $serverPath + '"'), '--host', '127.0.0.1', '--port', '8766')
}

$ttsUrl = 'http://127.0.0.1:8880/health'
$ttsHealth = Get-LocalJson $ttsUrl
$azureScript = Join-Path $PSScriptRoot 'LocalTtsServer\Start-Azure.ps1'
$pendingAzureInput = Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like "*$azureScript*" }
if ($null -ne $ttsHealth -and $ttsHealth.backend -eq 'azure' -and $ttsHealth.status -eq 'ok') {
    Add-Status 'Azure TTS' 'READY' $ttsUrl
}
elseif (Test-LocalPort 8880) {
    Add-Status 'Azure TTS' 'CONFIGURATION REQUIRED / WRONG BACKEND' $ttsUrl
}
elseif ($pendingAzureInput) {
    Add-Status 'Azure TTS' 'WAITING FOR INPUT IN EXISTING TTS WINDOW' $ttsUrl
}
elseif ($StatusOnly -or $SkipTts) {
    Add-Status 'Azure TTS' $(if ($SkipTts) { 'SKIPPED - SECURE INPUT WINDOW MAY BE PENDING' } else { 'STOPPED' }) $ttsUrl
}
else {
    # Visible only because this terminal asks for a secret using Read-Host -AsSecureString.
    $escapedScript = $azureScript.Replace("'", "''")
    $command = '$Host.UI.RawUI.WindowTitle = ''Memory Palace - Azure TTS - enter key here''; ' +
        '$env:LOCAL_TTS_HOST = ''0.0.0.0''; & ''' + $escapedScript + ''''
    Start-Process -FilePath powershell.exe -WindowStyle Normal -WorkingDirectory $ProjectRoot `
        -ArgumentList ('-NoProfile -NoExit -ExecutionPolicy Bypass -Command "' + $command + '"') | Out-Null
    Add-Status 'Azure TTS' 'ENTER KEY AND REGION IN TTS WINDOW' $ttsUrl
}

$adb = Get-Command adb -ErrorAction SilentlyContinue
$AdbPath = if ($adb) { $adb.Source } else { '' }
if (-not $AdbPath) {
    $versionText = Get-Content -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt') -Raw
    if ($versionText -match 'm_EditorVersion:\s*(\S+)') {
        $candidate = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$($Matches[1])\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
        if (Test-Path -LiteralPath $candidate) { $AdbPath = $candidate }
    }
}
if ($AdbPath -and -not $StatusOnly -and -not (Test-LocalPort 5037)) {
    & $AdbPath start-server
}
Add-Status 'ADB (Quest USB transfer)' $(if (Test-LocalPort 5037) { 'RUNNING' } elseif ($AdbPath) { 'STOPPED' } else { 'NOT FOUND - ONLY NEEDED FOR USB TRANSFER' }) '127.0.0.1:5037'

Write-Host ''
$Summary | Format-Table -AutoSize -Wrap
Write-Host "Background-service logs: $LogRoot"
Write-Host 'Use -StatusOnly to check again. TTS is ready only when /health reports status=ok and backend=azure.'
Write-Host 'The Azure key is not saved by this launcher. Keep the Azure TTS window open while using speech.'

$lanAddresses = Get-NetIPConfiguration -ErrorAction SilentlyContinue |
    Where-Object { $_.IPv4DefaultGateway } | ForEach-Object { $_.IPv4Address.IPAddress }
foreach ($address in $lanAddresses) {
    Write-Host "Quest package URL: http://${address}:8765/session_package_latest.json"
    Write-Host "Quest speech base URL: http://${address}:8880/v1"
}

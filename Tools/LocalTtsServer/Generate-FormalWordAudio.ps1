param(
    [string]$Voice = "es-ES-ElviraNeural",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$CatalogPath = Join-Path $ProjectRoot "Assets\Resources\MemPalaceDemoData.json"
$OutputDirectory = Join-Path $ProjectRoot "Assets\Resources\WordAudio"
$Catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$Pools = @($Catalog.wordSets | Where-Object { $_.setId -eq "formal_32_pool" })

if ($Pools.Count -ne 1) {
    throw "Expected exactly one formal_32_pool, found $($Pools.Count)."
}

$Words = @($Pools[0].words | ForEach-Object { $_.word })
$UniqueWords = @($Words | Sort-Object -Unique)
if ($Words.Count -ne 32 -or $UniqueWords.Count -ne 32) {
    throw "formal_32_pool must contain exactly 32 unique words. Found $($Words.Count) entries and $($UniqueWords.Count) unique words."
}

$SpeechKey = $env:AZURE_SPEECH_KEY
if ([string]::IsNullOrWhiteSpace($SpeechKey)) {
    $SecureKey = Read-Host "Azure Speech key" -AsSecureString
    $KeyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureKey)
    try {
        $SpeechKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($KeyPointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($KeyPointer)
    }
}

$Region = $env:AZURE_SPEECH_REGION
if ([string]::IsNullOrWhiteSpace($Region)) {
    $Region = Read-Host "Azure Speech region (for example: japaneast)"
}

$Region = $Region.Trim().ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($SpeechKey) -or $Region -notmatch "^[a-z0-9-]+$") {
    throw "A valid Azure Speech key and region are required."
}

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

function Get-PcmWavInfo {
    param([string]$Path)

    $Bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($Bytes.Length -lt 44 -or
        [System.Text.Encoding]::ASCII.GetString($Bytes, 0, 4) -ne "RIFF" -or
        [System.Text.Encoding]::ASCII.GetString($Bytes, 8, 4) -ne "WAVE") {
        throw "Azure returned an invalid WAV file: $Path"
    }

    $Channels = 0
    $SampleRate = 0
    $BitsPerSample = 0
    $DataBytes = 0
    $Offset = 12
    while ($Offset + 8 -le $Bytes.Length) {
        $ChunkId = [System.Text.Encoding]::ASCII.GetString($Bytes, $Offset, 4)
        $ChunkSize = [System.BitConverter]::ToInt32($Bytes, $Offset + 4)
        $ChunkDataOffset = $Offset + 8
        if ($ChunkSize -lt 0 -or $ChunkDataOffset + $ChunkSize -gt $Bytes.Length) {
            throw "Azure returned a malformed WAV chunk in $Path"
        }

        if ($ChunkId -eq "fmt " -and $ChunkSize -ge 16) {
            $AudioFormat = [System.BitConverter]::ToInt16($Bytes, $ChunkDataOffset)
            $Channels = [System.BitConverter]::ToInt16($Bytes, $ChunkDataOffset + 2)
            $SampleRate = [System.BitConverter]::ToInt32($Bytes, $ChunkDataOffset + 4)
            $BitsPerSample = [System.BitConverter]::ToInt16($Bytes, $ChunkDataOffset + 14)
            if ($AudioFormat -ne 1) {
                throw "Expected PCM audio from Azure, received WAV format $AudioFormat."
            }
        }
        elseif ($ChunkId -eq "data") {
            $DataBytes = $ChunkSize
        }

        $Offset = $ChunkDataOffset + $ChunkSize + ($ChunkSize % 2)
    }

    if ($Channels -ne 1 -or $SampleRate -ne 24000 -or $BitsPerSample -ne 16 -or $DataBytes -le 0) {
        throw "Expected mono 24 kHz 16-bit PCM audio from Azure: $Path"
    }

    return [pscustomobject]@{
        DurationSeconds = $DataBytes / [double]($SampleRate * $Channels * ($BitsPerSample / 8))
        SampleRate = $SampleRate
        Channels = $Channels
        BitsPerSample = $BitsPerSample
    }
}

$Endpoint = "https://$Region.tts.speech.microsoft.com/cognitiveservices/v1"
$Headers = @{
    "Ocp-Apim-Subscription-Key" = $SpeechKey
    "X-Microsoft-OutputFormat" = "riff-24khz-16bit-mono-pcm"
    "User-Agent" = "MemPalaceLLM-FormalWordAudio"
}
$Generated = 0
$Skipped = 0
for ($Index = 0; $Index -lt $Words.Count; $Index++) {
    $Word = $Words[$Index]
    $OutputPath = Join-Path $OutputDirectory ($Word + ".wav")
    if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
        Write-Host "[$($Index + 1)/$($Words.Count)] Existing: $Word"
        $Skipped++
        continue
    }

    Write-Host "[$($Index + 1)/$($Words.Count)] Azure es-ES: $Word"
    $EscapedWord = [System.Security.SecurityElement]::Escape($Word)
    $Ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="es-ES">
  <voice name="$Voice">$EscapedWord.</voice>
</speak>
"@
    $CandidatePath = $OutputPath + ".azure-candidate.wav"
    $SsmlBytes = [System.Text.Encoding]::UTF8.GetBytes($Ssml)
    try {
        Invoke-WebRequest `
            -Uri $Endpoint `
            -Method Post `
            -Headers $Headers `
            -ContentType "application/ssml+xml; charset=utf-8" `
            -Body $SsmlBytes `
            -OutFile $CandidatePath `
            -UseBasicParsing | Out-Null

        $Info = Get-PcmWavInfo -Path $CandidatePath
        if ($Info.DurationSeconds -lt 0.25 -or $Info.DurationSeconds -gt 5.0) {
            throw "Unexpected pronunciation duration $('{0:F2}' -f $Info.DurationSeconds)s for $Word."
        }

        Move-Item -LiteralPath $CandidatePath -Destination $OutputPath -Force
        Write-Host "  Saved $('{0:F2}' -f $Info.DurationSeconds)s, 24 kHz mono PCM."
        $Generated++
    }
    finally {
        if (Test-Path -LiteralPath $CandidatePath) {
            Remove-Item -LiteralPath $CandidatePath -Force
        }
    }
}

$FinalFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter "*.wav" -File)
$FinalNames = @($FinalFiles.BaseName)
$Missing = @($Words | Where-Object { $_ -notin $FinalNames })
$Extra = @($FinalNames | Where-Object { $_ -notin $Words })
if ($FinalFiles.Count -ne 32 -or $Missing.Count -gt 0 -or $Extra.Count -gt 0) {
    throw "WordAudio validation failed: files=$($FinalFiles.Count), missing=$($Missing.Count), extra=$($Extra.Count)."
}

Write-Host "Prepared all 32 Azure $Voice Spanish word clips in $OutputDirectory (generated=$Generated, existing=$Skipped)."

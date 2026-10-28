[CmdletBinding()]
param([string]$PackagePath)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $latest = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Builds/Collaboration') -Filter 'MemoryPalace-Source-*.zip' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'No collaboration package found. Run Export-CollaborationPackage.ps1 first.' }
    $PackagePath = $latest.FullName
}
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $entries = @{}
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName
        if ($entries.ContainsKey($name) -or $name -match '(^/|\\|(^|/)\.\.(/|$)|^[A-Za-z]:)') {
            throw "Unsafe or duplicate ZIP path: $name"
        }
        if ($name -match '(?i)(^|/)(\.git|Library|Temp|UserSettings|Demos|VRSessionPackages|ExperimentExports|GeneratedRooms|\.local-tts)(/|$)' -or
            $name -match '(?i)(LocalConfig|session_package|\.(png|jpe?g|fbx|wav|mp3|apk|unitypackage)$)') {
            throw "Disallowed ZIP entry: $name"
        }
        $entries[$name] = $entry
    }
    foreach ($required in @('README.md', 'SHARING_NOTES.md', 'SOURCE_MANIFEST.json', 'ProjectSettings/ProjectVersion.txt',
        'Packages/manifest.json', 'Packages/packages-lock.json', 'Assets/Scenes/SampleScene.unity',
        'Assets/Resources/EnglishInflectionsNotice.txt', 'Docs/DEVELOPER_GUIDE.md')) {
        if (-not $entries.ContainsKey($required)) { throw "Required ZIP entry missing: $required" }
    }
    $reader = [IO.StreamReader]::new($entries['SOURCE_MANIFEST.json'].Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    $sourceNames = @{}
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in $manifest.files) {
            if ($sourceNames.ContainsKey($file.path) -or -not $entries.ContainsKey($file.path)) {
                throw "Manifest entry is duplicated or missing: $($file.path)"
            }
            $sourceNames[$file.path] = $true
            $stream = $entries[$file.path].Open()
            try { $actual = [BitConverter]::ToString($hashAlgorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose() }
            if ($actual -ne $file.sha256) { throw "Source hash mismatch: $($file.path)" }
            if ($file.path -like 'Assets/*' -and $file.path -notlike '*.meta' -and
                -not $entries.ContainsKey($file.path + '.meta')) {
                throw "Unity asset is missing its meta: $($file.path)"
            }
        }
    }
    finally { $hashAlgorithm.Dispose() }
    if ($entries.Count -ne $sourceNames.Count + 2) { throw 'ZIP has unlisted source entries.' }

    # Resolve relative Markdown links against the archive, not the original workspace.
    foreach ($name in @($entries.Keys | Where-Object { $_ -like '*.md' })) {
        $reader = [IO.StreamReader]::new($entries[$name].Open())
        try { $body = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($match in [regex]::Matches($body, '\]\(([^)]+)\)')) {
            $link = $match.Groups[1].Value.Trim('<', '>')
            if ($link -match '^(?:[a-z][a-z0-9+.-]*:|#|/)') { continue }
            $link = ($link -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($link)) { continue }
            $baseUri = [Uri]::new('https://source.invalid/' + $name)
            $resolved = [Uri]::UnescapeDataString(([Uri]::new($baseUri, $link)).AbsolutePath.TrimStart('/'))
            if (-not $entries.ContainsKey($resolved) -and -not @($entries.Keys | Where-Object { $_.StartsWith($resolved.TrimEnd('/') + '/') }).Count) {
                throw "Broken relative Markdown link in ${name}: $link"
            }
        }
    }
    Write-Host ("Package checks passed: {0} sources; hashes, exclusions, Unity metas and Markdown links verified." -f $sourceNames.Count)
    Write-Host $PackagePath
}
finally { $archive.Dispose() }

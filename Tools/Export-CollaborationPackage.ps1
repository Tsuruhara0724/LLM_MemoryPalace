[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$separator = [IO.Path]::DirectorySeparatorChar
$rootPrefix = $projectRoot.TrimEnd($separator) + $separator

# An explicit source allowlist: never archive the working directory wholesale.
$sourceDirectories = @(
    'Assets/Scripts', 'Assets/Editor', 'Assets/Scenes', 'Assets/Settings',
    'Assets/XR', 'Assets/Plugins', 'Assets/Resources/Shaders',
    'Assets/Resources/ExampleRooms', 'Docs/MnemonicStoryPromptV2', 'Tools', 'ProjectSettings'
)
$sourceFiles = @(
    '.gitignore', 'README.md',
    'Packages/manifest.json', 'Packages/packages-lock.json',
    'Assets/Resources/EnglishInflections.bytes',
    'Assets/Resources/EnglishInflectionsNotice.txt',
    'Assets/Resources/MemPalaceDemoData.json', 'Assets/Resources/MemPalaceRoomSpec.json',
    'Assets/Resources/FurniturePrefabs/README.md',
    'Assets/Resources/FurniturePrefabs/bookshelf_cc0/license.txt',
    'Assets/Resources/WordImages/README.md', 'Assets/Resources/WordImages/ATTRIBUTION.md',
    'Docs/DEVELOPER_GUIDE.md', 'Docs/PROJECT_CONTEXT.md', 'Docs/STARTUP.md',
    'Docs/THIRD_PARTY_ASSETS.md', 'Docs/MAINTENANCE_2026-10-28.md', 'Docs/VR_CLICK_TO_LEARN.md',
    'Docs/VR_COMFORT_VIGNETTE.md', 'Docs/PARTICIPANT_STORY_ENGLISH.md',
    'Docs/VOCABULARY_AUDIT.md', 'Docs/StoryGeneratorPrompt_MSF21.cs',
    'Docs/STORY_GENERATOR_FRAMEWORK_SHARE.md'
)
$extensions = @('.cs', '.meta', '.json', '.inputactions', '.unity', '.asset',
    '.xml', '.shader', '.hlsl', '.asmdef', '.asmref', '.md', '.txt', '.py',
    '.ps1', '.cmd', '.html', '.js', '.css')
$paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Add-SourcePath([string]$relative) {
    $relative = $relative.Replace('\', '/')
    $fullPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Source path escapes the project.'
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return }
    $item = Get-Item -LiteralPath $fullPath
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Linked files are not allowed in the source package: $relative"
    }
    [void]$paths.Add($relative)
}

foreach ($relative in $sourceFiles) {
    Add-SourcePath $relative
    Add-SourcePath ($relative + '.meta')
}
foreach ($directory in $sourceDirectories) {
    $fullDirectory = Join-Path $projectRoot $directory
    if (-not (Test-Path -LiteralPath $fullDirectory -PathType Container)) { continue }
    # Reject junctions instead of accidentally following local service environments.
    $items = @(Get-ChildItem -LiteralPath $fullDirectory -Recurse -Force)
    if (@($items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -gt 0) {
        throw "Linked directories/files require manual review: $directory"
    }
    foreach ($item in $items) {
        if ($item.PSIsContainer -or $item.FullName -match '[\\/](?:__pycache__|\.venv|node_modules|\.git)[\\/]') { continue }
        if ($extensions -notcontains $item.Extension.ToLowerInvariant()) { continue }
        $relative = $item.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        # The delayed-test fixture is synthetic; no other session/result JSON belongs here.
        if ($relative -match '(?i)(?:^|/)(?:session_package[^/]*|session_P-(?!DEMO_)[^/]*|.*LocalConfig[^/]*|\.env[^/]*)$') { continue }
        Add-SourcePath $relative
    }
}

# Retain folder GUIDs only when the corresponding exported folder exists.
foreach ($relative in @($paths)) {
    $parent = [IO.Path]::GetDirectoryName($relative)
    while (-not [string]::IsNullOrEmpty($parent)) {
        Add-SourcePath ($parent.Replace('\', '/') + '.meta')
        $parent = [IO.Path]::GetDirectoryName($parent)
    }
}

$required = @('Assets/Scenes/SampleScene.unity', 'Assets/Scripts/Runtime/MemoryPalaceExperimentController.cs',
    'Assets/Scripts/Services/FurniturePrefabAvailability.cs', 'ProjectSettings/ProjectVersion.txt',
    'Packages/manifest.json', 'Docs/DEVELOPER_GUIDE.md')
foreach ($relative in $required) {
    if (-not $paths.Contains($relative)) { throw "Required source missing: $relative" }
}

# Conservative pattern check; never print credential values. Manual review remains necessary.
$credentialPattern = '(?i)\bsk-ant-[A-Za-z0-9_-]{20,}|\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}|\bAIza[0-9A-Za-z_-]{30,}|(?:api[_-]?key|AZURE_SPEECH_KEY)["'']?\s*[:=]\s*["''][A-Za-z0-9_+/=-]{24,}["'']|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
$manifest = @()
foreach ($relative in @($paths | Sort-Object)) {
    if ($relative -match '(?i)(?:^|/)(?:ExperimentExports|GeneratedRooms|Demos|VRSessionPackages|Library|Temp|UserSettings|\.git|\.local-tts)(?:/|$)' -or
        $relative -match '(?i)(?:LocalConfig|session_package)' -or
        $relative -match '(?i)\.(?:png|jpg|jpeg|fbx|wav|mp3|unitypackage|apk)$') {
        throw "Disallowed source file selected: $relative"
    }
    $fullPath = Join-Path $projectRoot $relative
    if ($relative -notlike '*.bytes' -and [IO.File]::ReadAllText($fullPath) -match $credentialPattern) {
        throw "Potential credential detected; review this file locally: $relative"
    }
    $manifest += [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$outputDirectory = Join-Path $projectRoot 'Builds/Collaboration'
[void][IO.Directory]::CreateDirectory($outputDirectory)
$outputPath = Join-Path $outputDirectory ('MemoryPalace-Source-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
$partialPath = $outputPath + '.partial'
$notes = @'
# Memory Palace development source

Start with README.md and Docs/DEVELOPER_GUIDE.md.
This archive contains no Git history, local provider configuration or participant exports.
Furniture source models, word/UI images, old image catalogs and prepared word audio are
intentionally omitted. Procedural furniture and blank image placeholders are for development
only, not equivalent research stimuli. Regenerate or restore authorized word audio before
attempting a complete study. See Docs/THIRD_PARTY_ASSETS.md for the material audit.

SOURCE_MANIFEST.json records the exported source hashes. No source files in the original
workspace were changed by this export. Secret-pattern scanning is a precaution, not a guarantee
that arbitrary future project changes are safe to publish. Review before distributing.
'@

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = $null
$archive = $null
try {
    $stream = [IO.File]::Open($partialPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    foreach ($entry in $manifest) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $projectRoot $entry.path), $entry.path, [IO.Compression.CompressionLevel]::Optimal)
    }
    $metadata = [ordered]@{
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        purpose = 'Development source; research assets and local data excluded'
        files = $manifest
    } | ConvertTo-Json -Depth 5
    foreach ($extra in @(@{ name = 'SHARING_NOTES.md'; value = $notes }, @{ name = 'SOURCE_MANIFEST.json'; value = $metadata })) {
        $zipEntry = $archive.CreateEntry($extra.name)
        $writer = [IO.StreamWriter]::new($zipEntry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($extra.value) } finally { $writer.Dispose() }
    }
    $archive.Dispose(); $archive = $null
    $stream.Dispose(); $stream = $null
    Move-Item -LiteralPath $partialPath -Destination $outputPath
    Write-Host ("Created {0} ({1} source files, {2:N1} MiB)" -f $outputPath, $manifest.Count, ((Get-Item -LiteralPath $outputPath).Length / 1MB))
    Write-Host ('SHA256: ' + (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash)
}
finally {
    if ($null -ne $archive) { $archive.Dispose() }
    if ($null -ne $stream) { $stream.Dispose() }
    if (Test-Path -LiteralPath $partialPath -PathType Leaf) { Remove-Item -LiteralPath $partialPath }
}

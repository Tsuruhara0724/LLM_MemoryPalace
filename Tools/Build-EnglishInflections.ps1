param([string]$SourceDirectory)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $projectDirectory '.utmp/agid-source' }
$sourcePath = Join-Path $SourceDirectory 'infl.txt'
$expectedHash = '3B614BB15416D97D3B3ABB0D384939BF46F5B50E2EF920D41BDD11EC1D07BAE5'
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'AGID source does not match the documented snapshot.'
}

# Mechanical data conversion only. Do not infer forms or include doubtful/rare entries.
$entries = [Collections.Generic.SortedDictionary[string, Collections.Generic.SortedSet[string]]]::new([StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadLines($sourcePath)) {
    if ($line -cnotmatch '^([a-z]{2,}) ([VNA]): (.+)$') { continue }
    $lemma = $Matches[1]
    $formsText = $Matches[3]
    foreach ($entry in ($formsText -split '[|,]')) {
        if ($entry.Trim() -cnotmatch '^([a-z]+)([~<!?]*)(?: (\d+(?:\.\d+)?))?(?: \{[^}]*\})?$') { continue }
        $form = $Matches[1]
        if ($Matches[2]) { continue }
        if ($Matches[3] -and [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture) -ge 2) { continue }
        if ($form -ceq $lemma) { continue }
        if (-not $entries.ContainsKey($lemma)) {
            $entries.Add($lemma, [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal))
        }
        [void]$entries[$lemma].Add($form)
    }
}

$table = [Text.StringBuilder]::new()
[void]$table.Append("# AGID-derived inflections; Copyright 2000-2016 Kevin Atkinson and upstream contributors.`n")
[void]$table.Append("# Source/notices: EnglishInflectionsNotice.txt; no inferred/doubtful/level-2 forms.`n")
foreach ($entry in $entries.GetEnumerator()) {
    [void]$table.Append($entry.Key).Append("`t").Append(($entry.Value -join ',')).Append("`n")
}
$resourceDirectory = Join-Path $projectDirectory 'Assets/Resources'
$outputPath = Join-Path $resourceDirectory 'EnglishInflections.bytes'
$output = [IO.File]::Create($outputPath)
try {
    $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::Optimal, $true)
    try {
        $data = [Text.Encoding]::UTF8.GetBytes($table.ToString())
        $gzip.Write($data, 0, $data.Length)
    } finally { $gzip.Dispose() }
} finally { $output.Dispose() }

$readmeHtml = [IO.File]::ReadAllText((Join-Path $SourceDirectory 'readme.html'))
$pre = [regex]::Match($readmeHtml, '(?s)<pre[^>]*>(.*?)</pre>')
if (-not $pre.Success) { throw 'Cannot locate upstream AGID documentation/notices.' }
$notice = [Net.WebUtility]::HtmlDecode([regex]::Replace($pre.Groups[1].Value, '<[^>]+>', ''))
if (-not $notice.Contains('COPYRIGHT AND SOURCE:') -or -not $notice.Contains('Princeton University')) {
    throw 'Incomplete AGID copyright notices.'
}
$provenance = @"
Memory Palace English inflection data
Upstream documentation: https://wordlist.aspell.net/agid-readme/
Data snapshot: https://raw.githubusercontent.com/chadsowald/agid-irony-parser/6814807058dee1b9694dac547f768cd7428474c2/AgidIronyParser.Tests/infl.txt
SHA256: $expectedHash
Adaptation: lowercase lexical entries with known POS; exclude flagged and level-2 variants; merge forms per lemma; gzip UTF-8 tab/comma table.
The upstream documentation and notices follow in full. No parser implementation was copied.

"@
[IO.File]::WriteAllText((Join-Path $resourceDirectory 'EnglishInflectionsNotice.txt'), $provenance + $notice, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Lemmas = $entries.Count; CompressedBytes = (Get-Item -LiteralPath $outputPath).Length; TextCharacters = $table.Length }

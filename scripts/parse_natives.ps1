<#
.SYNOPSIS
  Tear apart api_docs/gta5-nativedb-data/natives.json (legacy aggregated file) into
  per-native JSON files under <SourceDir>\natives_parsed\, organized by namespace,
  plus a root index.json for quick hash lookups.

.DESCRIPTION
  Output layout:
    <OutDir>\
    ├── index.json
    └── by_namespace\
        └── <NS>\
            └── <NAME-or-HASH>.json   (one file per native)

  The OutDir is cleaned (existing files removed) before writing, so re-runs are
  deterministic. Use -KeepOut to merge into an existing parsed tree.

.PARAMETER Source
  Path to legacy natives.json. Default: <project root>\api_docs\gta5-nativedb-data\natives.json

.PARAMETER OutDir
  Output directory. Default: <SourceDir>\natives_parsed

.PARAMETER KeepOut
  If set, do not delete the existing OutDir before writing.

.EXAMPLE
  pwsh -File scripts\parse_natives.ps1

.EXAMPLE
  pwsh -File scripts\parse_natives.ps1 -Source 'D:\corpus\natives.json' -OutDir 'D:\corpus\parsed'
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$OutDir,
    [switch]$KeepOut
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DefaultSource = Join-Path $ProjectRoot 'api_docs\gta5-nativedb-data\natives.json'
$DefaultOutDir = Join-Path (Split-Path -Parent $DefaultSource) 'natives_parsed'
if (-not $PSBoundParameters.ContainsKey('Source')) { $Source = $DefaultSource }
if (-not $PSBoundParameters.ContainsKey('OutDir')) { $OutDir = $DefaultOutDir }

function ConvertTo-SafeName([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $null }
    return ($s.Trim() -replace '[<>:"/\\|?*]', '_').ToUpper()
}

# Pre-flight
if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw "Không tìm thấy source file: '$Source'. Hãy chạy scripts/bootstrap_api_docs.ps1 trước (hoặc dùng -Source)."
}

# Clean output unless -KeepOut
if ((Test-Path -LiteralPath $OutDir) -and -not $KeepOut) {
    Write-Host "Cleaning $OutDir ..."
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$byNsDir = Join-Path $OutDir 'by_namespace'
New-Item -ItemType Directory -Force -Path $byNsDir | Out-Null

# Load + parse
Write-Host "Parsing $Source -> $OutDir"
$rawJson = Get-Content -LiteralPath $Source -Raw -Encoding utf8
$data = $rawJson | ConvertFrom-Json

$index = [ordered]@{}
$nsCount = 0; $nativeCount = 0; $skipped = 0

foreach ($nsProp in $data.PSObject.Properties) {
    $nsRaw = $nsProp.Name
    $nsSafe = ConvertTo-SafeName $nsRaw
    if (-not $nsSafe) { continue }
    $nsDir = Join-Path $byNsDir $nsSafe
    New-Item -ItemType Directory -Force -Path $nsDir | Out-Null
    $nsCount++
    $nameSeen = @{}
    $nsValue = $nsProp.Value

    foreach ($hashProp in $nsValue.PSObject.Properties) {
        $hash = $hashProp.Name
        $entry = $hashProp.Value
        $nativeName = $entry.name
        $fileSafe = $null
        if ($nativeName -and ($nativeName -ne $hash)) {
            $fileSafe = ConvertTo-SafeName $nativeName
        }
        if (-not $fileSafe) { $fileSafe = ConvertTo-SafeName $hash }
        if (-not $fileSafe) { $skipped++; continue }

        $original = $fileSafe
        if ($nameSeen.ContainsKey($fileSafe)) {
            $shortHash = if ($hash -like '0x*') { $hash.Substring(2) } else { $hash }
            $fileSafe = "${original}_$shortHash"
        }
        $nameSeen[$fileSafe] = $true

        $entryWithCtx = [ordered]@{ hash = $hash; namespace = $nsRaw }
        foreach ($p in $entry.PSObject.Properties) { $entryWithCtx[$p.Name] = $p.Value }

        $outFile = Join-Path $nsDir "$fileSafe.json"
        $entryJson = $entryWithCtx | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($outFile, $entryJson, (New-Object System.Text.UTF8Encoding $false))
        $index[$hash] = "by_namespace/$nsSafe/$fileSafe.json"
        $nativeCount++
    }
}

$indexPath = Join-Path $OutDir 'index.json'
$indexJson = $index | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($indexPath, $indexJson, (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Done. namespaces=$nsCount natives=$nativeCount skipped=$skipped out='$OutDir'"

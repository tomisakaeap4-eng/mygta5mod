<#
.SYNOPSIS
  Tear apart local_api_docs/ScriptHookVDotNet3.xml (SHVDN v3 .NET XML doc) into
  per-member JSON files under <SourceDir>\parsed\, organized by namespace+type,
  plus a root index.json for quick member-name lookups.

.DESCRIPTION
  Output layout:
    <OutDir>\
    ├── index.json
    └── by_namespace\
        └── <NS>\
            └── <TypeName>\
                └── <Kind>__<MemberName>.json   (one file per <member>)

  The OutDir is cleaned (existing files removed) before writing, so re-runs are
  deterministic. Use -KeepOut to merge into an existing parsed tree.

.PARAMETER Source
  Path to SHVDN v3 XML doc. Default: <project root>\local_api_docs\ScriptHookVDotNet3.xml

.PARAMETER OutDir
  Output directory. Default: <SourceDir>\parsed

.PARAMETER KeepOut
  If set, do not delete the existing OutDir before writing.

.EXAMPLE
  pwsh -File scripts\parse_local_api_docs.ps1
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$OutDir,
    [switch]$KeepOut
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DefaultSource = Join-Path $ProjectRoot 'local_api_docs\ScriptHookVDotNet3.xml'
$DefaultOutDir = Join-Path (Split-Path -Parent $DefaultSource) 'parsed'
if (-not $PSBoundParameters.ContainsKey('Source')) { $Source = $DefaultSource }
if (-not $PSBoundParameters.ContainsKey('OutDir')) { $OutDir = $DefaultOutDir }

function ConvertTo-SafeName([string]$s, [int]$MaxLen = 120) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $null }
    $s = $s.Trim() -replace '[<>:"/\\|?*]', '_'
    if ($s.Length -gt $MaxLen) {
        $hash = [System.Security.Cryptography.MD5]::HashData([System.Text.Encoding]::UTF8.GetBytes($s))
        $hashStr = -join ($hash[0..3] | ForEach-Object { $_.ToString('x2') })
        $s = $s.Substring(0, $MaxLen - 9) + '_' + $hashStr
    }
    return $s
}

if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw "Không tìm thấy source file: '$Source'."
}

if ((Test-Path -LiteralPath $OutDir) -and -not $KeepOut) {
    Write-Host "Cleaning $OutDir ..."
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "Parsing $Source -> $OutDir"

# Load + parse XML
$xml = [xml](Get-Content -LiteralPath $Source -Raw -Encoding utf8)
$members = $xml.doc.members
if ($null -eq $members) {
    throw "Không tìm thấy <members> element trong $Source."
}

$index = [ordered]@{}
$count = 0
$skipped = 0
$seen = @{}

foreach ($m in $members.member) {
    $name = $m.name
    if ([string]::IsNullOrEmpty($name)) { $skipped++; continue }
    if ($name -notmatch '^([TMPFE]):(.+)$') { $skipped++; continue }
    $kind = $matches[1]
    $rest = $matches[2]
    $parts = $rest -split '\.'
    if ($parts.Count -lt 2) { $skipped++; continue }
    $memberName = $parts[-1]
    if ($kind -eq 'M') {
        $parenIdx = $memberName.IndexOf('(')
        if ($parenIdx -gt 0) { $memberName = $memberName.Substring(0, $parenIdx) }
    }
    $typeParts = $parts[0..($parts.Count - 2)]
    $typeName = $typeParts[-1]
    $ns = if ($typeParts.Count -gt 1) { ($typeParts[0..($typeParts.Count - 2)] -join '.') } else { '' }

    $nsSafe = ConvertTo-SafeName $ns; if (-not $nsSafe) { $nsSafe = '_root' }
    $typeSafe = ConvertTo-SafeName $typeName; if (-not $typeSafe) { $typeSafe = '_anon' }
    $memberSafe = ConvertTo-SafeName $memberName; if (-not $memberSafe) { $memberSafe = '_anon' }

    $relDir = "by_namespace/$nsSafe/$typeSafe"
    $base = "${kind}__${memberSafe}"
    $collisionKey = "$relDir/$base"
    if ($seen.ContainsKey($collisionKey)) {
        $hash = [System.Security.Cryptography.MD5]::HashData([System.Text.Encoding]::UTF8.GetBytes($name))
        $hashStr = -join ($hash[0..3] | ForEach-Object { $_.ToString('x2') })
        $base = "${base}_${hashStr}"
    }
    $seen[$collisionKey] = $true

    $relPath = "$relDir/$base.json"
    $outFile = Join-Path $OutDir $relPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outFile) | Out-Null

    $entry = [ordered]@{
        name = $name
        kind = $kind
        namespace = $ns
        typeName = $typeName
        memberName = $memberName
    }
    if ($m.summary)   { $entry.summary   = [string]$m.summary }
    if ($m.remarks)   { $entry.remarks   = [string]$m.remarks }
    if ($m.returns)   { $entry.returns   = [string]$m.returns }
    if ($m.value)     { $entry.value     = [string]$m.value }
    if ($m.param) {
        $params = @()
        foreach ($p in $m.param) {
            $params += [ordered]@{
                name = $p.name
                description = [string]$p
            }
        }
        $entry.params = $params
    }
    if ($m.exception) {
        $exceptions = @()
        foreach ($e in $m.exception) {
            $exceptions += [ordered]@{
                cref = $e.cref
                description = [string]$e
            }
        }
        $entry.exceptions = $exceptions
    }

    $entryJson = $entry | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($outFile, $entryJson, (New-Object System.Text.UTF8Encoding $false))
    $index[$name] = $relPath
    $count++
}

$indexPath = Join-Path $OutDir 'index.json'
$indexJson = $index | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($indexPath, $indexJson, (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Done. members=$count skipped=$skipped out='$OutDir'"

<#
.SYNOPSIS
  Tear apart local_api_docs/ScriptHookVDotNet3.xml (SHVDN v3 .NET XML doc) into
  REAL sub-XML files (NOT a JSON reformat) that mirror the original XML structure
  one-to-one:
    - <assembly>  -> assembly.xml                  (the original <assembly> element)
    - <members>   -> members\                      (a directory mirroring <members>)
        - <member> -> members\<K>__<Name>.xml      (one file per <member>, with the
                                                  original <member> element verbatim)
    - index.json                                   (member-name -> relative-path)

.DESCRIPTION
  This is deliberately NOT modeled after the parse_natives.ps1 / gen.json layout
  (which used by_namespace/<NS>/<name>.json). Here the folder structure IS the
  XML structure: 1 dir for <assembly>, 1 dir for <members>, 1 file per <member>.
  The agent can read these files directly with any XML parser.

  Each .xml file is a valid standalone XML document with a `<?xml ?>` declaration
  and the <member> element pretty-printed with 4-space indent (matches source).

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

# XmlWriter settings: pretty-print with 4-space indent, no BOM, include XML decl
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.IndentChars = '    '
$settings.Encoding = New-Object System.Text.UTF8Encoding $false
$settings.OmitXmlDeclaration = $false
$settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

# --- <assembly> -> assembly.xml ----------------------------------------------
if ($xml.doc.assembly) {
    $asmPath = Join-Path $OutDir 'assembly.xml'
    $asmDoc = New-Object System.Xml.XmlDocument
    [void]$asmDoc.AppendChild($asmDoc.ImportNode($xml.doc.assembly, $true))
    $writer = [System.Xml.XmlWriter]::Create($asmPath, $settings)
    $asmDoc.Save($writer)
    $writer.Close()
    Write-Host "  assembly.xml: wrote <assembly> element"
}

# --- <members> -> members\<K>__<Name>.xml -----------------------------------
$members = $xml.doc.members
if ($null -eq $members) {
    throw "Không tìm thấy <members> element trong $Source."
}

$membersDir = Join-Path $OutDir 'members'
New-Item -ItemType Directory -Force -Path $membersDir | Out-Null

$index = [ordered]@{}
$seen = @{}
$count = 0
$skipped = 0

foreach ($m in $members.member) {
    $name = $m.name
    if ([string]::IsNullOrEmpty($name)) { $skipped++; continue }
    if ($name -notmatch '^([TMPFE]):(.+)$') { $skipped++; continue }
    $kind = $matches[1]
    $rest = $matches[2]

    # Split "Namespace.TypeName.MemberName[(Params)]" on the LAST '.'
    $lastDot = $rest.LastIndexOf('.')
    if ($lastDot -le 0) { $skipped++; continue }

    $memberName = $rest.Substring($lastDot + 1)
    if ($kind -eq 'M') {
        $parenIdx = $memberName.IndexOf('(')
        if ($parenIdx -gt 0) { $memberName = $memberName.Substring(0, $parenIdx) }
    }
    # Sanitize generic backticks: ``Call`1`` -> ``Call_1``
    $memberName = $memberName -replace '`', '_'

    $base = "${kind}__${memberName}"
    if ($seen.ContainsKey($base)) {
        # Use MD5.Create() (works on PS 5.1 with .NET Framework 4.8 + PS 7+ with .NET 5+).
        # MD5.HashData (static) is .NET 5+ only and would throw on PS 5.1.
        $md5 = [System.Security.Cryptography.MD5]::Create()
        try {
            $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($name))
        } finally {
            $md5.Dispose()
        }
        $hashStr = -join ($hash[0..3] | ForEach-Object { $_.ToString('x2') })
        $base = "${base}_${hashStr}"
    }
    $seen[$base] = $true

    $outFile = Join-Path $membersDir "${base}.xml"
    $memberDoc = New-Object System.Xml.XmlDocument
    [void]$memberDoc.AppendChild($memberDoc.ImportNode($m, $true))
    $writer = [System.Xml.XmlWriter]::Create($outFile, $settings)
    $memberDoc.Save($writer)
    $writer.Close()

    $index[$name] = "members/${base}.xml"
    $count++
}

# --- index.json (auxiliary; the .xml files are the source of truth) ----------
$indexPath = Join-Path $OutDir 'index.json'
$indexJson = $index | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($indexPath, $indexJson, (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Done. members=$count skipped=$skipped out='$OutDir'"

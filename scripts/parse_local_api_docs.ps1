<#
.SYNOPSIS
  Parse local ScriptHookVDotNet and LemonUI XML docs into validated lookup trees.

.DESCRIPTION
  Each XML <member> is preserved in its own valid XML document. The root index
  stays compact and points to type/kind lookup shards that contain exact
  canonical member names, qualified names, signatures, kinds, and paths.

  With no -Source/-OutDir arguments, this parses both default local API docs:
    local_api_docs/ScriptHookVDotNet3.xml -> local_api_docs/parsed/scripthookvdotnet3
    local_api_docs/LemonUI.SHVDN3.xml     -> local_api_docs/parsed/lemonui-shvdn3

  Supplying -Source and/or -OutDir switches to single-document mode. All output
  is generated in a staging directory and validated before replace.
#>
[CmdletBinding()]
param(
    [Alias('s')]
    [string]$Source,
    [Alias('o')]
    [string]$OutDir,
    [switch]$KeepOut
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Parser = Join-Path $PSScriptRoot 'parse_api_docs.py'

if (-not (Test-Path -LiteralPath $Parser -PathType Leaf)) {
    throw "Missing shared parser: $Parser"
}

$Python = Get-Command python -ErrorAction SilentlyContinue
$UsePyLauncher = $false
if ($null -eq $Python) {
    $Python = Get-Command py -ErrorAction SilentlyContinue
    $UsePyLauncher = $null -ne $Python
}
if ($null -eq $Python) {
    throw 'Python 3 is required to parse local API XML. Install Python 3 and make python or py available on PATH.'
}

function Get-DocumentId {
    param([Parameter(Mandatory=$true)][string]$Path)

    $Name = [System.IO.Path]::GetFileNameWithoutExtension($Path).ToLowerInvariant()
    $Name = $Name -replace '[^a-z0-9]+', '-'
    $Name = $Name.Trim('-')
    if ([string]::IsNullOrWhiteSpace($Name)) {
        return 'local-api-doc'
    }
    return $Name
}

function Invoke-ParseLocalXml {
    param(
        [Parameter(Mandatory=$true)][string]$DocumentSource,
        [Parameter(Mandatory=$true)][string]$DocumentOutDir
    )

    if (-not (Test-Path -LiteralPath $DocumentSource -PathType Leaf)) {
        throw "Missing local API XML: $DocumentSource"
    }

    Write-Host "Parsing local API XML: $DocumentSource -> $DocumentOutDir"

    $PythonArguments = @()
    if ($UsePyLauncher) { $PythonArguments += '-3' }
    $PythonArguments += @($Parser, 'local-xml', '--source', $DocumentSource, '--out-dir', $DocumentOutDir)
    if ($KeepOut) { $PythonArguments += '--keep-out' }

    & $Python.Source @PythonArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$LocalApiDocsRoot = Join-Path $ProjectRoot 'local_api_docs'
$ParsedRoot = Join-Path $LocalApiDocsRoot 'parsed'
$SingleDocumentMode = $PSBoundParameters.ContainsKey('Source') -or $PSBoundParameters.ContainsKey('OutDir')

if ($SingleDocumentMode) {
    if (-not $PSBoundParameters.ContainsKey('Source')) {
        $Source = Join-Path $LocalApiDocsRoot 'ScriptHookVDotNet3.xml'
    }
    if (-not $PSBoundParameters.ContainsKey('OutDir')) {
        $OutDir = Join-Path $ParsedRoot (Get-DocumentId -Path $Source)
    }

    Invoke-ParseLocalXml -DocumentSource $Source -DocumentOutDir $OutDir
}
else {
    $Documents = @(
        [pscustomobject]@{
            Source = Join-Path $LocalApiDocsRoot 'ScriptHookVDotNet3.xml'
            OutDir = Join-Path $ParsedRoot 'scripthookvdotnet3'
        },
        [pscustomobject]@{
            Source = Join-Path $LocalApiDocsRoot 'LemonUI.SHVDN3.xml'
            OutDir = Join-Path $ParsedRoot 'lemonui-shvdn3'
        }
    )

    foreach ($Document in $Documents) {
        Invoke-ParseLocalXml -DocumentSource $Document.Source -DocumentOutDir $Document.OutDir
    }
}

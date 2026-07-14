<#
.SYNOPSIS
  Parse NativeDB Legacy natives.json into a validated lookup tree.

.DESCRIPTION
  The parser writes every native to a unique, readable JSON file and creates an
  index with lookups by hash and by "namespace:name".  The output is staged and
  validated before it replaces the active directory, so a failed parse does not
  leave a partial corpus behind.
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
if (-not $PSBoundParameters.ContainsKey('Source')) {
    $Source = Join-Path $ProjectRoot 'api_docs\gta5-nativedb-data\natives.json'
}
if (-not $PSBoundParameters.ContainsKey('OutDir')) {
    $OutDir = Join-Path (Split-Path -Parent $Source) 'natives_parsed'
}

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
    throw 'Python 3 is required to parse NativeDB. Install Python 3 and make python or py available on PATH.'
}

$PythonArguments = @()
if ($UsePyLauncher) { $PythonArguments += '-3' }
$PythonArguments += @($Parser, 'natives', '--source', $Source, '--out-dir', $OutDir)
if ($KeepOut) { $PythonArguments += '--keep-out' }

& $Python.Source @PythonArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

<#
.SYNOPSIS
  Parse ScriptHookVDotNet3.xml into a complete, validated member lookup tree.

.DESCRIPTION
  Each XML <member> is preserved in its own valid XML document. The index keeps
  the exact canonical member name, qualified name, signature, kind, and path.
  All output is generated in a staging directory and validated before replace.
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
    $Source = Join-Path $ProjectRoot 'local_api_docs\ScriptHookVDotNet3.xml'
}
if (-not $PSBoundParameters.ContainsKey('OutDir')) {
    $OutDir = Join-Path (Split-Path -Parent $Source) 'parsed'
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
    throw 'Python 3 is required to parse local API XML. Install Python 3 and make python or py available on PATH.'
}

$PythonArguments = @()
if ($UsePyLauncher) { $PythonArguments += '-3' }
$PythonArguments += @($Parser, 'local-xml', '--source', $Source, '--out-dir', $OutDir)
if ($KeepOut) { $PythonArguments += '--keep-out' }

& $Python.Source @PythonArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

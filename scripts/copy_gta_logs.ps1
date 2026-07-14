<#
.SYNOPSIS
  Copy ScriptHookVDotNet and ScriptHookV logs into this project's logs directory.

.DESCRIPTION
  Missing source files are reported without failing the copy. Existing target
  logs are preserved unless -Force is supplied.
#>
[CmdletBinding()]
param(
    [Alias('s')]
    [string]$SourceDir = 'C:\Games\Grand Theft Auto V',
    [Alias('o')]
    [string]$LogsDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not $PSBoundParameters.ContainsKey('LogsDir')) {
    $LogsDir = Join-Path $ProjectRoot 'logs'
}

if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
    throw ("GTA V directory does not exist: {0}" -f $SourceDir)
}
New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null

$copied = 0
$skipped = 0
foreach ($name in @('ScriptHookVDotNet.log', 'ScriptHookV.log')) {
    $sourcePath = Join-Path $SourceDir $name
    $targetPath = Join-Path $LogsDir $name

    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        Write-Host ("SKIP missing source: {0}" -f $sourcePath)
        $skipped++
        continue
    }
    if ((Test-Path -LiteralPath $targetPath -PathType Leaf) -and -not $Force) {
        Write-Host ("SKIP existing target: {0} (use -Force to overwrite)" -f $targetPath)
        $skipped++
        continue
    }

    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    Write-Host ("COPIED: {0} -> {1}" -f $sourcePath, $targetPath)
    $copied++
}

Write-Host ("Done. copied={0}, skipped={1}, target={2}" -f $copied, $skipped, $LogsDir)

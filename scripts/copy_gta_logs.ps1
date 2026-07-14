<#
.SYNOPSIS
  Copy 2 SHVDN / ScriptHookV log files from the GTA V install dir into <project root>\logs.

.DESCRIPTION
  Copies ScriptHookVDotNet.log and ScriptHookV.log from -SourceDir to -LogsDir.
  Missing source files are skipped with a notice (no error).
  Existing target files are skipped unless -Force is given.
  Target directory is created if missing.

.PARAMETER SourceDir
  GTA V install dir (default: 'C:\Games\Grand Theft Auto V').

.PARAMETER LogsDir
  Target logs dir (default: '<project root>\logs').

.PARAMETER Force
  Overwrite existing files in target.

.EXAMPLE
  pwsh -File scripts\copy_gta_logs.ps1

.EXAMPLE
  pwsh -File scripts\copy_gta_logs.ps1 -SourceDir 'D:\GTA V' -LogsDir 'C:\work\logs' -Force
#>
[CmdletBinding()]
param(
    [string]$SourceDir = 'C:\Games\Grand Theft Auto V',
    [string]$LogsDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Default $LogsDir: <project root>\logs (script lives in <project root>\scripts\)
if (-not $PSBoundParameters.ContainsKey('LogsDir')) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
    $LogsDir = Join-Path $ProjectRoot 'logs'
}

$LogFiles = @('ScriptHookVDotNet.log', 'ScriptHookV.log')

# Pre-flight: source dir phải tồn tại
if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
    throw "Không tìm thấy thư mục GTA V: '$SourceDir'. Hãy chạy với -SourceDir '<đường dẫn thật>'."
}

# Đảm bảo target dir tồn tại
if (-not (Test-Path -LiteralPath $LogsDir -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null
}

$copied = 0; $skipped = 0
foreach ($name in $LogFiles) {
    $src = Join-Path $SourceDir $name
    $dst = Join-Path $LogsDir $name
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        Write-Host "SKIP: '$src' không tồn tại"
        $skipped++
        continue
    }
    if ((Test-Path -LiteralPath $dst) -and -not $Force) {
        Write-Host "SKIP: '$dst' đã tồn tại (dùng -Force để ghi đè)"
        $skipped++
        continue
    }
    Copy-Item -LiteralPath $src -Destination $dst -Force:$Force
    Write-Host "COPIED: '$src' -> '$dst'"
    $copied++
}

Write-Host ""
Write-Host "Done. copied=$copied, skipped=$skipped, target='$LogsDir'"

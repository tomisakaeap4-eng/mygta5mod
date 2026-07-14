<#
.SYNOPSIS
  Clone or fast-forward the three API source repositories into <project root>\api_docs.
#>
[CmdletBinding()]
param(
    [Alias('d')]
    [string]$ApiDocsRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'api_docs')
)

$ErrorActionPreference = 'Stop'

function Require-Command([string]$Name) {
    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available on PATH: $Name"
    }
}

function Invoke-Git([string[]]$Arguments) {
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Sync-Repository([string]$Url, [string]$Destination, [string]$Branch) {
    $gitDirectory = Join-Path $Destination '.git'
    if (Test-Path -LiteralPath $gitDirectory -PathType Container) {
        Write-Host "Updating $Destination"
        Invoke-Git @('-C', $Destination, 'fetch', '--all', '--prune')
        Invoke-Git @('-C', $Destination, 'checkout', $Branch)
        Invoke-Git @('-C', $Destination, 'pull', '--ff-only', 'origin', $Branch)
        return
    }
    if (Test-Path -LiteralPath $Destination) {
        throw "Destination exists but is not a Git repository: $Destination"
    }
    Write-Host "Cloning $Url -> $Destination"
    Invoke-Git @('clone', '--depth', '1', '--branch', $Branch, $Url, $Destination)
}

Require-Command 'git'
$ApiDocsRoot = [System.IO.Path]::GetFullPath($ApiDocsRoot)
New-Item -ItemType Directory -Force -Path $ApiDocsRoot | Out-Null

Sync-Repository 'https://github.com/scripthookvdotnet/scripthookvdotnet.git' (Join-Path $ApiDocsRoot 'scripthookvdotnet') 'main'
Sync-Repository 'https://github.com/scripthookvdotnet/scripthookvdotnet.wiki.git' (Join-Path $ApiDocsRoot 'scripthookvdotnet.wiki') 'master'
Sync-Repository 'https://github.com/alloc8or/gta5-nativedb-data.git' (Join-Path $ApiDocsRoot 'gta5-nativedb-data') 'master'

Write-Host "Corpus is ready at: $ApiDocsRoot"

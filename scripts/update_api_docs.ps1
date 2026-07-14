<#
.SYNOPSIS
  Fast-forward the three API source repositories in <project root>\api_docs.
#>
[CmdletBinding()]
param(
    [Alias('d')]
    [string]$ApiDocsRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'api_docs')
)

$ErrorActionPreference = 'Stop'

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Required command is not available on PATH: git'
}

$ApiDocsRoot = [System.IO.Path]::GetFullPath($ApiDocsRoot)
$repositories = @(
    (Join-Path $ApiDocsRoot 'scripthookvdotnet')
    (Join-Path $ApiDocsRoot 'scripthookvdotnet.wiki')
    (Join-Path $ApiDocsRoot 'gta5-nativedb-data')
)

foreach ($repository in $repositories) {
    if (-not (Test-Path -LiteralPath (Join-Path $repository '.git') -PathType Container)) {
        throw "Missing corpus repository: $repository. Run scripts\\bootstrap_api_docs.ps1 first."
    }
    Write-Host "Updating $repository"
    & git -C $repository pull --ff-only
    if ($LASTEXITCODE -ne 0) {
        throw "git pull failed for $repository with exit code $LASTEXITCODE."
    }
}

Write-Host "Corpus is up to date at: $ApiDocsRoot"

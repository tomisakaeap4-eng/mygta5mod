<#
.SYNOPSIS
  Clone or fast-forward the API source repositories into <project root>\api_docs.
#>
[CmdletBinding()]
param(
    [Alias('d')]
    [string]$ApiDocsRoot
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

function Clone-Or-Update([string]$Url, [string]$Destination, [string]$Branch) {
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
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not $PSBoundParameters.ContainsKey('ApiDocsRoot')) {
    $ApiDocsRoot = Join-Path $ProjectRoot 'api_docs'
}
$ApiDocsRoot = [System.IO.Path]::GetFullPath($ApiDocsRoot)
New-Item -ItemType Directory -Force -Path $ApiDocsRoot | Out-Null

$Repositories = @(
    [pscustomobject]@{ Url = 'https://github.com/scripthookvdotnet/scripthookvdotnet.git'; Name = 'scripthookvdotnet'; Branch = 'main' }
    [pscustomobject]@{ Url = 'https://github.com/scripthookvdotnet/scripthookvdotnet.wiki.git'; Name = 'scripthookvdotnet.wiki'; Branch = 'master' }
    [pscustomobject]@{ Url = 'https://github.com/alloc8or/gta5-nativedb-data.git'; Name = 'gta5-nativedb-data'; Branch = 'master' }
    [pscustomobject]@{ Url = 'https://github.com/LemonUIbyLemon/LemonUI.git'; Name = 'lemonui'; Branch = 'master' }
    [pscustomobject]@{ Url = 'https://github.com/LemonUIbyLemon/Examples.git'; Name = 'lemonui-examples'; Branch = 'master' }
    [pscustomobject]@{ Url = 'https://github.com/LemonUIbyLemon/LemonUI.wiki.git'; Name = 'lemonui-wiki'; Branch = 'master' }
    [pscustomobject]@{ Url = 'https://github.com/openai/openai-dotnet.git'; Name = 'openai-dotnet'; Branch = 'main' }
)

foreach ($Repository in $Repositories) {
    Clone-Or-Update $Repository.Url (Join-Path $ApiDocsRoot $Repository.Name) $Repository.Branch
}

Write-Host "Corpus is ready at: $ApiDocsRoot"

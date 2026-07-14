param(
    [string]$ApiDocsRoot = (Join-Path $PSScriptRoot "api_docs")
)

$ErrorActionPreference = "Stop"

$repos = @(
    (Join-Path $ApiDocsRoot "scripthookvdotnet"),
    (Join-Path $ApiDocsRoot "scripthookvdotnet.wiki"),
    (Join-Path $ApiDocsRoot "gta5-nativedb-data")
)

foreach ($repo in $repos) {
    if (-not (Test-Path (Join-Path $repo ".git"))) {
        throw "Thiếu repository: $repo. Hãy chạy scripts/bootstrap_api_docs.ps1 trước."
    }
    Write-Host "Updating $repo"
    git -C $repo pull --ff-only
}

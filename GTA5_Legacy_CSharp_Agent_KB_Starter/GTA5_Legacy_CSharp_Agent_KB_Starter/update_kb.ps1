param(
    [string]$KbRoot = (Join-Path $PSScriptRoot "kb")
)

$ErrorActionPreference = "Stop"

$repos = @(
    (Join-Path $KbRoot "scripthookvdotnet"),
    (Join-Path $KbRoot "scripthookvdotnet.wiki"),
    (Join-Path $KbRoot "gta5-nativedb-data")
)

foreach ($repo in $repos) {
    if (-not (Test-Path (Join-Path $repo ".git"))) {
        throw "Thiếu repository: $repo. Hãy chạy bootstrap_kb.ps1 trước."
    }
    Write-Host "Updating $repo"
    git -C $repo pull --ff-only
}

python (Join-Path $PSScriptRoot "build_corpus.py")

param(
    [string]$ApiDocsRoot = (Join-Path $PSScriptRoot "api_docs")
)

$ErrorActionPreference = "Stop"

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Không tìm thấy '$Name'. Hãy cài Git/Python và mở lại PowerShell."
    }
}

function Clone-Or-Update([string]$Url, [string]$Destination, [string]$Branch) {
    if (Test-Path (Join-Path $Destination ".git")) {
        Write-Host "Updating $Destination"
        git -C $Destination fetch --all --prune
        git -C $Destination checkout $Branch
        git -C $Destination pull --ff-only origin $Branch
    }
    else {
        Write-Host "Cloning $Url"
        git clone --depth 1 --branch $Branch $Url $Destination
    }
}

Require-Command "git"
Require-Command "python"

New-Item -ItemType Directory -Force -Path $ApiDocsRoot | Out-Null

Clone-Or-Update `
  "https://github.com/scripthookvdotnet/scripthookvdotnet.git" `
  (Join-Path $ApiDocsRoot "scripthookvdotnet") `
  "main"

Clone-Or-Update `
  "https://github.com/scripthookvdotnet/scripthookvdotnet.wiki.git" `
  (Join-Path $ApiDocsRoot "scripthookvdotnet.wiki") `
  "master"

Clone-Or-Update `
  "https://github.com/alloc8or/gta5-nativedb-data.git" `
  (Join-Path $ApiDocsRoot "gta5-nativedb-data") `
  "master"

Write-Host ""
Write-Host "Đã tải nguồn."

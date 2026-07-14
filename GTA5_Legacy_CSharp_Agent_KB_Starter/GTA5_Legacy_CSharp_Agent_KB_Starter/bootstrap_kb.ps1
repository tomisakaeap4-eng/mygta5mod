param(
    [string]$KbRoot = (Join-Path $PSScriptRoot "kb")
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

New-Item -ItemType Directory -Force -Path $KbRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "inputs\local_api_docs") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "inputs\project") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "inputs\logs") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "output") | Out-Null

Clone-Or-Update `
  "https://github.com/scripthookvdotnet/scripthookvdotnet.git" `
  (Join-Path $KbRoot "scripthookvdotnet") `
  "main"

Clone-Or-Update `
  "https://github.com/scripthookvdotnet/scripthookvdotnet.wiki.git" `
  (Join-Path $KbRoot "scripthookvdotnet.wiki") `
  "master"

Clone-Or-Update `
  "https://github.com/alloc8or/gta5-nativedb-data.git" `
  (Join-Path $KbRoot "gta5-nativedb-data") `
  "master"

Write-Host ""
Write-Host "Đã tải nguồn."
Write-Host "Tiếp theo:"
Write-Host "1. Copy ScriptHookVDotNet3.xml đúng phiên bản vào inputs\local_api_docs"
Write-Host "2. Copy project của bạn vào inputs\project"
Write-Host "3. Copy ScriptHookVDotNet.log và ScriptHookV.log vào inputs\logs"
Write-Host "4. Chạy: python .\build_corpus.py"

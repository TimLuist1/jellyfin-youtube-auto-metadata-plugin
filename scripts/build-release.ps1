param(
    [string]$Version = "0.1.0.5",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "Jellyfin.Plugin.YoutubeMetadata\Jellyfin.Plugin.YoutubeMetadata.csproj"
$publishPath = Join-Path $repoRoot "artifacts\publish"
$stagePath = Join-Path $repoRoot "artifacts\stage\$Version"
$zipPath = Join-Path $repoRoot "artifacts\youtube-auto-metadata_$Version.zip"

Write-Host "Publishing project..."
dotnet publish $projectPath -c $Configuration -o $publishPath

if (Test-Path $stagePath) {
    Remove-Item -Recurse -Force $stagePath
}
New-Item -ItemType Directory -Path $stagePath | Out-Null

Write-Host "Collecting publish artifacts..."
$requiredFiles = @(
    "Jellyfin.Plugin.YoutubeMetadata.dll",
    "NYoutubeDLP.dll",
    "System.IO.Abstractions.dll"
)

foreach ($file in $requiredFiles) {
    $source = Join-Path $publishPath $file
    if (-not (Test-Path $source)) {
        throw "Required artifact not found: $source"
    }

    Copy-Item $source -Destination (Join-Path $stagePath $file) -Force
}

$metaJson = @{
    category = "Metadata"
    changelog = "Packaging fix for Jellyfin 10.11.6 compatibility."
    description = "Automatically fetches YouTube metadata and thumbnails by title, even when no YouTube ID exists in filenames."
    guid = "bb165877-2d2a-458d-aa02-52b9f632d974"
    name = "YouTube Auto Metadata"
    overview = "YouTube metadata provider with title-based matching and optional AI cleanup."
    owner = "TimLuist1"
    targetAbi = "10.11.6"
    timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    version = $Version
}

$metaJson | ConvertTo-Json | Set-Content -Path (Join-Path $stagePath "meta.json") -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Write-Host "Creating zip package..."
Compress-Archive -Path (Join-Path $stagePath "*") -DestinationPath $zipPath -Force

$md5 = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash.ToLowerInvariant()

Write-Host ""
Write-Host "Release artifact:"
Write-Host "  $zipPath"
Write-Host "MD5 checksum:"
Write-Host "  $md5"
Write-Host ""
Write-Host "manifest.json version block:"
Write-Host "{"
Write-Host "  `"version`": `"$Version`","
Write-Host "  `"targetAbi`": `"10.11.6`","
Write-Host "  `"sourceUrl`": `"https://github.com/TimLuist1/jellyfin-youtube-auto-metadata-plugin/releases/download/v$Version/youtube-auto-metadata_$Version.zip`","
Write-Host "  `"checksum`": `"$md5`","
Write-Host "  `"timestamp`": `"`$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')`""
Write-Host "}"

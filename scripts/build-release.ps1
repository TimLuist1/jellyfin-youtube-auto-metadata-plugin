param(
    [string]$Version = "0.1.0.3",
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
Get-ChildItem $publishPath -File | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $stagePath $_.Name) -Force
}

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

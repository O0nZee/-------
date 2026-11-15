param([string]$Channel = '8.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $projectRoot '.tools'
$sdkRoot = Join-Path $toolsRoot 'dotnet'
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
$metadata = Invoke-RestMethod -Uri "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$Channel/releases.json" -TimeoutSec 30
$sdkVersion = $metadata.'latest-sdk'
$sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdkVersion } | Select-Object -First 1
$archive = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
if (-not $archive) { throw 'The official release metadata did not contain the Windows SDK archive.' }
$zipPath = Join-Path $toolsRoot "dotnet-sdk-$sdkVersion.zip"
Write-Output "Downloading official .NET SDK $sdkVersion into the project tools folder."
Invoke-WebRequest -Uri $archive.url -OutFile $zipPath -TimeoutSec 600
$actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash
if ($actualHash -ne $archive.hash) { throw 'SDK archive verification failed. The archive was not extracted.' }
New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $sdkRoot -Force
Write-Output "Verified and installed SDK $sdkVersion."
& (Join-Path $sdkRoot 'dotnet.exe') --version

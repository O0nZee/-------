param([switch]$Publish)
. "$PSScriptRoot/Common.ps1"
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'obj/project.assets.json'))) {
    & "$PSScriptRoot/Restore.ps1"
}
if ($Publish) { & $dotnetPath publish GraduationPlatform.csproj --no-restore -c Release -o artifacts/app }
else { & $dotnetPath build GraduationPlatform.csproj --no-restore -c Release }
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

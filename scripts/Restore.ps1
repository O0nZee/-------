. "$PSScriptRoot/Common.ps1"
& $dotnetPath restore GraduationPlatform.csproj
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }

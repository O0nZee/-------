param([ValidateRange(1024,65535)][int]$Port=5000,[switch]$NoBuild,[string]$DataDirectory='data')
. "$PSScriptRoot/Common.ps1" -InstallSdk
$localUrl = "http://localhost:$Port"
$health = $null
try { $health = Invoke-RestMethod -Uri "$localUrl/api/health" -TimeoutSec 2 } catch { }
if ($health.isSuccess -and $health.data.database -eq 'SQLite' -and $health.data.version -eq '3.0.0') {
    Write-Output "The platform is already running: $localUrl"
    Write-Output 'Use another -Port to start a separate data directory.'
    return
}
if (-not $NoBuild) { & "$PSScriptRoot/Build.ps1" }
$applicationPath=Join-Path $projectRoot 'bin/Release/net8.0/GraduationPlatform.dll'
if (-not (Test-Path -LiteralPath $applicationPath)) { throw 'Build the application first.' }
Write-Output "Open $localUrl in your browser. Press Ctrl+C in this window to stop the platform."
& $dotnetPath $applicationPath --contentRoot $projectRoot --environment Development --urls $localUrl --Platform:PublicUrl $localUrl --Platform:DataDirectory $DataDirectory
if ($LASTEXITCODE -ne 0) { throw 'The application could not start. Check the log above or choose a different -Port.' }

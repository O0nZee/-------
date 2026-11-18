param(
    [ValidateRange(1024,65535)][int]$Port = 5000,
    [switch]$NoBuild,
    [ValidateNotNullOrEmpty()][string]$DataDirectory = 'data',
    [switch]$NoBrowser,
    [switch]$Foreground,
    [ValidateRange(5,120)][int]$StartupTimeoutSeconds = 60
)
. "$PSScriptRoot/Server.Common.ps1"
$localUrl = "http://localhost:$Port"
$resolvedDataDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($serverProjectRoot, $DataDirectory))
$launcherLock = Enter-PlatformLauncherLock
try {
    $state = Get-PlatformServerState -Port $Port
    $managedProcess = Get-PlatformServerProcess -State $state
    if (Get-PlatformServerHealth -Url $localUrl) {
        if ($managedProcess -and $state.dataDirectory -ne $resolvedDataDirectory) {
            throw "Another data directory is running on port $Port. Stop it first or choose another -Port."
        }
        Write-Output "The platform is already running: $localUrl"
        if (-not $NoBrowser) { Open-PlatformBrowser -Url $localUrl }
        return
    }
    if ($managedProcess) {
        Show-PlatformStartupLogs -OutputLog $state.outputLog -ErrorLog $state.errorLog
        throw "The existing server is not responding. Run scripts/Stop.ps1 -Port $Port, then start it again."
    }
    Remove-PlatformServerState -Port $Port
    if (-not (Test-PlatformPortAvailable -Port $Port)) {
        throw "Port $Port is used by another application. Close that application or use -Port with another number."
    }
    $databaseLockPath = Join-Path $resolvedDataDirectory '.server.lock'
    if (Test-Path -LiteralPath $databaseLockPath) {
        try { $databaseLock = [IO.File]::Open($databaseLockPath, 'Open', 'ReadWrite', 'None'); $databaseLock.Dispose() }
        catch [IO.IOException] { throw 'This data directory is already used by another platform server. Stop that server or choose another -DataDirectory.' }
    }
    . "$PSScriptRoot/Common.ps1" -InstallSdk
    if (-not $NoBuild) { & "$PSScriptRoot/Build.ps1" }
    $applicationPath = Join-Path $projectRoot 'bin/Release/net8.0/GraduationPlatform.dll'
    if (-not (Test-Path -LiteralPath $applicationPath)) { throw 'Build the application first, or start without -NoBuild.' }
    $applicationArguments = @($applicationPath, '--contentRoot', $projectRoot, '--environment', 'Development', '--urls', $localUrl,
        '--Platform:PublicUrl', $localUrl, '--Platform:DataDirectory', $resolvedDataDirectory)
    if ($Foreground) {
        $launcherLock.Dispose()
        $launcherLock = $null
        Write-Output "Open $localUrl after the server starts. Press Ctrl+C in this window to stop it."
        & $dotnetPath @applicationArguments
        if ($LASTEXITCODE -ne 0) { throw 'The application could not start. Check the log above.' }
        return
    }
    $logDirectory = Join-Path $resolvedDataDirectory 'logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $logName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $outputLog = Join-Path $logDirectory "$logName.stdout.log"
    $errorLog = Join-Path $logDirectory "$logName.stderr.log"
    $configurationPath = Join-Path $serverRunDirectory "launch-$Port.json"
    [ordered]@{
        port = $Port
        executablePath = $dotnetPath
        arguments = $applicationArguments
        dataDirectory = $resolvedDataDirectory
        url = $localUrl
        outputLog = $outputLog
        errorLog = $errorLog
    } | ConvertTo-Json | Set-Content -LiteralPath $configurationPath -Encoding UTF8
    $powershellPath = (Get-Command powershell.exe -CommandType Application).Source
    $runnerArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Run-Server.ps1'),
        '-ConfigurationPath', $configurationPath) | ForEach-Object { ConvertTo-PlatformProcessArgument $_ }
    # No stream redirection here: Windows launches a separate hidden console without inheriting the caller's pipes.
    # Run-Server.ps1 owns the application's redirected logs inside that independent console.
    $runnerProcess = Start-Process -FilePath $powershellPath -ArgumentList ($runnerArguments -join ' ') `
        -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    try {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($timer.Elapsed.TotalSeconds -lt $StartupTimeoutSeconds) {
            $runnerProcess.Refresh()
            if ($runnerProcess.HasExited) { throw "The platform launcher exited with code $($runnerProcess.ExitCode) before the server was ready." }
            if (Test-Path -LiteralPath $errorLog) {
                $fatalError = Get-Content -LiteralPath $errorLog -TotalCount 5 -Encoding UTF8 | Where-Object { $_ -match '^Unhandled exception\.' }
                if ($fatalError) { throw 'The application failed during startup. See the error log below.' }
            }
            $state = Get-PlatformServerState -Port $Port
            $serverProcess = Get-PlatformServerProcess -State $state
            if ($serverProcess -and (Get-PlatformServerHealth -Url $localUrl)) {
                Write-Output "The platform is ready: $localUrl"
                Write-Output "The server runs in the background. Use Stop.cmd or scripts/Stop.ps1 -Port $Port to stop it."
                Write-Output "Logs: $logDirectory"
                if (-not $NoBrowser) { Open-PlatformBrowser -Url $localUrl }
                return
            }
            Start-Sleep -Milliseconds 250
        }
        throw "The platform did not become ready within $StartupTimeoutSeconds seconds."
    } catch {
        if (-not $runnerProcess.HasExited) { Stop-Process -InputObject $runnerProcess; $runnerProcess.WaitForExit(10000) | Out-Null }
        $state = Get-PlatformServerState -Port $Port
        $serverProcess = Get-PlatformServerProcess -State $state
        if ($serverProcess) { Stop-Process -InputObject $serverProcess; $serverProcess.WaitForExit(10000) | Out-Null }
        Remove-PlatformServerState -Port $Port
        Show-PlatformStartupLogs -OutputLog $outputLog -ErrorLog $errorLog
        throw
    }
} finally {
    if ($launcherLock) { $launcherLock.Dispose() }
}

param([ValidateRange(1024,65535)][int]$Port = 5000)
. "$PSScriptRoot/Server.Common.ps1"
$launcherLock = Enter-PlatformLauncherLock
try {
    $state = Get-PlatformServerState -Port $Port
    $serverProcess = Get-PlatformServerProcess -State $state
    if ($serverProcess) {
        Stop-Process -InputObject $serverProcess
        if (-not $serverProcess.WaitForExit(10000)) { throw 'The server did not stop. Try again.' }
        $runnerProcess = Get-PlatformRunnerProcess -State $state
        if ($runnerProcess -and -not $runnerProcess.WaitForExit(5000)) {
            Stop-Process -InputObject $runnerProcess
        }
        Remove-PlatformServerState -Port $Port
        Write-Output "The platform has stopped on port $Port. Start.cmd will start it again."
    } else {
        Remove-PlatformServerState -Port $Port
        if (Get-PlatformServerHealth -Url "http://localhost:$Port") {
            throw 'This server was started outside Start.cmd. Stop it from its original window with Ctrl+C.'
        }
        Write-Output "No managed platform server is running on port $Port."
    }
} finally {
    $launcherLock.Dispose()
}

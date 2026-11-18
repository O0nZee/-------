param([Parameter(Mandatory=$true)][string]$ConfigurationPath)
$ErrorActionPreference = 'Stop'
$configuration = Get-Content -LiteralPath $ConfigurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
. "$PSScriptRoot/Server.Common.ps1"
$serverProcess = $null
try {
    $processArguments = ($configuration.arguments | ForEach-Object { ConvertTo-PlatformProcessArgument $_ }) -join ' '
    $serverProcess = Start-Process -FilePath $configuration.executablePath -ArgumentList $processArguments `
        -WorkingDirectory $serverProjectRoot -WindowStyle Hidden -RedirectStandardOutput $configuration.outputLog `
        -RedirectStandardError $configuration.errorLog -PassThru
    $runnerProcess = Get-Process -Id $PID
    $state = [ordered]@{
        projectRoot = $serverProjectRoot
        processId = $serverProcess.Id
        startedUtcTicks = $serverProcess.StartTime.ToUniversalTime().Ticks.ToString()
        executablePath = $configuration.executablePath
        runnerProcessId = $runnerProcess.Id
        runnerStartedUtcTicks = $runnerProcess.StartTime.ToUniversalTime().Ticks.ToString()
        runnerExecutablePath = $runnerProcess.Path
        applicationPath = $configuration.arguments[0]
        dataDirectory = $configuration.dataDirectory
        url = $configuration.url
        outputLog = $configuration.outputLog
        errorLog = $configuration.errorLog
    }
    $statePath = Join-Path $serverRunDirectory "server-$($configuration.port).json"
    $temporaryStatePath = $statePath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporaryStatePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryStatePath, $statePath)
    $serverProcess.WaitForExit()
    exit $serverProcess.ExitCode
} catch {
    if ($serverProcess -and -not $serverProcess.HasExited) { Stop-Process -InputObject $serverProcess }
    $_ | Out-String | Add-Content -LiteralPath $configuration.errorLog -Encoding UTF8
    exit 1
}

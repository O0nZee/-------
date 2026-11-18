$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) } catch { }
$serverProjectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$serverRunDirectory = Join-Path $serverProjectRoot '.tools/run'

function Enter-PlatformLauncherLock {
    New-Item -ItemType Directory -Path $serverRunDirectory -Force | Out-Null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 60) {
        try {
            return [IO.File]::Open((Join-Path $serverRunDirectory 'launcher.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 250
        }
    }
    throw 'Another start or stop operation is still running. Try again when it finishes.'
}

function Get-PlatformServerState {
    param([int]$Port)
    $statePath = Join-Path $serverRunDirectory "server-$Port.json"
    if (Test-Path -LiteralPath $statePath) {
        Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
}

function Get-PlatformServerProcess {
    param($State)
    if (-not $State -or $State.projectRoot -ne $serverProjectRoot) { return $null }
    $serverProcess = Get-Process -Id ([int]$State.processId) -ErrorAction SilentlyContinue
    if (-not $serverProcess) { return $null }
    try {
        if ($serverProcess.HasExited) { return $null }
        if ($serverProcess.Path -eq $State.executablePath -and
            $serverProcess.StartTime.ToUniversalTime().Ticks.ToString() -eq $State.startedUtcTicks) {
            return $serverProcess
        }
    } catch { }
    return $null
}

function Get-PlatformRunnerProcess {
    param($State)
    if (-not $State -or $State.projectRoot -ne $serverProjectRoot -or -not $State.runnerProcessId) { return $null }
    $runnerProcess = Get-Process -Id ([int]$State.runnerProcessId) -ErrorAction SilentlyContinue
    if (-not $runnerProcess) { return $null }
    try {
        if ($runnerProcess.HasExited) { return $null }
        if ($runnerProcess.Path -eq $State.runnerExecutablePath -and
            $runnerProcess.StartTime.ToUniversalTime().Ticks.ToString() -eq $State.runnerStartedUtcTicks) {
            return $runnerProcess
        }
    } catch { }
    return $null
}

function Get-PlatformServerHealth {
    param([string]$Url)
    $response = $null
    try {
        $request = [Net.HttpWebRequest]::Create("$Url/api/health")
        $request.Proxy = $null
        $request.Timeout = 1000
        $request.ReadWriteTimeout = 1000
        $response = $request.GetResponse()
        $reader = [IO.StreamReader]::new($response.GetResponseStream())
        try { $health = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        return ($health.isSuccess -and $health.data.status -eq 'healthy' -and $health.data.database -eq 'SQLite')
    } catch {
        return $false
    } finally {
        if ($response) { $response.Dispose() }
    }
}

function Test-PlatformPortAvailable {
    param([int]$Port)
    foreach ($address in @([Net.IPAddress]::Loopback, [Net.IPAddress]::IPv6Loopback)) {
        $listener = [Net.Sockets.TcpListener]::new($address, $Port)
        $listener.Server.ExclusiveAddressUse = $true
        try { $listener.Start() } catch [Net.Sockets.SocketException] {
            if ($_.Exception.SocketErrorCode -ne [Net.Sockets.SocketError]::AddressFamilyNotSupported) { return $false }
        } finally { $listener.Stop() }
    }
    return $true
}

function ConvertTo-PlatformProcessArgument {
    param([string]$Value)
    # Start-Process joins ArgumentList into a Windows command line.
    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    return '"' + ($escaped -replace '(\\+)$', '$1$1') + '"'
}

function Remove-PlatformServerState {
    param([int]$Port)
    $statePath = Join-Path $serverRunDirectory "server-$Port.json"
    if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath }
}

function Show-PlatformStartupLogs {
    param([string]$OutputLog, [string]$ErrorLog)
    foreach ($logPath in @($OutputLog, $ErrorLog)) {
        if (Test-Path -LiteralPath $logPath) {
            Write-Output "Log: $logPath"
            $firstLines = @(Get-Content -LiteralPath $logPath -TotalCount 20 -Encoding UTF8)
            if ($firstLines.Count -lt 20) { $firstLines }
            else {
                $firstLines | Select-Object -First 5
                Get-Content -LiteralPath $logPath -Tail 15 -Encoding UTF8
            }
        }
    }
}

function Open-PlatformBrowser {
    param([string]$Url)
    try { Start-Process -FilePath $Url }
    catch { Write-Warning "The server is ready. Open $Url manually; the default browser could not be launched." }
}

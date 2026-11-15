param([switch]$InstallSdk)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$dotnetPath = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $hasSdk = $installedDotnet -and (@(& $installedDotnet.Source --list-sdks) -match '^8\.0\.').Count -gt 0
    if ($hasSdk) {
        $dotnetPath = $installedDotnet.Source
    } elseif ($InstallSdk) {
        & "$PSScriptRoot/Install-DotNet.ps1"
    } else {
        throw 'A .NET 8 SDK is required. Run Start.cmd or scripts/Install-DotNet.ps1 first.'
    }
}
$env:GRADUATION_DOTNET = $dotnetPath
$pythonPath = $env:Similarity__PythonExecutable
$pythonArgs = @()
if ([string]::IsNullOrWhiteSpace($pythonPath)) {
    $pythonCandidates = @((Join-Path $projectRoot '.tools/python/python.exe'), (Join-Path $projectRoot '.tools/python/bin/python3'))
    foreach ($pythonCommand in @('python3', 'python', 'py')) {
        $pythonApplication = Get-Command $pythonCommand -CommandType Application -ErrorAction SilentlyContinue
        if ($pythonApplication -and $pythonApplication.Source -notlike '*WindowsApps*') { $pythonCandidates += $pythonApplication.Source }
    }
    $pythonProfile = [Environment]::GetFolderPath('UserProfile')
    if ([string]::IsNullOrWhiteSpace($pythonProfile)) { $pythonProfile = $env:USERPROFILE }
    if (-not [string]::IsNullOrWhiteSpace($pythonProfile)) {
        $pythonCandidates += Join-Path $pythonProfile '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    }
    $pythonNode = Get-Command node -CommandType Application -ErrorAction SilentlyContinue
    if ($pythonNode) {
        $pythonDependencies = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $pythonNode.Source))
        if (-not [string]::IsNullOrWhiteSpace($pythonDependencies)) {
            $pythonCandidates += Join-Path $pythonDependencies 'python/python.exe'
        }
    }
    $pythonPath = $pythonCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ($pythonPath) {
    $env:Similarity__PythonExecutable = $pythonPath
    if ([IO.Path]::GetFileNameWithoutExtension($pythonPath) -eq 'py') { $pythonArgs = @('-3') }
    $pythonDir = Split-Path -Parent $pythonPath
    if ($env:PATH -notlike "*$pythonDir*") { $env:PATH = "$pythonDir;$env:PATH" }
}
$nodePath = (Join-Path $projectRoot '.tools/node/bin/node.exe')
if (-not (Test-Path -LiteralPath $nodePath)) { $nodePath = (Join-Path $projectRoot '.tools/node/node.exe') }
if (Test-Path -LiteralPath $nodePath) {
    $nodeDir = Split-Path -Parent $nodePath
    if ($env:PATH -notlike "*$nodeDir*") { $env:PATH = "$nodeDir;$env:PATH" }
}
$dotnetDir = Split-Path -Parent $dotnetPath
if ($env:PATH -notlike "*$dotnetDir*") { $env:PATH = "$dotnetDir;$env:PATH" }
Set-Location -LiteralPath $projectRoot

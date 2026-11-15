param([int]$Port=5127,[string]$Name='api-main')
. "$PSScriptRoot/Common.ps1"
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Invalid test server name.' }
$runtimeDirectory=Join-Path $projectRoot 'tests/.runtime'
New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
$logPath=Join-Path $runtimeDirectory "$Name.stdout.log"
$errorPath=Join-Path $runtimeDirectory "$Name.stderr.log"
$applicationPath=Join-Path $projectRoot 'bin/Release/net8.0/GraduationPlatform.dll'
$arguments=@('"'+$applicationPath+'"','--environment','Development','--urls',"http://localhost:$Port",'--Platform:PublicUrl',"http://localhost:$Port",'--Platform:DataDirectory',"tests/.runtime/$Name")
$process=Start-Process -FilePath $dotnetPath -ArgumentList $arguments -WorkingDirectory $projectRoot -RedirectStandardOutput $logPath -RedirectStandardError $errorPath -WindowStyle Hidden -PassThru
Set-Content -LiteralPath (Join-Path $runtimeDirectory "$Name.pid") -Value $process.Id
Write-Output "Test server process $($process.Id) on port $Port"

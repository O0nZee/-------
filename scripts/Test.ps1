param([switch]$Browser,[switch]$SkipBuild)
. "$PSScriptRoot/Common.ps1"
if (-not $pythonPath) { throw 'Python 3.10 or newer is required for text-analysis tests. Set Similarity__PythonExecutable when needed.' }
& $pythonPath @pythonArgs -I -B -X utf8 -m unittest discover -s tests -p test_similarity.py
if ($LASTEXITCODE -ne 0) { throw 'Python text-analysis tests failed.' }
if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath 'tests/obj/project.assets.json')) {
        & $dotnetPath restore tests/BackendTests.csproj
        if ($LASTEXITCODE -ne 0) { throw 'Test dependency restore failed.' }
    }
    & $dotnetPath build tests/BackendTests.csproj --no-restore -c Release -p:BackendTestBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
}
$testApplicationPath = Join-Path $projectRoot 'tests/.runtime/build/GraduationPlatform/net8.0/GraduationPlatform.dll'
$testRunnerPath = Join-Path $projectRoot 'tests/.runtime/build/BackendTests/net8.0/BackendTests.dll'
if (-not (Test-Path -LiteralPath $testApplicationPath) -or -not (Test-Path -LiteralPath $testRunnerPath)) {
    throw 'Isolated test binaries are missing. Run scripts/Test.ps1 without -SkipBuild first.'
}
& $dotnetPath $testRunnerPath
if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js 22 or later is required for API and browser acceptance tests.' }
Get-ChildItem -LiteralPath 'Frontend/Scripts' -Filter '*.js' -File | ForEach-Object {
    & node --check $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax failed: $($_.Name)" }
}
$previousTestApplication = $env:GRADUATION_APP_DLL
& node tests/registration.test.mjs
if ($LASTEXITCODE -ne 0) { throw 'Registration regression tests failed.' }
try {
    $env:GRADUATION_APP_DLL = $testApplicationPath
    & node tests/hosting.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'HTTPS proxy hosting tests failed.' }
    & node tests/api.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'API acceptance tests failed.' }
    if ($Browser) {
        & node tests/browser.test.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Browser acceptance tests failed.' }
    }
} finally {
    $env:GRADUATION_APP_DLL = $previousTestApplication
}
Write-Output 'All requested checks passed. Acceptance tests use isolated data, not the local application database.'

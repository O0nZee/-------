param([switch]$Offline, [string]$PublicUrl)
if ($PublicUrl) {
    $publicUri = $null
    if (-not [Uri]::TryCreate($PublicUrl, [UriKind]::Absolute, [ref]$publicUri) -or
        $publicUri.Scheme -ne 'https' -or $publicUri.HostNameType -ne [UriHostNameType]::Dns -or
        $publicUri.IsLoopback -or -not $publicUri.IsDefaultPort -or $publicUri.AbsolutePath -ne '/' -or
        $publicUri.UserInfo -or $publicUri.Query -or $publicUri.Fragment) {
        throw 'PublicUrl must be an HTTPS website address, for example https://o0nzee.alwaysdata.net.'
    }
    $PublicUrl = $publicUri.GetLeftPart([UriPartial]::Authority)
}
. "$PSScriptRoot/Common.ps1"

$releaseName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$releaseDirectory = Join-Path $projectRoot "artifacts/alwaysdata/$releaseName"
$siteDirectory = Join-Path $releaseDirectory 'site'
$zipPath = Join-Path $releaseDirectory 'graduation-alwaysdata.zip'
New-Item -ItemType Directory -Path $siteDirectory -Force | Out-Null
$publishProperties = @('-p:UseAppHost=false', '-p:SelfContained=false', '-p:EnableRuntimePackDownload=false',
    '-p:EnableAppHostPackDownload=false', '-p:BaseIntermediateOutputPath=obj/alwaysdata/', '-p:MSBuildProjectExtensionsPath=obj/alwaysdata/')
$originalAppData = $env:APPDATA
try {
    $env:APPDATA = Join-Path $projectRoot '.tools/publish-appdata'
    New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
    $restoreArguments = @('restore', 'GraduationPlatform.csproj', '--configfile', (Join-Path $projectRoot 'NuGet.Config'), '-r', 'linux-x64') + $publishProperties
    if ($Offline) { $restoreArguments += @('--source', (Join-Path $projectRoot '.tools/packages'), '-p:NuGetAudit=false') }
    & $dotnetPath @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'Publish dependency restore failed.' }
    & $dotnetPath publish GraduationPlatform.csproj --no-restore -c Release -r linux-x64 --self-contained false @publishProperties -o $siteDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Alwaysdata publish failed.' }
} finally { $env:APPDATA = $originalAppData }

# Hosting settings are generated only for the release; local development settings stay in the project.
$configuration = Get-Content -LiteralPath (Join-Path $siteDirectory 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$configuration.PSObject.Properties.Remove('Urls')
$configuration.Platform.DemoData = $false
$configuration.Similarity.PythonExecutable = 'python3'
if ($PublicUrl) {
    $configuration.Platform.PublicUrl = $PublicUrl
    $configuration.AllowedHosts = $publicUri.DnsSafeHost
}
$configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $siteDirectory 'appsettings.json') -Encoding UTF8
$utf8 = [Text.UTF8Encoding]::new($false)
$launcherSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'start-alwaysdata.sh')).Replace("`r`n", "`n")
if ($PublicUrl) {
    $defaultPublicUrl = 'export Platform__PublicUrl="${Platform__PublicUrl:-' + $PublicUrl + '}"'
    $launcherSource = $launcherSource.Replace('# PUBLIC_URL_DEFAULT', $defaultPublicUrl)
    foreach ($page in @('index.html', 'Frontend/Pages/login.html')) {
        $pagePath = Join-Path $siteDirectory $page
        $pageSource = [IO.File]::ReadAllText($pagePath)
        $pageSource = $pageSource.Replace('http://localhost:5000', $PublicUrl)
        $pageSource = $pageSource.Replace('شغّل <bdi>Start.cmd</bdi> من المجلد الرئيسي، ثم افتح المنصة لتسجيل الدخول ومتابعة مشروعك.',
            'افتح موقع المنصة على الإنترنت لتسجيل الدخول ومتابعة مشروعك.')
        $pageSource = $pageSource.Replace('شغّل <bdi>Start.cmd</bdi> ثم افتح الموقع لتسجيل الدخول.',
            'افتح موقع المنصة على الإنترنت لتسجيل الدخول.')
        [IO.File]::WriteAllText($pagePath, $pageSource, $utf8)
    }
}
[IO.File]::WriteAllText((Join-Path $siteDirectory 'start-alwaysdata.sh'), $launcherSource, $utf8)
$guideSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'docs/Alwaysdata.md'))
if ($PublicUrl) {
    $guideSource = $guideSource.Replace('https://ACCOUNT.alwaysdata.net', $PublicUrl)
    if ($publicUri.DnsSafeHost -match '^([a-z0-9-]+)\.alwaysdata\.net$') {
        $guideSource = $guideSource.Replace('ACCOUNT', $Matches[1])
    }
}
[IO.File]::WriteAllText((Join-Path $releaseDirectory 'Alwaysdata.md'), $guideSource, $utf8)

# Store archive paths with forward slashes so unzip on Linux recreates the directories correctly.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $siteDirectory -File -Recurse | ForEach-Object {
        $entryName = $_.FullName.Substring($siteDirectory.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Output "Site files: $siteDirectory"
Write-Output "Upload archive: $zipPath"
Write-Output "Instructions: $(Join-Path $releaseDirectory 'Alwaysdata.md')"

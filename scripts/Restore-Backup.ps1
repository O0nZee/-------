param(
    [Parameter(Mandatory=$true)][string]$Archive,
    [string]$Destination='data-restored'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$rootPrefix = $projectRoot.TrimEnd([char[]]@('\','/')) + [System.IO.Path]::DirectorySeparatorChar
$archivePath = if ([System.IO.Path]::IsPathRooted($Archive)) { $Archive } else { Join-Path $projectRoot $Archive }
$archivePath = (Resolve-Path -LiteralPath $archivePath).Path
$destinationPath = if ([System.IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $projectRoot $Destination }
$destinationPath = [System.IO.Path]::GetFullPath($destinationPath)
if (-not $destinationPath.StartsWith($rootPrefix,[System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Restore into a new directory inside this project.'
}
if (Test-Path -LiteralPath $destinationPath) {
    throw 'The destination already exists. Choose a new directory; current data will not be overwritten.'
}
$destinationPrefix = $destinationPath.TrimEnd([char[]]@('\','/')) + [System.IO.Path]::DirectorySeparatorChar
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($zip.Entries)
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        if ($entry.FullName -cnotmatch '^(graduation\.db|uploads/[a-f0-9]{32}\.bin|keys/key-[a-f0-9-]{36}\.xml)$' -or -not $names.Add($entry.FullName)) {
            throw 'Invalid backup: unexpected, duplicate, or unsafe entry.'
        }
        $target = [System.IO.Path]::GetFullPath((Join-Path $destinationPath $entry.FullName))
        if (-not $target.StartsWith($destinationPrefix,[System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Invalid backup: a file would escape the destination.'
        }
    }
    $databaseEntry = $entries | Where-Object { $_.FullName -ceq 'graduation.db' }
    if (-not $databaseEntry) { throw 'Invalid backup: graduation.db is missing.' }
    $databaseStream = $databaseEntry.Open()
    try {
        $header = New-Object byte[] 16
        $headerCount = $databaseStream.Read($header,0,16)
        if ($headerCount -ne 16 -or [System.Text.Encoding]::ASCII.GetString($header,0,15) -ne 'SQLite format 3' -or $header[15] -ne 0) {
            throw 'Invalid backup: the database is not SQLite.'
        }
    } finally { $databaseStream.Dispose() }
    New-Item -ItemType Directory -Path $destinationPath | Out-Null
    foreach ($entry in $entries) {
        $target = [System.IO.Path]::GetFullPath((Join-Path $destinationPath $entry.FullName))
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $source = $entry.Open()
        try {
            $output = [System.IO.File]::Open($target,[System.IO.FileMode]::CreateNew,[System.IO.FileAccess]::Write,[System.IO.FileShare]::None)
            try { $source.CopyTo($output) } finally { $output.Dispose() }
        } finally { $source.Dispose() }
    }
} finally { $zip.Dispose() }
Write-Output "Restored database, attachments, and authentication keys into: $destinationPath"
Write-Output 'Stop the current server, then start this copy with scripts/Start.ps1 -DataDirectory <restored-directory>.'

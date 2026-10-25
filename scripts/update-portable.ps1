param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$destination = Join-Path $repo 'app\win-x64'
$configPath = Join-Path $package 'DirectPDF.runtimeconfig.json'
foreach ($name in @('DirectPDF.exe', 'DirectPDF.dll', 'pdfium.dll', 'coreclr.dll', 'samples\Welcome.pdf', 'licenses\pdfium\licenses\pdfium.txt', 'licenses\dotnet\Microsoft.NETCore.App\LICENSE.TXT')) {
    if (!(Test-Path -LiteralPath (Join-Path $package $name))) { throw "Incomplete standalone distribution: missing $name" }
}
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (!$config.runtimeOptions.includedFrameworks) { throw 'Use a self-contained distribution with its .NET runtime included.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'DirectPDF.dll')).ProductVersion.Split('+')[0]
$project = [xml](Get-Content -LiteralPath (Join-Path $repo 'src\DirectPDF.App\DirectPDF.App.csproj') -Raw)
$expectedVersion = [string]$project.Project.PropertyGroup.Version
if ($version -ne $expectedVersion) { throw "Bundle version $version does not match source version $expectedVersion" }
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File)
if ($files | Where-Object { $_.Length -ge 100MB }) { throw 'A bundle file exceeds the regular GitHub Git file-size limit.' }
if ($destination.Equals($package, [StringComparison]::OrdinalIgnoreCase) -or $package.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The source distribution must be outside the destination bundle folder.' }
if (Test-Path -LiteralPath $destination) {
    $resolvedDestination = (Resolve-Path -LiteralPath $destination).Path
    if (!$resolvedDestination.Equals($destination, [StringComparison]::OrdinalIgnoreCase) -or !$resolvedDestination.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Bundle destination escaped the repository.' }
    $backup = Join-Path $repo ('artifacts\bundle-backups\' + [Guid]::NewGuid().ToString('N'))
    if (!$backup.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup destination escaped the repository.' }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($backup)) -Force | Out-Null
    Move-Item -LiteralPath $resolvedDestination -Destination $backup
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$entries = @()
foreach ($file in $files) {
    $relative = $file.FullName.Substring($package.Length).TrimStart('\')
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $entries += [ordered]@{ path = $relative.Replace('\', '/'); bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest = [ordered]@{ version = $version; platform = 'win-x64'; selfContained = $true; includedFrameworks = @($config.runtimeOptions.includedFrameworks); files = @($entries | Sort-Object { $_.path }) }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repo 'app\manifest.json') -Encoding UTF8
Write-Output "Bundled DirectPDF ${version}: $($files.Count) files with runtime and licenses."

param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$bundle = Join-Path $repo 'app\win-x64'
$manifest = Get-Content -LiteralPath (Join-Path $repo 'app\manifest.json') -Raw | ConvertFrom-Json
if (!$manifest.selfContained -or $manifest.platform -ne 'win-x64') { throw 'The bundle must include its Windows x64 runtime.' }
$project = [xml](Get-Content -LiteralPath (Join-Path $repo 'src\DirectPDF.App\DirectPDF.App.csproj') -Raw)
if ($manifest.version -ne [string]$project.Project.PropertyGroup.Version) { throw 'Source and bundled app versions differ.' }
$expectedFiles = @($manifest.files)
if (@(Get-ChildItem -LiteralPath $bundle -Recurse -File).Count -ne $expectedFiles.Count) { throw 'Unexpected or missing bundle files.' }
foreach ($file in $expectedFiles) {
    $path = [IO.Path]::GetFullPath((Join-Path $bundle $file.path))
    if (!$path.StartsWith($bundle + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A manifest path escaped the bundle.' }
    if (!(Test-Path -LiteralPath $path)) { throw "Missing bundled file: $($file.path)" }
    if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Bundle file mismatch: $($file.path)" }
}
$config = Get-Content -LiteralPath (Join-Path $bundle 'DirectPDF.runtimeconfig.json') -Raw | ConvertFrom-Json
if (@($config.runtimeOptions.includedFrameworks).Count -lt 2 -or $config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) { throw 'The application requires an external runtime.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $bundle 'DirectPDF.dll')).ProductVersion.Split('+')[0] -ne $manifest.version) { throw 'The application binary has an unexpected version.' }
Write-Output "PASS bundle version and $($expectedFiles.Count) file hashes; no external .NET runtime required."
if ($VerifyOnly) { return }

# Run the executable directly, with no SDK command, restore, or dependency fetch.
$process = Start-Process -FilePath (Join-Path $bundle 'DirectPDF.exe') -ArgumentList ('"' + (Join-Path $bundle 'samples\Welcome.pdf') + '"') -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) { throw "Bundled app exited prematurely: $($process.ExitCode)" }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Bundled app did not load the sample PDF.' }
    } while ($process.MainWindowTitle -notlike '*Welcome.pdf*')
    if (!$process.CloseMainWindow() -or !$process.WaitForExit(10000)) { throw 'Bundled app did not close cleanly.' }
    if ($process.ExitCode -ne 0) { throw "Bundled app failed: $($process.ExitCode)" }
    Write-Output 'PASS bundled executable opens a PDF and closes without an SDK or dependency installation.'
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}

param([string]$Archive = 'artifacts\DirectPDF-win-x64-self-contained.zip')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$archivePath = if ([IO.Path]::IsPathRooted($Archive)) { $Archive } else { Join-Path $repo $Archive }
$output = Join-Path $repo ('artifacts\package-tests\' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archivePath -DestinationPath $output
$appPath = Join-Path $output 'DirectPDF.exe'
$sample = Join-Path $output 'samples\Welcome.pdf'
$process = Start-Process -FilePath $appPath -ArgumentList ('"' + $sample + '"') -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) { throw "Packaged app exited prematurely: $($process.ExitCode)" }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Packaged app did not load the sample document.' }
    } while ($process.MainWindowTitle -notlike '*Welcome.pdf*')
    if (!$process.CloseMainWindow()) { throw 'Could not close the packaged application window.' }
    if (!$process.WaitForExit(10000)) { throw 'Packaged app did not close cleanly.' }
    if ($process.ExitCode -ne 0) { throw "Packaged app failed with exit code $($process.ExitCode)" }
    Write-Output "PASS extracted standalone app opens a PDF and closes cleanly: $output"
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}

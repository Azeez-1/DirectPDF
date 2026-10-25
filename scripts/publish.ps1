param([switch]$SelfContained, [string]$OutputDirectory)
. "$PSScriptRoot\environment.ps1"
Push-Location $RepoRoot
try {
    $mode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
    $output = if ($OutputDirectory) { [IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputDirectory)) } else { Join-Path $RepoRoot "artifacts\DirectPDF-win-x64-$mode" }
    $contained = if ($SelfContained) { 'true' } else { 'false' }
    Invoke-Dotnet publish src/DirectPDF.App -c Release -r win-x64 --self-contained $contained -o $output -p:PublishSingleFile=false -p:PublishTrimmed=false -p:RestoreLockedMode=true
    if ($SelfContained) {
        $runtimeConfig = Get-Content -LiteralPath (Join-Path $output 'DirectPDF.runtimeconfig.json') -Raw | ConvertFrom-Json
        foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
            $package = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
            $packagePath = Join-Path $env:NUGET_PACKAGES "$package\$($framework.version)"
            $licenseOutput = Join-Path $output "licenses\dotnet\$($framework.name)"
            New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
            $notices = Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }
            if (!$notices) { throw "Missing runtime license notices for $package" }
            foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $licenseOutput }
        }
    }
    foreach ($required in @('DirectPDF.exe', 'pdfium.dll', 'samples\Welcome.pdf', 'licenses\pdfium\licenses\pdfium.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $required))) { throw "Missing distribution file: $required" }
    }
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'README.md') -Destination $output
    Compress-Archive -Path "$output\*" -DestinationPath "$output.zip" -Force
    Write-Output "Packaged: $output.zip"
} finally { Pop-Location }

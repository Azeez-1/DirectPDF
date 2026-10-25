$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $script:RepoRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $script:RepoRoot '.nuget\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$localSdk = Join-Path $script:RepoRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localSdk) { $script:Dotnet = $localSdk }
else { $script:Dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
function Invoke-Dotnet {
    & $script:Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet exited with code $LASTEXITCODE" }
}

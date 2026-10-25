. "$PSScriptRoot\environment.ps1"
Push-Location $RepoRoot
try { Invoke-Dotnet run --project tests/DirectPDF.UiTests -c Release --no-build }
finally { Pop-Location }

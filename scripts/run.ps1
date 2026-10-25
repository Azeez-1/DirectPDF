. "$PSScriptRoot\environment.ps1"
Push-Location $RepoRoot
try { Invoke-Dotnet run --project src/DirectPDF.App -c Release --no-build }
finally { Pop-Location }

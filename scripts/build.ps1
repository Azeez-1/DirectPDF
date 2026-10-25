param([switch]$NoRestore)
. "$PSScriptRoot\environment.ps1"
Push-Location $RepoRoot
try {
    if (!$NoRestore) { Invoke-Dotnet restore DirectPDF.slnx --locked-mode }
    Invoke-Dotnet build DirectPDF.slnx -c Release --no-restore
} finally { Pop-Location }

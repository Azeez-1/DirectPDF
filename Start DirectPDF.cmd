@echo off
setlocal
if not exist "%~dp0app\win-x64\DirectPDF.exe" (
    echo The bundled DirectPDF app is missing. Please obtain a complete copy of this repository.
    exit /b 1
)
start "" /D "%~dp0app\win-x64" "%~dp0app\win-x64\DirectPDF.exe"

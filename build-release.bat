@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "SOLUTION=%ROOT_DIR%SunnyNet.sln"
set "OUT_DIR=%ROOT_DIR%artifacts\Release"

echo.
echo [Release] Cleaning output directory...
if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
mkdir "%OUT_DIR%"

echo.
echo [Release] Building...
dotnet build "%SOLUTION%" -c Release "-p:OutDir=%OUT_DIR%" -p:DebugType=none -p:DebugSymbols=false
if errorlevel 1 (
    echo.
    echo [Release] Build failed.
    exit /b 1
)

echo.
echo [Release] Removing PDB files...
del /s /q "%OUT_DIR%\*.pdb" >nul 2>nul

dir /s /b "%OUT_DIR%\*.pdb" >nul 2>nul
if not errorlevel 1 (
    echo.
    echo [Release] Build failed: PDB files still exist.
    exit /b 1
)

echo.
echo [Release] Build complete: %OUT_DIR%\
exit /b 0

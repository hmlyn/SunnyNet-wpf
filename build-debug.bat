@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "SOLUTION=%ROOT_DIR%SunnyNet.sln"
set "OUT_DIR=%ROOT_DIR%artifacts\Debug"

echo.
echo [Debug] Cleaning output directory...
if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"
mkdir "%OUT_DIR%"

echo.
echo [Debug] Building...
dotnet build "%SOLUTION%" -c Debug "-p:OutDir=%OUT_DIR%"
if errorlevel 1 (
    echo.
    echo [Debug] Build failed.
    exit /b 1
)

echo.
echo [Debug] Build complete: %OUT_DIR%\
exit /b 0

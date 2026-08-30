@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "SOLUTION=%ROOT_DIR%SunnyNet.sln"
set "PROJECT=%ROOT_DIR%src\SunnyNet.Wpf\SunnyNet.Wpf.csproj"
set "ARTIFACTS_DIR=%ROOT_DIR%artifacts"
set "FRAMEWORK_DIR=%ARTIFACTS_DIR%\Release"
set "SELF_CONTAINED_DIR=%ARTIFACTS_DIR%\Release-self-contained"
set "SELF_CONTAINED_BACKEND_SOURCE=%ROOT_DIR%src\SunnyNet.Wpf\bin\Release\net8.0-windows\win-x64\backend"
set "MCP_EXE=%ROOT_DIR%sunnymcptool\build\bin\sunnynet-mcp.exe"

for /f "usebackq delims=" %%V in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$xml=[xml](Get-Content -Raw '%PROJECT%'); foreach($group in $xml.Project.PropertyGroup){ if($group.Version){ $group.Version; break } }"`) do set "VERSION=%%V"
if "%VERSION%"=="" (
    echo [Release] Build failed: unable to read project version.
    exit /b 1
)

set "FRAMEWORK_ZIP=%ARTIFACTS_DIR%\SunnyNet-wpf-v%VERSION%-win-x64.zip"
set "SELF_CONTAINED_ZIP=%ARTIFACTS_DIR%\SunnyNet-wpf-v%VERSION%-win-x64-self-contained.zip"

echo.
echo [Release] Version: %VERSION%

echo.
echo [Release] Cleaning output directories...
call :CleanDir "%FRAMEWORK_DIR%" || exit /b 1
call :CleanDir "%SELF_CONTAINED_DIR%" || exit /b 1

echo.
echo [Release] Building framework-dependent package...
dotnet build "%SOLUTION%" -c Release "-p:OutDir=%FRAMEWORK_DIR%" -p:DebugType=none -p:DebugSymbols=false
if errorlevel 1 (
    echo.
    echo [Release] Framework-dependent build failed.
    exit /b 1
)

call :RemovePdb "%FRAMEWORK_DIR%" || exit /b 1
call :PackageMcp "%FRAMEWORK_DIR%" || exit /b 1
call :ZipDir "%FRAMEWORK_DIR%" "%FRAMEWORK_ZIP%" || exit /b 1

echo.
echo [Release] Building self-contained package...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -o "%SELF_CONTAINED_DIR%" -p:DebugType=none -p:DebugSymbols=false -p:PublishSingleFile=false
if errorlevel 1 (
    echo.
    echo [Release] Self-contained publish failed.
    exit /b 1
)

call :CopyBackend "%SELF_CONTAINED_BACKEND_SOURCE%" "%SELF_CONTAINED_DIR%\backend" || exit /b 1
call :RemovePdb "%SELF_CONTAINED_DIR%" || exit /b 1
call :PackageMcp "%SELF_CONTAINED_DIR%" || exit /b 1
call :ZipDir "%SELF_CONTAINED_DIR%" "%SELF_CONTAINED_ZIP%" || exit /b 1

echo.
echo [Release] Build complete:
echo [Release]   %FRAMEWORK_ZIP%
echo [Release]   %SELF_CONTAINED_ZIP%
exit /b 0

:CleanDir
if exist "%~1" rmdir /s /q "%~1"
if exist "%~1" (
    echo [Release] Build failed: unable to clean "%~1".
    echo [Release] Close any running SunnyNet.exe from this directory and retry.
    exit /b 1
)
mkdir "%~1"
if errorlevel 1 (
    echo [Release] Build failed: unable to create "%~1".
    exit /b 1
)
exit /b 0

:RemovePdb
echo.
echo [Release] Removing PDB files from "%~1"...
del /s /q "%~1\*.pdb" >nul 2>nul
dir /s /b "%~1\*.pdb" >nul 2>nul
if not errorlevel 1 (
    echo [Release] Build failed: PDB files still exist in "%~1".
    exit /b 1
)
exit /b 0

:PackageMcp
echo.
echo [Release] Packaging MCP bridge into "%~1"...
if exist "%MCP_EXE%" (
    if not exist "%~1\mcp" mkdir "%~1\mcp"
    copy /y "%MCP_EXE%" "%~1\mcp\sunnynet-mcp.exe" >nul
    if errorlevel 1 (
        echo [Release] Build failed: unable to copy sunnynet-mcp.exe.
        exit /b 1
    )
    echo [Release] Included: "%~1\mcp\sunnynet-mcp.exe"
) else (
    echo [Release] Warning: %MCP_EXE% not found, skipped MCP bridge packaging.
)
exit /b 0

:CopyBackend
echo.
echo [Release] Packaging Go backend into "%~2"...
if not exist "%~1\SunnyNetBridge.dll" (
    echo [Release] Build failed: Go backend output not found at "%~1".
    exit /b 1
)
if not exist "%~2" mkdir "%~2"
xcopy /e /i /y "%~1\*" "%~2\" >nul
if errorlevel 1 (
    echo [Release] Build failed: unable to copy Go backend.
    exit /b 1
)
exit /b 0

:ZipDir
echo.
echo [Release] Creating zip "%~2"...
if exist "%~2" del /f /q "%~2"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%~1\*' -DestinationPath '%~2' -CompressionLevel Optimal -Force"
if errorlevel 1 (
    echo [Release] Build failed: unable to create zip "%~2".
    exit /b 1
)
exit /b 0

@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "SOLUTION=%ROOT_DIR%SunnyNet.sln"
set "OUT_DIR=%ROOT_DIR%artifacts\Release"
set "MCP_EXE=%ROOT_DIR%sunnymcptool\build\bin\sunnynet-mcp.exe"
set "MCP_OUT_DIR=%OUT_DIR%\mcp"

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
echo [Release] Packaging MCP bridge...
if exist "%MCP_EXE%" (
    if not exist "%MCP_OUT_DIR%" mkdir "%MCP_OUT_DIR%"
    copy /y "%MCP_EXE%" "%MCP_OUT_DIR%\sunnynet-mcp.exe" >nul
    if errorlevel 1 (
        echo.
        echo [Release] Build failed: unable to copy sunnynet-mcp.exe.
        exit /b 1
    )
    echo [Release] Included: %MCP_OUT_DIR%\sunnynet-mcp.exe
) else (
    echo [Release] Warning: %MCP_EXE% not found, skipped MCP bridge packaging.
)

echo.
echo [Release] Build complete: %OUT_DIR%\
exit /b 0

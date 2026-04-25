@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "SOLUTION=%ROOT_DIR%SunnyNet.sln"
set "OUT_DIR=%ROOT_DIR%artifacts\Debug"
set "MCP_EXE=%ROOT_DIR%sunnymcptool\build\bin\sunnynet-mcp.exe"
set "MCP_OUT_DIR=%OUT_DIR%\mcp"

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
echo [Debug] Packaging MCP bridge...
if exist "%MCP_EXE%" (
    if not exist "%MCP_OUT_DIR%" mkdir "%MCP_OUT_DIR%"
    copy /y "%MCP_EXE%" "%MCP_OUT_DIR%\sunnynet-mcp.exe" >nul
    if errorlevel 1 (
        echo.
        echo [Debug] Build failed: unable to copy sunnynet-mcp.exe.
        exit /b 1
    )
    echo [Debug] Included: %MCP_OUT_DIR%\sunnynet-mcp.exe
) else (
    echo [Debug] Warning: %MCP_EXE% not found, skipped MCP bridge packaging.
)

echo.
echo [Debug] Build complete: %OUT_DIR%\
exit /b 0

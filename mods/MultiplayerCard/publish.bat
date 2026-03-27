@echo off
setlocal
title MultiplayerCard Publish

set "SCRIPT_DIR=%~dp0"
set "PROJECT_FILE=%SCRIPT_DIR%MultiplayerCard.csproj"
set "OUTPUT_DIR=D:\steam\steamapps\common\Slay the Spire 2\mods\MultiplayerCard"

echo.
echo ============================================
echo   MultiplayerCard Publish
echo ============================================
echo.

if not exist "%PROJECT_FILE%" (
    echo [ERROR] Project file not found:
    echo         %PROJECT_FILE%
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet was not found in PATH.
    exit /b 1
)

echo [INFO] Running dotnet publish...
dotnet publish "%PROJECT_FILE%"
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed.
    echo [TIP] If the DLL is locked, close Slay the Spire 2 and run again.
    exit /b 1
)

echo.
echo [OK] Publish complete.
echo [PATH] %OUTPUT_DIR%
echo.
exit /b 0

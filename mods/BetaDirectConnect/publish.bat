@echo off
setlocal EnableExtensions EnableDelayedExpansion
title BetaDirectConnect Publish

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR:~0,-1%"
set "PROJECT_FILE=%SCRIPT_DIR%BetaDirectConnect.csproj"
set "OUTPUT_DIR=D:\steam\steamapps\common\Slay the Spire 2\mods\BetaDirectConnect"
set "CONFIG_FILE=D:\githubprojects\AgentTheSpire\config.json"
set "GODOT_EXE="

echo.
echo ============================================
echo   BetaDirectConnect Publish
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

if exist "%CONFIG_FILE%" (
    for /f "tokens=2,* delims=:" %%I in ('findstr /c:"\"godot_exe_path\"" "%CONFIG_FILE%"') do (
        set "GODOT_EXE=%%J"
    )
    if defined GODOT_EXE (
        for /f "tokens=* delims= " %%I in ("!GODOT_EXE!") do set "GODOT_EXE=%%~I"
        set "GODOT_EXE=!GODOT_EXE:,=!"
        set "GODOT_EXE=!GODOT_EXE:"=!"
    )
)

if defined GODOT_EXE (
    if exist "%GODOT_EXE%" (
        echo [INFO] Reimporting Godot assets...
        "%GODOT_EXE%" --headless --path "%PROJECT_DIR%" --import
        if errorlevel 1 (
            echo.
            echo [ERROR] Godot asset import failed.
            exit /b 1
        )
    ) else (
        echo [WARN] Godot executable not found:
        echo        %GODOT_EXE%
        echo [WARN] Skipping asset reimport.
    )
) else (
    echo [WARN] godot_exe_path is not configured in config.json.
    echo [WARN] Skipping asset reimport.
)

echo [INFO] Running dotnet publish...
dotnet publish "%PROJECT_FILE%"
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed.
    exit /b 1
)

echo.
echo [OK] Publish complete.
echo [PATH] %OUTPUT_DIR%
echo.
exit /b 0

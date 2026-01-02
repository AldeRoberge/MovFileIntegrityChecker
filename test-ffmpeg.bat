@echo off
REM Test script for FFmpeg auto-installation feature
REM This script demonstrates the FFmpeg check functionality

echo ========================================
echo FFmpeg Auto-Installation Test
echo ========================================
echo.

REM Show current FFmpeg status
echo Checking if FFmpeg is currently available...
where ffmpeg >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] FFmpeg found in PATH
    ffmpeg -version | findstr /C:"version"
) else (
    echo [INFO] FFmpeg not found in PATH
    echo The application will offer to download it on first run.
)
echo.

echo ========================================
echo Running MovFileIntegrityChecker...
echo ========================================
echo.

cd /d "%~dp0"
dotnet run --project MovFileIntegrityChecker


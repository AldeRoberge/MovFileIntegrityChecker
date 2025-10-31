@echo off
REM Test Dashboard Generation Script
REM This script generates a sample dashboard using the test JSON reports

echo ==========================================
echo  Dashboard Generation Test
echo ==========================================
echo.

cd /d "%~dp0"

echo Copying test reports to temporary location...
if not exist "TestReports\temp" mkdir "TestReports\temp"
copy /Y "TestReports\*.json" "TestReports\temp\" > nul

echo.
echo Running dashboard generation...
echo.
echo When prompted, enter this path:
echo %CD%\TestReports
echo.

dotnet run -- --global-analysis

echo.
echo ==========================================
echo Test complete!
echo Check TestReports folder for the generated HTML file.
echo ==========================================
pause


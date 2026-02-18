# FFmpeg Auto-Installer Script
Write-Host "=== FFmpeg Auto-Installer ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script will automatically download and install FFmpeg for MovFileIntegrityChecker." -ForegroundColor Yellow
Write-Host ""
# Run the CLI app and automatically answer 'y' to the ffmpeg installation prompt
Write-Host "Starting installation..." -ForegroundColor Green
echo "y" | .\MovFileIntegrityChecker.CLI\bin\Debug\net9.0\MovFileIntegrityChecker.CLI.exe
Write-Host ""
Write-Host "Installation process completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Verifying FFmpeg installation..." -ForegroundColor Cyan
# Check if ffmpeg was installed in the local folder
$localFfmpegPath = "$env:LOCALAPPDATA\MovFileIntegrityChecker\ffmpeg\bin\ffmpeg.exe"
if (Test-Path $localFfmpegPath) {
    Write-Host "FFmpeg successfully installed at: $localFfmpegPath" -ForegroundColor Green
    # Test if it works
    & $localFfmpegPath -version 2>&1 | Select-Object -First 1
} else {
    Write-Host "FFmpeg installation could not be verified." -ForegroundColor Red
    Write-Host "You can install it manually from: https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -ForegroundColor Yellow
}

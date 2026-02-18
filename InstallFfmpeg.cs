// Quick installer for FFmpeg
using MovFileIntegrityChecker.Core.Utilities;

Console.WriteLine("=== FFmpeg Installer ===\n");

if (await FfmpegHelper.EnsureFfmpegInstalledAsync())
{
    Console.WriteLine("\nFFmpeg is ready to use!");
    Console.WriteLine($"You can now run the main application.");
}
else
{
    Console.WriteLine("\nFFmpeg installation failed or was cancelled.");
    Console.WriteLine("Please install FFmpeg manually from: https://ffmpeg.org/download.html");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();


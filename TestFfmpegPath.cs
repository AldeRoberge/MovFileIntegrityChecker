// Test program to verify FFmpeg path resolution
using MovFileIntegrityChecker.Core.Utilities;

Console.WriteLine("=== FFmpeg Path Resolution Test ===");
Console.WriteLine();

Console.WriteLine($"FfmpegHelper.IsFfmpegAvailable(): {FfmpegHelper.IsFfmpegAvailable()}");
Console.WriteLine($"FfmpegHelper.GetFfmpegPath(): {FfmpegHelper.GetFfmpegPath()}");
Console.WriteLine($"FfmpegHelper.GetFfprobePath(): {FfmpegHelper.GetFfprobePath()}");
Console.WriteLine();

// Test if the paths actually exist
string ffmpegPath = FfmpegHelper.GetFfmpegPath();
string ffprobePath = FfmpegHelper.GetFfprobePath();

Console.WriteLine($"FFmpeg path exists: {File.Exists(ffmpegPath)}");
Console.WriteLine($"FFprobe path exists: {File.Exists(ffprobePath)}");
Console.WriteLine();

// Try to execute ffmpeg
try
{
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = ffmpegPath,
        Arguments = "-version",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = System.Diagnostics.Process.Start(psi);
    if (process != null)
    {
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);
        
        Console.WriteLine("FFmpeg version output:");
        Console.WriteLine(output.Split('\n')[0]);
        Console.WriteLine();
        Console.WriteLine("✓ FFmpeg is working correctly!");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error running FFmpeg: {ex.Message}");
}


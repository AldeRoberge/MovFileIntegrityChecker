using System.Diagnostics;
using System.Globalization;

namespace MovFileIntegrityChecker.Services
{
    public class VideoAnalyzer
    {
        public double GetVideoDuration(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return 0;

                // Wait a bit for ffprobe to produce output
                string output = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);

                if (!string.IsNullOrWhiteSpace(output) && double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
                    return duration;

                // If ffprobe failed, try to parse duration from stderr as a fallback
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    var digits = new string(stderr.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
                    if (double.TryParse(digits.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur2))
                        return dur2;
                }
            }
            catch
            {
                // ignore and return 0
            }

            return 0;
        }
    }
}


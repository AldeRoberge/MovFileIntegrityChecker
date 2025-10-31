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

        public string? GetRandomFrameBase64(string filePath)
        {
            try
            {
                double duration = GetVideoDuration(filePath);
                var rnd = new Random();

                // Candidate timestamps (seconds)
                var timestamps = new List<double> { 0 };
                if (duration > 0)
                {
                    timestamps.Add(Math.Min(Math.Max(0.1, duration / 2.0), Math.Max(0.1, duration - 0.5)));
                    if (duration > 2.0) timestamps.Add(Math.Max(0.5, duration - 1.0));
                }

                // Add a random timestamp if duration is known
                if (duration > 0.5)
                {
                    double max = Math.Max(0.1, duration - 0.5);
                    timestamps.Add(rnd.NextDouble() * max);
                }

                // Ensure unique and reasonable timestamps
                timestamps = timestamps.Distinct().Select(t => Math.Max(0, t)).Take(5).ToList();

                // Try extraction strategies for each timestamp
                foreach (var ts in timestamps)
                {
                    // Strategy 1: write to a temp file using fast seek (-ss before -i)
                    var tmp = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".png");
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-ss {ts.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -frames:v 1 -vf scale=640:-1 -y -hide_banner -loglevel error \"{tmp}\"",
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            if (!p.WaitForExit(10000))
                            {
                                try { p.Kill(true); } catch { }
                            }
                        }

                        if (File.Exists(tmp))
                        {
                            var fi = new FileInfo(tmp);
                            if (fi.Length > 100)
                            {
                                byte[] data = File.ReadAllBytes(tmp);
                                try { File.Delete(tmp); } catch { }
                                return Convert.ToBase64String(data);
                            }
                            try { File.Delete(tmp); } catch { }
                        }
                    }
                    catch
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }

                    // Strategy 2: accurate seek (position after -i)
                    try
                    {
                        var tmp2 = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".png");
                        var psi2 = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-i \"{filePath}\" -ss {ts.ToString(CultureInfo.InvariantCulture)} -frames:v 1 -vf scale=640:-1 -y -hide_banner -loglevel error \"{tmp2}\"",
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var p2 = Process.Start(psi2);
                        if (p2 != null)
                        {
                            if (!p2.WaitForExit(12000))
                            {
                                try { p2.Kill(true); } catch { }
                            }
                        }

                        if (File.Exists(tmp2))
                        {
                            var fi2 = new FileInfo(tmp2);
                            if (fi2.Length > 100)
                            {
                                byte[] data = File.ReadAllBytes(tmp2);
                                try { File.Delete(tmp2); } catch { }
                                return Convert.ToBase64String(data);
                            }
                            try { File.Delete(tmp2); } catch { }
                        }
                    }
                    catch
                    {
                        // ignore and continue
                    }

                    // Strategy 3: try piping to stdout
                    try
                    {
                        var psi3 = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-ss {ts.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -frames:v 1 -vf scale=640:-1 -f image2 -vcodec png pipe:1 -hide_banner -loglevel error",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var proc = Process.Start(psi3);
                        if (proc != null)
                        {
                            using var ms = new MemoryStream();
                            var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms);
                            if (!copyTask.Wait(7000))
                            {
                                try { proc.Kill(true); } catch { }
                            }
                            else
                            {
                                proc.WaitForExit(2000);
                            }

                            if (ms.Length > 100)
                                return Convert.ToBase64String(ms.ToArray());
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}


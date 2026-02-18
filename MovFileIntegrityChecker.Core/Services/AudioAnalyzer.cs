using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MovFileIntegrityChecker.Core.Utilities;

namespace MovFileIntegrityChecker.Core.Services
{
    public class AudioTrackInfo
    {
        public int Index { get; set; }
        public string Codec { get; set; } = string.Empty;
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public string Bitrate { get; set; } = string.Empty;
        public string Language { get; set; } = "und";
        public double Duration { get; set; }
        public List<float> WaveformData { get; set; } = new();
        public bool HasAudio { get; set; } = true;
    }

    public class AudioAnalyzer
    {
        private const int WaveformSamples = 200; // Number of data points for waveform

        public static List<AudioTrackInfo> AnalyzeAudioTracks(string filePath)
        {
            var tracks = new List<AudioTrackInfo>();

            try
            {
                // Get audio track information using ffprobe
                var psi = new ProcessStartInfo
                {
                    FileName = FfmpegHelper.GetFfprobePath(),
                    Arguments = $"-v quiet -print_format json -show_streams -select_streams a \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return tracks;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var jsonDoc = JsonDocument.Parse(output);
                    if (jsonDoc.RootElement.TryGetProperty("streams", out var streams))
                    {
                        int index = 0;
                        foreach (var stream in streams.EnumerateArray())
                        {
                            var trackInfo = new AudioTrackInfo
                            {
                                Index = index
                            };

                            if (stream.TryGetProperty("codec_name", out var codec))
                                trackInfo.Codec = codec.GetString() ?? "unknown";

                            if (stream.TryGetProperty("channels", out var channels))
                                trackInfo.Channels = channels.GetInt32();

                            if (stream.TryGetProperty("sample_rate", out var sampleRate))
                                trackInfo.SampleRate = sampleRate.GetInt32();

                            if (stream.TryGetProperty("bit_rate", out var bitrate))
                                trackInfo.Bitrate = FormatBitrate(bitrate.GetString() ?? "0");

                            if (stream.TryGetProperty("duration", out var duration))
                            {
                                if (double.TryParse(duration.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
                                    trackInfo.Duration = dur;
                            }

                            if (stream.TryGetProperty("tags", out var tags))
                            {
                                if (tags.TryGetProperty("language", out var lang))
                                    trackInfo.Language = lang.GetString() ?? "und";
                            }

                            // Generate waveform data
                            trackInfo.WaveformData = GenerateWaveform(filePath, index, trackInfo.Duration);

                            // Check if track has actual audio
                            trackInfo.HasAudio = CheckHasAudio(trackInfo.WaveformData);

                            tracks.Add(trackInfo);
                            index++;
                        }
                    }
                }
            }
            catch
            {
                // Silently fail if ffprobe is not available
            }

            return tracks;
        }

        private static bool CheckHasAudio(List<float> waveform)
        {
            // Check if waveform has any significant amplitude (not just flat line)
            if (waveform.Count == 0) return false;
            
            var avg = waveform.Average();
            var variance = waveform.Select(x => Math.Pow(x - avg, 2)).Average();
            
            // If variance is very low, it's likely a silent/empty track
            return variance > 0.001;
        }

        private static List<float> GenerateWaveform(string filePath, int streamIndex, double duration)
        {
            var waveform = new List<float>();

            try
            {
                if (duration <= 0)
                    duration = 10; // Default fallback

                // Calculate segment duration based on total duration
                var segmentDuration = Math.Max(0.1, duration / WaveformSamples);
                
                for (int i = 0; i < WaveformSamples; i++)
                {
                    var startTime = i * segmentDuration;
                    if (startTime >= duration) break;
                    
                    var volume = GetVolumeAtTime(filePath, streamIndex, startTime, Math.Min(segmentDuration, duration - startTime));
                    waveform.Add(volume);
                }

                // Fill remaining with last value if needed
                while (waveform.Count < WaveformSamples)
                {
                    waveform.Add(waveform.Count > 0 ? waveform[waveform.Count - 1] : 0.5f);
                }
            }
            catch
            {
                // Fallback to flat line
                for (int i = 0; i < WaveformSamples; i++)
                    waveform.Add(0.5f);
            }

            return waveform;
        }

        private static float GetVolumeAtTime(string filePath, int streamIndex, double startTime, double duration)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FfmpegHelper.GetFfmpegPath(),
                    Arguments = $"-ss {startTime.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -map 0:a:{streamIndex} -t {duration.ToString(CultureInfo.InvariantCulture)} -af \"volumedetect\" -f null -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return 0.5f;

                string stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(true); } catch { }
                    return 0.5f;
                }

                // Parse mean_volume from output
                // Example: [Parsed_volumedetect_0 @ 0x...] mean_volume: -16.9 dB
                var meanVolumeLine = stderr.Split('\n')
                    .FirstOrDefault(line => line.Contains("mean_volume:"));

                if (meanVolumeLine != null)
                {
                    var parts = meanVolumeLine.Split(new[] { "mean_volume:" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        var volumeStr = parts[1].Split(new[] { ' ', 'd', 'B' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (!string.IsNullOrEmpty(volumeStr) && double.TryParse(volumeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var volumeDb))
                        {
                            // Convert dB to 0-1 range (assuming -60dB to 0dB range)
                            // -60dB or lower = 0, 0dB = 1
                            var normalized = (volumeDb + 60.0) / 60.0;
                            return (float)Math.Max(0, Math.Min(1, normalized));
                        }
                    }
                }
            }
            catch
            {
            }

            return 0.5f; // Default middle value if detection fails
        }

        private static string FormatBitrate(string bitrate)
        {
            if (long.TryParse(bitrate, out var bits))
            {
                if (bits >= 1000000)
                    return $"{bits / 1000000.0:F1} Mbps";
                if (bits >= 1000)
                    return $"{bits / 1000:F0} kbps";
                return $"{bits} bps";
            }
            return bitrate;
        }
    }
}


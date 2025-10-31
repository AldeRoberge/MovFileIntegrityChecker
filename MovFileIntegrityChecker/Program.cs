using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovFileIntegrityChecker
{
public class MovIntegrityChecker
{
    // Common MOV/MP4 atom types
    private static readonly HashSet<string> ValidAtomTypes = new HashSet<string>
    {
        "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot",
        "mvhd", "trak", "tkhd", "mdia", "mdhd", "hdlr", "minf",
        "vmhd", "smhd", "dinf", "stbl", "stsd", "stts", "stsc",
        "stsz", "stco", "co64", "edts", "elst", "udta", "meta"
    };

    public class AtomInfo
    {
        public string Type       { get; set; } = string.Empty;
        public long   Size       { get; set; }
        public long   Offset     { get; set; }
        public bool   IsComplete { get; set; }
    }

    public class FileCheckResult
    {
        public string         FilePath       { get; set; } = string.Empty;
        public bool           HasIssues      => Issues.Count > 0;
        public List<string>   Issues         { get; set; } = new List<string>();
        public List<AtomInfo> Atoms          { get; set; } = new List<AtomInfo>();
        public long           FileSize       { get; set; }
        public long           BytesValidated { get; set; }
        public double         TotalDuration  { get; set; }
        public double         PlayableDuration { get; set; }
    }

    private static FileCheckResult CheckFileIntegrity(string filePath)
    {
        var result = new FileCheckResult
        {
            FilePath = filePath,
        };

        if (!File.Exists(filePath))
        {
            result.Issues.Add("File does not exist");
            return result;
        }

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            long fileLength = fs.Length;
            result.FileSize = fileLength;

            if (fileLength < 8)
            {
                result.Issues.Add("File too small to be a valid MOV (< 8 bytes)");
                return result;
            }

            // Parse all atoms in the file
            bool hasStructuralErrors = false;
            bool hasIncompleteAtoms = false;
            long position = 0;

            while (position < fileLength)
            {
                fs.Position = position;

                // Read atom size (4 bytes, big-endian)
                byte[] sizeBytes = new byte[4];
                int bytesRead = fs.Read(sizeBytes, 0, 4);
                if (bytesRead < 4)
                {
                    result.Issues.Add($"Incomplete atom header at offset {position:N0}");
                    hasIncompleteAtoms = true;
                    break;
                }

                long atomSize = ReadBigEndianUInt32(sizeBytes);

                // Read atom type (4 bytes)
                byte[] typeBytes = new byte[4];
                bytesRead = fs.Read(typeBytes, 0, 4);
                if (bytesRead < 4)
                {
                    result.Issues.Add($"Incomplete atom type at offset {position:N0}");
                    hasIncompleteAtoms = true;
                    break;
                }

                string atomType = Encoding.ASCII.GetString(typeBytes);

                // Handle extended size (size == 1)
                long headerSize = 8;
                if (atomSize == 1)
                {
                    byte[] extSizeBytes = new byte[8];
                    bytesRead = fs.Read(extSizeBytes, 0, 8);
                    if (bytesRead < 8)
                    {
                        result.Issues.Add($"Incomplete extended size for atom '{atomType}' at offset {position:N0}");
                        hasIncompleteAtoms = true;
                        break;
                    }

                    atomSize = ReadBigEndianUInt64(extSizeBytes);
                    headerSize = 16;
                }
                // Handle size == 0 (atom extends to end of file)
                else if (atomSize == 0)
                {
                    atomSize = fileLength - position;
                }

                // Validate atom size
                if (atomSize < headerSize)
                {
                    result.Issues.Add($"Invalid atom size ({atomSize}) at offset {position:N0} for type '{atomType}'");
                    hasStructuralErrors = true;
                    break;
                }

                // Check if atom extends beyond file
                bool isComplete = (position + atomSize) <= fileLength;

                var atom = new AtomInfo
                {
                    Type = atomType,
                    Size = atomSize,
                    Offset = position,
                    IsComplete = isComplete
                };
                result.Atoms.Add(atom);

                if (!isComplete)
                {
                    long available = fileLength - position;
                    long missing = atomSize - available;
                    result.Issues.Add($"Incomplete atom '{atomType}' at offset {position:N0}: Expected {atomSize:N0} bytes, available {available:N0} bytes, missing {missing:N0} bytes ({(missing * 100.0 / atomSize):F1}%)");
                    hasIncompleteAtoms = true;
                    break;
                }

                // Warn about unknown atom types
                if (!ValidAtomTypes.Contains(atomType) && !IsAsciiPrintable(atomType))
                {
                    result.Issues.Add($"Unknown/invalid atom type '{atomType}' at offset {position:N0}");
                }

                position += atomSize;
            }

            result.BytesValidated = position;

            // Check for required atoms
            bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
            bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
            bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

            if (!hasFtyp && result.Atoms.Count > 0)
            {
                result.Issues.Add("Missing 'ftyp' atom (file type header)");
            }

            if (!hasMoov && result.Atoms.Count > 0)
            {
                result.Issues.Add("Missing 'moov' atom (metadata)");
            }

            if (!hasMdat && result.Atoms.Count > 0)
            {
                result.Issues.Add("Missing 'mdat' atom (media data)");
            }

            // Validate ftyp is first (if present)
            if (hasFtyp && result.Atoms.Count > 0 && result.Atoms[0].Type != "ftyp")
            {
                result.Issues.Add($"'ftyp' atom should be first, but found at offset {result.Atoms.First(a => a.Type == "ftyp").Offset:N0}");
            }

            // Check last atom alignment
            if (result.Atoms.Count > 0)
            {
                var lastAtom = result.Atoms.Last();
                long declaredEnd = lastAtom.Offset + lastAtom.Size;

                if (lastAtom.IsComplete && declaredEnd != fileLength)
                {
                    long gap = fileLength - declaredEnd;
                    result.Issues.Add($"Gap of {gap:N0} bytes after last atom '{lastAtom.Type}' at offset {declaredEnd:N0}");
                }
            }

            // Final verdict
            if (hasStructuralErrors || hasIncompleteAtoms || result.Atoms.Count == 0)
            {
                result.Issues.Add("File structure is invalid or incomplete");
            }

            // Get video duration using ffprobe
            result.TotalDuration = GetVideoDuration(filePath);
            
            // Estimate playable duration based on bytes validated
            if (result.TotalDuration > 0 && result.FileSize > 0)
            {
                double completionRatio = (double)result.BytesValidated / result.FileSize;
                result.PlayableDuration = result.TotalDuration * completionRatio;
            }
            else
            {
                result.PlayableDuration = 0;
            }
        }
        catch (Exception ex)
        {
            result.Issues.Add($"Error reading file: {ex.Message}");
        }

        return result;
    }

    private static void PrintDetailedResult(FileCheckResult result)
    {
        Console.WriteLine($"\n{'='}{new string('=', 80)}");
        Console.WriteLine($"File: {Path.GetFileName(result.FilePath)}");
        Console.WriteLine(new string('=', 80));

        Console.WriteLine($"\n📊 Analysis Summary:");
        Console.WriteLine($"   File Size: {result.FileSize:N0} bytes");
        Console.WriteLine($"   Atoms Found: {result.Atoms.Count}");
        Console.WriteLine($"   Bytes Validated: {result.BytesValidated:N0} / {result.FileSize:N0} ({(result.BytesValidated * 100.0 / Math.Max(1, result.FileSize)):F1}%)");

        // Display duration timeline if available
        if (result.TotalDuration > 0)
        {
            Console.WriteLine($"\n⏱️  Duration Timeline:");
            Console.WriteLine($"   Total Duration: {FormatDuration(result.TotalDuration)}");
            
            if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
            {
                Console.WriteLine($"   Playable Duration: {FormatDuration(result.PlayableDuration)}");
                double playablePercent = (result.PlayableDuration / result.TotalDuration) * 100.0;
                
                // Create visual timeline bar (50 chars wide)
                int barWidth = 50;
                int greenWidth = (int)(barWidth * playablePercent / 100.0);
                int redWidth = barWidth - greenWidth;
                
                string greenBar = new string('█', greenWidth);
                string redBar = new string('█', redWidth);
                
                Console.Write($"   ");
                WriteSuccess($"{greenBar}");
                Console.Write($"");
                WriteError($"{redBar}\n");
                Console.WriteLine($"   |");
                Console.WriteLine($"   {FormatDuration(0)}          {FormatDuration(result.PlayableDuration)} (break)          {FormatDuration(result.TotalDuration)}");
                Console.WriteLine($"   Start           Missing: {FormatDuration(result.TotalDuration - result.PlayableDuration)}           End");
            }
            else
            {
                Console.WriteLine($"   Status: Complete playback expected");
                int barWidth = 50;
                string greenBar = new string('█', barWidth);
                Console.Write($"   ");
                WriteSuccess($"{greenBar}\n");
                Console.WriteLine($"   |");
                Console.WriteLine($"   {FormatDuration(0)}                                      {FormatDuration(result.TotalDuration)}");
                Console.WriteLine($"   Start                                   End");
            }
        }

        if (result.Atoms.Count > 0)
        {
            Console.WriteLine($"\n📦 Atom Structure:");
            foreach (var atom in result.Atoms)
            {
                string status = atom.IsComplete ? "✅" : "❌";
                string knownType = ValidAtomTypes.Contains(atom.Type) ? "" : " (unknown)";
                Console.WriteLine($"   {status} [{atom.Type}]{knownType} - Size: {atom.Size:N0} bytes, Offset: {atom.Offset:N0}");
            }

            // Check for required atoms
            bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
            bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
            bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

            Console.WriteLine($"\n🔍 Key Atoms:");
            Console.WriteLine($"   ftyp (file type): {(hasFtyp ? "✅ Found" : "❌ Missing")}");
            Console.WriteLine($"   moov (metadata): {(hasMoov ? "✅ Found" : "❌ Missing")}");
            Console.WriteLine($"   mdat (media data): {(hasMdat ? "✅ Found" : "❌ Missing")}");
        }

        if (result.Issues.Count > 0)
        {
            Console.WriteLine($"\n⚠️  Issues Found ({result.Issues.Count}):");
            foreach (var issue in result.Issues)
            {
                WriteWarning($"   • {issue}");
            }
        }

        if (result.HasIssues)
        {
            WriteError("\n❌ File Status: CORRUPTED or INCOMPLETE");
        }
        else
        {
            WriteSuccess("\n✅ File Status: VALID and COMPLETE");
        }
    }

    private static uint ReadBigEndianUInt32(byte[] data)
    {
        return ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
    }

    private static long ReadBigEndianUInt64(byte[] data)
    {
        return ((long)data[0] << 56) | ((long)data[1] << 48) | ((long)data[2] << 40) | ((long)data[3] << 32) |
               ((long)data[4] << 24) | ((long)data[5] << 16) | ((long)data[6] << 8) | data[7];
    }

    private static bool IsAsciiPrintable(string str)
    {
        return str.All(c => c >= 32 && c <= 126);
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "00:00:00";
        
        int hours = (int)(seconds / 3600);
        int minutes = (int)((seconds % 3600) / 60);
        int secs = (int)(seconds % 60);
        
        return $"{hours:D2}:{minutes:D2}:{secs:D2}";
    }

    // --- Added helpers for extracting a random frame using ffprobe/ffmpeg ---
    private static double GetVideoDuration(string filePath)
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

            // If ffprobe failed, try to parse duration from stderr as a fallback (sometimes ffprobe prints to stderr)
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

    // Tries multiple extraction strategies and timestamps to increase the chance of success
    private static string? GetRandomFrameBase64(string filePath)
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
                        // wait longer for more complex files
                        if (!p.WaitForExit(10000))
                        {
                            try { p.Kill(true); } catch { }
                        }
                    }

                    if (File.Exists(tmp))
                    {
                        var fi = new FileInfo(tmp);
                        if (fi.Length > 100) // small sanity size
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

                // Strategy 3: try piping to stdout (older approach) as a last resort
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
                        // copy but with a timeout guard
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

            // All attempts failed
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void CreateErrorReport(FileCheckResult result)
    {
        try
        {
            string filePath = result.FilePath;
            string folder = Path.GetDirectoryName(filePath)!;
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string reportPath = Path.Combine(folder, $"{baseName}-Incomplet.html");

            var sb = new StringBuilder();

            // Try to get a random frame as base64 (may return null)
            string? frameBase64 = GetRandomFrameBase64(filePath);

            // HTML Header with embedded CSS
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"fr\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"    <title>Rapport d'Intégrité - {Path.GetFileName(filePath)}</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
            sb.AppendLine("        body {");
            sb.AppendLine("            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;");
            sb.AppendLine("            background: #000000;");
            sb.AppendLine("            min-height: 100vh;");
            sb.AppendLine("            padding: 40px 20px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .container {");
            sb.AppendLine("            max-width: 1000px;");
            sb.AppendLine("            margin: 0 auto;");
            sb.AppendLine("            background: #1a1a1a;");
            sb.AppendLine("            border-radius: 16px;");
            sb.AppendLine("            box-shadow: 0 20px 60px rgba(0,0,0,0.5);");
            sb.AppendLine("            overflow: hidden;");
            sb.AppendLine("            border: 1px solid #333;");
            sb.AppendLine("        }");
            sb.AppendLine("        .header {");
            sb.AppendLine("            background: #111111;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            padding: 30px 40px;");
            sb.AppendLine("            text-align: center;");
            sb.AppendLine("            border-bottom: 2px solid #ff8c00;");
            sb.AppendLine("        }");
            sb.AppendLine("        .header h1 {");
            sb.AppendLine("            font-size: 1.8em;");
            sb.AppendLine("            margin-bottom: 15px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .header .subtitle {");
            sb.AppendLine("            font-size: 1em;");
            sb.AppendLine("            color: #ccc;");
            sb.AppendLine("            margin-bottom: 15px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .header .warning {");
            sb.AppendLine("            font-size: 0.95em;");
            sb.AppendLine("            color: #ff8c00;");
            sb.AppendLine("            line-height: 1.5;");
            sb.AppendLine("            margin-top: 15px;");
            sb.AppendLine("            padding-top: 15px;");
            sb.AppendLine("            border-top: 1px solid #333;");
            sb.AppendLine("        }");
            sb.AppendLine("        .header .warning strong {");
            sb.AppendLine("            display: block;");
            sb.AppendLine("            margin-bottom: 8px;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("        }");
            sb.AppendLine("        .content {");
            sb.AppendLine("            padding: 40px;");
            sb.AppendLine("            background: #1a1a1a;");
            sb.AppendLine("        }");
            sb.AppendLine("        .section {");
            sb.AppendLine("            margin-bottom: 30px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .section-title {");
            sb.AppendLine("            font-size: 1.5em;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            margin-bottom: 15px;");
            sb.AppendLine("            padding-bottom: 10px;");
            sb.AppendLine("            border-bottom: 2px solid #333;");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            align-items: center;");
            sb.AppendLine("            gap: 10px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .info-grid {");
            sb.AppendLine("            display: grid;");
            sb.AppendLine("            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));");
            sb.AppendLine("            gap: 20px;");
            sb.AppendLine("            margin-top: 20px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .info-card {");
            sb.AppendLine("            background: #222222;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            padding: 20px;");
            sb.AppendLine("            border-radius: 12px;");
            sb.AppendLine("            box-shadow: 0 4px 15px rgba(0,0,0,0.5);");
            sb.AppendLine("            border: 1px solid #333;");
            sb.AppendLine("            border-left: 3px solid #ff8c00;");
            sb.AppendLine("        }");
            sb.AppendLine("        .info-card .label {");
            sb.AppendLine("            font-size: 0.9em;");
            sb.AppendLine("            color: #999;");
            sb.AppendLine("            margin-bottom: 5px;");
            sb.AppendLine("            text-transform: uppercase;");
            sb.AppendLine("            letter-spacing: 0.5px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .info-card .value {");
            sb.AppendLine("            font-size: 1.5em;");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("        }");
            sb.AppendLine("        .issue-list {");
            sb.AppendLine("            background: #2a1a1a;");
            sb.AppendLine("            border-left: 4px solid #ff8c00;");
            sb.AppendLine("            padding: 20px;");
            sb.AppendLine("            border-radius: 8px;");
            sb.AppendLine("        }");
            sb.AppendLine("        .issue-item {");
            sb.AppendLine("            padding: 10px 0;");
            sb.AppendLine("            border-bottom: 1px solid #333;");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            align-items: start;");
            sb.AppendLine("            gap: 10px;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("        }");
            sb.AppendLine("        .issue-item:last-child {");
            sb.AppendLine("            border-bottom: none;");
            sb.AppendLine("        }");
            sb.AppendLine("        .issue-icon {");
            sb.AppendLine("            color: #ff8c00;");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            flex-shrink: 0;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-table {");
            sb.AppendLine("            width: 100%;");
            sb.AppendLine("            border-collapse: collapse;");
            sb.AppendLine("            margin-top: 20px;");
            sb.AppendLine("            box-shadow: 0 2px 10px rgba(0,0,0,0.5);");
            sb.AppendLine("            border-radius: 8px;");
            sb.AppendLine("            overflow: hidden;");
            sb.AppendLine("            border: 1px solid #333;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-table thead {");
            sb.AppendLine("            background: #111111;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            border-bottom: 2px solid #ff8c00;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-table th {");
            sb.AppendLine("            padding: 15px;");
            sb.AppendLine("            text-align: left;");
            sb.AppendLine("            font-weight: 600;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-table td {");
            sb.AppendLine("            padding: 12px 15px;");
            sb.AppendLine("            border-bottom: 1px solid #333;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-table tbody tr {");
            sb.AppendLine("            background: #1a1a1a;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-table tbody tr:hover {");
            sb.AppendLine("            background: #252525;");
            sb.AppendLine("        }");
            sb.AppendLine("        .status-badge {");
            sb.AppendLine("            display: inline-block;");
            sb.AppendLine("            padding: 5px 12px;");
            sb.AppendLine("            border-radius: 20px;");
            sb.AppendLine("            font-size: 0.85em;");
            sb.AppendLine("            font-weight: 600;");
            sb.AppendLine("        }");
            sb.AppendLine("        .status-complete {");
            sb.AppendLine("            background: #1a3a1a;");
            sb.AppendLine("            color: #90ee90;");
            sb.AppendLine("            border: 1px solid #90ee90;");
            sb.AppendLine("        }");
            sb.AppendLine("        .status-incomplete {");
            sb.AppendLine("            background: #3a1a1a;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            border: 1px solid #ff8c00;");
            sb.AppendLine("        }");
            sb.AppendLine("        .atom-type {");
            sb.AppendLine("            font-family: 'Courier New', monospace;");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            font-size: 1.1em;");
            sb.AppendLine("        }");
            sb.AppendLine("        .footer {");
            sb.AppendLine("            background: #111111;");
            sb.AppendLine("            padding: 20px 40px;");
            sb.AppendLine("            text-align: center;");
            sb.AppendLine("            color: #888;");
            sb.AppendLine("            font-size: 0.9em;");
            sb.AppendLine("            border-top: 1px solid #333;");
            sb.AppendLine("        }");
            sb.AppendLine("        .progress-bar {");
            sb.AppendLine("            width: 100%;");
            sb.AppendLine("            height: 30px;");
            sb.AppendLine("            background: #2a2a2a;");
            sb.AppendLine("            border-radius: 15px;");
            sb.AppendLine("            overflow: hidden;");
            sb.AppendLine("            margin-top: 10px;");
            sb.AppendLine("            border: 1px solid #444;");
            sb.AppendLine("        }");
            sb.AppendLine("        .progress-fill {");
            sb.AppendLine("            height: 100%;");
            sb.AppendLine("            background: linear-gradient(90deg, #ff8c00 0%, #ffa500 100%);");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            align-items: center;");
            sb.AppendLine("            justify-content: center;");
            sb.AppendLine("            color: #000000;");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            font-size: 0.85em;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-container {");
            sb.AppendLine("            margin-top: 30px;");
            sb.AppendLine("            padding: 20px;");
            sb.AppendLine("            background: #222222;");
            sb.AppendLine("            border-radius: 12px;");
            sb.AppendLine("            border: 1px solid #333;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-title {");
            sb.AppendLine("            font-size: 1.2em;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            margin-bottom: 15px;");
            sb.AppendLine("            font-weight: 600;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-bar {");
            sb.AppendLine("            width: 100%;");
            sb.AppendLine("            height: 40px;");
            sb.AppendLine("            background: #2a2a2a;");
            sb.AppendLine("            border-radius: 8px;");
            sb.AppendLine("            overflow: hidden;");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            border: 2px solid #444;");
            sb.AppendLine("            margin: 15px 0;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-good {");
            sb.AppendLine("            background: linear-gradient(90deg, #00aa00 0%, #00cc00 100%);");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            align-items: center;");
            sb.AppendLine("            justify-content: center;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            font-size: 0.9em;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-bad {");
            sb.AppendLine("            background: linear-gradient(90deg, #cc0000 0%, #aa0000 100%);");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            align-items: center;");
            sb.AppendLine("            justify-content: center;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            font-size: 0.9em;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-labels {");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            justify-content: space-between;");
            sb.AppendLine("            margin-top: 10px;");
            sb.AppendLine("            color: #ccc;");
            sb.AppendLine("            font-size: 0.9em;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-label {");
            sb.AppendLine("            display: flex;");
            sb.AppendLine("            flex-direction: column;");
            sb.AppendLine("            align-items: center;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-label.start { align-items: flex-start; }");
            sb.AppendLine("        .timeline-label.end { align-items: flex-end; }");
            sb.AppendLine("        .timeline-label .time {");
            sb.AppendLine("            font-weight: bold;");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("            font-size: 1.1em;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-info {");
            sb.AppendLine("            margin-top: 15px;");
            sb.AppendLine("            padding: 15px;");
            sb.AppendLine("            background: #1a1a1a;");
            sb.AppendLine("            border-radius: 8px;");
            sb.AppendLine("            border-left: 3px solid #ff8c00;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-info div {");
            sb.AppendLine("            color: #ccc;");
            sb.AppendLine("            margin: 5px 0;");
            sb.AppendLine("        }");
            sb.AppendLine("        .timeline-info strong {");
            sb.AppendLine("            color: #ffffff;");
            sb.AppendLine("        }");
            sb.AppendLine("        .preview img { max-width: 100%; border-radius: 8px; display: block; margin: 15px auto; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <div class=\"container\">");

            // Header
            sb.AppendLine("        <div class=\"header\">");
            sb.AppendLine("            <h1>⚠️ Rapport d'Intégrité Fichier</h1>");
            sb.AppendLine($"            <div class=\"subtitle\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
            sb.AppendLine("            <div class=\"warning\">");
            sb.AppendLine("                <strong>Avertissement - Fichier potentiellement incomplet</strong>");
            sb.AppendLine("                Ce fichier a été automatiquement détecté comme étant potentiellement incomplet. ");
            sb.AppendLine("                Veuillez le visionner jusqu'à la fin dans VLC pour vérifier s'il se lit correctement.");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");

            // Content
            sb.AppendLine("        <div class=\"content\">");

            // Embed preview if available
            if (!string.IsNullOrEmpty(frameBase64))
            {
                sb.AppendLine("            <div class=\"section\">");
                sb.AppendLine("                <div class=\"section-title\">Preview</div>");
                sb.AppendLine("                <div class=\"preview\">");
                sb.AppendLine($"                    <img src=\"data:image/png;base64,{frameBase64}\" alt=\"Preview\">");
                sb.AppendLine("                </div>");
                sb.AppendLine("            </div>");
            }
            else
            {
                sb.AppendLine("            <div class=\"section\">");
                sb.AppendLine("                <div class=\"section-title\">Preview</div>");
                sb.AppendLine("                <div class=\"issue-list\">");
                sb.AppendLine("                    <div class=\"issue-item\">");
                sb.AppendLine("                        <span class=\"issue-icon\">ℹ️</span>");
                sb.AppendLine("                        <span>Preview not available (ffmpeg/ffprobe not found or frame extraction failed).</span>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                </div>");
                sb.AppendLine("            </div>");
            }

            sb.AppendLine("            <div class=\"section\">");
            sb.AppendLine("                <div class=\"section-title\">📊 Résumé Technique</div>");
            sb.AppendLine("                <div class=\"info-grid\">");
            sb.AppendLine("                    <div class=\"info-card\">");
            sb.AppendLine("                        <div class=\"label\">Nom du fichier</div>");
            sb.AppendLine($"                        <div class=\"value\">{System.Security.SecurityElement.Escape(Path.GetFileName(filePath))}</div>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                    <div class=\"info-card\">");
            sb.AppendLine("                        <div class=\"label\">Taille du fichier</div>");
            sb.AppendLine($"                        <div class=\"value\">{result.FileSize:N0} octets</div>");
            sb.AppendLine($"                        <div class=\"label\">({result.FileSize / (1024.0 * 1024.0):F2} MB)</div>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                    <div class=\"info-card\">");
            sb.AppendLine("                        <div class=\"label\">Octets validés</div>");
            sb.AppendLine($"                        <div class=\"value\">{result.BytesValidated:N0}</div>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                    <div class=\"info-card\">");
            sb.AppendLine("                        <div class=\"label\">Atoms détectés</div>");
            sb.AppendLine($"                        <div class=\"value\">{result.Atoms.Count}</div>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                </div>");

            double validationPercent = result.FileSize > 0 ? (result.BytesValidated * 100.0 / result.FileSize) : 0;
            sb.AppendLine("                <div class=\"progress-bar\">");
            sb.AppendLine($"                    <div class=\"progress-fill\" style=\"width: {validationPercent.ToString("F1", CultureInfo.InvariantCulture)}%\">\n                        {validationPercent:F1}% validé\n                    </div>");
            sb.AppendLine("                </div>");

            // Add duration timeline if available
            if (result.TotalDuration > 0)
            {
                sb.AppendLine("                <div class=\"timeline-container\">");
                sb.AppendLine("                    <div class=\"timeline-title\">⏱️ Chronologie de la Vidéo</div>");
                
                double playablePercent = result.TotalDuration > 0 ? (result.PlayableDuration / result.TotalDuration) * 100.0 : 0;
                double brokenPercent = 100.0 - playablePercent;
                
                sb.AppendLine("                    <div class=\"timeline-bar\">");
                if (playablePercent > 0)
                {
                    sb.AppendLine($"                        <div class=\"timeline-good\" style=\"width: {playablePercent.ToString("F1", CultureInfo.InvariantCulture)}%\">");
                    sb.AppendLine($"                            ✓ Lecture OK");
                    sb.AppendLine("                        </div>");
                }
                if (brokenPercent > 0)
                {
                    sb.AppendLine($"                        <div class=\"timeline-bad\" style=\"width: {brokenPercent.ToString("F1", CultureInfo.InvariantCulture)}%\">");
                    sb.AppendLine($"                            ✗ Corrompu");
                    sb.AppendLine("                        </div>");
                }
                sb.AppendLine("                    </div>");
                
                sb.AppendLine("                    <div class=\"timeline-labels\">");
                sb.AppendLine("                        <div class=\"timeline-label start\">");
                sb.AppendLine("                            <div class=\"time\">00:00:00</div>");
                sb.AppendLine("                            <div>Début</div>");
                sb.AppendLine("                        </div>");
                
                if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                {
                    sb.AppendLine("                        <div class=\"timeline-label\">");
                    sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(FormatDuration(result.PlayableDuration))}</div>");
                    sb.AppendLine("                            <div style=\"color: #ff8c00;\">⚠️ Point de rupture</div>");
                    sb.AppendLine("                        </div>");
                }
                
                sb.AppendLine("                        <div class=\"timeline-label end\">");
                sb.AppendLine($"                            <div class=\"time\">{System.Security.SecurityElement.Escape(FormatDuration(result.TotalDuration))}</div>");
                sb.AppendLine("                            <div>Fin</div>");
                sb.AppendLine("                        </div>");
                sb.AppendLine("                    </div>");
                
                sb.AppendLine("                    <div class=\"timeline-info\">");
                sb.AppendLine($"                        <div><strong>Durée totale:</strong> {System.Security.SecurityElement.Escape(FormatDuration(result.TotalDuration))}</div>");
                sb.AppendLine($"                        <div><strong>Durée lisible:</strong> {System.Security.SecurityElement.Escape(FormatDuration(result.PlayableDuration))} ({playablePercent:F1}%)</div>");
                if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
                {
                    double missingDuration = result.TotalDuration - result.PlayableDuration;
                    sb.AppendLine($"                        <div style=\"color: #ff8c00;\"><strong>Durée manquante:</strong> {System.Security.SecurityElement.Escape(FormatDuration(missingDuration))} ({brokenPercent:F1}%)</div>");
                }
                sb.AppendLine("                    </div>");
                sb.AppendLine("                </div>");
            }
            
            sb.AppendLine("            </div>");

            if (result.Issues.Count > 0)
            {
                sb.AppendLine("            <div class=\"section\">\n                <div class=\"section-title\">❌ Problèmes Détectés</div>\n                <div class=\"issue-list\">\n");
                foreach (var issue in result.Issues)
                {
                    sb.AppendLine("                    <div class=\"issue-item\">\n                        <span class=\"issue-icon\">❌</span>\n                        <span>" + System.Security.SecurityElement.Escape(issue) + "</span>\n                    </div>");
                }
                sb.AppendLine("                </div>\n            </div>");
            }

            if (result.Atoms.Count > 0)
            {
                sb.AppendLine("            <div class=\"section\">\n                <div class=\"section-title\">📦 Structure des Atoms</div>\n                <table class=\"atom-table\">\n                    <thead>\n                        <tr>\n                            <th>Type</th>\n                            <th>Taille</th>\n                            <th>Offset</th>\n                            <th>Statut</th>\n                        </tr>\n                    </thead>\n                    <tbody>");
                foreach (var atom in result.Atoms)
                {
                    string statusClass = atom.IsComplete ? "status-complete" : "status-incomplete";
                    string statusText = atom.IsComplete ? "✅ Complet" : "❌ Incomplet";
                    sb.AppendLine("                        <tr>\n                            <td><span class=\"atom-type\">" + System.Security.SecurityElement.Escape(atom.Type) + "</span></td>\n                            <td>" + atom.Size.ToString("N0") + " octets</td>\n                            <td>" + atom.Offset.ToString("N0") + "</td>\n                            <td><span class=\"status-badge " + statusClass + "\">" + statusText + "</span></td>\n                        </tr>");
                }
                sb.AppendLine("                    </tbody>\n                </table>\n            </div>");
            }

            sb.AppendLine("        </div>");

            // Footer
            sb.AppendLine("        <div class=\"footer\">\n            Généré automatiquement le " + DateTime.Now.ToString("yyyy-MM-dd") + " à " + DateTime.Now.ToString("HH:mm:ss") + "<br>\n            Outil : MovIntegrityChecker (rapport automatique)\n        </div>");

            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WriteWarning($"Impossible d'écrire le rapport d'erreur : {ex.Message}");
        }
    }

    private static void WriteWarning(string message)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ForegroundColor = oldColor;
    }

    private static void WriteSuccess(string message)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ForegroundColor = oldColor;
    }

    private static void WriteError(string message)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ForegroundColor = oldColor;
    }

    private static void WriteInfo(string message)
    {
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ForegroundColor = oldColor;
    }

    private static void DeleteEmptyDirectories(string rootPath, bool recursive)
    {
        try
        {
            var directories = Directory.GetDirectories(
                rootPath,
                "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            int deletedCount = 0;

            // Sort by descending path length to delete deep folders first
            foreach (var dir in directories.OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    deletedCount++;
                    WriteSuccess($"Deleted empty folder: {dir}");
                }
            }

            // Check root folder itself
            if (!Directory.EnumerateFileSystemEntries(rootPath).Any())
            {
                Directory.Delete(rootPath);
                deletedCount++;
                WriteSuccess($"Deleted empty root folder: {rootPath}");
            }

            if (deletedCount > 0)
                WriteInfo($"\n✅ {deletedCount} empty folder(s) deleted.");
            else
                WriteInfo("\nNo empty folders found.");
        }
        catch (Exception ex)
        {
            WriteError($"Error while deleting empty folders: {ex.Message}");
        }
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("=== MOV File Integrity Checker ===\n");

        // Example for debugging — remove these lines when using real command-line args
        args = new string[]
        {
            "T:\\SPT\\SP\\Mont\\Prod1\\2_COU\\_DL",
            /*"T:\\SPT\\SP\\Mont\\Prod2\\2_COU\\_DL",*/
            /*"T:\\SPT\\SP\\Mont\\Backup\\2_COU\\_DL",*/
            "-r",
            "--delete-empty"
        };

        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Check single file:  program.exe <path_to_mov_file>");
            Console.WriteLine("  Check folder(s):    program.exe <path_to_folder1> <path_to_folder2> ...");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  -r, --recursive       Check subfolders recursively");
            Console.WriteLine("  -s, --summary         Show summary only (no detailed output)");
            Console.WriteLine("  -d, --delete-empty    Delete empty folders after processing");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  program.exe video.mov");
            Console.WriteLine("  program.exe C:\\Videos -r");
            Console.WriteLine("  program.exe C:\\Videos D:\\MoreVideos --recursive --delete-empty --summary");
            return;
        }

        // Extract flags
        bool recursive = args.Any(a => a == "-r" || a == "--recursive");
        bool summaryOnly = args.Any(a => a == "-s" || a == "--summary");
        bool deleteEmpty = args.Any(a => a == "-d" || a == "--delete-empty");

        // Extract paths (everything that is not a flag)
        var paths = args.Where(a => !a.StartsWith("-")).ToList();

        if (paths.Count == 0)
        {
            WriteError("No folder or file paths specified.");
            return;
        }

        var results = new List<FileCheckResult>();

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                // Single file
                WriteInfo($"\nChecking file: {path}\n");
                var result = CheckFileIntegrity(path);
                results.Add(result);

                if (result.HasIssues)
                    CreateErrorReport(result);

                if (!summaryOnly)
                    PrintDetailedResult(result);
            }
            else if (Directory.Exists(path))
            {
                // Directory
                WriteInfo($"\nChecking folder: {path}");
                WriteInfo($"Recursive: {(recursive ? "Yes" : "No")}\n");

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var extensions = new[] { "*.mov", "*.mp4", "*.m4v", "*.m4a" };

                var files = extensions
                    .SelectMany(ext => Directory.GetFiles(path, ext, searchOption))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    WriteWarning($"No MOV/MP4 files found in: {path}");
                    continue;
                }

                WriteInfo($"Found {files.Count} file(s) to check...\n");

                int current = 0;
                foreach (var file in files)
                {
                    current++;

                    if (summaryOnly)
                        Console.Write($"\rProcessing: {current}/{files.Count} - {Path.GetFileName(file)}".PadRight(80));
                    else
                        WriteInfo($"[{current}/{files.Count}] Checking: {Path.GetFileName(file)}");

                    var result = CheckFileIntegrity(file);
                    results.Add(result);

                    if (result.HasIssues)
                        CreateErrorReport(result);

                    if (!summaryOnly)
                        PrintDetailedResult(result);
                }

                if (summaryOnly)
                    Console.WriteLine("\n");

                // 🔥 Delete empty folders if requested
                if (deleteEmpty)
                {
                    DeleteEmptyDirectories(path, recursive);
                }
            }
            else
            {
                WriteError($"Path not found: {path}");
                Environment.ExitCode = 1;
            }
        }

        // Print summary
        Console.WriteLine($"\n{new string('=', 80)}");
        Console.WriteLine("SUMMARY");
        Console.WriteLine(new string('=', 80));

        int totalFiles = results.Count;
        int validFiles = results.Count(r => !r.HasIssues);
        int corruptedFiles = results.Count(r => r.HasIssues);
        long totalSize = results.Sum(r => r.FileSize);

        Console.WriteLine($"\nTotal Files Checked: {totalFiles}");
        WriteSuccess($"Valid Files: {validFiles} ({(validFiles * 100.0 / Math.Max(1, totalFiles)):F1}%)");
        WriteError($"Corrupted/Incomplete Files: {corruptedFiles} ({(corruptedFiles * 100.0 / Math.Max(1, totalFiles)):F1}%)");
        Console.WriteLine($"Total Size: {totalSize:N0} bytes ({totalSize / (1024.0 * 1024.0):F2} MB)");

        if (corruptedFiles > 0)
        {
            Console.WriteLine($"\n❌ Corrupted/Incomplete Files:");
            foreach (var result in results.Where(r => r.HasIssues))
            {
                WriteError($"   • {Path.GetFileName(result.FilePath)}");
                if (!summaryOnly && result.Issues.Count > 0)
                {
                    foreach (var issue in result.Issues.Take(3))
                        Console.WriteLine($"      - {issue}");
                    if (result.Issues.Count > 3)
                        Console.WriteLine($"      ... and {result.Issues.Count - 3} more issue(s)");
                }
            }
        }

        Environment.ExitCode = corruptedFiles > 0 ? 1 : 0;
    }
}
}

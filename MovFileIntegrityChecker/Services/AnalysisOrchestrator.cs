// The conductor of the whole operation - coordinates all the different analyzers.
// Takes your file or folder, runs it through the checks, generates reports, and shows pretty output.
// Basically the air traffic controller making sure everything happens in the right order.

using MovFileIntegrityChecker.Models;
using static MovFileIntegrityChecker.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.Services
{
    public class AnalysisOrchestrator
    {
        private readonly FileAnalyzer _fileAnalyzer = new();
        private readonly JsonReportGenerator _jsonReportGenerator = new();

        public List<FileCheckResult> AnalyzePaths(string[] paths, bool recursive, bool summaryOnly, bool deleteEmpty,
            Action<FileCheckResult>? htmlReportGenerator = null)
        {
            var results = new List<FileCheckResult>();

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    results.AddRange(AnalyzeSingleFile(path, summaryOnly, htmlReportGenerator));
                }
                else if (Directory.Exists(path))
                {
                    results.AddRange(AnalyzeDirectory(path, recursive, summaryOnly, deleteEmpty, htmlReportGenerator));
                }
                else
                {
                    WriteError($"Path not found: {path}");
                    Environment.ExitCode = 1;
                }
            }

            return results;
        }

        private List<FileCheckResult> AnalyzeSingleFile(string filePath, bool summaryOnly,
            Action<FileCheckResult>? htmlReportGenerator)
        {
            WriteInfo($"\nChecking file: {filePath}\n");
            var result = _fileAnalyzer.CheckFileIntegrity(filePath);

            _jsonReportGenerator.CreateReport(result);

            if (result.HasIssues)
            {
                htmlReportGenerator?.Invoke(result);
            }

            if (!summaryOnly)
            {
                PrintDetailedResult(result);
            }

            return new List<FileCheckResult> { result };
        }

        private List<FileCheckResult> AnalyzeDirectory(string dirPath, bool recursive, bool summaryOnly,
            bool deleteEmpty, Action<FileCheckResult>? htmlReportGenerator)
        {
            var results = new List<FileCheckResult>();

            WriteInfo($"\nChecking folder: {dirPath}");
            WriteInfo($"Recursive: {(recursive ? "Yes" : "No")}\n");

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var extensions = new[] { "*.mov", "*.mp4", "*.m4v", "*.m4a" };

            var files = extensions
                .SelectMany(ext => Directory.GetFiles(dirPath, ext, searchOption))
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
            {
                WriteWarning($"No MOV/MP4 files found in: {dirPath}");
                return results;
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

                var result = _fileAnalyzer.CheckFileIntegrity(file);
                results.Add(result);

                _jsonReportGenerator.CreateReport(result);

                if (result.HasIssues)
                {
                    htmlReportGenerator?.Invoke(result);
                }

                if (!summaryOnly)
                {
                    PrintDetailedResult(result);
                }
            }

            if (summaryOnly)
                Console.WriteLine("\n");

            if (deleteEmpty)
            {
                Utilities.FileSystemHelper.DeleteEmptyDirectories(dirPath, recursive);
            }

            return results;
        }

        private void PrintDetailedResult(FileCheckResult result)
        {
            Console.WriteLine($"\n{'='}{new string('=', 80)}");
            Console.WriteLine($"File: {Path.GetFileName(result.FilePath)}");
            Console.WriteLine(new string('=', 80));

            Console.WriteLine($"\n📊 Analysis Summary:");
            Console.WriteLine($"   File Size: {result.FileSize:N0} bytes");
            Console.WriteLine($"   Atoms Found: {result.Atoms.Count}");
            Console.WriteLine($"   Bytes Validated: {result.BytesValidated:N0} / {result.FileSize:N0} ({(result.BytesValidated * 100.0 / Math.Max(1, result.FileSize)):F1}%)");

            if (result.TotalDuration > 0)
            {
                PrintDurationTimeline(result);
            }

            if (result.Atoms.Count > 0)
            {
                PrintAtomStructure(result);
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

        private void PrintDurationTimeline(FileCheckResult result)
        {
            Console.WriteLine($"\n⏱️  Duration Timeline:");
            Console.WriteLine($"   Total Duration: {FormatDuration(result.TotalDuration)}");

            if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
            {
                Console.WriteLine($"   Playable Duration: {FormatDuration(result.PlayableDuration)}");
                double playablePercent = (result.PlayableDuration / result.TotalDuration) * 100.0;

                const int barWidth = 50;
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

        private void PrintAtomStructure(FileCheckResult result)
        {
            var validAtomTypes = new HashSet<string>
            {
                "ftyp", "moov", "mdat", "free", "skip", "wide", "pnot",
                "mvhd", "trak", "tkhd", "mdia", "mdhd", "hdlr", "minf",
                "vmhd", "smhd", "dinf", "stbl", "stsd", "stts", "stsc",
                "stsz", "stco", "co64", "edts", "elst", "udta", "meta"
            };

            Console.WriteLine($"\n📦 Atom Structure:");
            foreach (var atom in result.Atoms)
            {
                string status = atom.IsComplete ? "✅" : "❌";
                string knownType = validAtomTypes.Contains(atom.Type) ? "" : " (unknown)";
                Console.WriteLine($"   {status} [{atom.Type}]{knownType} - Size: {atom.Size:N0} bytes, Offset: {atom.Offset:N0}");
            }

            bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
            bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
            bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

            Console.WriteLine($"\n🔍 Key Atoms:");
            Console.WriteLine($"   ftyp (file type): {(hasFtyp ? "✅ Found" : "❌ Missing")}");
            Console.WriteLine($"   moov (metadata): {(hasMoov ? "✅ Found" : "❌ Missing")}");
            Console.WriteLine($"   mdat (media data): {(hasMdat ? "✅ Found" : "❌ Missing")}");
        }

        public void PrintSummary(List<FileCheckResult> results, bool summaryOnly)
        {
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


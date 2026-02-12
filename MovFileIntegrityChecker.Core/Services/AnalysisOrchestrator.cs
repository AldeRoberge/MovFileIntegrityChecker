// The conductor of the whole operation - coordinates all the different analyzers.
// Takes your file or folder, runs it through the checks, generates reports, and shows pretty output.
// Basically the air traffic controller making sure everything happens in the right order.

using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Constants;
using static MovFileIntegrityChecker.Core.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.Core.Services
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

            var extensions = new[] { ".mov", ".mp4", ".m4v", ".m4a" };
            var files = SafeGetFiles(dirPath, extensions, recursive);

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
                    Write($"\rProcessing: {current}/{files.Count} - {Path.GetFileName(file)}".PadRight(80));
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
                WriteLine("\n");

            if (deleteEmpty)
            {
                Utilities.FileSystemHelper.DeleteEmptyDirectories(dirPath, recursive);
            }

            return results;
        }

        private List<string> SafeGetFiles(string path, string[] extensions, bool recursive)
        {
            var files = new List<string>();

            try
            {
                // Add files in current directory
                foreach (var file in Directory.GetFiles(path))
                {
                    if (extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    {
                        files.Add(file);
                    }
                }

                // Recurse if needed
                if (recursive)
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        files.AddRange(SafeGetFiles(dir, extensions, recursive));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                WriteWarning($"Skipping inaccessible directory: {path}");
            }
            catch (PathTooLongException)
            {
                 WriteWarning($"Skipping directory with too long path: {path}");
            }
            catch (Exception ex)
            {
                WriteError($"Error accessing directory {path}: {ex.Message}");
            }

            return files;
        }

        private void PrintDetailedResult(FileCheckResult result)
        {
            WriteLine($"\n{'='}{new string('=', 80)}");
            WriteLine($"File: {Path.GetFileName(result.FilePath)}");
            WriteLine(new string('=', 80));

            WriteLine($"\n📊 Analysis Summary:");
            WriteLine($"   File Size: {result.FileSize:N0} bytes");
            WriteLine($"   Atoms Found: {result.Atoms.Count}");
            WriteLine($"   Bytes Validated: {result.BytesValidated:N0} / {result.FileSize:N0} ({(result.BytesValidated * 100.0 / Math.Max(1, result.FileSize)):F1}%)");

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
                WriteLine($"\n⚠️  Issues Found ({result.Issues.Count}):");
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
            WriteLine($"\n⏱️  Duration Timeline:");
            WriteLine($"   Total Duration: {FormatDuration(result.TotalDuration)}");

            if (result.HasIssues && result.PlayableDuration < result.TotalDuration)
            {
                WriteLine($"   Playable Duration: {FormatDuration(result.PlayableDuration)}");
                double playablePercent = (result.PlayableDuration / result.TotalDuration) * 100.0;

                const int barWidth = 50;
                int greenWidth = (int)(barWidth * playablePercent / 100.0);
                int redWidth = barWidth - greenWidth;

                string greenBar = new string('█', greenWidth);
                string redBar = new string('█', redWidth);

                Write($"   ");
                WriteSuccess($"{greenBar}");
                Write($"");
                WriteError($"{redBar}\n");
                WriteLine($"   |");
                WriteLine($"   {FormatDuration(0)}          {FormatDuration(result.PlayableDuration)} (break)          {FormatDuration(result.TotalDuration)}");
                WriteLine($"   Start           Missing: {FormatDuration(result.TotalDuration - result.PlayableDuration)}           End");
            }
            else
            {
                WriteLine($"   Status: Complete playback expected");
                int barWidth = 50;
                string greenBar = new string('█', barWidth);
                Write($"   ");
                WriteSuccess($"{greenBar}\n");
                WriteLine($"   |");
                WriteLine($"   {FormatDuration(0)}                                      {FormatDuration(result.TotalDuration)}");
                WriteLine($"   Start                                   End");
            }
        }

        private void PrintAtomStructure(FileCheckResult result)
        {
            WriteLine($"\n📦 Atom Structure:");
            foreach (var atom in result.Atoms)
            {
                string status = atom.IsComplete ? "✅" : "❌";
                string knownType = AtomConstants.ValidAtomTypes.Contains(atom.Type) ? "" : " (unknown)";
                WriteLine($"   {status} [{atom.Type}]{knownType} - Size: {atom.Size:N0} bytes, Offset: {atom.Offset:N0}");
            }

            bool hasFtyp = result.Atoms.Any(a => a.Type == "ftyp");
            bool hasMoov = result.Atoms.Any(a => a.Type == "moov");
            bool hasMdat = result.Atoms.Any(a => a.Type == "mdat");

            WriteLine($"\n🔍 Key Atoms:");
            WriteLine($"   ftyp (file type): {(hasFtyp ? "✅ Found" : "❌ Missing")}");
            WriteLine($"   moov (metadata): {(hasMoov ? "✅ Found" : "❌ Missing")}");
            WriteLine($"   mdat (media data): {(hasMdat ? "✅ Found" : "❌ Missing")}");
        }

        public void PrintSummary(List<FileCheckResult> results, bool summaryOnly)
        {
            WriteLine($"\n{new string('=', 80)}");
            WriteLine("SUMMARY");
            WriteLine(new string('=', 80));

            int totalFiles = results.Count;
            int validFiles = results.Count(r => !r.HasIssues);
            int corruptedFiles = results.Count(r => r.HasIssues);
            long totalSize = results.Sum(r => r.FileSize);

            WriteLine($"\nTotal Files Checked: {totalFiles}");
            WriteSuccess($"Valid Files: {validFiles} ({(validFiles * 100.0 / Math.Max(1, totalFiles)):F1}%)");
            WriteError($"Corrupted/Incomplete Files: {corruptedFiles} ({(corruptedFiles * 100.0 / Math.Max(1, totalFiles)):F1}%)");
            WriteLine($"Total Size: {totalSize:N0} bytes ({totalSize / (1024.0 * 1024.0):F2} MB)");

            if (corruptedFiles > 0)
            {
                WriteLine($"\n❌ Corrupted/Incomplete Files:");
                foreach (var result in results.Where(r => r.HasIssues))
                {
                    WriteError($"   • {Path.GetFileName(result.FilePath)}");
                    if (!summaryOnly && result.Issues.Count > 0)
                    {
                        foreach (var issue in result.Issues.Take(3))
                            WriteLine($"      - {issue}");
                        if (result.Issues.Count > 3)
                            WriteLine($"      ... and {result.Issues.Count - 3} more issue(s)");
                    }
                }
            }

            Environment.ExitCode = corruptedFiles > 0 ? 1 : 0;
        }
    }
}


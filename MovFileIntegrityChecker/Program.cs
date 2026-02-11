using MovFileIntegrityChecker.Models;
using MovFileIntegrityChecker.Services;
using MovFileIntegrityChecker.Utilities;
using static MovFileIntegrityChecker.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker
{
    /// <summary>
    /// Main entry point for the MOV File Integrity Checker application.
    /// This class has been refactored to use a service-oriented architecture.
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== MOV File Integrity Checker ===\n");

            // Check if ffmpeg is installed, download if needed
            if (!await FfmpegHelper.EnsureFfmpegInstalledAsync())
            {
                return; // Exit if ffmpeg is not available
            }

            // Check for global analysis flag
            if (args.Any(a => a == "--global-analysis" || a == "-g"))
            {
                LegacyReportGenerators.RunGlobalAnalysis();
                return;
            }

            if (args.Length == 0)
            {
                // Interactive mode - show menu
                ShowMainMenu();
                return;
            }

            // Extract flags
            bool recursive = args.Any(a => a == "-r" || a == "--recursive");
            bool summaryOnly = args.Any(a => a == "-s" || a == "--summary");
            bool deleteEmpty = args.Any(a => a == "-d" || a == "--delete-empty");

            // Extract paths (everything that is not a flag)
            var paths = args.Where(a => !a.StartsWith("-")).ToArray();

            if (paths.Length == 0)
            {
                WriteError("No folder or file paths specified.");
                return;
            }

            // Run per-file analysis using the new orchestrator
            RunPerFileAnalysis(paths, recursive, summaryOnly, deleteEmpty);
        }

        private static void ShowMainMenu()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        MOV File Integrity Checker - Analysis Mode             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Please select an analysis mode:");
            Console.WriteLine();
            Console.WriteLine("  1. Per-File Analysis     - Analyze individual video files");
            Console.WriteLine("  2. Global Analysis       - Generate global report from JSON files");
            Console.WriteLine("  3. Both                  - Run per-file analysis then global report");
            Console.WriteLine("  4. Exit");
            Console.WriteLine();
            Console.Write("Enter your choice (1-4): ");

            string? choice = Console.ReadLine();
            Console.WriteLine();

            switch (choice?.Trim())
            {
                case "1":
                    RunPerFileAnalysisInteractive();
                    break;
                case "2":
                    LegacyReportGenerators.RunGlobalAnalysis();
                    break;
                case "3":
                    RunPerFileAnalysisInteractive();
                    Console.WriteLine("\n" + new string('=', 80));
                    Console.WriteLine("Starting Global Analysis...");
                    Console.WriteLine(new string('=', 80) + "\n");
                    LegacyReportGenerators.RunGlobalAnalysis();
                    break;
                case "4":
                    Console.WriteLine("Exiting...");
                    return;
                default:
                    WriteError("Invalid choice. Please run the program again.");
                    break;
            }
        }

        private static void RunPerFileAnalysisInteractive()
        {
            Console.Write("Enter the path to a file or folder (default: DemoFiles\\Broken.mp4): ");
            string? path = Console.ReadLine()?.Trim().Trim('"');

            if (string.IsNullOrEmpty(path))
            {
                // Use relative path to DemoFiles (relative to bin output directory)
                path = Path.Combine("..", "..", "..", "DemoFiles", "Broken.mp4");
                Console.WriteLine($"Using default path: {path}");
            }

            Console.Write("Recursive search? (y/n, default: y): ");
            bool recursive = Console.ReadLine()?.Trim().ToLower() != "n";

            Console.Write("Delete empty folders? (y/n, default: n): ");
            bool deleteEmpty = Console.ReadLine()?.Trim().ToLower() == "y";

            Console.WriteLine();

            RunPerFileAnalysis(new[] { path }, recursive, summaryOnly: false, deleteEmpty);
        }

        private static void RunPerFileAnalysis(string[] paths, bool recursive, bool summaryOnly, bool deleteEmpty)
        {
            var orchestrator = new AnalysisOrchestrator();
            
            // Use legacy HTML report generator as a callback
            var results = orchestrator.AnalyzePaths(
                paths, 
                recursive, 
                summaryOnly, 
                deleteEmpty,
                htmlReportGenerator: LegacyReportGenerators.CreateErrorReport
            );

            orchestrator.PrintSummary(results, summaryOnly);
        }
    }
}


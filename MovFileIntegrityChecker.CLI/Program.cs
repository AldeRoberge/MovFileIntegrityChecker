// Main entry point for the MovFileIntegrityChecker application.
// Handles user input and orchestration of analysis services.

using MovFileIntegrityChecker.CLI.Services;
using MovFileIntegrityChecker.Core.Services;
using MovFileIntegrityChecker.Core.Utilities;
using MovFileIntegrityChecker.CLI.Utilities;
using static MovFileIntegrityChecker.CLI.Utilities.ConsoleHelper;

namespace MovFileIntegrityChecker.CLI
{
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
            // Load user preferences
            var preferences = UserPreferences.Load();


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
            Console.Write($"Enter your choice (1-4, default: {preferences.LastMenuChoice}): ");

            string? input = Console.ReadLine();
            string choice = string.IsNullOrWhiteSpace(input) ? preferences.LastMenuChoice : input.Trim();
            Console.WriteLine();

            // Save the choice for next time
            preferences.LastMenuChoice = choice;
            preferences.Save();

            switch (choice)
            {
                case "1":
                    RunPerFileAnalysisInteractive(preferences);
                    break;
                case "2":
                    LegacyReportGenerators.RunGlobalAnalysis();
                    break;
                case "3":
                    RunPerFileAnalysisInteractive(preferences);
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

        private static void RunPerFileAnalysisInteractive(UserPreferences preferences)
        {
            // Determine default path to show
            string defaultPath = string.IsNullOrEmpty(preferences.LastPath)
                ? Path.Combine("..", "..", "..", "DemoFiles", "Broken.mp4")
                : preferences.LastPath;


            Console.Write($"Enter the path to a file or folder (default: {defaultPath}): ");
            string? pathInput = Console.ReadLine()?.Trim().Trim('"');

            string path;
            if (string.IsNullOrEmpty(pathInput))
            {
                path = defaultPath;
                Console.WriteLine($"Using default path: {path}");
            }
            else
            {
                path = pathInput;
            }

            Console.Write($"Recursive search? (y/n, default: {(preferences.LastRecursive ? "y" : "n")}): ");
            string? recursiveInput = Console.ReadLine()?.Trim().ToLower();
            bool recursive = string.IsNullOrEmpty(recursiveInput)
                ? preferences.LastRecursive
                : recursiveInput == "y";

            Console.Write($"Delete empty folders? (y/n, default: {(preferences.LastDeleteEmpty ? "y" : "n")}): ");
            string? deleteEmptyInput = Console.ReadLine()?.Trim().ToLower();
            bool deleteEmpty = string.IsNullOrEmpty(deleteEmptyInput)
                ? preferences.LastDeleteEmpty
                : deleteEmptyInput == "y";

            Console.WriteLine();

            // Save preferences for next time
            preferences.LastPath = path;
            preferences.LastRecursive = recursive;
            preferences.LastDeleteEmpty = deleteEmpty;
            preferences.Save();

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
using MovFileIntegrityChecker.Core;
using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Services;
using MovFileIntegrityChecker.Core.Utilities;

namespace MovFileIntegrityChecker.Web.Services
{
    public class ScannerService
    {
        private CancellationTokenSource? _cts;
        private Task? _currentScanTask;
        private readonly LogService _logService;

        public bool IsScanning => _currentScanTask != null && !_currentScanTask.IsCompleted;
        public string Status { get; private set; } = "Ready";
        public event Action? OnStatusChanged;
        public event Action<FileCheckResult>? OnResultAdded;

        // Auto-scan properties
        private System.Threading.Timer? _autoScanTimer;
        public bool IsAutoScanEnabled { get; private set; }
        public int AutoScanIntervalHours { get; private set; } = 24;
        public DateTime? NextAutoScanTime { get; private set; }

        public ScannerService(LogService logService)
        {
            _logService = logService;
        }

        public System.Collections.Concurrent.ConcurrentQueue<FileCheckResult> RecentResults { get; } = new();

        public void ClearResults()
        {
            RecentResults.Clear();
            OnStatusChanged?.Invoke();
        }

        public async Task StartScanAsync(string path, bool recursive = true)
        {
            if (IsScanning)
            {
                ConsoleHelper.WriteWarning("Scan already in progress.");
                return;
            }

            _cts = new CancellationTokenSource();
            Status = "Scanning...";
            OnStatusChanged?.Invoke();

            _currentScanTask = Task.Run(() => RunScan(path, recursive, _cts.Token));
            await Task.CompletedTask; // Return immediately to let UI update
        }

        public void StopScan()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                Status = "Stopping...";
                ConsoleHelper.WriteWarning("Stopping scan...");
                OnStatusChanged?.Invoke();
            }
        }

        private void RunScan(string path, bool recursive, CancellationToken token)
        {
            try
            {
                var orchestrator = new AnalysisOrchestrator();

                // We use a callback that respects the cancellation token if possible
                // Note: The current AnalysisOrchestrator doesn't accept a CancellationToken directly in AnalyzePaths
                // but we can wrap the execution somewhat.
                // For a proper implementation, we should update AnalysisOrchestrator to accept CancellationToken.
                // For now, we rely on the fact that it iterates files, so we might need to modify Core later.
                // However, without modifying Core right now, we just run it.
                // Wait! AnalysisOrchestrator is synchronous. We can't cancel it easily unless we modify it.
                // Refactoring AnalysisOrchestrator to be async or accept CT is a good next step, 
                // but let's stick to the plan.

                // Create a wrapper for the report generator
                Action<FileCheckResult> reportGenerator = (result) =>
                {
                    if (token.IsCancellationRequested) return;
                    LegacyReportGenerator.CreateErrorReport(result);
                    LegacyReportGenerator.CreateJsonReport(result);

                    RecentResults.Enqueue(result);
                    OnResultAdded?.Invoke(result);
                    OnStatusChanged?.Invoke();
                };

                ConsoleHelper.WriteInfo($"Starting scan on: {path}");

                // TODO: Update AnalysisOrchestrator to be async/cancellable
                var results = orchestrator.AnalyzePaths(
                    new[] { path },
                    recursive,
                    summaryOnly: false,
                    deleteEmpty: false,
                    htmlReportGenerator: reportGenerator
                );

                if (token.IsCancellationRequested)
                {
                    ConsoleHelper.WriteWarning("Scan cancelled.");
                    Status = "Cancelled";
                }
                else
                {
                    ConsoleHelper.WriteSuccess("Scan completed successfully.");
                    Status = "Completed";
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Scan failed: {ex.Message}");
                Status = "Error";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                OnStatusChanged?.Invoke();
            }
        }

        public void ToggleAutoScan(bool enable, int intervalHours, string path)
        {
            IsAutoScanEnabled = enable;
            AutoScanIntervalHours = intervalHours;

            if (IsAutoScanEnabled)
            {
                // Schedule next scan
                ScheduleNextScan(path);
                ConsoleHelper.WriteInfo($"Auto-scan enabled. Next scan in {intervalHours} hours.");
            }
            else
            {
                _autoScanTimer?.Dispose();
                _autoScanTimer = null;
                NextAutoScanTime = null;
                ConsoleHelper.WriteInfo("Auto-scan disabled.");
            }
            OnStatusChanged?.Invoke();
        }

        public void UpdateAutoScanInterval(int intervalHours, string path)
        {
            if (AutoScanIntervalHours == intervalHours) return; // No change

            AutoScanIntervalHours = intervalHours;
            if (IsAutoScanEnabled)
            {
                ConsoleHelper.WriteInfo($"Auto-scan interval updated to {intervalHours} hours.");
                ScheduleNextScan(path);
            }
            OnStatusChanged?.Invoke();
        }

        private void ScheduleNextScan(string path)
        {
            _autoScanTimer?.Dispose();
            NextAutoScanTime = DateTime.Now.AddHours(AutoScanIntervalHours);

            var delay = NextAutoScanTime.Value - DateTime.Now;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            _autoScanTimer = new System.Threading.Timer(async _ =>
            {
                if (!IsScanning)
                {
                    ConsoleHelper.WriteInfo("Starting scheduled auto-scan...");
                    await StartScanAsync(path);
                    ScheduleNextScan(path); // Re-schedule
                }
                else
                {
                    ConsoleHelper.WriteWarning("Skipping auto-scan because a scan is already in progress.");
                    ScheduleNextScan(path); // Try again later
                }
            }, null, delay, Timeout.InfiniteTimeSpan);

            OnStatusChanged?.Invoke();
        }
    }
}

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
        private readonly LocalizationService _localizer;

        public bool IsScanning => _currentScanTask != null && !_currentScanTask.IsCompleted;
        public string Status { get; private set; } = "";
        public event Action? OnStatusChanged;
        public event Action<FileCheckResult>? OnResultAdded;

        // Auto-scan properties
        private System.Threading.Timer? _autoScanTimer;
        public bool IsAutoScanEnabled { get; private set; }
        public int AutoScanIntervalHours { get; private set; } = 24;
        public DateTime? NextAutoScanTime { get; private set; }

        public ScannerService(LogService logService, LocalizationService localizer)
        {
            _logService = logService;
            _localizer = localizer;
            Status = _localizer["Ready"];
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
            Status = _localizer["Scanning"];
            OnStatusChanged?.Invoke();

            _currentScanTask = Task.Run(() => RunScan(path, recursive, _cts.Token));
            await Task.CompletedTask; // Return immediately to let UI update
        }

        public void StopScan()
        {
            if (_cts is { IsCancellationRequested: false })
            {
                _cts.Cancel();
                Status = _localizer["Stopping"];
                ConsoleHelper.WriteWarning("Stopping scan...");
                OnStatusChanged?.Invoke();
            }
        }

        private void RunScan(string path, bool recursive, CancellationToken token)
        {
            try
            {
                var orchestrator = new AnalysisOrchestrator();

                // Create a wrapper for the report generator (only called for files with issues)
                Action<FileCheckResult> reportGenerator = (result) =>
                {
                    if (token.IsCancellationRequested) return;
                    LegacyReportGenerator.CreateErrorReport(result);
                    LegacyReportGenerator.CreateJsonReport(result);
                };

                // Create a streaming callback that adds results as they're generated
                Action<FileCheckResult> streamingCallback = (result) =>
                {
                    if (token.IsCancellationRequested) return;
                    
                    RecentResults.Enqueue(result);
                    OnResultAdded?.Invoke(result);
                };

                ConsoleHelper.WriteInfo($"Starting scan on: {path}");

                // Use the new onResultCallback parameter for real-time streaming
                orchestrator.AnalyzePaths(
                    new[] { path },
                    recursive,
                    summaryOnly: false,
                    deleteEmpty: false,
                    htmlReportGenerator: reportGenerator,
                    onResultCallback: streamingCallback
                );

                if (token.IsCancellationRequested)
                {
                    ConsoleHelper.WriteWarning("Scan cancelled.");
                    Status = _localizer["Cancelled"];
                }
                else
                {
                    ConsoleHelper.WriteSuccess("Scan completed successfully.");
                    Status = _localizer["Completed"];
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Scan failed: {ex.Message}");
                Status = _localizer["Error"];
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

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
        private readonly AjaFilesService _ajaFilesService;

        public bool IsScanning => _currentScanTask != null && !_currentScanTask.IsCompleted;
        public string Status { get; private set; } = "";
        public event Action? OnStatusChanged;
        public event Action<FileCheckResult>? OnResultAdded;

        // Auto-scan properties
        private System.Threading.Timer? _autoScanTimer;
        public bool IsAutoScanEnabled { get; private set; }
        public int AutoScanIntervalHours { get; private set; } = 24;
        public DateTime? NextAutoScanTime { get; private set; }

        // Dictionary to lookup AJA download URLs by file path
        private Dictionary<string, (string DownloadUrl, string ServerName)> _ajaFileMap = new();

        public ScannerService(LogService logService, LocalizationService localizer, AjaFilesService ajaFilesService)
        {
            _logService = logService;
            _localizer = localizer;
            _ajaFilesService = ajaFilesService;
            Status = _localizer["Ready"];
        }

        public System.Collections.Concurrent.ConcurrentQueue<FileCheckResult> RecentResults { get; } = new();

        // Custom report folder settings
        public string? CustomReportFolder { get; set; }
        public bool UseCustomReportFolder { get; set; }

        public void ClearResults()
        {
            RecentResults.Clear();
            OnStatusChanged?.Invoke();
        }

        public async Task StartScanAsync(string path, bool recursive = true, string? customReportFolder = null)
        {
            if (IsScanning)
            {
                ConsoleHelper.WriteWarning("Scan already in progress.");
                return;
            }

            _cts = new CancellationTokenSource();
            Status = _localizer["Scanning"];
            OnStatusChanged?.Invoke();

            // Try to load AJA files with timeout (don't block scan if it hangs)
            try
            {
                if (!_ajaFilesService.FileStatuses.Any())
                {
                    ConsoleHelper.WriteInfo("Loading AJA server information (with 10s timeout)...");

                    var ajaLoadTask = _ajaFilesService.StartScanAsync();
                    var completedTask = await Task.WhenAny(ajaLoadTask, Task.Delay(10000));

                    if (completedTask == ajaLoadTask)
                    {
                        // Wait for the scan to complete (with another timeout)
                        var startWait = DateTime.Now;
                        while (_ajaFilesService.IsScanning && (DateTime.Now - startWait).TotalSeconds < 30)
                        {
                            await Task.Delay(500);
                        }

                        if (_ajaFilesService.IsScanning)
                        {
                            ConsoleHelper.WriteWarning("AJA scan timed out after 30s - continuing without AJA data");
                        }
                    }
                    else
                    {
                        ConsoleHelper.WriteWarning("AJA scan start timed out - continuing without AJA data");
                    }
                }

                // Build AJA file lookup map (even if partial data)
                _ajaFileMap.Clear();
                foreach (var status in _ajaFilesService.FileStatuses)
                {
                    // Use filename as key for more robust matching regardless of local path
                    _ajaFileMap[status.Clip.ClipName] = (status.Clip.DownloadUrl, status.Clip.ServerName);
                }

                if (_ajaFileMap.Any())
                {
                    ConsoleHelper.WriteInfo($"Loaded {_ajaFileMap.Count} AJA file references");
                }
                else
                {
                    ConsoleHelper.WriteInfo("No AJA file references loaded - continuing without download links");
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteWarning($"Failed to load AJA data: {ex.Message} - continuing without download links");
            }

            _currentScanTask = Task.Run(() => RunScan(path, recursive, customReportFolder, _cts.Token));
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

        private void RunScan(string path, bool recursive, string? customReportFolder, CancellationToken token)
        {
            try
            {
                var orchestrator = new AnalysisOrchestrator();

                // Create a wrapper for the report generator (only called for files with issues)
                Action<FileCheckResult> reportGenerator = (result) =>
                {
                    if (token.IsCancellationRequested) return;

                    // Enrich with AJA info before generating the report
                    var fileName = Path.GetFileName(result.FilePath);
                    if (_ajaFileMap.TryGetValue(fileName, out var ajaInfo))
                    {
                        result.AjaDownloadUrl = ajaInfo.DownloadUrl;
                        result.AjaServerName = ajaInfo.ServerName;
                    }

                    LegacyReportGenerator.CreateErrorReport(result, customReportFolder);
                    LegacyReportGenerator.CreateJsonReport(result, customReportFolder);
                };

                // Create a streaming callback that adds results as they're generated
                Action<FileCheckResult> streamingCallback = (result) =>
                {
                    if (token.IsCancellationRequested) return;

                    // Check if this file is from an AJA server and populate download info
                    var fileName = Path.GetFileName(result.FilePath);
                    if (_ajaFileMap.TryGetValue(fileName, out var ajaInfo))
                    {
                        result.AjaDownloadUrl = ajaInfo.DownloadUrl;
                        result.AjaServerName = ajaInfo.ServerName;
                    }

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

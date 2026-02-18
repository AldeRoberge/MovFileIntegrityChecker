using MovFileIntegrityChecker.Core.Models;
using MovFileIntegrityChecker.Core.Services;
using MovFileIntegrityChecker.Core.Utilities;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MovFileIntegrityChecker.Web.Services
{
    public class AjaFilesService
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logService;
        private readonly LocalizationService _localizer;
        private CancellationTokenSource? _cts;
        private Task? _currentScanTask;

        public bool IsScanning => _currentScanTask != null && !_currentScanTask.IsCompleted;
        public string Status { get; private set; } = "";
        public event Action? OnStatusChanged;
        public event Action<AjaFileStatus>? OnFileStatusAdded;

        public ConcurrentBag<AjaFileStatus> FileStatuses { get; } = new();

        // Default AJA servers
        public List<AjaServer> AjaServers { get; set; } = new()
        {
            new() { Name = "D421-Master", Url = "http://10.42.0.112/", Type = "Master" },
            new() { Name = "D421-Backup", Url = "http://10.42.0.113/", Type = "Backup" },
            new() { Name = "D402-Master", Url = "http://10.42.0.14/", Type = "Master" },
            new() { Name = "D402-Backup", Url = "http://10.42.0.15/", Type = "Backup" },
            new() { Name = "D404-BU1", Url = "http://10.42.0.120/", Type = "Backup" },
            new() { Name = "D404-BU2", Url = "http://10.42.0.121/", Type = "Backup" },
            new() { Name = "D404-BU3", Url = "http://10.42.0.122/", Type = "Backup" },
            new() { Name = "D404-BU4", Url = "http://10.42.0.123/", Type = "Backup" },
            new() { Name = "D404-MA1-Master", Url = "http://10.42.0.116/", Type = "Master" },
            new() { Name = "D404-MA2", Url = "http://10.42.0.117/", Type = "Master" },
            new() { Name = "D404-MA3", Url = "http://10.42.0.118/", Type = "Master" },
            new() { Name = "D404-MA4", Url = "http://10.42.0.119/", Type = "Master" }
        };

        // Default local scan folders
        public List<string> LocalScanFolders { get; set; } = new()
        {
            @"T:\SPT\SP\Mont\Prod1\2_COU\_DL",
            @"T:\SPT\SP\Mont\Prod2\2_COU\_DL",
            @"T:\SPT\SP\Mont\Backup\2_COU\_DL"
        };

        public AjaFilesService(IHttpClientFactory httpClientFactory, LogService logService, LocalizationService localizer)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _logService = logService;
            _localizer = localizer;
            Status = _localizer["Ready"];
        }

        public async Task StartScanAsync()
        {
            if (IsScanning)
            {
                ConsoleHelper.WriteWarning("AJA scan already in progress.");
                return;
            }

            _cts = new CancellationTokenSource();
            Status = _localizer["ScanningAjaServers"];
            OnStatusChanged?.Invoke();

            FileStatuses.Clear();

            _currentScanTask = Task.Run(() => RunScanAsync(_cts.Token));
            await Task.CompletedTask;
        }

        public void StopScan()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                Status = _localizer["Stopping"];
                ConsoleHelper.WriteWarning("Stopping AJA scan...");
                OnStatusChanged?.Invoke();
            }
        }

        private async Task RunScanAsync(CancellationToken token)
        {
            try
            {
                ConsoleHelper.WriteInfo($"Scanning {AjaServers.Count} AJA servers...");

                // Fetch clips from all servers in parallel
                var tasks = AjaServers.Select(server => FetchClipsFromServerAsync(server, token)).ToArray();
                var results = await Task.WhenAll(tasks);

                if (token.IsCancellationRequested)
                {
                    Status = _localizer["Cancelled"];
                    OnStatusChanged?.Invoke();
                    return;
                }

                // Flatten all clips
                var allClips = results.SelectMany(clips => clips).ToList();
                ConsoleHelper.WriteSuccess($"Found {allClips.Count} total clips across all servers.");

                // Cross-reference with local files
                await CrossReferenceWithLocalFilesAsync(allClips, token);

                Status = _localizer["Completed"];
                ConsoleHelper.WriteSuccess("AJA scan completed successfully.");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"AJA scan failed: {ex.Message}");
                Status = _localizer["Error"];
            }
            finally
            {
                OnStatusChanged?.Invoke();
            }
        }

        private async Task<List<AjaClip>> FetchClipsFromServerAsync(AjaServer server, CancellationToken token)
        {
            var clips = new List<AjaClip>();

            try
            {
                ConsoleHelper.WriteInfo($"Fetching clips from {server.Name}...");

                var url = $"{server.Url}clips";
                var response = await _httpClient.GetStringAsync(url, token);

                // Parse JavaScript array format
                clips = ParseAjaClipsResponse(response, server);

                ConsoleHelper.WriteSuccess($"{server.Name}: Found {clips.Count} clips");
            }
            catch (TaskCanceledException)
            {
                ConsoleHelper.WriteWarning($"{server.Name}: Request cancelled");
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteWarning($"{server.Name}: Connection failed - {ex.Message}");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"{server.Name}: Error - {ex.Message}");
            }

            return clips;
        }

        private List<AjaClip> ParseAjaClipsResponse(string response, AjaServer server)
        {
            var clips = new List<AjaClip>();

            try
            {
                // Parse JavaScript object array format:
                // { clipname: "file.mov", timestamp: "02/17/26 12:07:26", fourcc: "apcs", ... }
                var pattern = @"\{\s*clipname:\s*""([^""]+)""\s*,\s*timestamp:\s*""([^""]+)""\s*,\s*fourcc:\s*""([^""]+)""\s*,\s*width:\s*""([^""]+)""\s*,\s*height:\s*""([^""]+)""\s*,\s*framecount:\s*""([^""]+)""\s*,\s*framerate:\s*""([^""]+)""\s*,\s*interlace:\s*""([^""]+)""\s*\}";

                var matches = Regex.Matches(response, pattern);

                foreach (Match match in matches)
                {
                    clips.Add(new AjaClip
                    {
                        ClipName = match.Groups[1].Value,
                        Timestamp = match.Groups[2].Value,
                        FourCC = match.Groups[3].Value,
                        Width = match.Groups[4].Value,
                        Height = match.Groups[5].Value,
                        FrameCount = match.Groups[6].Value,
                        FrameRate = match.Groups[7].Value,
                        Interlace = match.Groups[8].Value,
                        ServerName = server.Name,
                        ServerUrl = server.Url
                    });
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to parse clips response: {ex.Message}");
            }

            return clips;
        }

        private async Task CrossReferenceWithLocalFilesAsync(List<AjaClip> clips, CancellationToken token)
        {
            ConsoleHelper.WriteInfo("Cross-referencing with local files...");

            // Build a dictionary of local files for faster lookup
            var localFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in LocalScanFolders)
            {
                if (!Directory.Exists(folder))
                {
                    ConsoleHelper.WriteWarning($"Folder not found: {folder}");
                    continue;
                }

                try
                {
                    // Only scan top-level files (not recursive) to avoid hanging
                    var files = Directory.GetFiles(folder, "*.mov", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        localFiles.TryAdd(fileName, file);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteError($"Error scanning folder {folder}: {ex.Message}");
                }
            }

            ConsoleHelper.WriteInfo($"Found {localFiles.Count} local .mov files");

            // Process each clip
            await Task.Run(() =>
            {
                foreach (var clip in clips)
                {
                    if (token.IsCancellationRequested) break;

                    var status = new AjaFileStatus
                    {
                        Clip = clip,
                        ExistsLocally = localFiles.TryGetValue(clip.ClipName, out var localPath),
                        LocalPath = localPath
                    };

                    // If file exists locally, we could optionally check if it's corrupted
                    // For now, we'll just mark it as existing
                    if (status.ExistsLocally)
                    {
                        // You can add integrity check here if needed
                        status.IsCorrupted = null; // Unknown for now
                    }

                    FileStatuses.Add(status);
                    OnFileStatusAdded?.Invoke(status);
                }
            }, token);

            var missingCount = FileStatuses.Count(s => !s.ExistsLocally);
            ConsoleHelper.WriteInfo($"Cross-reference complete. {missingCount} clips missing locally.");
        }

        public void ClearResults()
        {
            FileStatuses.Clear();
            OnStatusChanged?.Invoke();
        }
    }
}
using System.Collections.Concurrent;
using MovFileIntegrityChecker.Core.Utilities;

namespace MovFileIntegrityChecker.Web.Services
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = string.Empty;
        public ConsoleColor Color { get; set; } = ConsoleColor.White;
        public string CssClass => Color switch
        {
            ConsoleColor.Red => "text-danger",
            ConsoleColor.Yellow => "text-warning",
            ConsoleColor.Green => "text-success",
            ConsoleColor.Cyan => "text-info",
            _ => "text-secondary"
        };
    }

    public class LogService : IDisposable
    {
        private readonly ConcurrentQueue<LogEntry> _logs = new();
        private const int MaxLogs = 1000;
        
        public event Action? OnLogAdded;

        public LogService()
        {
            // Subscribe to the Core ConsoleHelper events
            ConsoleHelper.OnLog += HandleLog;
        }

        private void HandleLog(string message, ConsoleColor color)
        {
            var entry = new LogEntry { Message = message, Color = color };
            _logs.Enqueue(entry);
            
            // Keep the buffer size under control
            while (_logs.Count > MaxLogs)
            {
                _logs.TryDequeue(out _);
            }

            OnLogAdded?.Invoke();
        }

        public List<LogEntry> GetLogs()
        {
            return _logs.ToList();
        }

        public void Clear()
        {
            _logs.Clear();
            OnLogAdded?.Invoke();
        }

        public void Dispose()
        {
            ConsoleHelper.OnLog -= HandleLog;
        }
    }
}

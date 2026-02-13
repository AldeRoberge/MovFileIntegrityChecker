// Just some simple helpers to make console output look pretty with colors.
// Because staring at plain white text is boring, and color-coded messages
// make it way easier to spot errors and successes at a glance.

namespace MovFileIntegrityChecker.Core.Utilities
{
    public static class ConsoleHelper
    {
        public static event Action<string, ConsoleColor>? OnLog;

        public static void WriteWarning(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
            OnLog?.Invoke(message, ConsoleColor.Yellow);
        }

        public static void WriteSuccess(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
            OnLog?.Invoke(message, ConsoleColor.Green);
        }

        public static void WriteError(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
            OnLog?.Invoke(message, ConsoleColor.Red);
        }

        public static void WriteInfo(string message)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ForegroundColor = oldColor;
            OnLog?.Invoke(message, ConsoleColor.Cyan);
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "00:00:00";

            int hours = (int)(seconds / 3600);
            int minutes = (int)((seconds % 3600) / 60);
            int secs = (int)(seconds % 60);

            return $"{hours:D2}:{minutes:D2}:{secs:D2}";
        }
    }
}


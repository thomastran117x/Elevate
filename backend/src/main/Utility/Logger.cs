using System.Net;

namespace backend.main.Utilities
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    public sealed class LoggerOptions
    {
        public bool EnableFileLogging { get; set; } = true;

        public LogLevel MinFileLevel { get; set; } = LogLevel.Warn;

        public string? LogDirectory
        {
            get; set;
        }

        public string FileNamePattern { get; set; } = "log_{date}.txt";

        public bool UseUtcTimestamps { get; set; } = true;
    }

    public static class Logger
    {
        private static readonly object _consoleLock = new();
        private static readonly object _fileLock = new();
        private static volatile bool _fileLoggingFailed;
        private static LoggerOptions _options = new();
        private static string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        static Logger()
        {
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        public static void Configure(Action<LoggerOptions> configure)
        {
            var opts = new LoggerOptions();
            configure(opts);

            _options = opts;
            _logDirectory = string.IsNullOrWhiteSpace(opts.LogDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
                : opts.LogDirectory;

            if (_options.EnableFileLogging && !Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        public static void Debug(string message) => Write(LogLevel.Debug, message, null);
        public static void Info(string message) => Write(LogLevel.Info, message, null);
        public static void Warn(string message) => Write(LogLevel.Warn, message, null);
        public static void Error(string message) => Write(LogLevel.Error, message, null);

        public static void Warn(Exception ex, string? message = null) => Write(LogLevel.Warn, message, ex);
        public static void Error(Exception ex, string? message = null) => Write(LogLevel.Error, message, ex);

        public static void Log(LogLevel level, string message) => Write(level, message, null);
        public static void Log(LogLevel level, Exception ex, string? message = null) => Write(level, message, ex);

        private static void Write(LogLevel level, string? message, Exception? ex)
        {
            var now = _options.UseUtcTimestamps ? DateTime.UtcNow : DateTime.Now;
            var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelText = level.ToString().ToUpper().PadRight(5);
            var line = $"[{timestamp}] [{levelText}] {message ?? string.Empty}".TrimEnd();

            if (ex != null)
            {
                line += $"{Environment.NewLine}{ex.GetType().FullName}: {ex.Message}";
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    line += $"{Environment.NewLine}{ex.StackTrace}";
            }

            lock (_consoleLock)
            {
                var original = Console.ForegroundColor;

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[{timestamp}] ");

                Console.ForegroundColor = LevelColor(level);
                Console.Write($"[{levelText}] ");

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(message ?? string.Empty);

                if (ex != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"{ex.GetType().FullName}: {ex.Message}");
                    if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                        Console.WriteLine(ex.StackTrace);
                }

                Console.ForegroundColor = original;
            }

            if (_options.EnableFileLogging && level >= _options.MinFileLevel)
                if (_options.EnableFileLogging && level >= _options.MinFileLevel && !_fileLoggingFailed)
                {
                    try
                    {
                        var path = GetLogFilePath(now);

                        lock (_fileLock)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            File.AppendAllText(path, line + Environment.NewLine);
                        }
                    }
                    catch (Exception fileEx)
                    {
                        _fileLoggingFailed = true;

                        // Fallback: console-only warning (do NOT re-log through Logger)
                        lock (_consoleLock)
                        {
                            var original = Console.ForegroundColor;
                            Console.ForegroundColor = ConsoleColor.Red;

                            Console.WriteLine(
                                $"[LOGGER] File logging disabled due to error: {fileEx.GetType().Name} - {fileEx.Message}"
                            );

                            Console.ForegroundColor = original;
                        }
                    }
                }

        }

        private static ConsoleColor LevelColor(LogLevel level) => level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.Cyan,
            LogLevel.Warn => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

        private static string GetLogFilePath(DateTime now)
        {
            var dateToken = now.ToString("yyyyMMdd");
            var fileName = _options.FileNamePattern.Replace("{date}", dateToken);
            return Path.Combine(_logDirectory, fileName);
        }
    }
}

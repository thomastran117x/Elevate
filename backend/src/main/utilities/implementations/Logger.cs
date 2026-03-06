using backend.main.utilities.interfaces;

namespace backend.main.utilities.implementation
{
    using LogLevel = interfaces.LogLevel;
    public static class Logger
    {
        private static LoggerOptions _options = new();
        private static ICustomLogger? _instance;

        public static void Configure(Action<LoggerOptions> configure)
        {
            _options = new LoggerOptions();
            configure(_options);
        }

        public static LoggerOptions GetOptions() => _options;

        public static void SetInstance(ICustomLogger logger) => _instance = logger;

        public static void Debug(string message) => _instance?.Debug(message);
        public static void Info(string message) => _instance?.Info(message);
        public static void Warn(string message) => _instance?.Warn(message);
        public static void Error(string message) => _instance?.Error(message);
        public static void Warn(Exception ex, string? message = null) => _instance?.Warn(ex, message);
        public static void Error(Exception ex, string? message = null) => _instance?.Error(ex, message);
    }

    public sealed class FileLogger : ICustomLogger
    {
        private readonly object _consoleLock = new();
        private readonly object _fileLock = new();
        private volatile bool _fileLoggingFailed;
        private readonly LoggerOptions _options;
        private readonly string _logDirectory;

        public FileLogger(LoggerOptions? options = null)
        {
            _options = options ?? new LoggerOptions();

            _logDirectory = string.IsNullOrWhiteSpace(_options.LogDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
                : _options.LogDirectory;

            if (_options.EnableFileLogging && !Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        public void Debug(string message) => Write(LogLevel.Debug, message, null);
        public void Info(string message) => Write(LogLevel.Info, message, null);
        public void Warn(string message) => Write(LogLevel.Warn, message, null);
        public void Error(string message) => Write(LogLevel.Error, message, null);

        public void Warn(Exception ex, string? message = null)
            => Write(LogLevel.Warn, message, ex);

        public void Error(Exception ex, string? message = null)
            => Write(LogLevel.Error, message, ex);

        public void Log(LogLevel level, string message)
            => Write(level, message, null);

        public void Log(LogLevel level, Exception ex, string? message = null)
            => Write(level, message, ex);

        private void Write(LogLevel level, string? message, Exception? ex)
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

            WriteToConsole(timestamp, levelText, message, ex);

            if (_options.EnableFileLogging &&
                level >= _options.MinFileLevel &&
                !_fileLoggingFailed)
            {
                TryWriteToFile(now, line);
            }
        }

        private void WriteToConsole(
            string timestamp,
            string levelText,
            string? message,
            Exception? ex)
        {
            lock (_consoleLock)
            {
                var original = Console.ForegroundColor;

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[{timestamp}] ");

                Console.ForegroundColor = LevelColor(Enum.Parse<LogLevel>(levelText.Trim(), ignoreCase: true));
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
        }

        private void TryWriteToFile(DateTime now, string line)
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
            catch
            {
                _fileLoggingFailed = true;
            }
        }

        private ConsoleColor LevelColor(LogLevel level) => level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.Cyan,
            LogLevel.Warn => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

        private string GetLogFilePath(DateTime now)
        {
            var dateToken = now.ToString("yyyyMMdd");
            var fileName = _options.FileNamePattern.Replace("{date}", dateToken);
            return Path.Combine(_logDirectory, fileName);
        }
    }
}

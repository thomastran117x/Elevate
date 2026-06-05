using backend.main.shared.utilities.logger;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Utilities;

public class LoggerTests
{
    [Fact]
    public void Configure_ShouldReplaceOptions()
    {
        Logger.Configure(options =>
        {
            options.EnableFileLogging = true;
            options.MinFileLevel = LogLevel.Debug;
            options.LogDirectory = "custom-logs";
            options.UseUtcTimestamps = false;
            options.FileNamePattern = "events_{date}.log";
        });

        var options = Logger.GetOptions();

        options.EnableFileLogging.Should().BeTrue();
        options.MinFileLevel.Should().Be(LogLevel.Debug);
        options.LogDirectory.Should().Be("custom-logs");
        options.UseUtcTimestamps.Should().BeFalse();
        options.FileNamePattern.Should().Be("events_{date}.log");
    }

    [Fact]
    public void StaticLoggerMethods_ShouldDelegateToConfiguredInstance()
    {
        var logger = new RecordingLogger();
        Logger.SetInstance(logger);

        var warning = new InvalidOperationException("warned");
        var error = new ApplicationException("failed");

        Logger.Debug("debug");
        Logger.Info("info");
        Logger.Warn("warn");
        Logger.Error("error");
        Logger.Warn(warning, "warn-ex");
        Logger.Error(error, "error-ex");

        logger.Messages.Should().Equal(
            "Debug:debug",
            "Info:info",
            "Warn:warn",
            "Error:error",
            "WarnEx:warn-ex:InvalidOperationException",
            "ErrorEx:error-ex:ApplicationException");
    }

    [Fact]
    public void FileLogger_ShouldWriteConsoleAndFile_ForEligibleLevels()
    {
        var tempDirectory = CreateTempDirectory();
        var originalOut = Console.Out;
        var console = new StringWriter();

        try
        {
            Console.SetOut(console);

            var logger = new FileLogger(new LoggerOptions
            {
                EnableFileLogging = true,
                MinFileLevel = LogLevel.Info,
                LogDirectory = tempDirectory,
                UseUtcTimestamps = true,
                FileNamePattern = "events_{date}.log"
            });

            var ex = new InvalidOperationException("disk full");
            logger.Info("ready");
            logger.Error(ex, "write failed");

            var file = Directory.GetFiles(tempDirectory).Single();
            var fileContents = File.ReadAllText(file);
            var consoleOutput = NormalizeLineEndings(console.ToString());

            Path.GetFileName(file).Should().MatchRegex(@"^events_\d{8}\.log$");
            fileContents.Should().Contain("[INFO ] ready");
            fileContents.Should().Contain("[ERROR] write failed");
            fileContents.Should().Contain("System.InvalidOperationException: disk full");

            consoleOutput.Should().Contain("[INFO ] ready");
            consoleOutput.Should().Contain("[ERROR] write failed");
            consoleOutput.Should().Contain("System.InvalidOperationException: disk full");
        }
        finally
        {
            Console.SetOut(originalOut);
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    [Fact]
    public void FileLogger_ShouldRespectMinFileLevel()
    {
        var tempDirectory = CreateTempDirectory();
        var originalOut = Console.Out;
        var console = new StringWriter();

        try
        {
            Console.SetOut(console);

            var logger = new FileLogger(new LoggerOptions
            {
                EnableFileLogging = true,
                MinFileLevel = LogLevel.Warn,
                LogDirectory = tempDirectory,
                FileNamePattern = "threshold_{date}.log"
            });

            logger.Debug("too low");
            logger.Warn("persist me");

            var file = Directory.GetFiles(tempDirectory).Single();
            var fileContents = File.ReadAllText(file);

            fileContents.Should().NotContain("too low");
            fileContents.Should().Contain("persist me");
            NormalizeLineEndings(console.ToString()).Should().Contain("too low");
        }
        finally
        {
            Console.SetOut(originalOut);
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    [Fact]
    public void FileLogger_ShouldSwallowFileWriteFailures_AndContinueLoggingToConsole()
    {
        var tempDirectory = CreateTempDirectory();
        var originalOut = Console.Out;
        var console = new StringWriter();

        try
        {
            Console.SetOut(console);
            File.WriteAllText(Path.Combine(tempDirectory, "existing-file"), "content");

            var logger = new FileLogger(new LoggerOptions
            {
                EnableFileLogging = true,
                MinFileLevel = LogLevel.Debug,
                LogDirectory = tempDirectory,
                FileNamePattern = "existing-file\\nested_{date}.log"
            });

            logger.Warn("first warning");
            logger.Error("second warning");

            NormalizeLineEndings(console.ToString()).Should().Contain("first warning");
            NormalizeLineEndings(console.ToString()).Should().Contain("second warning");
            Directory.GetFiles(tempDirectory, "*.log", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            Console.SetOut(originalOut);
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "event-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n");

    private sealed class RecordingLogger : ICustomLogger
    {
        public List<string> Messages { get; } = [];

        public void Debug(string message) => Messages.Add($"Debug:{message}");

        public void Info(string message) => Messages.Add($"Info:{message}");

        public void Warn(string message) => Messages.Add($"Warn:{message}");

        public void Error(string message) => Messages.Add($"Error:{message}");

        public void Warn(Exception ex, string? message = null) =>
            Messages.Add($"WarnEx:{message}:{ex.GetType().Name}");

        public void Error(Exception ex, string? message = null) =>
            Messages.Add($"ErrorEx:{message}:{ex.GetType().Name}");

        public void Log(LogLevel level, string message) =>
            Messages.Add($"{level}:{message}");

        public void Log(LogLevel level, Exception ex, string? message = null) =>
            Messages.Add($"{level}Ex:{message}:{ex.GetType().Name}");
    }
}

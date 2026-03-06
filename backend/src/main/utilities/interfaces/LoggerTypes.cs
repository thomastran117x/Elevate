namespace backend.main.utilities.interfaces
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public sealed class LoggerOptions
    {
        public bool EnableFileLogging
        {
            get; set;
        }
        public LogLevel MinFileLevel { get; set; } = LogLevel.Warn;
        public string LogDirectory { get; set; } = "";
        public bool UseUtcTimestamps { get; set; } = true;
        public string FileNamePattern { get; set; } = "log_{date}.txt";
    }
}

namespace backend.main.utilities.interfaces
{
    public interface ICustomLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);

        void Warn(Exception ex, string? message = null);
        void Error(Exception ex, string? message = null);

        void Log(LogLevel level, string message);
        void Log(LogLevel level, Exception ex, string? message = null);
    }
}

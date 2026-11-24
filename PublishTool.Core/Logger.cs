using System.Text;

namespace PublishTool;

public static class Logger
{
    private static readonly StringBuilder LogBuilder = new();
    private static readonly Lock Lock = new();
    private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PublishTool", "Logs");

    public static void Log(string message, LogLevel logLevel)
    {
        string levelTag = logLevel switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARNING",
            _ => "ERROR"
        };

        lock (Lock)
        {
            LogBuilder.AppendLine($"[{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{levelTag}] {message}");
        }
    }

    public static void LogDebug(string message) => Log(message, LogLevel.Debug);
    public static void LogInfo(string message) => Log(message, LogLevel.Info);
    public static void LogWarning(string message) => Log(message, LogLevel.Warning);
    public static void LogError(string message) => Log(message, LogLevel.Error);

    public static void Write(Action<string> report)
    {
        lock (Lock)
        {
            string log = LogBuilder.ToString();
            if (log.Length < 1)
            {
                return;
            }

            try
            {
                report("Log was written to file: " + WriteToFile(log));
            }
            catch (Exception e)
            {
                report("Failed to write log");
                report(e.ToString());
            }
        }
    }

    private static string WriteToFile(string log)
    {
        string path = Path.Combine(LogDir, $"{DateTimeOffset.Now:yyyy-MM-dd}.log");
        if (!Directory.Exists(LogDir))
        {
            Directory.CreateDirectory(LogDir);
        }

        using var file = File.Open(path, FileMode.OpenOrCreate);
        file.Seek(0, SeekOrigin.End);
        file.Write(Encoding.UTF8.GetBytes(log));
        file.Flush();
        return path;
    }
}

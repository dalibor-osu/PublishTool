using System.Reflection;

namespace CoreServiceApp;

public sealed class Heartbeat(string name) : IDisposable
{
    private Timer? _timer;

    public void Start()
    {
        _timer = new Timer(_ => Write(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Dispose() => _timer?.Dispose();

    private void Write()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(directory);
        string version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim();
        string build = typeof(Heartbeat).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "AppVersion")?.Value ?? "?";
        File.AppendAllText(Path.Combine(directory, "heartbeat.log"),
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {name} version.txt={version} build={build}{Environment.NewLine}");
    }
}

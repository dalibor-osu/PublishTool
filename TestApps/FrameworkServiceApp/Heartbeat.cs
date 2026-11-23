using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace FrameworkServiceApp
{
    public sealed class Heartbeat : IDisposable
    {
        private readonly string _name;
        private Timer _timer;

        public Heartbeat(string name)
        {
            _name = name;
        }

        public void Start()
        {
            _timer = new Timer(_ => Write(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private void Write()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string directory = Path.Combine(baseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            string version = File.ReadAllText(Path.Combine(baseDirectory, "version.txt")).Trim();
            string build = typeof(Heartbeat).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "AppVersion")?.Value ?? "?";
            File.AppendAllText(Path.Combine(directory, "heartbeat.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {_name} version.txt={version} build={build}{Environment.NewLine}");
        }
    }
}

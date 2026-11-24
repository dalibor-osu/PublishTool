using PublishTool.Deployment;

namespace PublishTool.Tests.Support;

public sealed class FakeHostController : IHostController
{
    private readonly HashSet<string> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    public bool IisInstalled { get; set; } = true;

    public bool IisRunning { get; set; } = true;

    public bool FailIisStart { get; set; }

    public Action? OnIisStart { get; set; }

    public List<string> Calls { get; } = [];

    public FakeHostController WithService(string name, bool running = true)
    {
        _services.Add(name);
        if (running)
        {
            _running.Add(name);
        }

        return this;
    }

    public bool IsIisInstalled() => IisInstalled;

    public bool IsIisRunning() => IisRunning;

    public bool ServiceExists(string name) => _services.Contains(name);

    public bool IsServiceRunning(string name) => _running.Contains(name);

    public Task StopIisAsync(Action<string> report, CancellationToken ct)
    {
        Calls.Add("stop IIS");
        IisRunning = false;
        return Task.CompletedTask;
    }

    public Task StartIisAsync(Action<string> report, CancellationToken ct)
    {
        Calls.Add("start IIS");
        OnIisStart?.Invoke();
        if (FailIisStart)
        {
            throw new DeployException("iisreset /start failed");
        }

        IisRunning = true;
        return Task.CompletedTask;
    }

    public Task StopServiceAsync(string name, Action<string> report, CancellationToken ct)
    {
        Calls.Add($"stop {name}");
        _running.Remove(name);
        return Task.CompletedTask;
    }

    public Task StartServiceAsync(string name, Action<string> report, CancellationToken ct)
    {
        Calls.Add($"start {name}");
        _running.Add(name);
        return Task.CompletedTask;
    }
}

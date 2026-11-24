namespace PublishTool.Deployment;

public interface IHostController
{
    public bool IsIisInstalled();

    public bool IsIisRunning();

    public bool ServiceExists(string name);

    public bool IsServiceRunning(string name);

    public Task StopIisAsync(Action<string> report, CancellationToken ct);

    public Task StartIisAsync(Action<string> report, CancellationToken ct);

    public Task StopServiceAsync(string name, Action<string> report, CancellationToken ct);

    public Task StartServiceAsync(string name, Action<string> report, CancellationToken ct);
}

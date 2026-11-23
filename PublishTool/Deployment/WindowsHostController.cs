using System.Runtime.Versioning;
using System.ServiceProcess;
using PublishTool.Helpers;

namespace PublishTool.Deployment;

[SupportedOSPlatform("windows")]
public sealed class WindowsHostController : IHostController
{
    private const string IisServiceName = "W3SVC";
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public bool IsIisInstalled() => ServiceExists(IisServiceName);

    public bool IsIisRunning() => IsServiceRunning(IisServiceName);

    public bool ServiceExists(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            _ = service.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool IsServiceRunning(string name)
    {
        using var service = new ServiceController(name);
        return service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending);
    }

    public Task StopIisAsync(Action<string> report, CancellationToken ct) => RunIisResetAsync("/stop", report, ct);

    public Task StartIisAsync(Action<string> report, CancellationToken ct) => RunIisResetAsync("/start", report, ct);

    public async Task StopServiceAsync(string name, Action<string> report, CancellationToken ct)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            report($"{name} is already stopped");
            return;
        }

        if (service.Status != ServiceControllerStatus.StopPending)
        {
            report($"stopping {name}");
            service.Stop();
        }

        await WaitForAsync(service, ServiceControllerStatus.Stopped, ct);
        report($"{name} stopped");
    }

    public async Task StartServiceAsync(string name, Action<string> report, CancellationToken ct)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Running)
        {
            report($"{name} is already running");
            return;
        }

        if (service.Status != ServiceControllerStatus.StartPending)
        {
            report($"starting {name}");
            service.Start();
        }

        await WaitForAsync(service, ServiceControllerStatus.Running, ct);
        report($"{name} running");
    }

    private static async Task WaitForAsync(ServiceController service, ServiceControllerStatus status, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ServiceTimeout;
        while (true)
        {
            service.Refresh();
            if (service.Status == status)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new DeployException(
                    $"The service {service.ServiceName} did not reach the state {status} within {ServiceTimeout.TotalSeconds:0} seconds "
                    + $"(it is {service.Status}).");
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    private static async Task RunIisResetAsync(string argument, Action<string> report, CancellationToken ct)
    {
        var result = await ProcessHelper.RunAsync("iisreset", argument, onOutputLine: report, cancellationToken: ct);
        if (result.ExitCode != 0)
        {
            throw new DeployException($"iisreset {argument} failed with exit code {result.ExitCode}: {result.Error.Trim()}");
        }
    }
}

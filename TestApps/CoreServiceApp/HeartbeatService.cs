using System.ServiceProcess;

namespace CoreServiceApp;

public sealed class HeartbeatService : ServiceBase
{
    private Heartbeat? _heartbeat;

    public HeartbeatService() => ServiceName = "PublishToolTest.CoreService";

    protected override void OnStart(string[] args)
    {
        _heartbeat = new Heartbeat("CoreServiceApp");
        _heartbeat.Start();
    }

    protected override void OnStop() => _heartbeat?.Dispose();
}

using System.ServiceProcess;

namespace FrameworkServiceApp
{
    public sealed class HeartbeatService : ServiceBase
    {
        private Heartbeat _heartbeat;

        public HeartbeatService()
        {
            ServiceName = "PublishToolTest.FrameworkService";
        }

        protected override void OnStart(string[] args)
        {
            _heartbeat = new Heartbeat("FrameworkServiceApp");
            _heartbeat.Start();
        }

        protected override void OnStop()
        {
            _heartbeat?.Dispose();
        }
    }
}

using System.ServiceProcess;
using CoreServiceApp;

if (Environment.UserInteractive)
{
    using var heartbeat = new Heartbeat("CoreServiceApp");
    heartbeat.Start();
    Console.WriteLine("CoreServiceApp is writing Logs/heartbeat.log. Press Enter to stop.");
    Console.ReadLine();
}
else
{
    ServiceBase.Run(new HeartbeatService());
}

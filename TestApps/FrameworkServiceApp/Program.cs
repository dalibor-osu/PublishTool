using System;
using System.ServiceProcess;

namespace FrameworkServiceApp
{
    internal static class Program
    {
        private static void Main()
        {
            if (Environment.UserInteractive)
            {
                using (var heartbeat = new Heartbeat("FrameworkServiceApp"))
                {
                    heartbeat.Start();
                    Console.WriteLine("FrameworkServiceApp is writing Logs\\heartbeat.log. Press Enter to stop.");
                    Console.ReadLine();
                }
            }
            else
            {
                ServiceBase.Run(new HeartbeatService());
            }
        }
    }
}

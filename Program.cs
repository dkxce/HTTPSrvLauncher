using System.ServiceProcess;

namespace HTTPSrvLauncher
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[] { new HTTPSrvLauncher() };
            ServiceBase.Run(ServicesToRun);
        }
    }
}

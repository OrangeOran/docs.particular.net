﻿namespace Core4
{
    using System;
    using NServiceBus;
    using NServiceBus.Installation.Environments;
    using System.ServiceProcess;

    #region windowsservicehosting

    class ProgramService :
        ServiceBase
    {
        IBus bus;

        static void Main()
        {
            using (var service = new ProgramService())
            {
                if (Environment.UserInteractive)
                {
                    service.OnStart(null);

                    Console.WriteLine("Bus created and configured");
                    Console.WriteLine("Press any key to exit");
                    Console.ReadKey();

                    service.OnStop();

                    return;
                }
                Run(service);
            }
        }

        protected override void OnStart(string[] args)
        {
            var configure = Configure.With();
            configure.DefineEndpointName("EndpointName");
            bus = configure.UnicastBus()
                .CreateBus()
                .Start(() => configure.ForInstallationOn<Windows>().Install());
        }

        protected override void OnStop()
        {
            ((IDisposable) bus)?.Dispose();
        }
    }

    #endregion
}
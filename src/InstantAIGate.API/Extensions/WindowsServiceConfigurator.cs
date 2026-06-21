using Microsoft.Extensions.Hosting.WindowsServices;
using System.Diagnostics;
using System.ServiceProcess;

namespace InstantAIGate.API.Extensions
{
    public static class WindowsServiceConfigurator
    {
        public static bool ShouldRunAsService(string[] args)
        {
            return args.Contains("--run-as-service") || WindowsServiceHelpers.IsWindowsService();
        }

        public static WebApplicationOptions GetOptions(string[] args)
        {
            return new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = ShouldRunAsService(args) ? AppContext.BaseDirectory : default
            };
        }

        public static void ConfigureHost(WebApplicationBuilder builder, string[] args, string serviceName, string description)
        {
            if (ShouldRunAsService(args))
            {
                builder.Host.UseWindowsService(options =>
                {
                    options.ServiceName = serviceName;
                });
                EnsureServiceDescription(serviceName, description);
            }
        }

        private static void EnsureServiceDescription(string serviceName, string description)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"description \"{serviceName}\" \"{description}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Verb = "runas"
                };

                using var process = Process.Start(psi);
                process?.WaitForExit();
            }
            catch
            {
            }
        }
    }
}

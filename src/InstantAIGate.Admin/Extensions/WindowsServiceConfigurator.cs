using Microsoft.Extensions.Hosting.WindowsServices;
using System.Diagnostics;

namespace InstantAIGate.Admin.Extensions
{
    public static class WindowsServiceConfigurator
    {
        public static bool ShouldRunAsService(string[] args)
        {
            if (!OperatingSystem.IsWindows())
                return false;

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
                EnsureServiceRecovery(serviceName);
            }
        }

        private static void EnsureServiceDescription(string serviceName, string description)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {

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

        private static void EnsureServiceRecovery(string serviceName)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                string arguments = $"failure \"{serviceName}\" reset= 86400 actions= restart/5000/restart/5000/restart/5000";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = arguments,
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
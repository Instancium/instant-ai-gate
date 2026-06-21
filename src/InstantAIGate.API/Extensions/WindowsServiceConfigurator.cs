using Microsoft.Extensions.Hosting.WindowsServices;

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

        public static void ConfigureHost(WebApplicationBuilder builder, string[] args, string serviceName)
        {
            if (ShouldRunAsService(args))
            {
                builder.Host.UseWindowsService(options =>
                {
                    options.ServiceName = serviceName;
                });
            }
        }
    }
}

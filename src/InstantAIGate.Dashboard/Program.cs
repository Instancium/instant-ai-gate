namespace InstantAIGate.Dashboard;

using InstantAIGate.Dashboard.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(sp => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
        });

        var hubUrl = new Uri(
            new Uri(builder.HostEnvironment.BaseAddress),
            DashboardTelemetryClient.HubRelativePath);

        builder.Services.AddScoped<IAdminApiKeyStore, LocalStorageAdminApiKeyStore>();
        builder.Services.AddScoped<UiCommandService>();
        builder.Services.AddScoped(sp => new DashboardTelemetryClient(
            sp.GetRequiredService<IAdminApiKeyStore>(),
            sp.GetRequiredService<ILogger<DashboardTelemetryClient>>(),
            hubUrl));

        await builder.Build().RunAsync();
    }
}
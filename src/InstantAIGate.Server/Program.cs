namespace InstantAIGate.Server;

using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Interfaces.Session;
using InstantAIGate.Core.Services.Session;
using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.Server.Configuration;
using InstantAIGate.Server.Diagnostics;
using InstantAIGate.Server.Hubs;
using InstantAIGate.Server.Middleware;
using InstantAIGate.Server.Services.Workers;
using InstantAIGate.SSR.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.Channels;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseWindowsService(options =>
        {
            options.ServiceName = "InstantAIGate.Server";
        });

        builder.Services.Configure<StorageSettings>(
            builder.Configuration.GetSection("InstantAIGate:Storage"));

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("GatewayAdmin", policy => policy.RequireRole("Admin"));
            options.AddPolicy("GatewayUser", policy => policy.RequireRole("User", "Admin"));
        });

        builder.Services.AddHealthChecks()
            .AddCheck<ModelReadyHealthCheck>("model_ready");

        builder.Services.AddInstantAIGateInference();
        builder.Services.AddInstantAIGateSSR();
        builder.Services.AddSignalR();

        builder.Services.AddSingleton<IEphemeralSessionRegistry, EphemeralSessionRegistry>();
        builder.Services.AddSingleton<ISessionReaperService, SessionReaperService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ISessionReaperService>());

        var signalRLoggerProvider = new SignalRLoggerProvider();
        builder.Services.AddSingleton<ILoggerProvider>(signalRLoggerProvider);
        builder.Services.AddHostedService(sp => signalRLoggerProvider);

        builder.Services.AddHostedService<MetricsBroadcasterWorker>();

        var downloadChannel = Channel.CreateUnbounded<DownloadJob>();
        builder.Services.AddSingleton<ChannelWriter<DownloadJob>>(downloadChannel.Writer);
        builder.Services.AddSingleton<ChannelReader<DownloadJob>>(downloadChannel.Reader);
        builder.Services.AddHostedService<ModelDownloadWorker>();

        builder.Services.Configure<StartupModelSettings>(
            builder.Configuration.GetSection("InstantAIGate:StartupModel"));
        builder.Services.AddHostedService<ModelStartupWorker>();

        var app = builder.Build();

        var gatewayHubContext = app.Services.GetRequiredService<IHubContext<GatewayHub, IGatewayHubClient>>();
        signalRLoggerProvider.SetHubContext(gatewayHubContext);

        var nativeLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NativeStreamRedirector");
        InstantAIGate.Native.Logging.NativeStreamRedirector.Initialize(logMessage =>
        {
            nativeLogger.LogDebug("{NativeMessage}", logMessage);
        });

        app.UseMiddleware<GatewayExceptionMiddleware>();
        app.UseMiddleware<ApiKeyAuthMiddleware>();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Name == "model_ready",
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        app.MapHub<GatewayHub>("/hub/gateway");

        app.Run();
    }
}
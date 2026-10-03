using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.Server.Configuration;
using InstantAIGate.Server.Diagnostics;
using InstantAIGate.Server.Hubs;
using InstantAIGate.Server.Middleware;
using InstantAIGate.Server.Services.Workers;
using InstantAIGate.SSR.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "InstantAIGate.Server";
});

builder.Services.Configure<StorageSettings>(
    builder.Configuration.GetSection("InstantAIGate:Storage"));

builder.Services.AddControllers();

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

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Name == "model_ready"
});

app.MapHub<GatewayHub>("/hub/gateway");
app.MapHub<TelemetryHub>("/hub/telemetry");
app.MapHub<SessionChatHub>("/hub/chat");

app.Run();

public partial class Program { }
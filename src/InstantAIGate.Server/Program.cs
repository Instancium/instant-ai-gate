using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.Server.Diagnostics;
using InstantAIGate.Server.Middleware;
using InstantAIGate.Server.Services.Workers;
using InstantAIGate.SSR.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;


var builder = WebApplication.CreateBuilder(args);

// Enable Windows Service lifetime integration.
// Note: Description and DisplayName are OS-level registration concerns
// and are configured via the deployment script (InstallService.ps1).
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "InstantAIGate.Server";
});

builder.Services.Configure<StorageSettings>(
    builder.Configuration.GetSection("InstantAIGate:Storage"));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck<ModelReadyHealthCheck>("model_ready");

builder.Services.AddInstantAIGateInference();
builder.Services.AddInstantAIGateSSR();

builder.Services.AddSignalR();

// Register Telemetry Engine
var signalRLoggerProvider = new SignalRLoggerProvider();
builder.Services.AddSingleton<ILoggerProvider>(signalRLoggerProvider);
builder.Services.AddHostedService(sp => signalRLoggerProvider);
builder.Services.AddHostedService<MetricsBroadcasterWorker>();


var downloadChannel = Channel.CreateUnbounded<DownloadJob>();
builder.Services.AddSingleton<ChannelWriter<DownloadJob>>(downloadChannel.Writer);
builder.Services.AddSingleton<ChannelReader<DownloadJob>>(downloadChannel.Reader);
builder.Services.AddHostedService<ModelDownloadWorker>();

var app = builder.Build();

// Bind Hub Context to Logger Provider after build
var hubContext = app.Services.GetRequiredService<IHubContext<InstantAIGate.Server.Hubs.TelemetryHub, InstantAIGate.Server.Hubs.ITelemetryClient>>();
signalRLoggerProvider.SetHubContext(hubContext);

var nativeLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NativeStreamRedirector");
InstantAIGate.Native.Logging.NativeStreamRedirector.Initialize(logMessage =>
{
    nativeLogger.LogDebug("{NativeMessage}", logMessage);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<GatewayExceptionMiddleware>();
app.UseMiddleware<PortRoutingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

// Map diagnostic endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Name == "model_ready"
});

app.MapHub<InstantAIGate.Server.Hubs.TelemetryHub>("/hub/telemetry");
app.Run();

public partial class Program { }
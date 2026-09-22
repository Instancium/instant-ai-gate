using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.SSR.DependencyInjection;
using InstantAIGate.Server.Diagnostics;
using InstantAIGate.Server.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageSettings>(
    builder.Configuration.GetSection("InstantAIGate:Storage"));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck<ModelReadyHealthCheck>("model_ready");

builder.Services.AddInstantAIGateInference();
builder.Services.AddInstantAIGateSSR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<GatewayExceptionMiddleware>();
app.UseMiddleware<PortRoutingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

// Маппинг маршрутов диагностики
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false 
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Name == "model_ready" 
});

app.Run();

public partial class Program { }
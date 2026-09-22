using InstantAIGate.Native.DependencyInjection;
using InstantAIGate.SSR.DependencyInjection;
using InstantAIGate.Server.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Inject Core, Native, and SSR dependencies
builder.Services.AddInstantAIGateInference();
builder.Services.AddInstantAIGateSSR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Pipeline configuration (Order is critical)
app.UseMiddleware<GatewayExceptionMiddleware>();
app.UseMiddleware<PortRoutingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();
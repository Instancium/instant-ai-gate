using InstantAIGate.API.Authentication;
using InstantAIGate.API.Config;
using InstantAIGate.API.Extensions;
using InstantAIGate.API.Hub;
using InstantAIGate.API.Services;
using InstantAIGate.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var argsOptions = WindowsServiceConfigurator.GetOptions(args);
var builder = WebApplication.CreateBuilder(argsOptions);

WindowsServiceConfigurator.ConfigureHost(builder, args, "InstantAIGate.API");

builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection("ApiKeyOptions"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "AdminApiKey";
    options.DefaultChallengeScheme = "AdminApiKey";
    options.DefaultAuthenticateScheme = "AdminApiKey";
})
.AddScheme<AuthenticationSchemeOptions, AdminApiKeyHandler>("AdminApiKey", _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminApiKeyPolicy", policy =>
    {
        policy.AddAuthenticationSchemes("AdminApiKey");
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddOpenApi();

builder.Services.AddInstantAIGateInfrastructure(options =>
{
    options.RootPath = builder.Configuration["Storage:RootPath"] ?? "storage/models";
});

var corsSettings = builder.Configuration
    .GetSection("CorsSettings:AllowedOrigins")
    .Get<string[]>()
    ?? new[] { "http://localhost:5000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("GatewayCorsPolicy", policy =>
    {
        policy.WithOrigins(corsSettings)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR()
    .AddJsonProtocol(options => {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHostedService<TelemetryBroadcastService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();

app.UseCors("GatewayCorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<TelemetryHub>("/hubs/telemetry");

app.Run();
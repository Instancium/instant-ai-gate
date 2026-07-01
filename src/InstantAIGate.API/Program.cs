using InstantAIGate.API.Authentication;
using InstantAIGate.API.Config;
using InstantAIGate.API.Extensions;
using InstantAIGate.API.Hub;
using InstantAIGate.API.Services;
using InstantAIGate.Application.Config;
using InstantAIGate.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var argsOptions = WindowsServiceConfigurator.GetOptions(args);
var builder = WebApplication.CreateBuilder(argsOptions);

WindowsServiceConfigurator.ConfigureHost(builder, args, "InstantAIGate_API",
    "OpenAI-compatible gateway service providing secure, low-latency access to local Large Language Models.");

var gatewayConfig = new GatewayConfig();
builder.Configuration.Bind("GatewayConfig", gatewayConfig);

if (string.IsNullOrWhiteSpace(gatewayConfig.AdminKey))
{
    // Named cross-process mutex prevents two API instances from generating different keys simultaneously.
    using var mutex = new Mutex(false, @"Global\InstantAIGate_AdminKey");
    mutex.WaitOne();
    try
    {
        // Re-check inside the lock in case another instance just wrote the file.
        if (File.Exists(gatewayConfig.AdminKeyPath))
        {
            gatewayConfig.AdminKey = File.ReadAllText(gatewayConfig.AdminKeyPath).Trim();
        }
        else
        {
            if (!Directory.Exists(gatewayConfig.RootPath))
                Directory.CreateDirectory(gatewayConfig.RootPath);

            var secureToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
            File.WriteAllText(gatewayConfig.AdminKeyPath, secureToken);
            gatewayConfig.AdminKey = secureToken;
        }
    }
    finally
    {
        mutex.ReleaseMutex();
    }
}

builder.Services.AddSingleton(gatewayConfig);

builder.Services.AddMemoryCache();

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
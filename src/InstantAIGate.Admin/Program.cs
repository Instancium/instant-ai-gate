using InstantAIGate.Admin;
using InstantAIGate.Admin.Config;
using InstantAIGate.Admin.Extensions;
using InstantAIGate.Application.Config;
using OpenAI;
using System.ClientModel;

var argsOptions = WindowsServiceConfigurator.GetOptions(args);
var builder = WebApplication.CreateBuilder(argsOptions);

WindowsServiceConfigurator.ConfigureHost(builder, args, "InstantAIGate_Admin",
    "Infrastructure dashboard and management UI for secure on-premise LLM orchestration and resource tuning.");

var gatewayConfig = new GatewayConfig();
builder.Configuration.Bind("GatewayConfig", gatewayConfig);

if (string.IsNullOrWhiteSpace(gatewayConfig.AdminKey))
{
    // Admin never generates the key — that is exclusively the API's responsibility.
    // Poll until the API creates the key file, then read it.
    const int maxAttempts = 30;
    const int delayMs = 1000;

    for (var attempt = 0; attempt < maxAttempts; attempt++)
    {
        if (File.Exists(gatewayConfig.AdminKeyPath))
        {
            gatewayConfig.AdminKey = File.ReadAllText(gatewayConfig.AdminKeyPath).Trim();
            break;
        }

        Console.WriteLine($"[Admin] Waiting for API to initialize admin key... ({attempt + 1}/{maxAttempts})");
        Thread.Sleep(delayMs);
    }

    if (string.IsNullOrWhiteSpace(gatewayConfig.AdminKey))
        throw new InvalidOperationException(
            $"Admin key not found at '{gatewayConfig.AdminKeyPath}' after {maxAttempts} attempts. " +
            "Ensure the API service is running and has write permissions to the configured RootPath.");
}

builder.Services.AddSingleton(gatewayConfig);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorPages()
    .AddRazorRuntimeCompilation();

builder.Services.AddHealthChecks();

builder.Services.AddHttpClient();
builder.Services.Configure<APIClientOptions>(builder.Configuration.GetSection("APIClientOptions"));
builder.Services.AddTransient<ApiKeyHandler>();
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddHttpMessageHandler<ApiKeyHandler>();
});

builder.Services.AddSingleton(sp =>
{
    var clientOptions = builder.Configuration.GetSection("APIClientOptions").Get<APIClientOptions>() ?? new APIClientOptions();
    var openAiOptions = new OpenAIClientOptions { Endpoint = new Uri($"{clientOptions.PublicUrl}/v1") };
    return new OpenAIClient(new ApiKeyCredential(gatewayConfig.AdminKey!), openAiOptions);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapHealthChecks("/health");

app.Run();

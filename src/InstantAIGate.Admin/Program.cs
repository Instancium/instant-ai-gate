using InstantAIGate.Admin;
using InstantAIGate.Admin.Config;
using InstantAIGate.Admin.Extensions;
using OpenAI;
using System.ClientModel;

var argsOptions = WindowsServiceConfigurator.GetOptions(args);
var builder = WebApplication.CreateBuilder(argsOptions);

WindowsServiceConfigurator.ConfigureHost(builder, args, "InstantAIGate.Admin");

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorPages()
    .AddRazorRuntimeCompilation();

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
    return new OpenAIClient(new ApiKeyCredential(clientOptions.AdminApiKey), openAiOptions);
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

app.Run();

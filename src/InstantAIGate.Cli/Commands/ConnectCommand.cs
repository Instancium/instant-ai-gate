namespace InstantAIGate.Cli.Commands;

using InstantAIGate.Cli.Configuration;
using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.State;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.SSR.Dtos;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class ConnectCommand : IConsoleCommand
{
    private readonly GatewayClientProxy _proxy;
    private readonly CliSession _session;
    private readonly RemoteGatewaySettings _settings;

    public ConnectCommand(
        GatewayClientProxy proxy,
        CliSession session,
        IOptions<RemoteGatewaySettings> settings)
    {
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string Name => "/connect";
    public string Description => "Connects to a remote gateway instance or switches to local engine. Usage: /connect [baseUrl] [apiKey] or /connect local";

    public async Task ExecuteAsync(string argument, CancellationToken cancellationToken)
    {
        string trimmed = argument?.Trim() ?? string.Empty;
        if (string.Equals(trimmed, "local", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _proxy.SwitchToLocal();
                _session.IsRemoteMode = false;
                AnsiConsole.MarkupLine("[green]Switched to Local Inference Engine.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to switch to local mode:[/] {Markup.Escape(ex.Message)}");
            }
            return;
        }

        string baseUrl = _settings.BaseUrl;
        string key = _settings.ApiKey;
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            baseUrl = parts[0];
            key = parts.Length > 1 ? parts[1] : _settings.ApiKey;
        }

        try
        {
            await _proxy.SwitchToRemoteAsync(baseUrl, key, cancellationToken);
            _session.IsRemoteMode = true;

            var status = await _proxy.GetGatewayStatusAsync(cancellationToken);

            switch (status.Status)
            {
                case GatewayOperationalStatus.Ready:
                    AnsiConsole.MarkupLine($"[green]Successfully connected to Remote Gateway at[/] [cyan]{Markup.Escape(baseUrl)}[/]");
                    if (!string.IsNullOrWhiteSpace(status.ActiveModelId))
                    {
                        AnsiConsole.MarkupLine($"[dim]Active Model:[/] [bold green]{Markup.Escape(status.ActiveModelId)}[/] [dim](Ready)[/]");
                        _session.ActiveModelId = status.ActiveModelId;
                    }
                    break;

                case GatewayOperationalStatus.ModelDownloading:
                    AnsiConsole.MarkupLine($"[green]Connected to Remote Gateway at[/] [cyan]{Markup.Escape(baseUrl)}[/]");
                    AnsiConsole.MarkupLine($"[yellow]Remote Gateway is currently downloading startup model:[/] [bold cyan]{Markup.Escape(status.ActiveModelId ?? "unknown")}[/]");
                    AnsiConsole.MarkupLine("[dim]Streaming download progress... Press Ctrl+C to cancel observation.[/]\n");
                    await WatchDownloadAndLoadingAsync(status.ActiveModelId!, cancellationToken);
                    break;

                case GatewayOperationalStatus.ModelLoading:
                    AnsiConsole.MarkupLine($"[green]Connected to Remote Gateway at[/] [cyan]{Markup.Escape(baseUrl)}[/]");
                    AnsiConsole.MarkupLine($"[yellow]Remote Gateway is loading model into memory:[/] [bold cyan]{Markup.Escape(status.ActiveModelId ?? "unknown")}[/]");
                    await WatchModelLoadingAsync(status.ActiveModelId!, cancellationToken);
                    break;

                case GatewayOperationalStatus.Faulted:
                    AnsiConsole.MarkupLine($"[yellow]Connected to Remote Gateway at[/] [cyan]{Markup.Escape(baseUrl)}[/]");
                    AnsiConsole.MarkupLine($"[bold red]Remote Gateway startup is in Faulted state:[/] {Markup.Escape(status.ErrorMessage ?? "Unknown failure")}");
                    break;

                default:
                    AnsiConsole.MarkupLine($"[green]Successfully connected to Remote Gateway at[/] [cyan]{Markup.Escape(baseUrl)}[/]");
                    AnsiConsole.MarkupLine("[dim yellow]No model is currently loaded in memory. Use [cyan]/load <modelId>[/] to load one.[/]");
                    break;
            }
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Connection failed:[/] Service at [cyan]{Markup.Escape(baseUrl)}[/] is not reachable.");
            AnsiConsole.MarkupLine($"[dim red]Details:[/] {Markup.Escape(ex.Message)}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private async Task WatchDownloadAndLoadingAsync(string modelId, CancellationToken ct)
    {
        try
        {
            await _proxy.SubscribeToModelDownloadAsync(modelId, ct);
        }
        catch
        {
        }

        var progressCompleted = new TaskCompletionSource<bool>();
        Action<DownloadProgress>? onProgress = null;
        Action<GatewayStatusDetails>? onStatus = null;

        try
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var progressTask = ctx.AddTask($"[green]Downloading {Markup.Escape(modelId)}[/]", autoStart: true, maxValue: 100);

                    onProgress = p =>
                    {
                        if (string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                        {
                            progressTask.Value = Math.Clamp(p.Percentage, 0, 100);
                            double dlMb = p.BytesDownloaded / (1024.0 * 1024.0);
                            double totalMb = p.TotalBytes / (1024.0 * 1024.0);
                            double speedMb = p.SpeedBytesPerSecond / (1024.0 * 1024.0);
                            progressTask.Description = $"[green]Downloading {Markup.Escape(modelId)}[/] ({dlMb:F1}/{totalMb:F1} MB, {speedMb:F2} MB/s)";

                            if (p.Percentage >= 100f)
                            {
                                progressCompleted.TrySetResult(true);
                            }
                        }
                    };

                    onStatus = s =>
                    {
                        if (s.Status is GatewayOperationalStatus.ModelLoading or GatewayOperationalStatus.Ready)
                        {
                            progressTask.Value = 100;
                            progressCompleted.TrySetResult(true);
                        }
                        else if (s.Status == GatewayOperationalStatus.Faulted)
                        {
                            progressCompleted.TrySetException(new InvalidOperationException(s.ErrorMessage ?? "Download faulted on server"));
                        }
                    };

                    _proxy.DownloadProgressReceived += onProgress;
                    _proxy.GatewayStatusReceived += onStatus;

                    using var reg = ct.Register(() => progressCompleted.TrySetCanceled(ct));
                    await progressCompleted.Task;
                });

            await WatchModelLoadingAsync(modelId, ct);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Download monitoring canceled. The download continues in the background on the server.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Download monitoring error:[/] {Markup.Escape(ex.Message)}");
        }
        finally
        {
            if (onProgress != null)
            {
                _proxy.DownloadProgressReceived -= onProgress;
            }

            if (onStatus != null)
            {
                _proxy.GatewayStatusReceived -= onStatus;
            }

            try
            {
                await _proxy.UnsubscribeFromModelDownloadAsync(modelId, CancellationToken.None);
            }
            catch
            {
            }
        }
    }

    private async Task WatchModelLoadingAsync(string modelId, CancellationToken ct)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Loading model '{Markup.Escape(modelId)}' into runtime memory...", async ctx =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var snapshot = await _proxy.GetGatewayStatusAsync(ct);
                    if (snapshot.Status == GatewayOperationalStatus.Ready)
                    {
                        _session.ActiveModelId = snapshot.ActiveModelId ?? modelId;
                        break;
                    }

                    if (snapshot.Status == GatewayOperationalStatus.Faulted)
                    {
                        throw new InvalidOperationException(snapshot.ErrorMessage ?? "Model failed to load into memory.");
                    }

                    await Task.Delay(500, ct);
                }
            });

        AnsiConsole.MarkupLine($"[bold green]Model '{Markup.Escape(modelId)}' is loaded and ready for inference![/]\n");
    }
}
using InstantAIGate.Cli.State;
using InstantAIGate.Cli.Core;
using InstantAIGate.Core.Dtos.Inference;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstantAIGate.Cli;

public class TuiHostedService : BackgroundService
{
    private readonly TuiDashboardState _state;
    private readonly IGatewayClient _gateway;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Layout _rootLayout;
    private bool _needsRedraw = true;
    private bool _isInputModeImage = false; // Toggle for Ctrl+I modal emulation

    public TuiHostedService(TuiDashboardState state, IGatewayClient gateway, IHostApplicationLifetime lifetime)
    {
        _state = state;
        _gateway = gateway;
        _lifetime = lifetime;

        _state.OnStateChanged += () => _needsRedraw = true;

        _rootLayout = new Layout("Root")
            .SplitRows(
                new Layout("ZoneA_Header").Size(3),
                new Layout("ZoneB_Telemetry").Size(3),
                new Layout("ZoneC_Chat"),
                new Layout("ZoneD_Input").Size(3),
                new Layout("ZoneE_Footer").Size(1)
            );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _gateway.ConnectTelemetryAsync(
            metrics => { _state.LastMetrics = metrics; _state.NotifyUpdate(); },
            ssr => { _state.SsrProgress = ssr; _state.NotifyUpdate(); },
            stoppingToken
        );

        _ = Task.Run(() => CaptureInputLoop(stoppingToken), stoppingToken);

        await AnsiConsole.Live(_rootLayout)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async ctx =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (_needsRedraw)
                    {
                        UpdateLayoutPanels();
                        ctx.Refresh();
                        _needsRedraw = false;
                    }
                    await Task.Delay(50, stoppingToken);
                }
            });
    }

    private void UpdateLayoutPanels()
    {
        // Zone A
        _rootLayout["ZoneA_Header"].Update(
            new Panel(new FigletText("InstantAIGate").Color(Color.Cyan1))
                .Header($"[{Color.Green}]Mode: {_state.ConnectionMode} | Model: {_state.ActiveModelId}[/]"));

        // Zone B: Telemetry or SSR Progress
        if (_state.SsrProgress != null && _state.SsrProgress.Percentage < 100)
        {
            var p = _state.SsrProgress;
            double mbps = p.SpeedBytesPerSecond / 1024 / 1024;
            _rootLayout["ZoneB_Telemetry"].Update(
                new Panel($"[yellow]Downloading {p.ModelId}:[/] {p.Percentage:F1}% at {mbps:F2} MB/s")
                    .Expand().Border(BoxBorder.Rounded));
        }
        else
        {
            _rootLayout["ZoneB_Telemetry"].Update(
                new Panel($"Status: {_state.EngineStatus} | Speed: {_state.TokensPerSecond:F2} tok/s | Active: {_state.LastMetrics.ActiveLeases} | Pending: {_state.LastMetrics.PendingRequests}")
                    .Expand().Border(BoxBorder.Rounded));
        }

        // Zone C: Chat History + Current Streaming Response
        var chatMarkup = string.Join("\n\n", _state.ChatHistory.Select(m =>
            $"[bold {(m.Role == "user" ? "blue" : "green")}]{m.Role.ToUpper()}:[/]\n{m.Content}"));

        if (_state.IsGenerating && _state.CurrentAssistantResponse.Length > 0)
        {
            chatMarkup += $"\n\n[bold green]ASSISTANT (Generating...):[/]\n{_state.CurrentAssistantResponse}";
        }

        _rootLayout["ZoneC_Chat"].Update(
            new Panel(new Markup(string.IsNullOrEmpty(chatMarkup) ? "[grey]Chat history is empty...[/]" : chatMarkup))
                .Header("Chat History").Expand());

        // Zone D: Input
        string inputHeader = _isInputModeImage ? "[yellow]Enter Image Path (ESC to cancel):[/]" : "Input (Press ENTER to send)";
        string cursor = DateTime.Now.Millisecond < 500 ? "_" : " ";
        _rootLayout["ZoneD_Input"].Update(
            new Panel(_state.InputBuffer.ToString() + cursor)
                .Header(inputHeader).Expand());

        // Zone E: Footer
        _rootLayout["ZoneE_Footer"].Update(
            new Markup("[bold yellow]F1[/] Help | [bold yellow]F2[/] Models | [bold yellow]Ctrl+I[/] Attach Image | [bold yellow]ESC[/] Exit"));
    }

    private void CaptureInputLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(intercept: true);

                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    if (_isInputModeImage)
                    {
                        _isInputModeImage = false;
                        _state.ClearInput();
                    }
                    else
                    {
                        _lifetime.StopApplication();
                        break;
                    }
                }
                // Ctrl+I for Image Attachment
                else if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && keyInfo.Key == ConsoleKey.I)
                {
                    _isInputModeImage = true;
                    _state.InputBuffer.Clear();
                    _state.NotifyUpdate();
                }
                // F2 for Model List (System Message injection)
                else if (keyInfo.Key == ConsoleKey.F2)
                {
                    _state.ChatHistory.Enqueue(new ChatMessage("system", "Models Modal invoked. Fetching from catalog..."));
                    _state.NotifyUpdate();
                    // Implement modal fetch logic here
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    HandleEnterPress();
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (_state.InputBuffer.Length > 0)
                    {
                        _state.InputBuffer.Length--;
                        _state.NotifyUpdate();
                    }
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    _state.InputBuffer.Append(keyInfo.KeyChar);
                    _state.NotifyUpdate();
                }
            }
            Thread.Sleep(10);
        }
    }

    private void HandleEnterPress()
    {
        if (_state.InputBuffer.Length == 0) return;

        var input = _state.InputBuffer.ToString();
        _state.ClearInput();

        if (_isInputModeImage)
        {
            _state.PendingMedia.Add(new ImageFileContent(input));
            _state.ChatHistory.Enqueue(new ChatMessage("system", $"[Image attached: {input}]"));
            _isInputModeImage = false;
            _state.NotifyUpdate();
            return;
        }

        if (_state.IsGenerating) return; // Block double-submit

        _state.ChatHistory.Enqueue(new ChatMessage("user", input));
        _state.NotifyUpdate();

        // Fire and forget inference dispatch to avoid blocking input thread
        _ = DispatchInferenceAsync(input);
    }

    private async Task DispatchInferenceAsync(string textPrompt)
    {
        _state.IsGenerating = true;
        _state.EngineStatus = "GENERATING";
        _state.CurrentAssistantResponse.Clear();
        _state.NotifyUpdate();

        try
        {
            var message = new ChatMessage("user", textPrompt);
            await foreach (var chunk in _gateway.StreamChatAsync(_state.ActiveModelId, message, CancellationToken.None))
            {
                _state.CurrentAssistantResponse.Append(chunk);
                // Throttle UI updates slightly to prevent layout thrashing
                if (DateTime.Now.Millisecond % 50 == 0)
                {
                    _state.NotifyUpdate();
                }
            }
        }
        catch (Exception ex)
        {
            _state.CurrentAssistantResponse.Append($"\n[red]Error: {ex.Message}[/]");
        }
        finally
        {
            _state.ChatHistory.Enqueue(new ChatMessage("assistant", _state.CurrentAssistantResponse.ToString()));
            _state.CurrentAssistantResponse.Clear();
            _state.IsGenerating = false;
            _state.EngineStatus = "IDLE";
            _state.NotifyUpdate();
        }
    }
}
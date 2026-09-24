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

    // UI Components
    private readonly Layout _rootLayout;
    private bool _needsRedraw = true;

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
                        new Layout("ZoneC_Chat"), // Auto-fills remaining vertical space
                        new Layout("ZoneD_Input").Size(3),
                        new Layout("ZoneE_Footer").Size(1)
                    );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _gateway.ConnectTelemetryAsync(
            metrics => { _state.LastMetrics = metrics; _state.NotifyUpdate(); },
            ssr => { /* Handle SSR Progress visually later */ _state.NotifyUpdate(); },
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

        // Zone B
        _rootLayout["ZoneB_Telemetry"].Update(
            new Panel($"Status: {_state.EngineStatus} | Speed: {_state.TokensPerSecond:F2} tok/s | Active Leases: {_state.LastMetrics.ActiveLeases}")
                .Expand().Border(BoxBorder.Rounded));

        // Zone C
        var chatMarkup = string.Join("\n\n", _state.ChatHistory.Select(m =>
            $"[bold {(m.Role == "user" ? "blue" : "green")}]{m.Role.ToUpper()}:[/]\n{m.Content}"));

        _rootLayout["ZoneC_Chat"].Update(
            new Panel(new Markup(string.IsNullOrEmpty(chatMarkup) ? "[grey]Chat history is empty...[/]" : chatMarkup))
                .Header("Chat History").Expand());

        // Zone D
        _rootLayout["ZoneD_Input"].Update(
            new Panel(_state.InputBuffer.ToString() + (DateTime.Now.Millisecond < 500 ? "_" : " "))
                .Header("Input (Press ENTER to send)").Expand());

        // Zone E
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
                    _lifetime.StopApplication();
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    if (_state.InputBuffer.Length > 0)
                    {
                        // Dispatch to inference (stubbed for Phase 6.2)
                        _state.ChatHistory.Enqueue(new ChatMessage("user", _state.InputBuffer.ToString()));
                        _state.ClearInput();
                    }
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
}
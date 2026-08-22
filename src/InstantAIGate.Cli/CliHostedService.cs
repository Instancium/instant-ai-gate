using InstantAIGate.Core.Abstractions.Chat;
using InstantAIGate.Core.DTOs.Chat;
using InstantAIGate.Core.DTOs.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Spectre.Console;


namespace InstantAIGate.Cli
{
    public class CliHostedService : IHostedService
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IConfiguration _configuration;
        private readonly IChatAdapterFactory _adapterFactory;
        private Task? _applicationTask;
        private CancellationTokenSource? _cancellationTokenSource;

        public CliHostedService(
            IHostApplicationLifetime appLifetime,
            IConfiguration configuration,
            IChatAdapterFactory adapterFactory)
        {
            _appLifetime = appLifetime;
            _configuration = configuration;
            _adapterFactory = adapterFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _applicationTask = Task.Run(() => RunInteractiveLoopAsync(_cancellationTokenSource.Token), cancellationToken);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cancellationTokenSource != null)
            {
                await _cancellationTokenSource.CancelAsync();
            }

            if (_applicationTask != null)
            {
                await Task.WhenAny(_applicationTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }

        private async Task RunInteractiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                AnsiConsole.Clear();
                AnsiConsole.Write(
                    new FigletText("InstantAIGate")
                        .LeftJustified()
                        .Color(Color.Blue));

                List<ModelManifest> models = _configuration
                    .GetSection("InstantAIGate:Models")
                    .Get<List<ModelManifest>>() ?? new List<ModelManifest>();

                if (models.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: No models found in appsettings.json.[/]");
                    _appLifetime.StopApplication();
                    return;
                }

                AnsiConsole.MarkupLine($"[green]Successfully loaded {models.Count} model configurations.[/]");
                AnsiConsole.MarkupLine("Type [yellow]/help[/] for commands.\n");

                IChatAdapter? activeAdapter = null;
                ModelManifest? activeManifest = null;
                List<ChatMessage> chatHistory = new();
                List<MessagePart> nextMessageParts = new();
       
                string historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli_history.txt");

                if (File.Exists(historyFilePath))
                {
                    var savedHistory = File.ReadAllLines(historyFilePath);
                    ReadLine.AddHistory(savedHistory);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    AnsiConsole.Markup("[cyan]🧑 User:[/] ");
                    string input = ReadLine.Read("");

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        continue;
                    }

                    ReadLine.AddHistory(input);

                    try
                    {
                        var currentHistory = ReadLine.GetHistory();
                        if (currentHistory.Count > 100)
                        {
                            currentHistory = currentHistory.GetRange(currentHistory.Count - 100, 100);
                        }
                        File.WriteAllLines(historyFilePath, currentHistory);
                    }
                    catch {  }

                    // --- COMMAND HANDLING ---
                    if (input.StartsWith('/'))
                    {
                        string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        string command = parts[0].ToLowerInvariant();
                        string argument = parts.Length > 1 ? parts[1] : string.Empty;

                        if (command == "/exit")
                        {
                            break;
                        }
                        else if (command == "/help")
                        {
                            ShowHelp();
                        }
                        else if (command == "/list")
                        {
                            ShowModels(models);
                        }
                        else if (command == "/clear")
                        {
                            chatHistory.Clear();
                            nextMessageParts.Clear();

                            AnsiConsole.Clear();

                            AnsiConsole.Write(
                                new FigletText("InstantAIGate")
                                    .LeftJustified()
                                    .Color(Color.Blue));
                            AnsiConsole.MarkupLine($"[green]Successfully loaded {models.Count} model configurations.[/]");
                            AnsiConsole.MarkupLine("Type [yellow]/help[/] for commands.\n");

                            AnsiConsole.MarkupLine("[green]Chat history cleared.[/]");
                        }
                        else if (command == "/image")
                        {
                            if (string.IsNullOrWhiteSpace(argument))
                            {
                                AnsiConsole.MarkupLine("[red]Usage: /image <path_or_url>[/]");
                                continue;
                            }

                            nextMessageParts.Add(new ImageUrlPart { ImageUrl = new ImageUrl { Url = argument.Trim('"') } });
                            AnsiConsole.MarkupLine($"[green]Image attached:[/] {argument}");
                        }
                        else if (command == "/load")
                        {
                            var targetModel = models.Find(m => m.Id.Equals(argument, StringComparison.OrdinalIgnoreCase));
                            if (targetModel == null)
                            {
                                AnsiConsole.MarkupLine($"[red]Model '{argument}' not found. Use /list to see available models.[/]");
                                continue;
                            }

                            activeAdapter?.Dispose();
                            activeAdapter = null;
                            activeManifest = targetModel;
                            chatHistory.Clear();

                            try
                            {
                                await AnsiConsole.Status()
                                    .Spinner(Spinner.Known.Dots)
                                    .StartAsync($"Loading {targetModel.Name} into VRAM...", async ctx =>
                                    {
                                        activeAdapter = _adapterFactory.CreateAdapter(activeManifest);
                                        await activeAdapter.InitializeAsync(activeManifest, cancellationToken);
                                    });

                                AnsiConsole.MarkupLine($"[green]Model {targetModel.Name} successfully loaded and ready![/]");
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.MarkupLine($"[red]Failed to load model:[/] {ex.Message}");
                                activeAdapter = null;
                                activeManifest = null;
                            }
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[red]Unknown command. Type /help.[/]");
                        }

                        continue;
                    }

                    // --- INFERENCE HANDLING ---
                    if (activeManifest == null || activeAdapter == null)
                    {
                        AnsiConsole.MarkupLine("[red]No model loaded. Use /list and /load <id> first.[/]");
                        continue;
                    }

                    nextMessageParts.Add(new TextPart { Text = input });
                    chatHistory.Add(new ChatMessage { Role = "user", ContentParts = new List<MessagePart>(nextMessageParts) });
                    nextMessageParts.Clear();

                    var chatRequest = new ChatRequest
                    {
                        Model = activeManifest.Id,
                        Messages = chatHistory,
                        MaxTokens = 8192,
                        Temperature = 0.1f 
                    };

                    AnsiConsole.Markup("[blue]🤖 AI:[/] ");

                    try
                    {
                        var fullResponse = new System.Text.StringBuilder();

                        await foreach (var token in activeAdapter.GenerateStreamAsync(chatRequest, cancellationToken))
                        {
                            AnsiConsole.Write(token);
                            fullResponse.Append(token);
                        }
                        AnsiConsole.WriteLine();

                        chatHistory.Add(new ChatMessage
                        {
                            Role = "assistant",
                            ContentParts = new List<MessagePart> { new TextPart { Text = fullResponse.ToString() } }
                        });
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"\n[red]Generation Error:[/] {ex.Message}");
                        chatHistory.RemoveAt(chatHistory.Count - 1);
                    }
                }
            }
            finally
            {
                _appLifetime.StopApplication();
            }
        }

        private void ShowHelp()
        {
            var table = new Table();
            table.AddColumn("Command");
            table.AddColumn("Description");
            table.AddRow("[yellow]/list[/]", "List all available models.");
            table.AddRow("[yellow]/load <id>[/]", "Select and load a model into VRAM.");
            table.AddRow("[yellow]/image <path>[/]", "Attach an image to the next prompt.");
            table.AddRow("[yellow]/clear[/]", "Clear conversation history.");
            table.AddRow("[yellow]/exit[/]", "Quit the application.");
            AnsiConsole.Write(table);
        }

        private void ShowModels(List<ModelManifest> models)
        {
            var table = new Table();
            table.AddColumn("ID");
            table.AddColumn("Name");

            foreach (var model in models)
            {
                table.AddRow($"[cyan]{model.Id}[/]", model.Name);
            }

            AnsiConsole.Write(table);
        }
    }
}
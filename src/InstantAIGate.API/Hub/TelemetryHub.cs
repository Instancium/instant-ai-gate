namespace InstantAIGate.API.Hub
{
    public sealed class TelemetryHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public string Ping() => "Pong";

        public async Task SubscribeToMetrics()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "TelemetryConsumers");
        }

        public async Task UnsubscribeFromMetrics()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "TelemetryConsumers");
        }
    }
}
using Microsoft.AspNetCore.SignalR;

sealed class AgentControlHub(ILogger<AgentControlHub> logger, CatiaComStaService catiaComStaService) : Hub
{
    private const string HubPath = "/agentcontrolhub";

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Agent control hub connected: {ConnectionId}", Context.ConnectionId);
        await SendConnectionInfoAsync();
        await SendCatiaComStatusAsync();
        await SendServerMessageAsync("Globale SignalR-Verbindung aufgebaut.");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation(exception, "Agent control hub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task RequestConnectionInfo()
    {
        await SendConnectionInfoAsync();
        await SendCatiaComStatusAsync();
        await SendServerMessageAsync("Verbindungsdetails aktualisiert.");
    }

    public async Task SendClientMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new HubException("Die Nachricht darf nicht leer sein.");
        }

        logger.LogInformation("Agent control message from {ConnectionId}: {Message}", Context.ConnectionId, message);

        await Clients.Caller.SendAsync("ReceiveAgentMessage", "browser", message, DateTimeOffset.UtcNow);
        await SendServerMessageAsync($"API bestaetigt: {message}");
    }

    public async Task OpenEditorFile(string editorFileName)
    {
        var receipt = await catiaComStaService.OpenEditorFileAsync(editorFileName, Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveEditorFileOpened", receipt);
        await SendCatiaComStatusAsync();
        await SendServerMessageAsync($"COM/CATIA hat '{receipt.EditorFileName}' geoeffnet.");
    }

    public async Task SaveEditorInput(string input)
    {
        var receipt = await catiaComStaService.SaveOpenedEditorInputAsync(input, Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveEditorSaveResult", receipt);
        await SendCatiaComStatusAsync();
        await SendServerMessageAsync($"COM/CATIA hat '{receipt.EditorFileName}' gespeichert.");
    }

    private Task SendConnectionInfoAsync()
    {
        return Clients.Caller.SendAsync("ReceiveConnectionInfo", Context.ConnectionId, HubPath);
    }

    private Task SendCatiaComStatusAsync()
    {
        return Clients.Caller.SendAsync("ReceiveCatiaComStatus", catiaComStaService.GetStatus());
    }

    private Task SendServerMessageAsync(string message)
    {
        return Clients.Caller.SendAsync("ReceiveAgentMessage", "server", message, DateTimeOffset.UtcNow);
    }
}

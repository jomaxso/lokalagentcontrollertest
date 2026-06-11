using System.Threading.Channels;
using System.Runtime.Versioning;
using Local.Commands;
using Local.Lib;
using Microsoft.AspNetCore.SignalR;

namespace Local.Services;

[SupportedOSPlatform("windows")]
sealed class AgentControlHub(
    ILogger<AgentControlHub> logger,
    Channel<CatiaCommand> channel,
    CatiaScheduler catiaScheduler) : Hub
{
    private const string DefaultHubPath = "/agentcontrolhub";

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
        var command = new OpenEditorFileCommand(editorFileName);
        await channel.Writer.WriteAsync(command, Context.ConnectionAborted);

        var receipt = await command.Completion.WaitAsync(Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveEditorFileOpened", receipt);
        await SendCatiaComStatusAsync();
        await SendServerMessageAsync($"COM/CATIA hat '{receipt.EditorFileName}' geoeffnet.");
    }

    public async Task SaveEditorInput(string input)
    {
        var command = new SaveEditorInputCommand(input);
        await channel.Writer.WriteAsync(command, Context.ConnectionAborted);

        var receipt = await command.Completion.WaitAsync(Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveEditorSaveResult", receipt);
        await SendCatiaComStatusAsync();
        await SendServerMessageAsync($"COM/CATIA hat '{receipt.EditorFileName}' gespeichert.");
    }

    private Task SendConnectionInfoAsync()
    {
        var hubPath = Context.GetHttpContext()?.Request.Path.Value ?? DefaultHubPath;
        return Clients.Caller.SendAsync("ReceiveConnectionInfo", Context.ConnectionId, hubPath);
    }

    private Task SendCatiaComStatusAsync()
    {
        return Clients.Caller.SendAsync("ReceiveCatiaComStatus", catiaScheduler.GetStatus());
    }

    private Task SendServerMessageAsync(string message)
    {
        return Clients.Caller.SendAsync("ReceiveAgentMessage", "server", message, DateTimeOffset.UtcNow);
    }
}

using Microsoft.AspNetCore.SignalR.Client;

namespace WebApp.Client.Services;

public sealed class AgentControlHubService : IAsyncDisposable
{
    private const string HttpsBaseUrl = "https://localhost:7266";
    private const string HttpBaseUrl = "http://localhost:5158";
    private const string AgentHubPath = "/agentcontrolhub";
    private const string VersionPath = "/version";

    private static readonly (string BaseUrl, string HubUrl)[] CandidateEndpoints =
    [
        (HttpsBaseUrl, HttpsBaseUrl + AgentHubPath),
        (HttpBaseUrl, HttpBaseUrl + AgentHubPath)
    ];

    private readonly List<AgentHubMessage> messages = [];
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private HubConnection? hubConnection;

    public event Action? Changed;

    public IReadOnlyList<AgentHubMessage> Messages => messages;

    public string ConnectionStatus { get; private set; } = "Noch nicht gestartet";

    public string? ConnectedHubUrl { get; private set; }

    public string? ConnectionId { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool IsConnecting { get; private set; }

    public bool IsOpeningEditorFile { get; private set; }

    public bool IsSavingEditorInput { get; private set; }

    public bool IsConnected => hubConnection?.State == HubConnectionState.Connected;

    public CatiaComServiceStatus? CatiaComStatus { get; private set; }

    public CatiaComOpenResult? LastOpenedFileResult { get; private set; }

    public CatiaComSaveResult? LastSaveResult { get; private set; }

    public async Task EnsureStartedAsync()
    {
        await connectionLock.WaitAsync();

        try
        {
            if (hubConnection is not null)
            {
                if (hubConnection.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                {
                    return;
                }

                IsConnecting = true;
                ConnectionStatus = "Verbinde erneut...";
                ErrorMessage = null;
                NotifyChanged();

                await hubConnection.StartAsync();
                await hubConnection.SendAsync("RequestConnectionInfo");
                ConnectionStatus = "Verbunden";
                ErrorMessage = null;
                return;
            }

            IsConnecting = true;
            ConnectionStatus = "Verbinde...";
            ErrorMessage = null;
            NotifyChanged();

            var endpoint = await ResolveEndpointAsync();
            hubConnection = BuildConnection(endpoint.HubUrl);
            RegisterHandlers(hubConnection);

            await hubConnection.StartAsync();
            await hubConnection.SendAsync("RequestConnectionInfo");

            ConnectedHubUrl = endpoint.HubUrl;
            ConnectionStatus = "Verbunden";
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Die globale SignalR-Verbindung konnte nicht aufgebaut werden: {ex.Message}";
            ConnectionStatus = "Fehler";
            ConnectedHubUrl = null;
            ConnectionId = null;

            if (hubConnection is not null)
            {
                await hubConnection.DisposeAsync();
                hubConnection = null;
            }
        }
        finally
        {
            IsConnecting = false;
            NotifyChanged();
            connectionLock.Release();
        }
    }

    public async Task ReconnectAsync()
    {
        await connectionLock.WaitAsync();

        try
        {
            if (hubConnection is not null)
            {
                await hubConnection.DisposeAsync();
                hubConnection = null;
            }

            ConnectedHubUrl = null;
            ConnectionId = null;
            ConnectionStatus = "Noch nicht gestartet";
            ErrorMessage = null;
        }
        finally
        {
            connectionLock.Release();
        }

        await EnsureStartedAsync();
    }

    public async Task RequestConnectionInfoAsync()
    {
        if (hubConnection is not { State: HubConnectionState.Connected })
        {
            throw new InvalidOperationException("Die globale SignalR-Verbindung ist nicht aktiv.");
        }

        await hubConnection.SendAsync("RequestConnectionInfo");
    }

    public async Task SendMessageAsync(string message)
    {
        if (hubConnection is not { State: HubConnectionState.Connected })
        {
            throw new InvalidOperationException("Die globale SignalR-Verbindung ist nicht aktiv.");
        }

        await hubConnection.SendAsync("SendClientMessage", message);
    }

    public async Task OpenEditorFileAsync(string editorFileName)
    {
        if (hubConnection is not { State: HubConnectionState.Connected })
        {
            throw new InvalidOperationException("Die globale SignalR-Verbindung ist nicht aktiv.");
        }

        IsOpeningEditorFile = true;
        ErrorMessage = null;
        NotifyChanged();

        try
        {
            await hubConnection.InvokeAsync("OpenEditorFile", editorFileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Das Oeffnen der COM/CATIA-Datei ist fehlgeschlagen: {ex.Message}";
            throw;
        }
        finally
        {
            IsOpeningEditorFile = false;
            NotifyChanged();
        }
    }

    public async Task SaveEditorInputAsync(string input)
    {
        if (hubConnection is not { State: HubConnectionState.Connected })
        {
            throw new InvalidOperationException("Die globale SignalR-Verbindung ist nicht aktiv.");
        }

        IsSavingEditorInput = true;
        ErrorMessage = null;
        NotifyChanged();

        try
        {
            await hubConnection.InvokeAsync("SaveEditorInput", input);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Der COM/CATIA-Speichervorgang ist fehlgeschlagen: {ex.Message}";
            throw;
        }
        finally
        {
            IsSavingEditorInput = false;
            NotifyChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }

        connectionLock.Dispose();
    }

    private HubConnection BuildConnection(string hubUrl)
    {
        return new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();
    }

    private void RegisterHandlers(HubConnection connection)
    {
        connection.On<string, string, DateTimeOffset>("ReceiveAgentMessage", (source, message, timestamp) =>
        {
            messages.Add(new AgentHubMessage(timestamp, source, message));
            ErrorMessage = null;
            NotifyChanged();
        });

        connection.On<string, string>("ReceiveConnectionInfo", (connectionId, hubPath) =>
        {
            ConnectionId = connectionId;
            ConnectionStatus = $"Verbunden ({hubPath})";
            ErrorMessage = null;
            NotifyChanged();
        });

        connection.On<CatiaComServiceStatus>("ReceiveCatiaComStatus", status =>
        {
            CatiaComStatus = status;
            ErrorMessage = null;
            NotifyChanged();
        });

        connection.On<CatiaComOpenResult>("ReceiveEditorFileOpened", result =>
        {
            LastOpenedFileResult = result;
            messages.Add(new AgentHubMessage(result.OpenedAtUtc, "catia-com", result.Acknowledgement));
            ErrorMessage = null;
            NotifyChanged();
        });

        connection.On<CatiaComSaveResult>("ReceiveEditorSaveResult", result =>
        {
            LastSaveResult = result;
            messages.Add(new AgentHubMessage(result.SavedAtUtc, "catia-com", result.Acknowledgement));
            ErrorMessage = null;
            NotifyChanged();
        });

        connection.Reconnecting += error =>
        {
            ConnectionStatus = "Verbindung unterbrochen, versuche neu zu verbinden...";
            ErrorMessage = error?.Message;
            NotifyChanged();
            return Task.CompletedTask;
        };

        connection.Reconnected += async _ =>
        {
            ConnectionStatus = "Verbunden";
            ErrorMessage = null;

            if (hubConnection is not null)
            {
                await hubConnection.SendAsync("RequestConnectionInfo");
            }

            NotifyChanged();
        };

        connection.Closed += error =>
        {
            ConnectionStatus = "Verbindung getrennt";
            ErrorMessage = error?.Message;
            NotifyChanged();
            return Task.CompletedTask;
        };
    }

    private static async Task<(string BaseUrl, string HubUrl)> ResolveEndpointAsync()
    {
        foreach (var endpoint in CandidateEndpoints)
        {
            if (await IsEndpointAvailableAsync(endpoint.BaseUrl))
            {
                return endpoint;
            }
        }

        throw new InvalidOperationException("Kein lokaler API-Endpunkt fuer den globalen Agent-Hub war erreichbar.");
    }

    private static async Task<bool> IsEndpointAvailableAsync(string baseUrl)
    {
        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var response = await httpClient.GetAsync(VersionPath);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}

public sealed record AgentHubMessage(DateTimeOffset Timestamp, string Source, string Message);

public sealed record CatiaComServiceStatus(
    string ProtocolName,
    string EditorRootPath,
    bool IsRunning,
    int? StaThreadId,
    string? OpenedEditorFileName,
    string? OpenedEditorFilePath);

public sealed record CatiaComOpenResult(
    string ProtocolName,
    string EditorFileName,
    string EditorFilePath,
    long ExistingLength,
    DateTimeOffset OpenedAtUtc,
    int StaThreadId,
    string Acknowledgement);

public sealed record CatiaComSaveResult(
    string ProtocolName,
    string EditorFileName,
    string EditorFilePath,
    int CharacterCount,
    DateTimeOffset SavedAtUtc,
    int StaThreadId,
    string Acknowledgement);

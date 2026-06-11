using System.Collections.Concurrent;
using System.Threading.Channels;
using Local.Commands;
using Local.Lib;
using Microsoft.AspNetCore.SignalR;

namespace Local.Services;

public record CatiaJobDto(string JobId, string Status, string? Result = null, string? ErrorCode = null, string? ErrorMessage = null);

public class CatiaHub : Hub
{
    private readonly Channel<CatiaCommand> _channel;
    private readonly ICatiaProgressReporter _progressReporter;
    private static readonly ConcurrentDictionary<string, CatiaJobDto> _jobCache = new();

    public CatiaHub(Channel<CatiaCommand> channel, ICatiaProgressReporter progressReporter)
    {
        _channel = channel;
        _progressReporter = progressReporter;
    }

    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
        
        if (_jobCache.TryGetValue(jobId, out var cachedJob))
        {
            await Clients.Caller.SendAsync("JobStatusUpdate", cachedJob);
        }
    }
}
using System.Runtime.Versioning;
using System.Threading.Channels;
using Local.Lib;

namespace Local.Services;

[SupportedOSPlatform("windows")]
public class CatiaChannelWorker(Channel<CatiaCommand> channel, CatiaScheduler scheduler) : BackgroundService
{
    private readonly Channel<CatiaCommand> _channel = channel;
    private readonly CatiaScheduler _scheduler = scheduler;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _channel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_channel.Reader.TryRead(out var command))
            {
                _scheduler.Enqueue(command);
            }
        }
    }
}
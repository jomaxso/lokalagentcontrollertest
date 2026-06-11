using Local.Services;
using Microsoft.AspNetCore.SignalR;

namespace Local.Lib;

public interface ICatiaProgressReporter
{
    void ReportProgress(string jobId, string message, int percent);
}

public class SignalRProgressReporter : ICatiaProgressReporter
{
    private readonly IHubContext<CatiaHub> _hubContext;
    public SignalRProgressReporter(IHubContext<CatiaHub> hubContext) => _hubContext = hubContext;

    public void ReportProgress(string jobId, string message, int percent)
    {
        // Sendet Fortschritt an die spezifische SignalR-Gruppe des Jobs
        _ = _hubContext.Clients.Group(jobId).SendAsync("JobProgress", new { Message = message, Percent = percent });
    }
}
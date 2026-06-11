using System.Runtime.Versioning;
using System.Threading.Channels;
using Local.Lib;
using Local.Services;

var builder = WebApplication.CreateBuilder(args);

// CORS freischalten für die lokale Web-App (z.B. Port 3000)
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalWebAccess", policy =>
    {
        policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                uri.Host.Equals("webapp.icysea-083ad266.germanywestcentral.azurecontainerapps.io", StringComparison.OrdinalIgnoreCase))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // Lebenswichtig für WebSockets / SignalR
    });
});

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// 1. Thread-sicheren Datchannel (MTA-Welt) registrieren
builder.Services.AddSingleton(_ =>
    Channel.CreateBounded<CatiaCommand>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    }));

// 2. Progress-Infrastruktur mitsamt Hub-Zugriff hinterlegen
builder.Services.AddSingleton<ICatiaProgressReporter, SignalRProgressReporter>();

// 3. STA-Engine-Thread bereitstellen
builder.Services.AddSingleton<CatiaScheduler>();

// 4. Den permanenten Channel-Consumer-Dienst (HostedService) aktivieren
builder.Services.AddHostedService<CatiaChannelWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("LocalWebAccess");
app.MapHub<CatiaHub>("/catiaHub");

app.Run();

[SupportedOSPlatform("windows")]
partial class Program;
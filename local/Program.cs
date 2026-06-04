using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebAppClient", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("webapp.bluepond-6445599a.germanywestcentral.azurecontainerapps.io", StringComparison.OrdinalIgnoreCase)
            ))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Public HTTPS pages calling localhost trigger a Private Network Access preflight in the browser.
// We add this opt-in header after CORS so the Azure-hosted UI can reach the user's local API.
app.Use(async (context, next) =>
{
    var isPrivateNetworkPreflight =
        HttpMethods.IsOptions(context.Request.Method) &&
        string.Equals(context.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase);

    context.Response.OnStarting(() =>
    {
        if (isPrivateNetworkPreflight && context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
        {
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            context.Response.Headers.Append("Vary", "Access-Control-Request-Private-Network");
        }

        return Task.CompletedTask;
    });

    await next();
});

app.UseCors("WebAppClient");

app.UseHttpsRedirection();

app.MapGet("/version", () =>
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
    return TypedResults.Ok(new ApiVersionResponse(version));
})
.WithName("GetVersion");

app.MapGet("/weatherforecast", (ILogger<WeatherForecast> logger) =>
{
    logger.LogInformation("Fetching weather forecast");

    var forecast = WeatherData.CreateForecasts();

    logger.LogInformation("Weather forecast fetched successfully");
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/weatherforecast-sse", (ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("WeatherSse");

    logger.LogInformation("Weather SSE stream connected");

    return TypedResults.ServerSentEvents(WeatherData.StreamForecasts(logger, cancellationToken), eventType: "forecast");
});

app.MapHub<WeatherHub>("/weatherhub");

app.Run();

record ApiVersionResponse(string Version);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

sealed class WeatherHub(ILogger<WeatherHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Weather hub connected: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("ReceiveWeatherForecast", WeatherData.CreateForecasts());
        await base.OnConnectedAsync();
    }

    public Task RequestWeatherForecast()
    {
        return Clients.Caller.SendAsync("ReceiveWeatherForecast", WeatherData.CreateForecasts());
    }
}

static class WeatherData
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    public static WeatherForecast[] CreateForecasts()
    {
        return Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                Summaries[Random.Shared.Next(Summaries.Length)]
            ))
            .ToArray();
    }

    public static async IAsyncEnumerable<WeatherForecast> StreamForecasts(
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {

            foreach (var forecast in CreateForecasts())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                yield return forecast;
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }

        }
        finally
        {
            logger.LogInformation("Weather SSE stream disconnected");
        }
    }
}

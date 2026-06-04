using Microsoft.AspNetCore.SignalR;

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

app.UseCors("WebAppClient");

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", (ILogger<WeatherForecast> logger) =>
{
    logger.LogInformation("Fetching weather forecast");

    var forecast = WeatherData.CreateForecasts();

    logger.LogInformation("Weather forecast fetched successfully");
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapHub<WeatherHub>("/weatherhub");

app.Run();

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
}

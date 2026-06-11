using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Local.Services;
using Local.Lib;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebAppClient", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("webapp.icysea-083ad266.germanywestcentral.azurecontainerapps.io", StringComparison.OrdinalIgnoreCase)
            ))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddSingleton(_ =>
    Channel.CreateBounded<CatiaCommand>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    }));

builder.Services.AddSingleton<CatiaScheduler>();
builder.Services.AddHostedService<CatiaChannelWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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

app.MapHub<AgentControlHub>("/agentcontrolhub");

app.Run();

record ApiVersionResponse(string Version);

[SupportedOSPlatform("windows")]
partial class Program;
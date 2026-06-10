using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebApp.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<AgentControlHubService>();

await builder.Build().RunAsync();

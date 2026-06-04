#:sdk Aspire.AppHost.Sdk@13.4.2
#:package Aspire.Hosting.Azure.AppContainers@13.4.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject("webapp", "web/WebApp/WebApp.csproj")
    .WithExternalHttpEndpoints();

if (builder.ExecutionContext.IsRunMode)
{
    builder.AddProject("local", "local/local.csproj");
}

builder.AddAzureContainerAppEnvironment("aca-env");

builder.Build().Run();

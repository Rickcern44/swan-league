#:sdk Aspire.AppHost.Sdk@13.5.4
#:property AspireUseCliBundle=true
#:package Aspire.Hosting.JavaScript@13.5.4

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject("api", "src/Api/Api.csproj")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddViteApp("web", "src/web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

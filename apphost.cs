#:sdk Aspire.AppHost.Sdk@13.5.4
#:property AspireUseCliBundle=true
#:package Aspire.Hosting.JavaScript@13.5.4

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject("api", "src/Swan.Api/Swan.Api.csproj")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddProject("worker", "src/Swan.Worker/Swan.Worker.csproj");

builder.AddViteApp("web", "src/Swan.Web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

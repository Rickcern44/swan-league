#:sdk Aspire.AppHost.Sdk@13.5.4
#:property AspireUseCliBundle=true

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject("api", "src/Api/Api.csproj")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();

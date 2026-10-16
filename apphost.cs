#:sdk Aspire.AppHost.Sdk@13.6.1
#:property AspireUseCliBundle=true
#:package Aspire.Hosting.JavaScript@13.6.1
#:package Aspire.Hosting.PostgreSQL@13.6.1
#:package CommunityToolkit.Aspire.Hosting.MailPit@13.6.1-beta.930

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();
var database = postgres.AddDatabase("swan");
var mailpit = builder.AddMailPit("mailpit");

var api = builder.AddProject("api", "src/Swan.Api/Swan.Api.csproj")
    .WithReference(database)
    .WithReference(mailpit)
    .WaitFor(database)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddProject("worker", "src/Swan.Worker/Swan.Worker.csproj")
    .WithReference(database)
    .WaitFor(database);

builder.AddViteApp("web", "src/Swan.Web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

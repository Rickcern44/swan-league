var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var useVolumes = !bool.TryParse(builder.Configuration["UseVolumes"], out var configuredUseVolumes) || configuredUseVolumes;
if (useVolumes)
{
    postgres.WithDataVolume();
}

var database = postgres.AddDatabase("swan");
var mailpit = builder.AddMailPit("mailpit");

var api = builder.AddProject<Projects.Swan_Api>("api")
    .WithReference(database)
    .WithReference(mailpit)
    .WaitFor(database)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Swan_Worker>("worker")
    .WithReference(database)
    .WaitFor(database);

builder.AddViteApp("web", "../Swan.Web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

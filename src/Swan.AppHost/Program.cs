var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
// An Aspire-generated password changes when the AppHost state is recreated. Keep development
// databases disposable by default so an old volume can never reject that new credential. Set
// UseVolumes=true explicitly only when local database persistence is intentional.
var useVolumes = bool.TryParse(builder.Configuration["UseVolumes"], out var configuredUseVolumes) && configuredUseVolumes;
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

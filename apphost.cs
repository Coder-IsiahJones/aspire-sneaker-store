#:package Aspire.Hosting.AppHost@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.Dotnet@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.JavaScript@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.MongoDB@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.RabbitMQ@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.Redis@17.1.0-preview.1.26560.7
#:property AspireUseCliBundle=true

var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume();

var sneakerDb = mongo.AddDatabase("sneakerdb");

// The data volume keeps shoppers' bags across restarts.
var redis = builder.AddRedis("redis")
                   .WithDataVolume();

// The management plugin is RabbitMQ's own web UI. The dashboard links to it from this resource.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

var api = builder.AddDotnetProject("api", "./SneakerStore.Api/SneakerStore.Api.csproj")
    .WithReference(sneakerDb)
    .WaitFor(sneakerDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health")
    // Adds a "Swagger UI" link next to the API in the dashboard.
    .WithUrlForEndpoint("http", _ => new() { Url = "/swagger", DisplayText = "Swagger UI" });

// Two copies of the worker consume the same queues, so RabbitMQ deals the orders out between them.
builder.AddDotnetProject("worker", "./SneakerStore.Worker/SneakerStore.Worker.csproj")
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithReference(sneakerDb)
    .WaitFor(sneakerDb)
    .WithReplicas(2);

builder.AddViteApp("web", "./web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

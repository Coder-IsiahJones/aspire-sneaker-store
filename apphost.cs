#:package Aspire.Hosting.AppHost@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.Dotnet@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.JavaScript@17.1.0-preview.1.26560.7
#:package Aspire.Hosting.MongoDB@17.1.0-preview.1.26560.7
#:property AspireUseCliBundle=true

var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume();

var sneakerDb = mongo.AddDatabase("sneakerdb");

var api = builder.AddDotnetProject("api", "./SneakerStore.Api/SneakerStore.Api.csproj")
    .WithReference(sneakerDb)
    .WaitFor(sneakerDb)
    .WithHttpHealthCheck("/health");

builder.AddViteApp("web", "./web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

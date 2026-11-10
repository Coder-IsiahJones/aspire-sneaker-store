#:package Aspire.Hosting.JavaScript@13.6.5
#:package Aspire.Hosting.MongoDB@13.6.5
#:sdk Aspire.AppHost.Sdk@13.6.5
#:property AspireUseCliBundle=true
#:project ./SneakerStore.Api/SneakerStore.Api.csproj

var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume();

var sneakerDb = mongo.AddDatabase("sneakerdb");

var api = builder.AddProject<Projects.SneakerStore_Api>("api")
    .WithReference(sneakerDb)
    .WaitFor(sneakerDb)
    .WithHttpHealthCheck("/health");

builder.AddViteApp("web", "./web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

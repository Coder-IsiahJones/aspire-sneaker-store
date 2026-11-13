using RabbitMQ.Client;
using SneakerStore.Messaging;
using SneakerStore.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// The same resource names as apphost.cs. Aspire supplies both connection strings.
builder.AddRabbitMQClient("messaging");
builder.AddMongoDBClient("sneakerdb");

builder.Services.AddSingleton(services => new EventPublisher(services.GetRequiredService<IConnection>));
builder.Services.AddSingleton<OrderProgress>();
builder.Services.AddSingleton<IReceiptSender, SimulatedReceiptSender>();

// Two consumers in one process, each on its own queue and its own channel.
builder.Services.AddHostedService<ReceiptWorker>();
builder.Services.AddHostedService<WarehouseWorker>();

builder.Build().Run();

// ServiceDefaults brings in ASP.NET Core, which would otherwise make this Program public. The test
// project references the API and the worker, and it can only see one public Program.
internal partial class Program;

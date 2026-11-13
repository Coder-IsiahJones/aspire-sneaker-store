using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using RabbitMQ.Client;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace SneakerStore.Api.Tests;

/// <summary>
/// Starts one MongoDB, one Redis and one RabbitMQ container for the whole test run and an
/// in-memory copy of the API pointed at them. The API seeds its database on startup, the same
/// way it does under Aspire.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.0").Build();

    // The tag Aspire starts for AddRabbitMQ().WithManagementPlugin(), so tests see the same broker.
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3-management").Build();

    private WebApplicationFactory<Program>? _factory;

    public MongoClient Client { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    /// <summary>A broker connection of the test's own, separate from the one the API holds.</summary>
    public Task<IConnection> ConnectToBrokerAsync(CancellationToken cancellationToken = default) =>
        new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) }.CreateConnectionAsync(cancellationToken);

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_container.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());

        Client = new MongoClient(_container.GetConnectionString());

        // AddMongoDBClient("sneakerdb") reads ConnectionStrings:sneakerdb and takes the
        // database name from the URL, so the test connection string has to include it.
        var apiConnectionString = new MongoUrlBuilder(_container.GetConnectionString())
        {
            DatabaseName = "sneakerdb",
            AuthenticationSource = "admin",
        }.ToString();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:sneakerdb", apiConnectionString);
            builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
            builder.UseSetting("ConnectionStrings:messaging", _rabbit.GetConnectionString());
            // Every test client shares one address, so the real limit would trip immediately.
            builder.UseSetting("Auth:AttemptsPerMinute", "100000");
        });
    }

    /// <summary>A database no other test touches, for tests that need an empty collection.</summary>
    public IMongoDatabase CreateIsolatedDatabase() =>
        Client.GetDatabase($"test-{Guid.NewGuid():N}");

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Client?.Dispose();
        await _container.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbit.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "MongoDB";
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.Redis;

namespace SneakerStore.Api.Tests;

/// <summary>
/// Starts one MongoDB container for the whole test run and an in-memory copy of the API
/// pointed at it. The API seeds its database on startup, the same way it does under Aspire.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.0").Build();

    private WebApplicationFactory<Program>? _factory;

    public MongoClient Client { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_container.StartAsync(), _redis.StartAsync());

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
    }
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "MongoDB";
}

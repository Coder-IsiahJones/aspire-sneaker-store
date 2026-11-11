using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Tests;

[Collection(MongoCollection.Name)]
public sealed class SneakerSeederTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SneakerSeeder CreateSeeder(IMongoCollection<Sneaker> collection) =>
        new(collection, NullLogger<SneakerSeeder>.Instance);

    [Fact]
    public async Task SeedAsync_EmptyCollection_InsertsEverySeedSneaker()
    {
        var collection = fixture.CreateIsolatedDatabase().GetCollection<Sneaker>("sneakers");

        await CreateSeeder(collection).SeedAsync(Ct);

        var skus = await collection.Find(FilterDefinition<Sneaker>.Empty)
            .Project(s => s.Sku)
            .ToListAsync(Ct);
        Assert.Equal(SeedData.Sneakers().Select(s => s.Sku).Order(), skus.Order());
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicate()
    {
        var collection = fixture.CreateIsolatedDatabase().GetCollection<Sneaker>("sneakers");
        var seeder = CreateSeeder(collection);

        await seeder.SeedAsync(Ct);
        await seeder.SeedAsync(Ct);

        var count = await collection.CountDocumentsAsync(FilterDefinition<Sneaker>.Empty, cancellationToken: Ct);
        Assert.Equal(SeedData.Sneakers().Count, count);
    }

    [Fact]
    public async Task SeedAsync_CollectionAlreadyHasData_LeavesItAlone()
    {
        var collection = fixture.CreateIsolatedDatabase().GetCollection<Sneaker>("sneakers");
        var existing = SeedData.Sneakers()[0];
        await collection.InsertOneAsync(existing, cancellationToken: Ct);

        await CreateSeeder(collection).SeedAsync(Ct);

        var skus = await collection.Find(FilterDefinition<Sneaker>.Empty)
            .Project(s => s.Sku)
            .ToListAsync(Ct);
        Assert.Equal([existing.Sku], skus);
    }

    [Fact]
    public async Task SeedAsync_CreatesUniqueSkuIndex()
    {
        var collection = fixture.CreateIsolatedDatabase().GetCollection<Sneaker>("sneakers");
        await CreateSeeder(collection).SeedAsync(Ct);

        var duplicate = SeedData.Sneakers()[0];

        var error = await Assert.ThrowsAsync<MongoWriteException>(
            () => collection.InsertOneAsync(duplicate, cancellationToken: Ct));
        Assert.Equal(ServerErrorCategory.DuplicateKey, error.WriteError.Category);
    }
}

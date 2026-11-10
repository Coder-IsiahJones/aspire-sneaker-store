using MongoDB.Driver;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Data;

public sealed class SneakerSeeder(IMongoCollection<Sneaker> sneakers, ILogger<SneakerSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // A unique SKU means a second insert of the same shoe fails instead of duplicating it.
        await sneakers.Indexes.CreateOneAsync(
            new CreateIndexModel<Sneaker>(
                Builders<Sneaker>.IndexKeys.Ascending(s => s.Sku),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        var existing = await sneakers.CountDocumentsAsync(
            FilterDefinition<Sneaker>.Empty,
            cancellationToken: cancellationToken);

        if (existing > 0)
        {
            logger.LogInformation("Catalog already has {Count} sneakers, skipping seed", existing);
            return;
        }

        var seed = SeedData.Sneakers();
        await sneakers.InsertManyAsync(seed, cancellationToken: cancellationToken);
        logger.LogInformation("Seeded {Count} sneakers", seed.Count);
    }
}

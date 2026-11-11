using MongoDB.Driver;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Data;

public sealed class DropSeeder(
    IMongoCollection<Drop> drops,
    IMongoCollection<DropEntry> entries,
    ILogger<DropSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // One entry per entrant per drop, enforced by the database so simultaneous
        // requests can't slip through. EntrantId leads so the "my entries" lookup can use it.
        await entries.Indexes.CreateOneAsync(
            new CreateIndexModel<DropEntry>(
                Builders<DropEntry>.IndexKeys.Ascending(e => e.EntrantId).Ascending(e => e.DropId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        var now = DateTime.UtcNow;

        // The demo schedule is relative to the seed time and the data outlives restarts,
        // so without this every drop would eventually be stuck in the past.
        var live = await drops.CountDocumentsAsync(
            d => d.OpensAt <= now && d.ClosesAt > now,
            cancellationToken: cancellationToken);

        if (live > 0)
        {
            logger.LogInformation("{Count} drops are live, keeping the schedule", live);
            return;
        }

        await entries.DeleteManyAsync(FilterDefinition<DropEntry>.Empty, cancellationToken);
        await drops.DeleteManyAsync(FilterDefinition<Drop>.Empty, cancellationToken);

        var seed = SeedData.Drops(now);
        await drops.InsertManyAsync(seed.Select(s => s.Drop), cancellationToken: cancellationToken);

        var demoEntries = seed.SelectMany(s => Enumerable.Range(0, s.DemoEntrants).Select(i => new DropEntry
        {
            DropId = s.Drop.Id!,
            EntrantId = Guid.NewGuid().ToString("N"),
            Size = s.Drop.Sneaker.Sizes[i % s.Drop.Sneaker.Sizes.Length],
            EnteredAt = now,
        })).ToList();

        if (demoEntries.Count > 0)
        {
            await entries.InsertManyAsync(demoEntries, cancellationToken: cancellationToken);
        }

        logger.LogInformation("Seeded {Count} drops", seed.Count);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Tests;

[Collection(MongoCollection.Name)]
public sealed class DropSeederTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (IMongoCollection<Drop> Drops, IMongoCollection<DropEntry> Entries, DropSeeder Seeder) Create()
    {
        var database = fixture.CreateIsolatedDatabase();
        var drops = database.GetCollection<Drop>("drops");
        var entries = database.GetCollection<DropEntry>("dropEntries");
        return (drops, entries, new DropSeeder(drops, entries, NullLogger<DropSeeder>.Instance));
    }

    private static Drop MakeDrop(DateTime opensAt, DateTime closesAt)
    {
        var sneaker = SeedData.Sneakers()[0];
        sneaker.Id = ObjectId.GenerateNewId().ToString();
        return new Drop { Sneaker = sneaker, Pairs = 1, OpensAt = opensAt, ClosesAt = closesAt };
    }

    [Fact]
    public async Task SeedAsync_Empty_InsertsScheduleWithALiveDrop()
    {
        var (drops, entries, seeder) = Create();

        await seeder.SeedAsync(Ct);

        var now = DateTime.UtcNow;
        var seeded = await drops.Find(FilterDefinition<Drop>.Empty).ToListAsync(Ct);
        Assert.Equal(SeedData.Drops(now).Count, seeded.Count);
        Assert.Contains(seeded, d => d.PhaseAt(now) == DropPhase.Open);
        Assert.Contains(seeded, d => d.PhaseAt(now) == DropPhase.Upcoming);
        Assert.Equal(
            SeedData.Drops(now).Sum(s => s.DemoEntrants),
            await entries.CountDocumentsAsync(FilterDefinition<DropEntry>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task SeedAsync_DropIsLive_LeavesScheduleAlone()
    {
        var (drops, _, seeder) = Create();
        var now = DateTime.UtcNow;
        var live = MakeDrop(now.AddMinutes(-1), now.AddMinutes(5));
        await drops.InsertOneAsync(live, cancellationToken: Ct);

        await seeder.SeedAsync(Ct);

        var ids = await drops.Find(FilterDefinition<Drop>.Empty).Project(d => d.Id).ToListAsync(Ct);
        Assert.Equal([live.Id], ids);
    }

    [Fact]
    public async Task SeedAsync_NothingLive_ReplacesFinishedSchedule()
    {
        var (drops, entries, seeder) = Create();
        var now = DateTime.UtcNow;
        var finished = MakeDrop(now.AddDays(-2), now.AddDays(-1));
        await drops.InsertOneAsync(finished, cancellationToken: Ct);
        await entries.InsertOneAsync(
            new DropEntry { DropId = finished.Id!, EntrantId = "old", Size = 9, EnteredAt = now },
            cancellationToken: Ct);

        await seeder.SeedAsync(Ct);

        Assert.Equal(0, await drops.CountDocumentsAsync(d => d.Id == finished.Id, cancellationToken: Ct));
        Assert.Equal(0, await entries.CountDocumentsAsync(e => e.EntrantId == "old", cancellationToken: Ct));
    }

    [Fact]
    public async Task SeedAsync_CreatesUniqueEntryIndex()
    {
        var (_, entries, seeder) = Create();
        await seeder.SeedAsync(Ct);
        var dropId = ObjectId.GenerateNewId().ToString();
        DropEntry Entry() => new() { DropId = dropId, EntrantId = "same", Size = 9, EnteredAt = DateTime.UtcNow };
        await entries.InsertOneAsync(Entry(), cancellationToken: Ct);

        var error = await Assert.ThrowsAsync<MongoWriteException>(
            () => entries.InsertOneAsync(Entry(), cancellationToken: Ct));

        Assert.Equal(ServerErrorCategory.DuplicateKey, error.WriteError.Category);
    }
}

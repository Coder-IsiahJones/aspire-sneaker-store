using System.Security.Cryptography;
using MongoDB.Driver;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Data;

public sealed class DropRaffle(IMongoCollection<Drop> drops, IMongoCollection<DropEntry> entries)
{
    /// <summary>
    /// Draws winners for a closed drop the first time it's read after closing, so no
    /// background job is needed. Returns the drop with its winners set.
    /// </summary>
    public async Task<Drop> DrawAsync(Drop drop, CancellationToken cancellationToken = default)
    {
        if (drop.Winners is not null)
        {
            return drop;
        }

        var entrants = (await entries.Find(e => e.DropId == drop.Id)
            .Project(e => e.EntrantId)
            .ToListAsync(cancellationToken)).ToArray();

        RandomNumberGenerator.Shuffle<string>(entrants);
        var winners = entrants.Take(drop.Pairs).ToList();

        // Two requests can reach this at once. Only the first write lands, and both
        // re-read the stored result, so a draw can never change after it's made.
        await drops.UpdateOneAsync(
            d => d.Id == drop.Id && d.Winners == null,
            Builders<Drop>.Update.Set(d => d.Winners, winners),
            cancellationToken: cancellationToken);

        return await drops.Find(d => d.Id == drop.Id).FirstAsync(cancellationToken);
    }
}

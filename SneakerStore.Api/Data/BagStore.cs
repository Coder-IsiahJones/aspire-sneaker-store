using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Models;
using StackExchange.Redis;

namespace SneakerStore.Api.Data;

public enum BagAddResult
{
    Added,
    NotFound,
    InvalidSize,
    NotWinner,
    AlreadyInBag,
    AlreadyOrdered,
}

/// <summary>A bag moved aside under a temporary key, so nothing else can touch it.</summary>
public sealed record TakenBag(string Key, List<BagLine> Lines);

/// <summary>
/// Keeps each shopper's bag in a Redis hash of "sneakerId:size" to quantity. Only ids are
/// stored, so names and prices are always read fresh from MongoDB.
/// </summary>
public sealed class BagStore(
    IConnectionMultiplexer redis,
    IMongoCollection<Sneaker> sneakers,
    IMongoCollection<Drop> drops,
    IMongoCollection<DropEntry> entries,
    DropRaffle raffle)
{
    // Sliding: every add starts the clock again, so abandoned bags clean themselves up.
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private IDatabase Db => redis.GetDatabase();

    public static string KeyFor(string shopperId) => $"bag:{shopperId}";

    private static string Field(string sneakerId, double size) =>
        $"{sneakerId}:{size.ToString(CultureInfo.InvariantCulture)}";

    public Task<List<BagLine>> GetAsync(string shopperId) => ReadAsync(KeyFor(shopperId));

    private async Task<List<BagLine>> ReadAsync(RedisKey key)
    {
        var stored = await Db.HashGetAllAsync(key);
        if (stored.Length == 0)
        {
            return [];
        }

        var items = stored.Select(entry => Parse(entry)).ToList();
        var ids = items.Where(i => i.SneakerId is not null).Select(i => i.SneakerId!).Distinct().ToList();
        var found = await ResolveAsync(ids);

        var lines = new List<BagLine>();
        var stale = new List<RedisValue>();
        foreach (var item in items)
        {
            if (item.SneakerId is not null && found.TryGetValue(item.SneakerId, out var sneaker))
            {
                lines.Add(new BagLine(item.Field, sneaker, item.Size, item.Quantity));
            }
            else
            {
                stale.Add(item.Field);
            }
        }

        // A shoe can disappear, for example when the demo drops are reseeded.
        if (stale.Count > 0)
        {
            await Db.HashDeleteAsync(key, [.. stale]);
        }

        return [.. lines.OrderBy(l => l.Sneaker.Name, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Size)];
    }

    public async Task<BagAddResult> AddAsync(string shopperId, string sneakerId, double size)
    {
        if (!ObjectId.TryParse(sneakerId, out _))
        {
            return BagAddResult.NotFound;
        }

        var key = KeyFor(shopperId);
        var field = Field(sneakerId, size);

        var sneaker = await sneakers.Find(s => s.Id == sneakerId).FirstOrDefaultAsync();
        if (sneaker is not null)
        {
            if (!sneaker.Sizes.Contains(size))
            {
                return BagAddResult.InvalidSize;
            }

            await Db.HashIncrementAsync(key, field);
        }
        else
        {
            var drop = await drops.Find(d => d.Sneaker.Id == sneakerId).FirstOrDefaultAsync();
            if (drop is null)
            {
                return BagAddResult.NotFound;
            }

            if (drop.PhaseAt(DateTime.UtcNow) != DropPhase.Closed)
            {
                return BagAddResult.NotWinner;
            }

            var drawn = await raffle.DrawAsync(drop);
            if (drawn.Claimed.Contains(shopperId))
            {
                return BagAddResult.AlreadyOrdered;
            }

            if (!await WonAsync(drawn, shopperId, size))
            {
                return BagAddResult.NotWinner;
            }

            // A win is for one pair, so the line can only ever hold a quantity of 1.
            if (!await Db.HashSetAsync(key, field, 1, When.NotExists))
            {
                return BagAddResult.AlreadyInBag;
            }
        }

        await Db.KeyExpireAsync(key, Lifetime);
        return BagAddResult.Added;
    }

    public Task RemoveAsync(string shopperId, string lineKey) =>
        Db.HashDeleteAsync(KeyFor(shopperId), lineKey);

    /// <summary>
    /// Atomically moves the shopper's bag out of reach, so only one caller can ever act on it.
    /// Returns null if there is no bag. Renaming keeps the expiry, so a crash can't leak it.
    /// </summary>
    public async Task<TakenBag?> TakeAsync(string shopperId)
    {
        var temporary = $"bag:taken:{Guid.NewGuid():N}";
        try
        {
            await Db.KeyRenameAsync(KeyFor(shopperId), temporary);
        }
        catch (RedisServerException e) when (e.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new TakenBag(temporary, await ReadAsync(temporary));
    }

    public Task DiscardAsync(TakenBag taken) => Db.KeyDeleteAsync(taken.Key);

    /// <summary>Puts a taken bag back after a failure, keeping anything added meanwhile.</summary>
    public async Task RestoreAsync(TakenBag taken, string shopperId)
    {
        var key = KeyFor(shopperId);
        foreach (var line in taken.Lines)
        {
            await Db.HashIncrementAsync(key, line.Key, line.Quantity);
        }

        await Db.KeyExpireAsync(key, Lifetime);
        await DiscardAsync(taken);
    }

    /// <summary>Moves one shopper's bag into another's, adding quantities together.</summary>
    public async Task MergeAsync(string fromShopper, string toShopper)
    {
        var taken = await TakeAsync(fromShopper);
        if (taken is null)
        {
            return;
        }

        var ids = taken.Lines.Select(l => l.Sneaker.Id!).Distinct().ToList();
        var catalogIds = (await sneakers.Find(Builders<Sneaker>.Filter.In(s => s.Id, ids))
            .Project(s => s.Id)
            .ToListAsync()).ToHashSet();

        var key = KeyFor(toShopper);
        foreach (var line in taken.Lines)
        {
            if (catalogIds.Contains(line.Sneaker.Id))
            {
                await Db.HashIncrementAsync(key, line.Key, line.Quantity);
            }
            else
            {
                // A limited pair goes through the normal checks, which refuse it if the
                // account didn't win it or has already ordered it.
                await AddAsync(toShopper, line.Sneaker.Id!, line.Size);
            }
        }

        await Db.KeyExpireAsync(key, Lifetime);
        await DiscardAsync(taken);
    }

    private async Task<bool> WonAsync(Drop drawn, string shopperId, double size)
    {
        if (drawn.Winners?.Contains(shopperId) != true)
        {
            return false;
        }

        var entry = await entries.Find(e => e.DropId == drawn.Id && e.EntrantId == shopperId).FirstOrDefaultAsync();
        return entry?.Size == size;
    }

    private async Task<Dictionary<string, Sneaker>> ResolveAsync(List<string> ids)
    {
        var found = (await sneakers.Find(Builders<Sneaker>.Filter.In(s => s.Id, ids)).ToListAsync())
            .ToDictionary(s => s.Id!);

        // Anything not in the catalog may be a limited pair, which lives on its drop.
        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var limited = await drops.Find(Builders<Drop>.Filter.In(d => d.Sneaker.Id, missing)).ToListAsync();
            foreach (var drop in limited)
            {
                found[drop.Sneaker.Id!] = drop.Sneaker;
            }
        }

        return found;
    }

    private static (string Field, string? SneakerId, double Size, long Quantity) Parse(HashEntry entry)
    {
        var field = entry.Name.ToString();
        var parts = field.Split(':', 2);
        var valid = parts.Length == 2
            && ObjectId.TryParse(parts[0], out _)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        return valid
            ? (field, parts[0], double.Parse(parts[1], CultureInfo.InvariantCulture), (long)entry.Value)
            : (field, null, 0, 0);
    }
}

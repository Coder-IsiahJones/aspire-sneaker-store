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
}

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

    public async Task<List<BagLine>> GetAsync(string shopperId)
    {
        var key = KeyFor(shopperId);
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

            if (!await WonAsync(drop, shopperId, size))
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

    private async Task<bool> WonAsync(Drop drop, string shopperId, double size)
    {
        if (drop.PhaseAt(DateTime.UtcNow) != DropPhase.Closed)
        {
            return false;
        }

        var drawn = await raffle.DrawAsync(drop);
        if (drawn.Winners?.Contains(shopperId) != true)
        {
            return false;
        }

        var entry = await entries.Find(e => e.DropId == drop.Id && e.EntrantId == shopperId).FirstOrDefaultAsync();
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

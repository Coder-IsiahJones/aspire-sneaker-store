using MongoDB.Driver;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Data;

public sealed class OrderStore(
    IMongoCollection<Order> orders,
    IMongoCollection<Drop> drops,
    IMongoCollection<Sneaker> sneakers,
    BagStore bag)
{
    /// <summary>
    /// Turns the shopper's bag into an order. Returns null if there is nothing to buy.
    /// Taking the bag is atomic, so a double click or a second device can't order it twice.
    /// </summary>
    public async Task<Order?> PlaceAsync(string accountId, string shopperId)
    {
        var taken = await bag.TakeAsync(shopperId);
        if (taken is null)
        {
            return null;
        }

        var claimed = new List<string>();
        try
        {
            var ids = taken.Lines.Select(l => l.Sneaker.Id!).Distinct().ToList();
            var catalogIds = (await sneakers.Find(Builders<Sneaker>.Filter.In(s => s.Id, ids))
                .Project(s => s.Id)
                .ToListAsync()).ToHashSet();

            var kept = new List<BagLine>();
            foreach (var line in taken.Lines)
            {
                if (catalogIds.Contains(line.Sneaker.Id))
                {
                    kept.Add(line);
                }
                else if (await ClaimAsync(line.Sneaker.Id!, shopperId))
                {
                    claimed.Add(line.Sneaker.Id!);
                    kept.Add(line);
                }
            }

            if (kept.Count == 0)
            {
                await bag.DiscardAsync(taken);
                return null;
            }

            var order = new Order
            {
                AccountId = accountId,
                PlacedAt = DateTime.UtcNow,
                Lines = [.. kept.Select(ToOrderLine)],
                Total = kept.Sum(l => l.Sneaker.Price * l.Quantity),
            };
            await orders.InsertOneAsync(order);
            await bag.DiscardAsync(taken);
            return order;
        }
        catch
        {
            foreach (var sneakerId in claimed)
            {
                await drops.UpdateOneAsync(
                    d => d.Sneaker.Id == sneakerId,
                    Builders<Drop>.Update.Pull(d => d.Claimed, shopperId));
            }

            await bag.RestoreAsync(taken, shopperId);
            throw;
        }
    }

    public Task<List<Order>> ListAsync(string accountId) =>
        orders.Find(o => o.AccountId == accountId).SortByDescending(o => o.PlacedAt).ToListAsync();

    // A win buys exactly one pair. The filter and the update are one atomic step,
    // so only one order can ever claim it.
    private async Task<bool> ClaimAsync(string sneakerId, string shopperId)
    {
        var filter = Builders<Drop>.Filter.And(
            Builders<Drop>.Filter.Eq(d => d.Sneaker.Id, sneakerId),
            Builders<Drop>.Filter.AnyEq(d => d.Winners, shopperId),
            Builders<Drop>.Filter.Not(Builders<Drop>.Filter.AnyEq(d => d.Claimed, shopperId)));

        var result = await drops.UpdateOneAsync(filter, Builders<Drop>.Update.AddToSet(d => d.Claimed, shopperId));
        return result.ModifiedCount == 1;
    }

    private static OrderLine ToOrderLine(BagLine line) => new()
    {
        SneakerId = line.Sneaker.Id!,
        Sku = line.Sneaker.Sku,
        Name = line.Sneaker.Name,
        Brand = line.Sneaker.Brand,
        Colorway = line.Sneaker.Colorway,
        Colors = line.Sneaker.Colors,
        Size = line.Size,
        Quantity = line.Quantity,
        UnitPrice = line.Sneaker.Price,
    };
}

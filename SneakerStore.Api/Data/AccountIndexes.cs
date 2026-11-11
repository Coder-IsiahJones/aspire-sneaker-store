using MongoDB.Driver;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Data;

public sealed class AccountIndexes(IMongoCollection<Account> accounts, IMongoCollection<Order> orders)
{
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        await accounts.Indexes.CreateOneAsync(
            new CreateIndexModel<Account>(
                Builders<Account>.IndexKeys.Ascending(a => a.Email),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        await orders.Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(
                Builders<Order>.IndexKeys.Ascending(o => o.AccountId).Descending(o => o.PlacedAt)),
            cancellationToken: cancellationToken);
    }
}

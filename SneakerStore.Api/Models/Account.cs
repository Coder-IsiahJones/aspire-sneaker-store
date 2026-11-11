using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SneakerStore.Api.Models;

public sealed class Account
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Always stored lowercase, so the unique index also ignores case.
    public required string Email { get; init; }
    public required string PasswordHash { get; set; }

    // Plays the same role as a browser's X-Entrant-Id. It keys the account's bag and raffle
    // entries, so those follow the account to any device. Never sent to clients.
    public required string ShopperId { get; init; }

    public DateTime CreatedAt { get; init; }

    // Sneaker ids, oldest first.
    public List<string> Wishlist { get; init; } = [];
}

public sealed class Order
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public required string AccountId { get; init; }

    public DateTime PlacedAt { get; init; }
    public required List<OrderLine> Lines { get; init; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; init; }
}

// A copy of the shoe at the moment of purchase, so history doesn't change with the catalog.
public sealed class OrderLine
{
    public required string SneakerId { get; init; }
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required string Brand { get; init; }
    public required string Colorway { get; init; }
    public required string[] Colors { get; init; }
    public double Size { get; init; }
    public long Quantity { get; init; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal UnitPrice { get; init; }
}

public sealed record CredentialsRequest(string Email, string Password);

public sealed record AccountView(string Email);

// A 200 with a null account, so an anonymous page load isn't logged as a failed request.
public sealed record AccountSession(AccountView? Account);

public sealed record OrderView(string Id, DateTime PlacedAt, List<OrderLine> Lines, decimal Total)
{
    public static OrderView From(Order order) => new(order.Id!, order.PlacedAt, order.Lines, order.Total);
}

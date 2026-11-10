using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SneakerStore.Api.Models;

public sealed class Sneaker
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required string Brand { get; init; }
    public required string Colorway { get; init; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; init; }

    public int ReleaseYear { get; init; }

    // US men's sizes currently in stock.
    public required double[] Sizes { get; init; }

    // Hex colors for the frontend's shoe illustration: upper, overlays, sole.
    public required string[] Colors { get; init; }

    public required string Description { get; init; }
}

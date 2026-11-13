using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace SneakerStore.Worker;

/// <summary>
/// Records how far an order has got. Each timestamp is written once: the update only matches while
/// the field is still empty, so handling the same message a second time changes nothing.
/// </summary>
public sealed class OrderProgress(IMongoDatabase database)
{
    private readonly IMongoCollection<OrderRecord> _orders = database.GetCollection<OrderRecord>("orders");

    /// <returns>Whether this call was the one that set the timestamp.</returns>
    public Task<bool> ReceiptSentAsync(string orderId, DateTime at) =>
        SetOnceAsync(orderId, order => order.ReceiptSentAt, at);

    /// <returns>Whether this call was the one that set the timestamp.</returns>
    public Task<bool> PackedAsync(string orderId, DateTime at) =>
        SetOnceAsync(orderId, order => order.PackedAt, at);

    private async Task<bool> SetOnceAsync(
        string orderId, Expression<Func<OrderRecord, DateTime?>> field, DateTime at)
    {
        var stillEmpty = Builders<OrderRecord>.Filter.And(
            Builders<OrderRecord>.Filter.Eq(order => order.Id, orderId),
            Builders<OrderRecord>.Filter.Eq(field, null));

        var result = await _orders.UpdateOneAsync(stillEmpty, Builders<OrderRecord>.Update.Set(field, at));
        return result.ModifiedCount == 1;
    }

    // The worker's view of an order: only the fields it writes. The API owns the rest of the document.
    [BsonIgnoreExtraElements]
    private sealed class OrderRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public DateTime? ReceiptSentAt { get; set; }
        public DateTime? PackedAt { get; set; }
    }
}

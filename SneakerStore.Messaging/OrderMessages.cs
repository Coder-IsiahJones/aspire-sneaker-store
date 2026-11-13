using System.Text.Json;

namespace SneakerStore.Messaging;

/// <summary>Published by the API when checkout succeeds.</summary>
public sealed record OrderPlaced(
    string OrderId,
    string AccountId,
    string Email,
    long Pairs,
    decimal Total,
    DateTime PlacedAt);

/// <summary>Published by the worker once the receipt email has gone out.</summary>
public sealed record OrderConfirmed(string OrderId, string AccountId, DateTime At);

/// <summary>Published by the worker once the order is packed.</summary>
public sealed record OrderPacked(string OrderId, string AccountId, DateTime At);

/// <summary>Message bodies are camelCase JSON, the same shape the API sends to browsers.</summary>
public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ReadOnlyMemory<byte> Serialize<T>(T message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, Options);

    public static T Deserialize<T>(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<T>(body.Span, Options)
        ?? throw new JsonException($"The message body is not a {typeof(T).Name}.");
}

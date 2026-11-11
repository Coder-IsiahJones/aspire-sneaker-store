using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SneakerStore.Api.Models;

public static class DropPhase
{
    public const string Upcoming = "upcoming";
    public const string Open = "open";
    public const string Closed = "closed";
}

public static class DropResult
{
    public const string Pending = "pending";
    public const string Won = "won";
    public const string Lost = "lost";
}

public sealed class Drop
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Embedded rather than referenced: a limited pair is only available through its raffle,
    // so it must not show up in the regular catalog.
    public required Sneaker Sneaker { get; init; }

    public int Pairs { get; init; }
    public DateTime OpensAt { get; init; }
    public DateTime ClosesAt { get; init; }

    // Entrant ids of the winners. Null until the draw has run. Never sent to clients.
    public List<string>? Winners { get; init; }

    public string PhaseAt(DateTime now) =>
        now < OpensAt ? DropPhase.Upcoming
        : now < ClosesAt ? DropPhase.Open
        : DropPhase.Closed;
}

public sealed class DropEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public required string DropId { get; init; }

    public required string EntrantId { get; init; }
    public double Size { get; init; }
    public DateTime EnteredAt { get; init; }

    public DropEntryStatus StatusIn(Drop drop) => new(
        Size,
        drop.Winners is null ? DropResult.Pending
        : drop.Winners.Contains(EntrantId) ? DropResult.Won
        : DropResult.Lost);
}

public sealed record EnterDropRequest(double Size);

public sealed record DropEntryStatus(double Size, string Result);

public sealed record DropResponse(
    string Id,
    Sneaker Sneaker,
    int Pairs,
    DateTime OpensAt,
    DateTime ClosesAt,
    string Phase,
    int Entries,
    DropEntryStatus? Entry);

// ServerTime lets the browser run its countdowns against the server's clock, not its own.
public sealed record DropsResponse(DateTime ServerTime, List<DropResponse> Drops);

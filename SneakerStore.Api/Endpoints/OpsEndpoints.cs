using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MongoDB.Bson;
using RabbitMQ.Client;
using SneakerStore.Api.Messaging;
using SneakerStore.Messaging;

namespace SneakerStore.Api.Endpoints;

/// <summary>
/// A window into the broker for the back-room dashboard in ops/. These routes have no sign-in and
/// one of them publishes made-up orders, so Program.cs only maps them in Development.
/// </summary>
public static class OpsEndpoints
{
    private const int MaxRush = 200;

    // Not a real account. The store's event stream filters by account, so no shopper sees these.
    private const string RushAccount = "drop-rush";

    public static void MapOpsEndpoints(this WebApplication app)
    {
        var ops = app.MapGroup("/api/ops").WithTags("Ops (Development only)");

        ops.MapGet("/queues", async (IConnection connection, CancellationToken cancellationToken) =>
        {
            // A channel per request: a passive declare of a missing queue closes its channel.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await Topology.DeclareAsync(channel, cancellationToken);

            var depths = new List<QueueDepth>();
            foreach (var queue in (string[])[.. Topology.Queues.Work, Topology.Queues.DeadLetters])
            {
                // Asks about the queue without changing it. "Waiting" counts messages nobody holds
                // yet. An order a worker is busy with is not in this number.
                var state = await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
                depths.Add(new QueueDepth(queue, state.MessageCount, state.ConsumerCount));
            }

            return depths;
        })
        .WithName("GetQueues")
        .WithSummary("How many messages wait in each queue, and how many consumers it has")
        .Produces<List<QueueDepth>>();

        ops.MapGet("/events", (OrderFeed feed, IHostApplicationLifetime lifetime, CancellationToken requestAborted) =>
            TypedResults.ServerSentEvents(EverythingAsync(feed, lifetime, requestAborted)))
        .WithName("WatchEverything")
        .WithSummary("A Server-Sent Events stream of every order event, named by its routing key");

        ops.MapPost("/drop-rush", async (int? orders, bool? doomed, EventPublisher events, CancellationToken cancellationToken) =>
        {
            var count = Math.Clamp(orders ?? 25, 1, MaxRush);
            for (var i = 1; i <= count; i++)
            {
                // The last one, if asked for, goes to an address the simulated mail provider always
                // refuses. That order can never get its receipt.
                var email = doomed == true && i == count ? "nobody@stockroom.invalid" : $"shopper{i}@example.com";
                await events.PublishAsync(
                    Topology.Keys.OrderPlaced,
                    new OrderPlaced(
                        ObjectId.GenerateNewId().ToString(), RushAccount, email, Pairs: 1, Total: 180m, DateTime.UtcNow),
                    mandatory: true,
                    cancellationToken);
            }

            return new RushResult(count, doomed == true ? 1 : 0);
        })
        .WithName("StartDropRush")
        .WithSummary("Publish made-up orders, as if a drop just opened. They are never saved.")
        .Produces<RushResult>();
    }

    private static async IAsyncEnumerable<SseItem<JsonElement?>> EverythingAsync(
        OrderFeed feed,
        IHostApplicationLifetime lifetime,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var ended = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping);
        using var subscription = feed.Subscribe();

        yield return new SseItem<JsonElement?>(null, "ready");

        await foreach (var next in subscription.ReadAllAsync(ended.Token))
        {
            yield return new SseItem<JsonElement?>(next.Data, next.Key);
        }
    }
}

public sealed record QueueDepth(string Name, uint Waiting, uint Workers);

public sealed record RushResult(int Published, int Doomed);

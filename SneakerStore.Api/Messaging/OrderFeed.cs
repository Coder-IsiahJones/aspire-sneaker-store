using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SneakerStore.Messaging;

namespace SneakerStore.Api.Messaging;

/// <summary>One order event as it came off the broker. <see cref="Data"/> is the whole message body.</summary>
public sealed record FeedEvent(string Key, string OrderId, string AccountId, JsonElement Data)
{
    public static FeedEvent Parse(string key, ReadOnlyMemory<byte> body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new FeedEvent(
            key,
            root.GetProperty("orderId").GetString()!,
            root.GetProperty("accountId").GetString()!,
            root.Clone());
    }
}

/// <summary>
/// Listens to every order event and hands each one to the browsers connected to this API instance.
/// Its queue has no name, and is exclusive and auto-delete: the broker makes it up when this
/// instance connects and removes it when the connection closes. Every instance has its own, so
/// every instance sees every event. Compare the worker's queues, where each order goes to exactly
/// one consumer.
/// </summary>
public sealed class OrderFeed(IServiceProvider services, ILogger<OrderFeed> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Channel<FeedEvent>> _subscribers = new();

    /// <summary>Starts collecting events for one listener. Events arrive from this call on.</summary>
    public Subscription Subscribe()
    {
        // A slow browser loses its oldest events instead of holding memory. It can re-read orders.
        var inbox = Channel.CreateBounded<FeedEvent>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _subscribers[id] = inbox;
        return new Subscription(inbox.Reader, () => _subscribers.TryRemove(id, out _));
    }

    public sealed class Subscription(ChannelReader<FeedEvent> inbox, Action unsubscribe) : IDisposable
    {
        /// <summary>Yields events until the token is cancelled, then ends without throwing.</summary>
        public async IAsyncEnumerable<FeedEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await WaitAsync(cancellationToken))
            {
                while (inbox.TryRead(out var next))
                {
                    yield return next;
                }
            }
        }

        private async ValueTask<bool> WaitAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await inbox.WaitToReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void Dispose() => unsubscribe();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = services.GetRequiredService<IConnection>();
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await Topology.DeclareAsync(channel, stoppingToken);

                var queue = (await channel.QueueDeclareAsync(cancellationToken: stoppingToken)).QueueName;
                await channel.QueueBindAsync(
                    queue, Topology.Exchange, Topology.Keys.AnyOrderEvent, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) =>
                {
                    Broadcast(delivery);
                    return Task.CompletedTask;
                };

                // Auto-ack is fine for this queue. If an event is lost, the order's state is still in
                // the database, and a browser re-reads its orders whenever it reconnects.
                await channel.BasicConsumeAsync(queue, autoAck: true, consumer, stoppingToken);

                // The client library reconnects, and redeclares this queue, on its own after an outage.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "The order feed can't reach the broker. Trying again in 5 seconds.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void Broadcast(BasicDeliverEventArgs delivery)
    {
        FeedEvent next;
        try
        {
            next = FeedEvent.Parse(delivery.RoutingKey, delivery.Body);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(e, "Ignored a {Key} message the feed couldn't read.", delivery.RoutingKey);
            return;
        }

        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(next);
        }
    }
}

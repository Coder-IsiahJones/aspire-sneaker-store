using System.Collections.Concurrent;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SneakerStore.Api.Tests;

/// <summary>
/// These tests don't touch Stockroom's code. They record how the broker itself treats a message
/// that keeps failing, on a quorum queue set up like Stockroom's work queues. The worker's error
/// handling depends on these answers, so if a RabbitMQ upgrade changes one, a test here fails.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class BrokerSemanticsTests(MongoFixture fixture, ITestOutputHelper output)
{
    private const int Limit = 3;

    // Far more deliveries than the limit allows. Reaching this means the limit never applied.
    private const int GiveUpAfter = 15;

    private sealed record Seen(string Body, bool Redelivered, long? DeliveryCount, long? AcquiredCount)
    {
        public override string ToString() =>
            $"{Body}(redelivered={Redelivered}, x-delivery-count={Show(DeliveryCount)}, x-acquired-count={Show(AcquiredCount)})";

        private static string Show(long? value) => value?.ToString() ?? "none";
    }

    private static Seen Describe(BasicDeliverEventArgs delivery)
    {
        var headers = delivery.BasicProperties.Headers;
        return new Seen(
            Encoding.UTF8.GetString(delivery.Body.Span),
            delivery.Redelivered,
            Header(headers, "x-delivery-count"),
            Header(headers, "x-acquired-count"));
    }

    private static long? Header(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt64(value)
            : null;

    /// <summary>A quorum queue with a delivery limit, and a dead-letter queue behind it.</summary>
    private static async Task<(string Queue, string DeadLetters)> DeclareAsync(IChannel admin, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var (queue, deadExchange, deadLetters) = ($"semantics.{id}", $"semantics.{id}.dead", $"semantics.{id}.dead-letters");

        await admin.ExchangeDeclareAsync(deadExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await admin.QueueDeclareAsync(
            deadLetters, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: ct);
        await admin.QueueBindAsync(deadLetters, deadExchange, routingKey: "", cancellationToken: ct);

        await admin.QueueDeclareAsync(
            queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = Limit,
                ["x-dead-letter-exchange"] = deadExchange,
            },
            cancellationToken: ct);

        return (queue, deadLetters);
    }

    private static async Task PublishAsync(IChannel admin, string queue, CancellationToken ct, params string[] bodies)
    {
        foreach (var body in bodies)
        {
            // The default exchange routes straight to the queue named by the routing key.
            await admin.BasicPublishAsync(
                exchange: "", routingKey: queue, mandatory: true,
                new BasicProperties { DeliveryMode = DeliveryModes.Persistent },
                Encoding.UTF8.GetBytes(body), ct);
        }
    }

    private static async Task<IChannel> ConsumeAsync(
        IConnection connection, string queue, bool autoAck, CancellationToken ct,
        Func<IChannel, BasicDeliverEventArgs, Task> handler)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => handler(channel, delivery);
        await channel.BasicConsumeAsync(queue, autoAck, consumer, ct);
        return channel;
    }

    private static async Task<uint> ReadyAsync(IChannel admin, string queue, CancellationToken ct) =>
        (await admin.QueueDeclarePassiveAsync(queue, ct)).MessageCount;

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, CancellationToken ct, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100, ct);
        }

        return false;
    }

    /// <summary>Fails the same message over and over with the given call, and reports what the broker did.</summary>
    private async Task<(List<Seen> Deliveries, uint DeadLettered)> FailRepeatedlyAsync(
        Func<IChannel, ulong, CancellationToken, ValueTask> fail)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await fixture.ConnectToBrokerAsync(ct);
        await using var admin = await connection.CreateChannelAsync(cancellationToken: ct);
        var (queue, deadLetters) = await DeclareAsync(admin, ct);
        await PublishAsync(admin, queue, ct, "order-1");

        var seen = new ConcurrentQueue<Seen>();
        await using var worker = await ConsumeAsync(connection, queue, autoAck: false, ct, async (channel, delivery) =>
        {
            seen.Enqueue(Describe(delivery));
            if (seen.Count >= GiveUpAfter)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
                return;
            }

            await fail(channel, delivery.DeliveryTag, ct);
        });

        await WaitForAsync(async () => seen.Count >= GiveUpAfter || await ReadyAsync(admin, deadLetters, ct) == 1, ct);

        var deliveries = seen.ToList();
        var dead = await ReadyAsync(admin, deadLetters, ct);
        output.WriteLine($"deliveries: {deliveries.Count}, dead-lettered: {dead}");
        foreach (var delivery in deliveries)
        {
            output.WriteLine($"  {delivery}");
        }

        return (deliveries, dead);
    }

    [Fact]
    public async Task Reject_WithRequeue_IsDeliveredOncePlusTheLimit_ThenDeadLettered()
    {
        var (deliveries, dead) = await FailRepeatedlyAsync(
            (channel, tag, ct) => channel.BasicRejectAsync(tag, requeue: true, ct));

        Assert.Equal(1u, dead);

        // The limit counts redeliveries: the first delivery, then three more.
        Assert.Equal(Limit + 1, deliveries.Count);
        Assert.False(deliveries[0].Redelivered);
        Assert.All(deliveries.Skip(1), delivery => Assert.True(delivery.Redelivered));
        Assert.Equal([null, 1, 2, 3], deliveries.Select(delivery => delivery.DeliveryCount));
    }

    [Fact]
    public async Task Nack_WithRequeue_DoesNotCountTowardTheDeliveryLimit()
    {
        var (deliveries, dead) = await FailRepeatedlyAsync(
            (channel, tag, ct) => channel.BasicNackAsync(tag, multiple: false, requeue: true, ct));

        // On RabbitMQ 4.3 a nack with requeue is a plain return, not a failed delivery. The message
        // came back every time until the test stopped it, and it was never dead-lettered. This is
        // why the workers reject instead.
        Assert.Equal(0u, dead);
        Assert.Equal(GiveUpAfter, deliveries.Count);
        Assert.All(deliveries, delivery => Assert.Null(delivery.DeliveryCount));
    }

    [Fact]
    public async Task WorkerThatDiesHoldingAMessage_CountsTowardTheDeliveryLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await fixture.ConnectToBrokerAsync(ct);
        await using var admin = await connection.CreateChannelAsync(cancellationToken: ct);
        var (queue, deadLetters) = await DeclareAsync(admin, ct);
        await PublishAsync(admin, queue, ct, "order-1");

        var seen = new List<Seen>();
        for (var attempt = 0; attempt < GiveUpAfter; attempt++)
        {
            // A new connection per attempt, dropped without acking: the nearest thing to a crashed worker.
            var doomed = await fixture.ConnectToBrokerAsync(ct);
            var got = new TaskCompletionSource<Seen>(TaskCreationOptions.RunContinuationsAsynchronously);
            await ConsumeAsync(doomed, queue, autoAck: false, ct, (_, delivery) =>
            {
                got.TrySetResult(Describe(delivery));
                return Task.CompletedTask;
            });

            var delivered = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(3), ct)) == got.Task;
            await doomed.AbortAsync(ct);
            doomed.Dispose();

            if (!delivered)
            {
                break;
            }

            seen.Add(await got.Task);
        }

        await WaitForAsync(async () => await ReadyAsync(admin, deadLetters, ct) == 1, ct, seconds: 5);
        var dead = await ReadyAsync(admin, deadLetters, ct);
        output.WriteLine($"deliveries: {seen.Count}, dead-lettered: {dead}");
        seen.ForEach(delivery => output.WriteLine($"  {delivery}"));

        Assert.Equal(1u, dead);
        Assert.Equal(Limit + 1, seen.Count);
        Assert.All(seen.Skip(1), delivery => Assert.True(delivery.Redelivered));
    }

    [Fact]
    public async Task AutoAck_LosesTheMessage_WhenTheWorkerDies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await fixture.ConnectToBrokerAsync(ct);
        await using var admin = await connection.CreateChannelAsync(cancellationToken: ct);
        var (queue, deadLetters) = await DeclareAsync(admin, ct);
        await PublishAsync(admin, queue, ct, "order-1");

        var doomed = await fixture.ConnectToBrokerAsync(ct);
        var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await ConsumeAsync(doomed, queue, autoAck: true, ct, (_, _) =>
        {
            got.TrySetResult();
            return Task.CompletedTask;
        });
        await got.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await doomed.AbortAsync(ct);
        doomed.Dispose();

        // Nothing comes back: not to the queue, not to another worker, not to the dead letters.
        var redelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var second = await ConsumeAsync(connection, queue, autoAck: false, ct, (_, _) =>
        {
            redelivered.TrySetResult();
            return Task.CompletedTask;
        });
        var cameBack = await Task.WhenAny(redelivered.Task, Task.Delay(TimeSpan.FromSeconds(2), ct)) == redelivered.Task;

        Assert.False(cameBack);
        Assert.Equal(0u, await ReadyAsync(admin, queue, ct));
        Assert.Equal(0u, await ReadyAsync(admin, deadLetters, ct));
    }

    [Fact]
    public async Task RequeuedMessage_GoesBackToTheFrontOfTheQueue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await fixture.ConnectToBrokerAsync(ct);
        await using var admin = await connection.CreateChannelAsync(cancellationToken: ct);
        var (queue, _) = await DeclareAsync(admin, ct);
        await PublishAsync(admin, queue, ct, "a", "b", "c");

        var seen = new ConcurrentQueue<string>();
        await using var worker = await ConsumeAsync(connection, queue, autoAck: false, ct, async (channel, delivery) =>
        {
            var body = Encoding.UTF8.GetString(delivery.Body.Span);
            seen.Enqueue(body);

            // Fail "a" once, the first time it arrives. Everything else succeeds.
            if (body == "a" && !delivery.Redelivered)
            {
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: true, ct);
            }
            else
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            }
        });

        await WaitForAsync(() => Task.FromResult(seen.Count >= 4), ct);
        output.WriteLine($"order seen: {string.Join(", ", seen)}");

        Assert.Equal(["a", "a", "b", "c"], seen.ToArray());
    }
}

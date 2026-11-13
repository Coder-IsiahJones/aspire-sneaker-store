using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RabbitMQ.Client;
using SneakerStore.Api.Models;
using SneakerStore.Messaging;
using SneakerStore.Worker;

namespace SneakerStore.Api.Tests;

/// <summary>
/// The worker's handlers, built by hand against the test containers. Orders are placed through the
/// API first, so these also check that the worker and the API agree on the order document.
/// </summary>
[Collection(MongoCollection.Name)]
public sealed class WorkerTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrderProgress Progress => new(fixture.Client.GetDatabase("sneakerdb"));

    private sealed class CountingSender : IReceiptSender
    {
        public int Sent { get; private set; }

        public Task SendAsync(OrderPlaced order)
        {
            Sent++;
            return Task.CompletedTask;
        }
    }

    private static OrderPlaced Placed(string orderId, string email = "shopper@example.com") =>
        new(orderId, AccountId: "account-1", email, Pairs: 1, Total: 100m, PlacedAt: DateTime.UtcNow);

    private static async Task<OrderView> ReadAsync(HttpClient client, string orderId) =>
        (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Ct))!.Single(o => o.Id == orderId);

    private static WarehouseWorker Warehouse(IConnection connection, OrderProgress progress, EventPublisher events)
    {
        // No packing time, so the test doesn't wait for work that isn't real.
        var instant = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Warehouse:PackingSeconds"] = "0" })
            .Build();
        return new WarehouseWorker(connection, instant, progress, events, NullLogger<WarehouseWorker>.Instance);
    }

    [Fact]
    public async Task Receipt_HandledTwice_WritesTheTimestampOnce()
    {
        var client = fixture.Factory.CreateClient();
        var (_, order) = await OrderMessagingTests.CheckOutAsync(client);
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var events = new EventPublisher(() => connection);
        var sender = new CountingSender();
        var worker = new ReceiptWorker(connection, sender, Progress, events, NullLogger<ReceiptWorker>.Instance);

        await worker.HandleAsync(Placed(order.Id));
        var first = (await ReadAsync(client, order.Id)).ReceiptSentAt;

        // The same message again, as if the worker had died before acknowledging it.
        await Task.Delay(20, Ct);
        await worker.HandleAsync(Placed(order.Id));
        var second = (await ReadAsync(client, order.Id)).ReceiptSentAt;

        Assert.NotNull(first);
        Assert.Equal(first, second);

        // The database is safe to repeat. The email is not: it went out twice.
        Assert.Equal(2, sender.Sent);
    }

    [Fact]
    public async Task Packing_MarksTheOrder_AndAnnouncesIt()
    {
        var client = fixture.Factory.CreateClient();
        var (_, order) = await OrderMessagingTests.CheckOutAsync(client);
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareAsync(channel, Ct);
        var tap = (await channel.QueueDeclareAsync(cancellationToken: Ct)).QueueName;
        await channel.QueueBindAsync(tap, Topology.Exchange, Topology.Keys.OrderPacked, cancellationToken: Ct);
        await using var events = new EventPublisher(() => connection);

        await Warehouse(connection, Progress, events).HandleAsync(Placed(order.Id));

        var stored = await ReadAsync(client, order.Id);
        Assert.NotNull(stored.PackedAt);
        Assert.Null(stored.ReceiptSentAt);

        var announced = await channel.BasicGetAsync(tap, autoAck: true, Ct);
        Assert.NotNull(announced);
        Assert.Equal(order.Id, MessageJson.Deserialize<OrderPacked>(announced.Body).OrderId);
    }

    [Fact]
    public async Task UnknownOrder_IsHandledWithoutError()
    {
        // The dev-only drop rush publishes orders that were never saved. The update matches nothing.
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var events = new EventPublisher(() => connection);

        await Warehouse(connection, Progress, events).HandleAsync(Placed(ObjectId.GenerateNewId().ToString()));
    }

    [Fact]
    public async Task OrderThatKeepsFailing_EndsUpInDeadLetters()
    {
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareAsync(channel, Ct);
        await channel.QueuePurgeAsync(Topology.Queues.Receipts, Ct);
        await channel.QueuePurgeAsync(Topology.Queues.DeadLetters, Ct);

        await using var events = new EventPublisher(() => connection);
        var worker = new ReceiptWorker(
            connection,
            new SimulatedReceiptSender(NullLogger<SimulatedReceiptSender>.Instance),
            Progress,
            events,
            NullLogger<ReceiptWorker>.Instance);

        // The simulated mail provider refuses every .invalid address, however often it is asked.
        var doomed = Placed(ObjectId.GenerateNewId().ToString(), email: "nobody@stockroom.invalid");
        await worker.StartAsync(Ct);
        try
        {
            await events.PublishAsync(Topology.Keys.OrderPlaced, doomed, mandatory: true, Ct);

            BasicGetResult? dead = null;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (dead is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, Ct);
                dead = await channel.BasicGetAsync(Topology.Queues.DeadLetters, autoAck: true, Ct);
            }

            Assert.NotNull(dead);
            Assert.Equal(doomed.OrderId, MessageJson.Deserialize<OrderPlaced>(dead.Body).OrderId);

            // The broker records why it gave up on the message.
            var deaths = Assert.IsAssignableFrom<IEnumerable<object>>(dead.BasicProperties.Headers!["x-death"]);
            var death = Assert.IsAssignableFrom<IDictionary<string, object?>>(deaths.First());
            Assert.Equal("delivery_limit", Encoding.UTF8.GetString((byte[])death["reason"]!));
            Assert.Equal(Topology.Queues.Receipts, Encoding.UTF8.GetString((byte[])death["queue"]!));
        }
        finally
        {
            await worker.StopAsync(Ct);
        }
    }
}

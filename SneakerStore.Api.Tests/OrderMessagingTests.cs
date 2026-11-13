using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using RabbitMQ.Client;
using SneakerStore.Api.Models;
using SneakerStore.Messaging;

namespace SneakerStore.Api.Tests;

/// <summary>What checkout tells the broker. No worker runs here, so published orders stay in their queues.</summary>
[Collection(MongoCollection.Name)]
public sealed class OrderMessagingTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Registers a new shopper, bags one pair and checks out.</summary>
    internal static async Task<(string Email, OrderView Order)> CheckOutAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var registered = await client.PostAsJsonAsync(
            "/api/account/register", new CredentialsRequest(email, "correct horse battery"), Ct);
        registered.EnsureSuccessStatusCode();

        var sneaker = (await client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct))![0];
        var bagged = await client.PostAsJsonAsync(
            "/api/bag/items", new AddToBagRequest(sneaker.Id!, sneaker.Sizes[0]), Ct);
        bagged.EnsureSuccessStatusCode();

        var placed = await client.PostAsync("/api/orders", content: null, Ct);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (email, (await placed.Content.ReadFromJsonAsync<OrderView>(Ct))!);
    }

    private static async Task<BasicGetResult> NextMessageAsync(IChannel channel, string queue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await channel.BasicGetAsync(queue, autoAck: true, Ct) is { } message)
            {
                return message;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"Nothing arrived in {queue}.");
    }

    [Fact]
    public async Task Checkout_PublishesOrderPlaced()
    {
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareAsync(channel, Ct);

        // A temporary queue of the test's own, bound the way any new consumer would bind.
        var tap = (await channel.QueueDeclareAsync(cancellationToken: Ct)).QueueName;
        await channel.QueueBindAsync(tap, Topology.Exchange, Topology.Keys.OrderPlaced, cancellationToken: Ct);

        var (email, order) = await CheckOutAsync(fixture.Factory.CreateClient());

        var message = await NextMessageAsync(channel, tap);
        var placed = MessageJson.Deserialize<OrderPlaced>(message.Body);
        Assert.Equal(Topology.Keys.OrderPlaced, message.RoutingKey);
        Assert.Equal(order.Id, placed.OrderId);
        Assert.Equal(email, placed.Email);
        Assert.Equal(1, placed.Pairs);
        Assert.Equal(order.Total, placed.Total);

        // Persistent, so a queued order survives a broker restart.
        Assert.Equal(DeliveryModes.Persistent, message.BasicProperties.DeliveryMode);
    }

    [Fact]
    public async Task Checkout_PutsACopyInEachWorkQueue()
    {
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareAsync(channel, Ct);
        foreach (var queue in Topology.Queues.Work)
        {
            await channel.QueuePurgeAsync(queue, Ct);
        }

        var (_, order) = await CheckOutAsync(fixture.Factory.CreateClient());

        // One publish, two queues: each consumer gets its own copy of the same order.
        foreach (var queue in Topology.Queues.Work)
        {
            var placed = MessageJson.Deserialize<OrderPlaced>((await NextMessageAsync(channel, queue)).Body);
            Assert.Equal(order.Id, placed.OrderId);
        }
    }

    [Fact]
    public async Task Checkout_StillSucceeds_WhenTheBrokerIsUnreachable()
    {
        // Nothing listens on port 1. This is the gap an outbox would close: the order is saved,
        // and nobody is told about it.
        await using var offline = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:messaging", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Aspire:RabbitMQ:Client:MaxConnectRetryCount", "0");
        });
        var client = offline.CreateClient();

        var (_, order) = await CheckOutAsync(client);

        var listed = await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Ct);
        Assert.Contains(listed!, o => o.Id == order.Id);
    }
}

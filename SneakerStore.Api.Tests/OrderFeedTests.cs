using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Models;
using SneakerStore.Messaging;

namespace SneakerStore.Api.Tests;

/// <summary>The Server-Sent Events stream that keeps a shopper's orders current.</summary>
[Collection(MongoCollection.Name)]
public sealed class OrderFeedTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Reads one event: its "event:" and "data:" lines, up to the blank line that ends it.</summary>
    internal static async Task<(string Type, string Data)> NextEventAsync(StreamReader stream)
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        patience.CancelAfter(TimeSpan.FromSeconds(10));

        var (type, data, any) = ("message", "", false);
        while (await stream.ReadLineAsync(patience.Token) is { } line)
        {
            if (line.Length == 0)
            {
                if (any)
                {
                    return (type, data);
                }

                continue;
            }

            any = true;
            if (line.StartsWith("event:"))
            {
                type = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:"))
            {
                data += line["data:".Length..].Trim();
            }
        }

        throw new EndOfStreamException("The event stream ended.");
    }

    private static async Task<StreamReader> OpenStreamAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/orders/events", HttpCompletionOption.ResponseHeadersRead, Ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
    }

    [Fact]
    public async Task Stream_SaysReady_ThenSendsOnlyTheShoppersOwnOrders()
    {
        var client = fixture.Factory.CreateClient();
        var (email, order) = await OrderMessagingTests.CheckOutAsync(client);
        var accounts = fixture.Factory.Services.GetRequiredService<IMongoCollection<Account>>();
        var accountId = (await accounts.Find(a => a.Email == email).FirstAsync(Ct)).Id!;

        using var stream = await OpenStreamAsync(client);
        Assert.Equal("ready", (await NextEventAsync(stream)).Type);

        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var events = new EventPublisher(() => connection);
        var at = new DateTime(2026, 11, 12, 9, 30, 0, DateTimeKind.Utc);

        // Someone else's order first. If the stream leaked it, it would be the next event read.
        await events.PublishAsync(
            Topology.Keys.OrderPacked,
            new OrderPacked(ObjectId.GenerateNewId().ToString(), "someone-else", at),
            cancellationToken: Ct);
        await events.PublishAsync(
            Topology.Keys.OrderConfirmed, new OrderConfirmed(order.Id, accountId, at), cancellationToken: Ct);

        var (type, data) = await NextEventAsync(stream);
        var update = JsonSerializer.Deserialize<OrderUpdate>(data, MessageJson.Options)!;
        Assert.Equal(Topology.Keys.OrderConfirmed, type);
        Assert.Equal(order.Id, update.OrderId);
        Assert.Equal(at, update.At);
    }

    [Fact]
    public async Task Stream_DoesNotRepeatTheCheckoutItself()
    {
        var client = fixture.Factory.CreateClient();
        var (email, _) = await OrderMessagingTests.CheckOutAsync(client);
        var accounts = fixture.Factory.Services.GetRequiredService<IMongoCollection<Account>>();
        var accountId = (await accounts.Find(a => a.Email == email).FirstAsync(Ct)).Id!;

        using var stream = await OpenStreamAsync(client);
        Assert.Equal("ready", (await NextEventAsync(stream)).Type);

        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var events = new EventPublisher(() => connection);
        var second = ObjectId.GenerateNewId().ToString();
        await events.PublishAsync(
            Topology.Keys.OrderPlaced,
            new OrderPlaced(second, accountId, email, Pairs: 1, Total: 100m, PlacedAt: DateTime.UtcNow),
            cancellationToken: Ct);
        await events.PublishAsync(
            Topology.Keys.OrderPacked, new OrderPacked(second, accountId, DateTime.UtcNow), cancellationToken: Ct);

        // order.placed is skipped: the shopper already has that order from the checkout response.
        Assert.Equal(Topology.Keys.OrderPacked, (await NextEventAsync(stream)).Type);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using RabbitMQ.Client;
using SneakerStore.Api.Endpoints;
using SneakerStore.Messaging;

namespace SneakerStore.Api.Tests;

/// <summary>The dev-only routes behind the back-room dashboard.</summary>
[Collection(MongoCollection.Name)]
public sealed class OpsEndpointsTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task PurgeAsync()
    {
        await using var connection = await fixture.ConnectToBrokerAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await Topology.DeclareAsync(channel, Ct);
        foreach (var queue in (string[])[.. Topology.Queues.Work, Topology.Queues.DeadLetters])
        {
            await channel.QueuePurgeAsync(queue, Ct);
        }
    }

    private static async Task<Dictionary<string, QueueDepth>> QueuesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<QueueDepth>>("/api/ops/queues", Ct))!.ToDictionary(q => q.Name);

    [Fact]
    public async Task DropRush_PutsEveryOrderInBothWorkQueues()
    {
        await PurgeAsync();
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsync("/api/ops/drop-rush?orders=7&doomed=true", content: null, Ct);
        var result = await response.Content.ReadFromJsonAsync<RushResult>(Ct);

        Assert.Equal(new RushResult(Published: 7, Doomed: 1), result);

        // No worker runs in these tests, so everything published is still waiting.
        var queues = await QueuesAsync(client);
        Assert.Equal(7u, queues[Topology.Queues.Receipts].Waiting);
        Assert.Equal(7u, queues[Topology.Queues.Warehouse].Waiting);
        Assert.Equal(0u, queues[Topology.Queues.DeadLetters].Waiting);
        Assert.Equal(0u, queues[Topology.Queues.Receipts].Workers);
    }

    [Fact]
    public async Task DropRush_IsCapped()
    {
        await PurgeAsync();
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsync("/api/ops/drop-rush?orders=100000", content: null, Ct);

        Assert.Equal(200, (await response.Content.ReadFromJsonAsync<RushResult>(Ct))!.Published);
        await PurgeAsync();
    }

    [Fact]
    public async Task Events_CarryEveryOrderEvent_NamedByRoutingKey()
    {
        var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/ops/events", HttpCompletionOption.ResponseHeadersRead, Ct);
        using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        Assert.Equal("ready", (await OrderFeedTests.NextEventAsync(stream)).Type);

        await client.PostAsync("/api/ops/drop-rush?orders=1", content: null, Ct);

        var (type, data) = await OrderFeedTests.NextEventAsync(stream);
        Assert.Equal(Topology.Keys.OrderPlaced, type);
        Assert.Contains("shopper1@example.com", data);
    }

    [Theory]
    [InlineData("GET", "/api/ops/queues")]
    [InlineData("GET", "/api/ops/events")]
    [InlineData("POST", "/api/ops/drop-rush")]
    public async Task OutsideDevelopment_TheRoutesDoNotExist(string method, string path)
    {
        await using var production = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var response = await production.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

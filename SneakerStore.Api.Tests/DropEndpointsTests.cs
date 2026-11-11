using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Tests;

[Collection(MongoCollection.Name)]
public sealed class DropEndpointsTests(MongoFixture fixture)
{
    private readonly HttpClient _client = fixture.Factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IMongoCollection<Drop> Drops =>
        fixture.Factory.Services.GetRequiredService<IMongoCollection<Drop>>();

    private IMongoCollection<DropEntry> Entries =>
        fixture.Factory.Services.GetRequiredService<IMongoCollection<DropEntry>>();

    private static string NewEntrant() => Guid.NewGuid().ToString("N");

    private async Task<Drop> InsertDrop(TimeSpan opensIn, TimeSpan closesIn, int pairs = 1)
    {
        var now = DateTime.UtcNow;
        var sneaker = SeedData.Sneakers()[0];
        sneaker.Id = ObjectId.GenerateNewId().ToString();
        var drop = new Drop { Sneaker = sneaker, Pairs = pairs, OpensAt = now + opensIn, ClosesAt = now + closesIn };
        await Drops.InsertOneAsync(drop, cancellationToken: Ct);
        return drop;
    }

    private Task<Drop> InsertOpenDrop(int pairs = 1) =>
        InsertDrop(TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(10), pairs);

    private Task<Drop> InsertClosedDrop(int pairs = 1) =>
        InsertDrop(TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(-1), pairs);

    private async Task InsertEntries(Drop drop, IEnumerable<string> entrants) =>
        await Entries.InsertManyAsync(
            entrants.Select(e => new DropEntry
            {
                DropId = drop.Id!,
                EntrantId = e,
                Size = drop.Sneaker.Sizes[0],
                EnteredAt = DateTime.UtcNow,
            }),
            cancellationToken: Ct);

    private async Task<DropResponse> Fetch(Drop drop, string? entrant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/drops");
        if (entrant is not null)
        {
            request.Headers.Add("X-Entrant-Id", entrant);
        }

        var response = await _client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DropsResponse>(Ct);
        return body!.Drops.Single(d => d.Id == drop.Id);
    }

    private async Task<HttpResponseMessage> Enter(string dropId, string? entrant, double size)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/drops/{dropId}/entries")
        {
            Content = JsonContent.Create(new EnterDropRequest(size)),
        };
        if (entrant is not null)
        {
            request.Headers.Add("X-Entrant-Id", entrant);
        }

        return await _client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task ListDrops_ReportsPhaseFromTheClock()
    {
        var upcoming = await InsertDrop(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        var open = await InsertOpenDrop();
        var closed = await InsertClosedDrop();

        Assert.Equal(DropPhase.Upcoming, (await Fetch(upcoming)).Phase);
        Assert.Equal(DropPhase.Open, (await Fetch(open)).Phase);
        Assert.Equal(DropPhase.Closed, (await Fetch(closed)).Phase);
    }

    [Fact]
    public async Task ListDrops_DoesNotExposeWinners()
    {
        var drop = await InsertClosedDrop();
        await InsertEntries(drop, [NewEntrant()]);
        await Fetch(drop);

        var json = await _client.GetStringAsync("/api/drops", Ct);

        Assert.DoesNotContain("winners", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enter_OpenDrop_RecordsPendingEntry()
    {
        var drop = await InsertOpenDrop();
        var entrant = NewEntrant();

        var response = await Enter(drop.Id!, entrant, drop.Sneaker.Sizes[1]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var fetched = await Fetch(drop, entrant);
        Assert.Equal(1, fetched.Entries);
        Assert.Equal(new DropEntryStatus(drop.Sneaker.Sizes[1], DropResult.Pending), fetched.Entry);
    }

    [Fact]
    public async Task ListDrops_OnlyShowsTheCallersOwnEntry()
    {
        var drop = await InsertOpenDrop();
        await Enter(drop.Id!, NewEntrant(), drop.Sneaker.Sizes[0]);

        Assert.Null((await Fetch(drop, NewEntrant())).Entry);
        Assert.Null((await Fetch(drop)).Entry);
    }

    [Fact]
    public async Task Enter_Twice_Returns409AndKeepsOneEntry()
    {
        var drop = await InsertOpenDrop();
        var entrant = NewEntrant();
        await Enter(drop.Id!, entrant, drop.Sneaker.Sizes[0]);

        var second = await Enter(drop.Id!, entrant, drop.Sneaker.Sizes[1]);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, (await Fetch(drop)).Entries);
    }

    [Fact]
    public async Task Enter_SameGuidInDifferentFormat_CountsAsSameEntrant()
    {
        var drop = await InsertOpenDrop();
        var entrant = Guid.NewGuid();
        await Enter(drop.Id!, entrant.ToString("N"), drop.Sneaker.Sizes[0]);

        var second = await Enter(drop.Id!, entrant.ToString("D").ToUpperInvariant(), drop.Sneaker.Sizes[0]);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Enter_BeforeOpening_Returns409()
    {
        var drop = await InsertDrop(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

        var response = await Enter(drop.Id!, NewEntrant(), drop.Sneaker.Sizes[0]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enter_AfterClosing_Returns409()
    {
        var drop = await InsertClosedDrop();

        var response = await Enter(drop.Id!, NewEntrant(), drop.Sneaker.Sizes[0]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enter_SizeNotInDrop_Returns400()
    {
        var drop = await InsertOpenDrop();

        var response = await Enter(drop.Id!, NewEntrant(), 99);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000000000000000000000000000")]
    public async Task Enter_BadEntrantId_Returns400(string? entrant)
    {
        var drop = await InsertOpenDrop();

        var response = await Enter(drop.Id!, entrant, drop.Sneaker.Sizes[0]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-object-id")]
    [InlineData("000000000000000000000000")]
    public async Task Enter_UnknownDrop_Returns404(string id)
    {
        var response = await Enter(id, NewEntrant(), 9);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Draw_PicksExactlyAsManyWinnersAsPairs()
    {
        var drop = await InsertClosedDrop(pairs: 2);
        var entrants = Enumerable.Range(0, 6).Select(_ => NewEntrant()).ToList();
        await InsertEntries(drop, entrants);

        var results = new List<string>();
        foreach (var entrant in entrants)
        {
            results.Add((await Fetch(drop, entrant)).Entry!.Result);
        }

        Assert.Equal(2, results.Count(r => r == DropResult.Won));
        Assert.Equal(4, results.Count(r => r == DropResult.Lost));
    }

    [Fact]
    public async Task Draw_FewerEntrantsThanPairs_EveryoneWins()
    {
        var drop = await InsertClosedDrop(pairs: 5);
        var entrant = NewEntrant();
        await InsertEntries(drop, [entrant]);

        Assert.Equal(DropResult.Won, (await Fetch(drop, entrant)).Entry!.Result);
    }

    [Fact]
    public async Task Draw_ResultNeverChanges()
    {
        var drop = await InsertClosedDrop(pairs: 3);
        await InsertEntries(drop, Enumerable.Range(0, 20).Select(_ => NewEntrant()));
        await Fetch(drop);
        var first = (await Drops.Find(d => d.Id == drop.Id).FirstAsync(Ct)).Winners;

        await Fetch(drop);
        var second = (await Drops.Find(d => d.Id == drop.Id).FirstAsync(Ct)).Winners;

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Draw_ConcurrentReads_AgreeOnOneResult()
    {
        var drop = await InsertClosedDrop(pairs: 1);
        var entrants = Enumerable.Range(0, 30).Select(_ => NewEntrant()).ToList();
        await InsertEntries(drop, entrants);

        var results = await Task.WhenAll(entrants.Select(e => Fetch(drop, e)));

        Assert.Single(results, r => r.Entry!.Result == DropResult.Won);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesDrops()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"/api/drops\"", document);
        Assert.Contains("\"/api/drops/{id}/entries\"", document);
    }
}

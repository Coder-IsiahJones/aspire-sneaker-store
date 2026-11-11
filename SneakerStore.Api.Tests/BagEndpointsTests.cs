using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;
using StackExchange.Redis;

namespace SneakerStore.Api.Tests;

[Collection(MongoCollection.Name)]
public sealed class BagEndpointsTests(MongoFixture fixture)
{
    private readonly HttpClient _client = fixture.Factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IMongoCollection<Drop> Drops =>
        fixture.Factory.Services.GetRequiredService<IMongoCollection<Drop>>();

    private IMongoCollection<DropEntry> Entries =>
        fixture.Factory.Services.GetRequiredService<IMongoCollection<DropEntry>>();

    private IDatabase Redis =>
        fixture.Factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private static string NewShopper() => Guid.NewGuid().ToString("N");

    private async Task<Sneaker> AnySneaker()
    {
        var all = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct);
        return all![0];
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? shopper, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (shopper is not null)
        {
            request.Headers.Add("X-Entrant-Id", shopper);
        }

        return await _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> Add(string? shopper, string sneakerId, double size) =>
        Send(HttpMethod.Post, "/api/bag/items", shopper, new AddToBagRequest(sneakerId, size));

    private async Task<List<BagLine>> Bag(string shopper)
    {
        var response = await Send(HttpMethod.Get, "/api/bag", shopper);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<BagLine>>(Ct))!;
    }

    private async Task<Drop> InsertDrop(TimeSpan closesIn, List<string>? winners = null)
    {
        var now = DateTime.UtcNow;
        var sneaker = SeedData.Sneakers()[0];
        sneaker.Id = ObjectId.GenerateNewId().ToString();
        var drop = new Drop
        {
            Sneaker = sneaker,
            Pairs = 1,
            OpensAt = now.AddHours(-1),
            ClosesAt = now + closesIn,
            Winners = winners,
        };
        await Drops.InsertOneAsync(drop, cancellationToken: Ct);
        return drop;
    }

    private async Task<(Drop Drop, string Winner, double Size)> InsertWonDrop()
    {
        var winner = NewShopper();
        var drop = await InsertDrop(TimeSpan.FromMinutes(-1), [winner]);
        var size = drop.Sneaker.Sizes[0];
        await Entries.InsertOneAsync(
            new DropEntry { DropId = drop.Id!, EntrantId = winner, Size = size, EnteredAt = DateTime.UtcNow },
            cancellationToken: Ct);
        return (drop, winner, size);
    }

    [Fact]
    public async Task Bag_NewShopper_IsEmpty()
    {
        Assert.Empty(await Bag(NewShopper()));
    }

    [Fact]
    public async Task Add_StoresInRedisAndReturnsCurrentSneakerDetails()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();

        var response = await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single((await response.Content.ReadFromJsonAsync<List<BagLine>>(Ct))!);
        Assert.Equal($"{sneaker.Id}:{sneaker.Sizes[0]}", line.Key);
        Assert.Equal(sneaker.Name, line.Sneaker.Name);
        Assert.Equal(sneaker.Price, line.Sneaker.Price);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(1, await Redis.HashLengthAsync(BagStore.KeyFor(shopper)));
    }

    [Fact]
    public async Task Add_FractionalSize_KeepsItsKeyFormat()
    {
        var all = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct);
        var sneaker = all!.First(s => s.Sizes.Contains(9.5));
        var shopper = NewShopper();

        await Add(shopper, sneaker.Id!, 9.5);

        Assert.Equal($"{sneaker.Id}:9.5", Assert.Single(await Bag(shopper)).Key);
    }

    [Fact]
    public async Task Add_SameSizeTwice_BumpsQuantity()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();

        await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);
        await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);

        Assert.Equal(2, Assert.Single(await Bag(shopper)).Quantity);
    }

    [Fact]
    public async Task Add_Concurrently_LosesNothing()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Add(shopper, sneaker.Id!, sneaker.Sizes[0])));

        Assert.Equal(20, Assert.Single(await Bag(shopper)).Quantity);
    }

    [Fact]
    public async Task Add_DifferentSizes_AreSeparateLinesSortedBySize()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();

        await Add(shopper, sneaker.Id!, sneaker.Sizes[1]);
        await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);

        Assert.Equal([sneaker.Sizes[0], sneaker.Sizes[1]], (await Bag(shopper)).Select(l => l.Size));
    }

    [Fact]
    public async Task Add_SetsAnExpiry()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();

        await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);

        var ttl = await Redis.KeyTimeToLiveAsync(BagStore.KeyFor(shopper));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, BagStore.Lifetime - TimeSpan.FromMinutes(1), BagStore.Lifetime);
    }

    [Fact]
    public async Task Bags_AreSeparatePerShopper()
    {
        var sneaker = await AnySneaker();
        await Add(NewShopper(), sneaker.Id!, sneaker.Sizes[0]);

        Assert.Empty(await Bag(NewShopper()));
    }

    [Fact]
    public async Task Remove_DeletesTheLine()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();
        await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);
        await Add(shopper, sneaker.Id!, sneaker.Sizes[1]);
        var key = (await Bag(shopper))[0].Key;

        var response = await Send(HttpMethod.Delete, $"/api/bag/items/{Uri.EscapeDataString(key)}", shopper);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sneaker.Sizes[1], Assert.Single(await Bag(shopper)).Size);
    }

    [Fact]
    public async Task Remove_UnknownLine_LeavesBagAlone()
    {
        var sneaker = await AnySneaker();
        var shopper = NewShopper();
        await Add(shopper, sneaker.Id!, sneaker.Sizes[0]);

        var response = await Send(HttpMethod.Delete, "/api/bag/items/nope", shopper);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await Bag(shopper));
    }

    [Theory]
    [InlineData("not-an-object-id")]
    [InlineData("000000000000000000000000")]
    public async Task Add_UnknownSneaker_Returns404(string id)
    {
        var response = await Add(NewShopper(), id, 9);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_SizeNotStocked_Returns400()
    {
        var sneaker = await AnySneaker();

        var response = await Add(NewShopper(), sneaker.Id!, 99);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task BadShopperId_Returns400(string? shopper)
    {
        var sneaker = await AnySneaker();

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/bag", shopper)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(shopper, sneaker.Id!, sneaker.Sizes[0])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Delete, "/api/bag/items/x", shopper)).StatusCode);
    }

    [Fact]
    public async Task Add_LimitedPair_WinnerCanBagIt()
    {
        var (drop, winner, size) = await InsertWonDrop();

        var response = await Add(winner, drop.Sneaker.Id!, size);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(drop.Sneaker.Sku, Assert.Single(await Bag(winner)).Sneaker.Sku);
    }

    [Fact]
    public async Task Add_LimitedPair_OnlyOneAtATime()
    {
        var (drop, winner, size) = await InsertWonDrop();
        await Add(winner, drop.Sneaker.Id!, size);

        var second = await Add(winner, drop.Sneaker.Id!, size);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, Assert.Single(await Bag(winner)).Quantity);
    }

    [Fact]
    public async Task Add_LimitedPair_NonWinnerIsRefused()
    {
        var (drop, _, size) = await InsertWonDrop();

        var response = await Add(NewShopper(), drop.Sneaker.Id!, size);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Add_LimitedPair_WinnerCannotChangeSize()
    {
        var (drop, winner, _) = await InsertWonDrop();

        var response = await Add(winner, drop.Sneaker.Id!, drop.Sneaker.Sizes[1]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Add_LimitedPair_RefusedWhileRaffleIsOpen()
    {
        var shopper = NewShopper();
        var drop = await InsertDrop(TimeSpan.FromMinutes(10));

        var response = await Add(shopper, drop.Sneaker.Id!, drop.Sneaker.Sizes[0]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Bag_ShoeNoLongerExists_LineIsCleanedUp()
    {
        var (drop, winner, size) = await InsertWonDrop();
        await Add(winner, drop.Sneaker.Id!, size);

        await Drops.DeleteOneAsync(d => d.Id == drop.Id, Ct);

        Assert.Empty(await Bag(winner));
        Assert.False(await Redis.KeyExistsAsync(BagStore.KeyFor(winner)));
    }

    [Fact]
    public async Task Catalog_SecondRequestIsServedFromCache()
    {
        // A brand no other test asks for, so the first request is guaranteed to miss.
        var path = $"/api/sneakers?brand=uncached-{Guid.NewGuid():N}";

        var first = await _client.GetAsync(path, Ct);
        var second = await _client.GetAsync(path, Ct);

        Assert.Null(first.Headers.Age);
        Assert.NotNull(second.Headers.Age);
    }

    [Fact]
    public async Task Catalog_IgnoresUnrelatedQueryParameters()
    {
        var brand = $"uncached-{Guid.NewGuid():N}";
        await _client.GetAsync($"/api/sneakers?brand={brand}", Ct);

        var response = await _client.GetAsync($"/api/sneakers?brand={brand}&junk=1", Ct);

        Assert.NotNull(response.Headers.Age);
    }

    [Fact]
    public async Task BagAndDrops_AreNeverCached()
    {
        var shopper = NewShopper();
        await Send(HttpMethod.Get, "/api/bag", shopper);
        await Send(HttpMethod.Get, "/api/drops", shopper);

        Assert.Null((await Send(HttpMethod.Get, "/api/bag", shopper)).Headers.Age);
        Assert.Null((await Send(HttpMethod.Get, "/api/drops", shopper)).Headers.Age);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesBag()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"/api/bag\"", document);
        Assert.Contains("\"/api/bag/items\"", document);
        Assert.Contains("\"/api/bag/items/{key}\"", document);
    }
}

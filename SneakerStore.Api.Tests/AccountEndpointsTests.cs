using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Tests;

[Collection(MongoCollection.Name)]
public sealed class AccountEndpointsTests(MongoFixture fixture)
{
    private const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private T Service<T>() where T : notnull => fixture.Factory.Services.GetRequiredService<T>();
    private IMongoCollection<Account> Accounts => Service<IMongoCollection<Account>>();
    private IMongoCollection<Drop> Drops => Service<IMongoCollection<Drop>>();
    private IMongoCollection<DropEntry> Entries => Service<IMongoCollection<DropEntry>>();

    // Each client has its own cookie jar, so a new one acts like a new device.
    private HttpClient Device() => fixture.Factory.CreateClient();

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";
    private static string NewBrowser() => Guid.NewGuid().ToString("N");

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, object? body = null, string? browser = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (browser is not null)
        {
            request.Headers.Add("X-Entrant-Id", browser);
        }

        return await client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string? browser = null, string password = Password) =>
        Send(client, HttpMethod.Post, "/api/account/register", new CredentialsRequest(email, password), browser);

    private static Task<HttpResponseMessage> SignIn(HttpClient client, string email, string? browser = null, string password = Password) =>
        Send(client, HttpMethod.Post, "/api/account/sign-in", new CredentialsRequest(email, password), browser);

    private async Task<HttpClient> Member(string? email = null)
    {
        var client = Device();
        (await Register(client, email ?? NewEmail())).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, string sneakerId, double size, string? browser = null) =>
        Send(client, HttpMethod.Post, "/api/bag/items", new AddToBagRequest(sneakerId, size), browser);

    private static async Task<List<BagLine>> Bag(HttpClient client, string? browser = null)
    {
        var response = await Send(client, HttpMethod.Get, "/api/bag", browser: browser);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<BagLine>>(Ct))!;
    }

    private static async Task<OrderView> PlaceOrder(HttpClient client)
    {
        var response = await Send(client, HttpMethod.Post, "/api/orders");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderView>(Ct))!;
    }

    private static async Task<List<OrderView>> Orders(HttpClient client) =>
        (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Ct))!;

    private static async Task<List<Sneaker>> Wishlist(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Sneaker>>("/api/wishlist", Ct))!;

    private async Task<Sneaker> AnySneaker() =>
        (await Device().GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct))![0];

    private async Task<string> ShopperIdOf(string email) =>
        (await Accounts.Find(a => a.Email == email).FirstAsync(Ct)).ShopperId;

    private async Task<Drop> InsertDrop(TimeSpan closesIn, List<string>? winners = null, decimal? price = null)
    {
        var now = DateTime.UtcNow;
        var template = SeedData.Sneakers()[0];
        var sneaker = new Sneaker
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Sku = template.Sku,
            Name = template.Name,
            Brand = template.Brand,
            Colorway = template.Colorway,
            Price = price ?? template.Price,
            ReleaseYear = template.ReleaseYear,
            Sizes = template.Sizes,
            Colors = template.Colors,
            Description = template.Description,
        };
        var drop = new Drop { Sneaker = sneaker, Pairs = 1, OpensAt = now.AddHours(-1), ClosesAt = now + closesIn, Winners = winners };
        await Drops.InsertOneAsync(drop, cancellationToken: Ct);
        return drop;
    }

    private async Task InsertEntry(Drop drop, string shopper) =>
        await Entries.InsertOneAsync(
            new DropEntry { DropId = drop.Id!, EntrantId = shopper, Size = drop.Sneaker.Sizes[0], EnteredAt = DateTime.UtcNow },
            cancellationToken: Ct);

    private static async Task<DropResponse> FetchDrop(HttpClient client, Drop drop, string? browser = null)
    {
        var response = await Send(client, HttpMethod.Get, "/api/drops", browser: browser);
        var body = await response.Content.ReadFromJsonAsync<DropsResponse>(Ct);
        return body!.Drops.Single(d => d.Id == drop.Id);
    }

    [Fact]
    public async Task Account_Anonymous_IsNull()
    {
        var session = await Device().GetFromJsonAsync<AccountSession>("/api/account", Ct);

        Assert.Null(session!.Account);
    }

    [Fact]
    public async Task Register_SignsInAndExposesOnlyTheEmail()
    {
        var client = Device();
        var email = NewEmail();

        var response = await Register(client, email);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await client.GetStringAsync("/api/account", Ct);
        Assert.Contains(email, json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shopper", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_NeverStoresThePlainPassword()
    {
        var email = NewEmail();
        await Member(email);

        var stored = await Accounts.Find(a => a.Email == email).FirstAsync(Ct);

        Assert.DoesNotContain(Password, stored.PasswordHash);
        Assert.True(stored.PasswordHash.Length > 40);
    }

    [Fact]
    public async Task Register_SessionCookieIsHttpOnlyAndStrict()
    {
        var response = await Register(Device(), NewEmail());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("stockroom.session"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_SameEmailDifferentCase_Returns409()
    {
        var email = NewEmail();
        await Member(email);

        var response = await Register(Device(), email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", Password)]
    [InlineData("", Password)]
    [InlineData("someone@example.com", "short")]
    public async Task Register_BadCredentials_Returns400(string email, string password)
    {
        var response = await Register(Device(), email, password: password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_VeryLongPassword_Returns400()
    {
        var response = await Register(Device(), NewEmail(), password: new string('x', 129));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_CorrectPassword_IgnoresEmailCase()
    {
        var email = NewEmail();
        await Member(email);
        var other = Device();

        var response = await SignIn(other, email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await other.GetFromJsonAsync<AccountSession>("/api/account", Ct);
        Assert.Equal(email, session!.Account!.Email);
    }

    [Fact]
    public async Task SignIn_WrongPasswordAndUnknownEmail_LookIdentical()
    {
        var email = NewEmail();
        await Member(email);

        var wrong = await SignIn(Device(), email, password: "wrong password");
        var unknown = await SignIn(Device(), NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(Ct), await unknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task SignIn_FailedAttempt_DoesNotCreateASession()
    {
        var email = NewEmail();
        await Member(email);
        var client = Device();

        await SignIn(client, email, password: "wrong password");

        Assert.Null((await client.GetFromJsonAsync<AccountSession>("/api/account", Ct))!.Account);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        var client = await Member();

        var response = await Send(client, HttpMethod.Post, "/api/account/sign-out");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await client.GetFromJsonAsync<AccountSession>("/api/account", Ct))!.Account);
    }

    [Fact]
    public async Task Account_DeletedAfterSignIn_IsTreatedAsSignedOut()
    {
        var email = NewEmail();
        var client = await Member(email);
        await Accounts.DeleteOneAsync(a => a.Email == email, Ct);

        Assert.Null((await client.GetFromJsonAsync<AccountSession>("/api/account", Ct))!.Account);
    }

    [Theory]
    [InlineData("GET", "/api/wishlist")]
    [InlineData("PUT", "/api/wishlist/000000000000000000000000")]
    [InlineData("DELETE", "/api/wishlist/000000000000000000000000")]
    [InlineData("GET", "/api/orders")]
    [InlineData("POST", "/api/orders")]
    public async Task MemberRoutes_Anonymous_Return401WithoutRedirect(string method, string path)
    {
        var response = await Send(Device(), new HttpMethod(method), path, browser: NewBrowser());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_TooManyAttempts_Returns429()
    {
        await using var limited = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Auth:AttemptsPerMinute", "2"));
        var client = limited.CreateClient();

        await SignIn(client, NewEmail());
        await SignIn(client, NewEmail());
        var third = await SignIn(client, NewEmail());

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Bag_FollowsTheAccountBetweenDevices()
    {
        var sneaker = await AnySneaker();
        var email = NewEmail();
        var laptop = await Member(email);
        await Add(laptop, sneaker.Id!, sneaker.Sizes[0]);

        var phone = Device();
        await SignIn(phone, email);

        Assert.Equal(sneaker.Sku, Assert.Single(await Bag(phone)).Sneaker.Sku);
    }

    [Fact]
    public async Task SignIn_MovesTheBrowserBagIntoTheAccount()
    {
        var sneaker = await AnySneaker();
        var browser = NewBrowser();
        var client = Device();
        await Add(client, sneaker.Id!, sneaker.Sizes[0], browser);

        await Register(client, NewEmail(), browser);

        Assert.Single(await Bag(client));
        Assert.Empty(await Bag(Device(), browser));
    }

    [Fact]
    public async Task SignIn_AddsQuantitiesTogetherOnlyOnce()
    {
        var sneaker = await AnySneaker();
        var email = NewEmail();
        var member = await Member(email);
        await Add(member, sneaker.Id!, sneaker.Sizes[0]);
        var browser = NewBrowser();
        var phone = Device();
        await Add(phone, sneaker.Id!, sneaker.Sizes[0], browser);

        await SignIn(phone, email, browser);
        await SignIn(phone, email, browser);

        Assert.Equal(2, Assert.Single(await Bag(phone)).Quantity);
    }

    [Fact]
    public async Task SignOut_DoesNotLeaveTheAccountBagOnTheDevice()
    {
        var sneaker = await AnySneaker();
        var browser = NewBrowser();
        var client = await Member();
        await Add(client, sneaker.Id!, sneaker.Sizes[0], browser);

        await Send(client, HttpMethod.Post, "/api/account/sign-out");

        Assert.Empty(await Bag(client, browser));
    }

    [Fact]
    public async Task SignedIn_IgnoresAnotherShoppersHeader()
    {
        var sneaker = await AnySneaker();
        var victim = NewBrowser();
        await Add(Device(), sneaker.Id!, sneaker.Sizes[0], victim);

        var attacker = await Member();

        Assert.Empty(await Bag(attacker, victim));
    }

    [Fact]
    public async Task SignIn_KeepsARaffleEntryMadeBeforeSigningIn()
    {
        var drop = await InsertDrop(TimeSpan.FromMinutes(10));
        var browser = NewBrowser();
        await InsertEntry(drop, browser);
        var client = Device();

        await Register(client, NewEmail(), browser);

        Assert.NotNull((await FetchDrop(client, drop)).Entry);
        Assert.Null((await FetchDrop(Device(), drop, browser)).Entry);
    }

    [Fact]
    public async Task SignIn_KeepsARaffleWinMadeBeforeSigningIn()
    {
        var browser = NewBrowser();
        var drop = await InsertDrop(TimeSpan.FromMinutes(-1), [browser]);
        await InsertEntry(drop, browser);
        var client = Device();

        await Register(client, NewEmail(), browser);

        Assert.Equal(DropResult.Won, (await FetchDrop(client, drop)).Entry!.Result);
        Assert.Equal(HttpStatusCode.OK, (await Add(client, drop.Sneaker.Id!, drop.Sneaker.Sizes[0])).StatusCode);
    }

    [Fact]
    public async Task SignIn_MovesAWonPairAlreadyInTheBrowserBag()
    {
        var browser = NewBrowser();
        var drop = await InsertDrop(TimeSpan.FromMinutes(-1), [browser]);
        await InsertEntry(drop, browser);
        var client = Device();
        await Add(client, drop.Sneaker.Id!, drop.Sneaker.Sizes[0], browser);

        await Register(client, NewEmail(), browser);

        Assert.Equal(drop.Sneaker.Id, Assert.Single(await Bag(client)).Sneaker.Id);
    }

    [Fact]
    public async Task SignIn_AccountAlreadyEntered_DropsTheBrowserEntryAndItsWin()
    {
        var email = NewEmail();
        var member = await Member(email);
        var shopper = await ShopperIdOf(email);
        var browser = NewBrowser();
        var drop = await InsertDrop(TimeSpan.FromMinutes(-1), [browser]);
        await InsertEntry(drop, shopper);
        await InsertEntry(drop, browser);

        await SignIn(Device(), email, browser);

        Assert.Equal(1, await Entries.CountDocumentsAsync(e => e.DropId == drop.Id, cancellationToken: Ct));
        Assert.Equal(DropResult.Lost, (await FetchDrop(member, drop)).Entry!.Result);
    }

    [Fact]
    public async Task Raffle_OneEntryPerAccountAcrossDevices()
    {
        var drop = await InsertDrop(TimeSpan.FromMinutes(10));
        var email = NewEmail();
        var laptop = await Member(email);
        var phone = Device();
        await SignIn(phone, email);
        var body = new EnterDropRequest(drop.Sneaker.Sizes[0]);

        var first = await Send(laptop, HttpMethod.Post, $"/api/drops/{drop.Id}/entries", body, NewBrowser());
        var second = await Send(phone, HttpMethod.Post, $"/api/drops/{drop.Id}/entries", body, NewBrowser());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Wishlist_SavesNewestFirstAndIgnoresRepeats()
    {
        var all = (await Device().GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct))!;
        var client = await Member();

        await Send(client, HttpMethod.Put, $"/api/wishlist/{all[0].Id}");
        await Send(client, HttpMethod.Put, $"/api/wishlist/{all[1].Id}");
        await Send(client, HttpMethod.Put, $"/api/wishlist/{all[1].Id}");

        Assert.Equal([all[1].Id, all[0].Id], (await Wishlist(client)).Select(s => s.Id));
    }

    [Fact]
    public async Task Wishlist_RemoveDeletesIt()
    {
        var sneaker = await AnySneaker();
        var client = await Member();
        await Send(client, HttpMethod.Put, $"/api/wishlist/{sneaker.Id}");

        var response = await Send(client, HttpMethod.Delete, $"/api/wishlist/{sneaker.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await Wishlist(client));
    }

    [Fact]
    public async Task Wishlist_FollowsTheAccountAndStaysPrivate()
    {
        var sneaker = await AnySneaker();
        var email = NewEmail();
        var laptop = await Member(email);
        await Send(laptop, HttpMethod.Put, $"/api/wishlist/{sneaker.Id}");
        var phone = Device();
        await SignIn(phone, email);

        Assert.Single(await Wishlist(phone));
        Assert.Empty(await Wishlist(await Member()));
    }

    [Theory]
    [InlineData("not-an-object-id")]
    [InlineData("000000000000000000000000")]
    public async Task Wishlist_UnknownSneaker_Returns404(string id)
    {
        var response = await Send(await Member(), HttpMethod.Put, $"/api/wishlist/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_RecordsTheBagAndEmptiesIt()
    {
        var sneaker = await AnySneaker();
        var client = await Member();
        await Add(client, sneaker.Id!, sneaker.Sizes[0]);
        await Add(client, sneaker.Id!, sneaker.Sizes[0]);

        var order = await PlaceOrder(client);

        var line = Assert.Single(order.Lines);
        Assert.Equal(sneaker.Sku, line.Sku);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(sneaker.Price, line.UnitPrice);
        Assert.Equal(sneaker.Price * 2, order.Total);
        Assert.Empty(await Bag(client));
    }

    [Fact]
    public async Task Order_EmptyBag_Returns409()
    {
        var response = await Send(await Member(), HttpMethod.Post, "/api/orders");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Order_DoubleSubmit_CreatesOnlyOne()
    {
        var sneaker = await AnySneaker();
        var client = await Member();
        await Add(client, sneaker.Id!, sneaker.Sizes[0]);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Send(client, HttpMethod.Post, "/api/orders")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(await Orders(client));
    }

    [Fact]
    public async Task Orders_NewestFirstAndPrivate()
    {
        var sneaker = await AnySneaker();
        var client = await Member();
        await Add(client, sneaker.Id!, sneaker.Sizes[0]);
        var first = await PlaceOrder(client);
        await Add(client, sneaker.Id!, sneaker.Sizes[1]);
        var second = await PlaceOrder(client);

        Assert.Equal([second.Id, first.Id], (await Orders(client)).Select(o => o.Id));
        Assert.Empty(await Orders(await Member()));
    }

    [Fact]
    public async Task Order_KeepsThePricePaid()
    {
        var email = NewEmail();
        var client = await Member(email);
        var shopper = await ShopperIdOf(email);
        var drop = await InsertDrop(TimeSpan.FromMinutes(-1), [shopper], price: 200m);
        await InsertEntry(drop, shopper);
        await Add(client, drop.Sneaker.Id!, drop.Sneaker.Sizes[0]);
        await PlaceOrder(client);

        await Drops.UpdateOneAsync(d => d.Id == drop.Id, Builders<Drop>.Update.Set(d => d.Sneaker.Price, 999m), cancellationToken: Ct);

        Assert.Equal(200m, Assert.Single(Assert.Single(await Orders(client)).Lines).UnitPrice);
    }

    [Fact]
    public async Task Order_WonPairCanOnlyBeOrderedOnce()
    {
        var email = NewEmail();
        var client = await Member(email);
        var shopper = await ShopperIdOf(email);
        var drop = await InsertDrop(TimeSpan.FromMinutes(-1), [shopper]);
        await InsertEntry(drop, shopper);
        await Add(client, drop.Sneaker.Id!, drop.Sneaker.Sizes[0]);

        await PlaceOrder(client);

        Assert.True((await FetchDrop(client, drop)).Entry!.Claimed);
        Assert.Equal(HttpStatusCode.Conflict, (await Add(client, drop.Sneaker.Id!, drop.Sneaker.Sizes[0])).StatusCode);
    }

    [Fact]
    public async Task Catalog_StillCachedForSignedInShoppers_WithoutLeakingCookies()
    {
        var client = await Member();
        var path = $"/api/sneakers?brand=uncached-{Guid.NewGuid():N}";

        await client.GetAsync(path, Ct);
        var second = await client.GetAsync(path, Ct);

        Assert.NotNull(second.Headers.Age);
        Assert.False(second.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task MemberData_IsNeverCached()
    {
        var client = await Member();
        await client.GetAsync("/api/orders", Ct);
        await client.GetAsync("/api/wishlist", Ct);
        await client.GetAsync("/api/account", Ct);

        Assert.Null((await client.GetAsync("/api/orders", Ct)).Headers.Age);
        Assert.Null((await client.GetAsync("/api/wishlist", Ct)).Headers.Age);
        Assert.Null((await client.GetAsync("/api/account", Ct)).Headers.Age);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesAccounts()
    {
        var document = await Device().GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"/api/account/sign-in\"", document);
        Assert.Contains("\"/api/wishlist/{sneakerId}\"", document);
        Assert.Contains("\"/api/orders\"", document);
    }
}

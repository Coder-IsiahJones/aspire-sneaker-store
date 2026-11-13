using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;
using SneakerStore.Messaging;

namespace SneakerStore.Api.Endpoints;

public static class AccountEndpoints
{
    public const string RateLimitPolicy = "account-credentials";

    private const int MinPasswordLength = 8;

    // Hashing is deliberately slow, so very long passwords must not reach it.
    private const int MaxPasswordLength = 128;

    // Verified when an email isn't registered, so that response takes as long as a real
    // check and timing can't reveal which emails have accounts.
    private static readonly string DummyHash = new PasswordHasher<Account>().HashPassword(null!, "not-a-real-password");

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/api").WithTags("Account");

        account.MapGet("/account", async (HttpContext http, ClaimsPrincipal user, IMongoCollection<Account> accounts) =>
        {
            if (AccountId(user) is not { } id)
            {
                return new AccountSession(null);
            }

            var found = await accounts.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (found is null)
            {
                // The cookie outlived its account, for example after a database reset.
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return new AccountSession(null);
            }

            return new AccountSession(new AccountView(found.Email));
        })
        .WithName("GetAccount")
        .WithSummary("Get the signed-in account, or a null account")
        .Produces<AccountSession>();

        account.MapPost("/account/register", async (
            CredentialsRequest request,
            [FromHeader(Name = "X-Entrant-Id")] string? entrantHeader,
            HttpContext http,
            IMongoCollection<Account> accounts,
            IPasswordHasher<Account> hasher,
            DropRaffle raffle,
            BagStore bag) =>
        {
            if (!TryNormalizeEmail(request.Email, out var email)
                || request.Password is not { Length: >= MinPasswordLength and <= MaxPasswordLength })
            {
                return Results.Problem(
                    $"Use a valid email and a password of {MinPasswordLength} to {MaxPasswordLength} characters.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var created = new Account
            {
                Email = email,
                PasswordHash = hasher.HashPassword(null!, request.Password),
                ShopperId = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
            };

            try
            {
                await accounts.InsertOneAsync(created);
            }
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                return Results.Problem("That email already has an account.", statusCode: StatusCodes.Status409Conflict);
            }

            await SignInAsync(http, created, entrantHeader, raffle, bag);
            return Results.Created("/api/account", new AccountView(created.Email));
        })
        .RequireRateLimiting(RateLimitPolicy)
        .WithName("Register")
        .WithSummary("Create an account and sign in. This browser's bag and raffle entries move to it.")
        .Produces<AccountView>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status429TooManyRequests);

        account.MapPost("/account/sign-in", async (
            CredentialsRequest request,
            [FromHeader(Name = "X-Entrant-Id")] string? entrantHeader,
            HttpContext http,
            IMongoCollection<Account> accounts,
            IPasswordHasher<Account> hasher,
            DropRaffle raffle,
            BagStore bag) =>
        {
            var password = request.Password is { Length: <= MaxPasswordLength } ? request.Password : "";
            var found = TryNormalizeEmail(request.Email, out var email)
                ? await accounts.Find(a => a.Email == email).FirstOrDefaultAsync()
                : null;

            var verdict = hasher.VerifyHashedPassword(null!, found?.PasswordHash ?? DummyHash, password);
            if (found is null || verdict == PasswordVerificationResult.Failed)
            {
                return Results.Problem("That email and password don't match.", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            {
                await accounts.UpdateOneAsync(
                    a => a.Id == found.Id,
                    Builders<Account>.Update.Set(a => a.PasswordHash, hasher.HashPassword(null!, password)));
            }

            await SignInAsync(http, found, entrantHeader, raffle, bag);
            return Results.Ok(new AccountView(found.Email));
        })
        .RequireRateLimiting(RateLimitPolicy)
        .WithName("SignIn")
        .WithSummary("Sign in. This browser's bag and raffle entries move to the account.")
        .Produces<AccountView>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests);

        account.MapPost("/account/sign-out", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
        .WithName("SignOut")
        .WithSummary("Sign out")
        .Produces(StatusCodes.Status204NoContent);

        var member = app.MapGroup("/api").WithTags("Account").RequireAuthorization();

        member.MapGet("/wishlist", async (
            ClaimsPrincipal user,
            IMongoCollection<Account> accounts,
            IMongoCollection<Sneaker> sneakers) =>
        {
            var id = AccountId(user)!;
            var found = await accounts.Find(a => a.Id == id).FirstOrDefaultAsync();
            return found is null ? Results.Unauthorized() : Results.Ok(await ResolveWishlist(found, sneakers));
        })
        .WithName("GetWishlist")
        .WithSummary("List saved sneakers, newest first")
        .Produces<List<Sneaker>>()
        .Produces(StatusCodes.Status401Unauthorized);

        member.MapPut("/wishlist/{sneakerId}", async (
            string sneakerId,
            ClaimsPrincipal user,
            IMongoCollection<Account> accounts,
            IMongoCollection<Sneaker> sneakers) =>
        {
            if (!ObjectId.TryParse(sneakerId, out _)
                || !await sneakers.Find(s => s.Id == sneakerId).AnyAsync())
            {
                return Results.NotFound();
            }

            return await UpdateWishlist(user, accounts, sneakers, Builders<Account>.Update.AddToSet(a => a.Wishlist, sneakerId));
        })
        .WithName("SaveToWishlist")
        .WithSummary("Save a sneaker and return the wishlist")
        .Produces<List<Sneaker>>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        member.MapDelete("/wishlist/{sneakerId}", (
            string sneakerId,
            ClaimsPrincipal user,
            IMongoCollection<Account> accounts,
            IMongoCollection<Sneaker> sneakers) =>
            UpdateWishlist(user, accounts, sneakers, Builders<Account>.Update.Pull(a => a.Wishlist, sneakerId)))
        .WithName("RemoveFromWishlist")
        .WithSummary("Remove a saved sneaker and return the wishlist")
        .Produces<List<Sneaker>>()
        .Produces(StatusCodes.Status401Unauthorized);

        member.MapGet("/orders", async (ClaimsPrincipal user, OrderStore orders) =>
            (await orders.ListAsync(AccountId(user)!)).Select(OrderView.From).ToList())
        .WithName("ListOrders")
        .WithSummary("List the account's orders, newest first")
        .Produces<List<OrderView>>()
        .Produces(StatusCodes.Status401Unauthorized);

        member.MapPost("/orders", async (
            ClaimsPrincipal user,
            OrderStore orders,
            EventPublisher events,
            ILoggerFactory loggers) =>
        {
            var order = await orders.PlaceAsync(AccountId(user)!, user.FindFirstValue(ShopperIdentity.ClaimType)!);
            if (order is null)
            {
                return Results.Problem("The bag is empty.", statusCode: StatusCodes.Status409Conflict);
            }

            // The order is saved. Everything that follows from it (the receipt, the packing) happens
            // in the worker, so checkout only has to say that the order exists.
            await AnnounceAsync(order, user.FindFirstValue(ClaimTypes.Email)!, events, loggers.CreateLogger("Orders"));
            return Results.Created($"/api/orders/{order.Id}", OrderView.From(order));
        })
        .WithName("PlaceOrder")
        .WithSummary("Turn the bag into an order and announce it. No payment is taken.")
        .Produces<OrderView>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static string? AccountId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Publishes <see cref="OrderPlaced"/>. This runs after the order is saved and outside
    /// <see cref="OrderStore.PlaceAsync"/>, so a broker problem can't undo a checkout. It is not an
    /// outbox: if the broker is unreachable at this moment, the order exists and nobody hears about
    /// it, so it never gets a receipt or a packing time.
    /// </summary>
    private static async Task AnnounceAsync(Order order, string email, EventPublisher events, ILogger logger)
    {
        // Checkout shouldn't hang on the broker either.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await events.PublishAsync(
                Topology.Keys.OrderPlaced,
                new OrderPlaced(order.Id!, order.AccountId, email, order.Lines.Sum(l => l.Quantity), order.Total, order.PlacedAt),
                // Both work queues are bound to this key. If the broker can't route it, that's an error.
                mandatory: true,
                timeout.Token);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Order {OrderId} was saved but could not be announced.", order.Id);
        }
    }

    private static bool TryNormalizeEmail(string? input, out string email)
    {
        email = input?.Trim().ToLowerInvariant() ?? "";
        return email.Length <= 254
            && MailAddress.TryCreate(email, out var parsed)
            && parsed.Address == email;
    }

    private static async Task SignInAsync(
        HttpContext http,
        Account account,
        string? entrantHeader,
        DropRaffle raffle,
        BagStore bag)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id!),
                new Claim(ClaimTypes.Email, account.Email),
                new Claim(ShopperIdentity.ClaimType, account.ShopperId),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        // Entries first, because moving a won pair into the bag checks that the account won it.
        if (ShopperIdentity.TryNormalize(entrantHeader, out var browserId) && browserId != account.ShopperId)
        {
            await raffle.TransferAsync(browserId, account.ShopperId);
            await bag.MergeAsync(browserId, account.ShopperId);
        }
    }

    private static async Task<IResult> UpdateWishlist(
        ClaimsPrincipal user,
        IMongoCollection<Account> accounts,
        IMongoCollection<Sneaker> sneakers,
        UpdateDefinition<Account> update)
    {
        var id = AccountId(user)!;
        var updated = await accounts.FindOneAndUpdateAsync(
            a => a.Id == id,
            update,
            new FindOneAndUpdateOptions<Account> { ReturnDocument = ReturnDocument.After });

        return updated is null ? Results.Unauthorized() : Results.Ok(await ResolveWishlist(updated, sneakers));
    }

    private static async Task<List<Sneaker>> ResolveWishlist(Account account, IMongoCollection<Sneaker> sneakers)
    {
        var found = (await sneakers.Find(Builders<Sneaker>.Filter.In(s => s.Id, account.Wishlist)).ToListAsync())
            .ToDictionary(s => s.Id!);

        return [.. Enumerable.Reverse(account.Wishlist).Where(found.ContainsKey).Select(id => found[id])];
    }
}

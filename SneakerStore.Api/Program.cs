using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Endpoints;
using SneakerStore.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// "sneakerdb" matches the database resource name in apphost.cs.
// Aspire supplies the connection string, so there is none in appsettings.json.
builder.AddMongoDBClient("sneakerdb");

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<Sneaker>("sneakers"));
    
builder.Services.AddSingleton<SneakerSeeder>();

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<Drop>("drops"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<DropEntry>("dropEntries"));
builder.Services.AddSingleton<DropSeeder>();
builder.Services.AddSingleton<DropRaffle>();

// "redis" matches the resource name in apphost.cs. This registers the Redis-backed
// output cache and the IConnectionMultiplexer that BagStore uses.
builder.AddRedisOutputCache("redis");
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(CatalogCache.Policy, new CatalogCachePolicy());
});
builder.Services.AddSingleton<BagStore>();

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<Account>("accounts"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoDatabase>().GetCollection<Order>("orders"));
builder.Services.AddSingleton<AccountIndexes>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "stockroom.session";
        options.Cookie.HttpOnly = true;
        // Strict keeps the cookie off requests from other sites, which is the CSRF protection here.
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Local development is plain HTTP through the Vite proxy. HTTPS requests still get Secure.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        // This is an API, so answer with status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Slows password guessing. Behind a reverse proxy this needs forwarded headers,
// or every visitor shares the proxy's address.
var attemptsPerMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AccountEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = attemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseOutputCache();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document at /openapi/v1.json, Swagger UI at /swagger.
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Sneaker Store API v1");
    });
}

await app.Services.GetRequiredService<SneakerSeeder>().SeedAsync();
await app.Services.GetRequiredService<DropSeeder>().SeedAsync();
await app.Services.GetRequiredService<AccountIndexes>().EnsureAsync();

// Redis outlives the API, so drop anything cached from before this start's seed.
await app.Services.GetRequiredService<IOutputCacheStore>().EvictByTagAsync(CatalogCache.Tag, default);

var api = app.MapGroup("/api").WithTags("Sneakers").CacheOutput(CatalogCache.Policy);

api.MapGet("/sneakers", async (string? brand, IMongoCollection<Sneaker> sneakers) =>
{
    var filter = string.IsNullOrWhiteSpace(brand)
        ? Builders<Sneaker>.Filter.Empty
        : Builders<Sneaker>.Filter.Eq(s => s.Brand, brand);

    // Case-insensitive collation so "adidas" sorts with the A's instead of after "Vans".
    var options = new FindOptions { Collation = new Collation("en", strength: CollationStrength.Secondary) };

    return await sneakers.Find(filter, options)
        .SortBy(s => s.Brand)
        .ThenBy(s => s.Name)
        .ToListAsync();
})
.WithName("ListSneakers")
.WithSummary("List sneakers, optionally filtered by brand")
.Produces<List<Sneaker>>();

api.MapGet("/sneakers/{id}", async (string id, IMongoCollection<Sneaker> sneakers) =>
{
    if (!ObjectId.TryParse(id, out _))
    {
        return Results.NotFound();
    }

    var sneaker = await sneakers.Find(s => s.Id == id).FirstOrDefaultAsync();
    return sneaker is null ? Results.NotFound() : Results.Ok(sneaker);
})
.WithName("GetSneaker")
.WithSummary("Get one sneaker by id")
.Produces<Sneaker>()
.Produces(StatusCodes.Status404NotFound);

api.MapGet("/brands", async (IMongoCollection<Sneaker> sneakers) =>
{
    var brands = await sneakers
        .Distinct(s => s.Brand, FilterDefinition<Sneaker>.Empty)
        .ToListAsync();

    return brands.Order(StringComparer.OrdinalIgnoreCase);
})
.WithName("ListBrands")
.WithSummary("List distinct brand names")
.Produces<IEnumerable<string>>();

var dropsApi = app.MapGroup("/api").WithTags("Drops");

dropsApi.MapGet("/drops", async (
    [FromHeader(Name = "X-Entrant-Id")] string? entrantHeader,
    ClaimsPrincipal user,
    IMongoCollection<Drop> drops,
    IMongoCollection<DropEntry> entries,
    DropRaffle raffle) =>
{
    var now = DateTime.UtcNow;
    var stored = await drops.Find(FilterDefinition<Drop>.Empty).ToListAsync();

    var current = new List<Drop>(stored.Count);
    foreach (var drop in stored)
    {
        current.Add(drop.PhaseAt(now) == DropPhase.Closed ? await raffle.DrawAsync(drop) : drop);
    }

    var counts = (await entries.Aggregate()
            .Group(e => e.DropId, g => new { DropId = g.Key, Count = g.Count() })
            .ToListAsync())
        .ToDictionary(c => c.DropId, c => c.Count);

    var mine = ShopperIdentity.TryResolve(user, entrantHeader, out var entrantId)
        ? (await entries.Find(e => e.EntrantId == entrantId).ToListAsync()).ToDictionary(e => e.DropId)
        : [];

    var response = current
        .OrderBy(d => d.PhaseAt(now) switch
        {
            DropPhase.Open => 0,
            DropPhase.Upcoming => 1,
            _ => 2,
        })
        .ThenBy(d => d.ClosesAt)
        .Select(d => new DropResponse(
            d.Id!,
            d.Sneaker,
            d.Pairs,
            d.OpensAt,
            d.ClosesAt,
            d.PhaseAt(now),
            counts.GetValueOrDefault(d.Id!),
            mine.GetValueOrDefault(d.Id!)?.StatusIn(d)))
        .ToList();

    return new DropsResponse(now, response);
})
.WithName("ListDrops")
.WithSummary("List raffle drops, with the caller's entry if X-Entrant-Id is sent")
.Produces<DropsResponse>();

dropsApi.MapPost("/drops/{id}/entries", async (
    string id,
    EnterDropRequest request,
    [FromHeader(Name = "X-Entrant-Id")] string? entrantHeader,
    ClaimsPrincipal user,
    IMongoCollection<Drop> drops,
    IMongoCollection<DropEntry> entries) =>
{
    if (!ObjectId.TryParse(id, out _))
    {
        return Results.NotFound();
    }

    var drop = await drops.Find(d => d.Id == id).FirstOrDefaultAsync();
    if (drop is null)
    {
        return Results.NotFound();
    }

    if (!ShopperIdentity.TryResolve(user, entrantHeader, out var entrantId))
    {
        return BadEntrant();
    }

    if (!drop.Sneaker.Sizes.Contains(request.Size))
    {
        return Results.Problem("That size isn't part of this drop.", statusCode: StatusCodes.Status400BadRequest);
    }

    var now = DateTime.UtcNow;
    if (drop.PhaseAt(now) != DropPhase.Open)
    {
        return Results.Problem("This drop isn't taking entries.", statusCode: StatusCodes.Status409Conflict);
    }

    var entry = new DropEntry { DropId = id, EntrantId = entrantId, Size = request.Size, EnteredAt = now };
    try
    {
        await entries.InsertOneAsync(entry);
    }
    catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
    {
        return Results.Problem("Already entered.", statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Created($"/api/drops/{id}/entries", entry.StatusIn(drop));
})
.WithName("EnterDrop")
.WithSummary("Enter a drop's raffle, once per X-Entrant-Id")
.Produces<DropEntryStatus>(StatusCodes.Status201Created)
.ProducesProblem(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status409Conflict);

var bagApi = app.MapGroup("/api").WithTags("Bag");

bagApi.MapGet("/bag", async ([FromHeader(Name = "X-Entrant-Id")] string? entrantHeader, ClaimsPrincipal user, BagStore bag) =>
    ShopperIdentity.TryResolve(user, entrantHeader, out var shopperId)
        ? Results.Ok(await bag.GetAsync(shopperId))
        : BadEntrant())
.WithName("GetBag")
.WithSummary("Get the shopper's bag")
.Produces<List<BagLine>>()
.ProducesProblem(StatusCodes.Status400BadRequest);

bagApi.MapPost("/bag/items", async (
    AddToBagRequest request,
    [FromHeader(Name = "X-Entrant-Id")] string? entrantHeader,
    ClaimsPrincipal user,
    BagStore bag) =>
{
    if (!ShopperIdentity.TryResolve(user, entrantHeader, out var shopperId))
    {
        return BadEntrant();
    }

    return await bag.AddAsync(shopperId, request.SneakerId, request.Size) switch
    {
        BagAddResult.Added => Results.Ok(await bag.GetAsync(shopperId)),
        BagAddResult.NotFound => Results.NotFound(),
        BagAddResult.InvalidSize => Results.Problem("That size isn't available.", statusCode: StatusCodes.Status400BadRequest),
        BagAddResult.NotWinner => Results.Problem("Only the raffle's winners can bag this pair.", statusCode: StatusCodes.Status403Forbidden),
        BagAddResult.AlreadyOrdered => Results.Problem("This pair has already been ordered.", statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem("This pair is already in the bag.", statusCode: StatusCodes.Status409Conflict),
    };
})
.WithName("AddToBag")
.WithSummary("Add a pair to the shopper's bag and return the bag")
.Produces<List<BagLine>>()
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status409Conflict);

bagApi.MapDelete("/bag/items/{key}", async (
    string key,
    [FromHeader(Name = "X-Entrant-Id")] string? entrantHeader,
    ClaimsPrincipal user,
    BagStore bag) =>
{
    if (!ShopperIdentity.TryResolve(user, entrantHeader, out var shopperId))
    {
        return BadEntrant();
    }

    await bag.RemoveAsync(shopperId, key);
    return Results.Ok(await bag.GetAsync(shopperId));
})
.WithName("RemoveFromBag")
.WithSummary("Remove a line from the shopper's bag and return the bag")
.Produces<List<BagLine>>()
.ProducesProblem(StatusCodes.Status400BadRequest);

app.MapAccountEndpoints();

app.Run();

static IResult BadEntrant() =>
    Results.Problem("X-Entrant-Id must be a GUID.", statusCode: StatusCodes.Status400BadRequest);

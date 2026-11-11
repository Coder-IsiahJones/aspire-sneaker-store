using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
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

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

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

var api = app.MapGroup("/api").WithTags("Sneakers");

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

    var mine = TryNormalizeEntrant(entrantHeader, out var entrantId)
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

    if (!TryNormalizeEntrant(entrantHeader, out var entrantId))
    {
        return Results.Problem("X-Entrant-Id must be a GUID.", statusCode: StatusCodes.Status400BadRequest);
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

app.Run();

// Normalized so the same GUID written two ways can't count as two entrants.
static bool TryNormalizeEntrant(string? header, out string entrantId)
{
    if (Guid.TryParse(header, out var guid) && guid != Guid.Empty)
    {
        entrantId = guid.ToString("N");
        return true;
    }

    entrantId = "";
    return false;
}

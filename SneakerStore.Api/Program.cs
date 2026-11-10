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

var app = builder.Build();

app.MapDefaultEndpoints();

await app.Services.GetRequiredService<SneakerSeeder>().SeedAsync();

var api = app.MapGroup("/api");

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
});

api.MapGet("/sneakers/{id}", async (string id, IMongoCollection<Sneaker> sneakers) =>
{
    if (!ObjectId.TryParse(id, out _))
    {
        return Results.NotFound();
    }

    var sneaker = await sneakers.Find(s => s.Id == id).FirstOrDefaultAsync();
    return sneaker is null ? Results.NotFound() : Results.Ok(sneaker);
});

api.MapGet("/brands", async (IMongoCollection<Sneaker> sneakers) =>
{
    var brands = await sneakers
        .Distinct(s => s.Brand, FilterDefinition<Sneaker>.Empty)
        .ToListAsync();

    return brands.Order(StringComparer.OrdinalIgnoreCase);
});

app.Run();

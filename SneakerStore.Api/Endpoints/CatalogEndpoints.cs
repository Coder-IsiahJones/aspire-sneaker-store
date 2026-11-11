using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var catalogApi = app.MapGroup("/api").WithTags("Sneakers").CacheOutput(CatalogCache.Policy);

        catalogApi.MapGet("/sneakers", async (string? brand, IMongoCollection<Sneaker> sneakers) =>
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

        catalogApi.MapGet("/sneakers/{id}", async (string id, IMongoCollection<Sneaker> sneakers) =>
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

        catalogApi.MapGet("/brands", async (IMongoCollection<Sneaker> sneakers) =>
        {
            var brands = await sneakers
                .Distinct(s => s.Brand, FilterDefinition<Sneaker>.Empty)
                .ToListAsync();

            return brands.Order(StringComparer.OrdinalIgnoreCase);
        })
        .WithName("ListBrands")
        .WithSummary("List distinct brand names")
        .Produces<IEnumerable<string>>();
    }
}

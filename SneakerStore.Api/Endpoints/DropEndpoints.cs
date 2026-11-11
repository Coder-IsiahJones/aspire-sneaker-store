using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Endpoints;

public static class DropEndpoints
{
    public static void MapDropEndpoints(this WebApplication app)
    {
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
                return ShopperResults.BadEntrant();
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
    }
}

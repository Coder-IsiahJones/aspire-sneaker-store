using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Endpoints;

public static class BagEndpoints
{
    public static void MapBagEndpoints(this WebApplication app)
    {
        var bagApi = app.MapGroup("/api").WithTags("Bag");

        bagApi.MapGet("/bag", async ([FromHeader(Name = "X-Entrant-Id")] string? entrantHeader, ClaimsPrincipal user, BagStore bag) =>
            ShopperIdentity.TryResolve(user, entrantHeader, out var shopperId)
                ? Results.Ok(await bag.GetAsync(shopperId))
                : ShopperResults.BadEntrant())
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
                return ShopperResults.BadEntrant();
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
                return ShopperResults.BadEntrant();
            }

            await bag.RemoveAsync(shopperId, key);
            return Results.Ok(await bag.GetAsync(shopperId));
        })
        .WithName("RemoveFromBag")
        .WithSummary("Remove a line from the shopper's bag and return the bag")
        .Produces<List<BagLine>>()
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}

namespace SneakerStore.Api.Endpoints;

internal static class ShopperResults
{
    public static IResult BadEntrant() =>
        Results.Problem("X-Entrant-Id must be a GUID.", statusCode: StatusCodes.Status400BadRequest);
}

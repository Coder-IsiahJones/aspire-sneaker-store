using System.Security.Claims;

namespace SneakerStore.Api.Data;

public static class ShopperIdentity
{
    public const string ClaimType = "shopper";

    // Normalized so the same GUID written two ways can't count as two entrants.
    public static bool TryNormalize(string? header, out string shopperId)
    {
        if (Guid.TryParse(header, out var guid) && guid != Guid.Empty)
        {
            shopperId = guid.ToString("N");
            return true;
        }

        shopperId = "";
        return false;
    }

    /// <summary>
    /// A signed-in shopper is always their account, whatever the header says.
    /// Anyone else is the browser id from the header.
    /// </summary>
    public static bool TryResolve(ClaimsPrincipal user, string? header, out string shopperId)
    {
        if (user.FindFirstValue(ClaimType) is { } claim)
        {
            shopperId = claim;
            return true;
        }

        return TryNormalize(header, out shopperId);
    }
}

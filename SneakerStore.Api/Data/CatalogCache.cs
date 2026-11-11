using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace SneakerStore.Api.Data;

public static class CatalogCache
{
    public const string Policy = "catalog";
    public const string Tag = "catalog";
}

/// <summary>
/// The framework's default policy skips anyone signed in, because most pages differ per
/// user. The catalog is identical for everyone, so this caches it for members too.
/// Only apply it to public endpoints.
/// </summary>
public sealed class CatalogCachePolicy : IOutputCachePolicy
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        var isRead = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = isRead;
        context.AllowCacheStorage = isRead;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Lifetime;
        context.CacheVaryByRules.QueryKeys = "brand";
        context.Tags.Add(CatalogCache.Tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        // A refreshed session cookie must never be stored and replayed to someone else.
        if (response.StatusCode != StatusCodes.Status200OK || !StringValues.IsNullOrEmpty(response.Headers.SetCookie))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}

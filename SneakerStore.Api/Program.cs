using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OutputCaching;
using MongoDB.Driver;
using RabbitMQ.Client;
using SneakerStore.Api.Data;
using SneakerStore.Api.Endpoints;
using SneakerStore.Api.Models;
using SneakerStore.Messaging;

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

// "messaging" matches the RabbitMQ resource name in apphost.cs. This registers one shared
// IConnection, which EventPublisher opens the first time an order is announced.
builder.AddRabbitMQClient("messaging");
builder.Services.AddSingleton(services => new EventPublisher(services.GetRequiredService<IConnection>));

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

app.MapCatalogEndpoints();
app.MapDropEndpoints();
app.MapBagEndpoints();
app.MapAccountEndpoints();

app.Run();

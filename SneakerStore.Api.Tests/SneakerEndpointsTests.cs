using System.Net;
using System.Net.Http.Json;
using SneakerStore.Api.Data;
using SneakerStore.Api.Models;

namespace SneakerStore.Api.Tests;

[Collection(MongoCollection.Name)]
public sealed class SneakerEndpointsTests(MongoFixture fixture)
{
    private readonly HttpClient _client = fixture.Factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListSneakers_ReturnsWholeSeededCatalog()
    {
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct);

        Assert.NotNull(sneakers);
        Assert.Equal(
            SeedData.Sneakers().Select(s => s.Sku).Order(),
            sneakers.Select(s => s.Sku).Order());
        Assert.All(sneakers, s => Assert.False(string.IsNullOrEmpty(s.Id)));
    }

    [Fact]
    public async Task ListSneakers_SortsByBrandIgnoringCase_ThenByName()
    {
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct);

        // "adidas" is lowercase, so an ordinal sort would put it after "Vans".
        string[] expected =
        [
            "ADI-GZL-GRN", "ADI-SMB-OG",                  // adidas: Gazelle, Samba OG
            "ASC-GK14-CRM",                               // ASICS
            "CNV-C70-PCH",                                // Converse
            "JRD-AJ1-CHI", "JRD-AJ4-BRD",                 // Jordan
            "NB-550-WGR", "NB-990V6-GRY",                 // New Balance: 550, 990v6
            "NKE-AF1-WHT", "NKE-AM1-ROY", "NKE-DNK-PND",  // Nike: Air Force 1, Air Max 1, Dunk
            "VNS-OSK-BLK",                                // Vans
        ];
        Assert.Equal(expected, sneakers!.Select(s => s.Sku));
    }

    [Theory]
    [InlineData("Nike", 3)]
    [InlineData("adidas", 2)]
    [InlineData("Vans", 1)]
    public async Task ListSneakers_FiltersByBrand(string brand, int expectedCount)
    {
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>(
            $"/api/sneakers?brand={Uri.EscapeDataString(brand)}", Ct);

        Assert.Equal(expectedCount, sneakers!.Count);
        Assert.All(sneakers, s => Assert.Equal(brand, s.Brand));
    }

    [Fact]
    public async Task ListSneakers_BrandWithSpaces_IsMatched()
    {
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>(
            "/api/sneakers?brand=New%20Balance", Ct);

        Assert.Equal(["550", "990v6"], sneakers!.Select(s => s.Name));
    }

    [Fact]
    public async Task ListSneakers_BrandFilterIgnoresCase()
    {
        // The case-insensitive collation applies to the filter as well as the sort.
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers?brand=nike", Ct);

        Assert.Equal(3, sneakers!.Count);
        Assert.All(sneakers, s => Assert.Equal("Nike", s.Brand));
    }

    [Fact]
    public async Task ListSneakers_UnknownBrand_ReturnsEmptyList()
    {
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers?brand=Reebok", Ct);

        Assert.NotNull(sneakers);
        Assert.Empty(sneakers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20%20")]
    public async Task ListSneakers_BlankBrand_ReturnsEverything(string brand)
    {
        var sneakers = await _client.GetFromJsonAsync<List<Sneaker>>($"/api/sneakers?brand={brand}", Ct);

        Assert.Equal(SeedData.Sneakers().Count, sneakers!.Count);
    }

    [Fact]
    public async Task GetSneaker_ExistingId_ReturnsThatSneaker()
    {
        var all = await _client.GetFromJsonAsync<List<Sneaker>>("/api/sneakers", Ct);
        var first = all![0];

        var sneaker = await _client.GetFromJsonAsync<Sneaker>($"/api/sneakers/{first.Id}", Ct);

        Assert.NotNull(sneaker);
        Assert.Equal(first.Id, sneaker.Id);
        Assert.Equal(first.Sku, sneaker.Sku);
        Assert.Equal(first.Price, sneaker.Price);
        Assert.Equal(first.Sizes, sneaker.Sizes);
        Assert.Equal(first.Colors, sneaker.Colors);
    }

    [Theory]
    [InlineData("not-an-object-id")]
    [InlineData("123")]
    public async Task GetSneaker_MalformedId_Returns404(string id)
    {
        var response = await _client.GetAsync($"/api/sneakers/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSneaker_WellFormedButUnknownId_Returns404()
    {
        var response = await _client.GetAsync("/api/sneakers/000000000000000000000000", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBrands_ReturnsDistinctBrandsSortedIgnoringCase()
    {
        var brands = await _client.GetFromJsonAsync<List<string>>("/api/brands", Ct);

        Assert.Equal(["adidas", "ASICS", "Converse", "Jordan", "New Balance", "Nike", "Vans"], brands);
    }

    [Fact]
    public async Task Health_ReportsHealthyWhenMongoIsUp()
    {
        var response = await _client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task OpenApiDocument_DescribesAllOperations()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("\"/api/sneakers\"", document);
        Assert.Contains("\"/api/sneakers/{id}\"", document);
        Assert.Contains("\"/api/brands\"", document);
        Assert.Contains("ListSneakers", document);
        Assert.Contains("GetSneaker", document);
        Assert.Contains("ListBrands", document);
    }
}

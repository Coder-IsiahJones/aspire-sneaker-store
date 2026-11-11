using System.Text.RegularExpressions;
using SneakerStore.Api.Data;

namespace SneakerStore.Api.Tests;

/// <summary>Checks on the seed catalog itself. These don't need MongoDB.</summary>
public sealed partial class SeedDataTests
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    [Fact]
    public void Sneakers_HasTwelvePairs()
    {
        Assert.Equal(12, SeedData.Sneakers().Count);
    }

    [Fact]
    public void Sneakers_SkusAreUnique()
    {
        // The seeder puts a unique index on Sku, so a duplicate here would fail the seed.
        var duplicates = SeedData.Sneakers()
            .GroupBy(s => s.Sku)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Sneakers_HaveNoIdsYet()
    {
        // MongoDB assigns ids on insert.
        Assert.All(SeedData.Sneakers(), s => Assert.Null(s.Id));
    }

    [Fact]
    public void Sneakers_ReturnsFreshInstancesEachCall()
    {
        // InsertMany writes ids back onto the objects, so sharing them between calls would leak ids.
        var first = SeedData.Sneakers();
        var second = SeedData.Sneakers();

        Assert.All(first.Zip(second), pair => Assert.NotSame(pair.First, pair.Second));
    }

    [Fact]
    public void Sneakers_HaveThreeHexColorsForTheIllustration()
    {
        Assert.All(SeedData.Sneakers(), s =>
        {
            Assert.Equal(3, s.Colors.Length);
            Assert.All(s.Colors, c => Assert.Matches(HexColor(), c));
        });
    }

    [Fact]
    public void Sneakers_HaveSizesInAscendingOrder()
    {
        Assert.All(SeedData.Sneakers(), s =>
        {
            Assert.NotEmpty(s.Sizes);
            Assert.Equal(s.Sizes.Order(), s.Sizes);
            Assert.Equal(s.Sizes.Distinct(), s.Sizes);
        });
    }

    [Fact]
    public void Sneakers_HaveSensiblePricesAndYears()
    {
        Assert.All(SeedData.Sneakers(), s =>
        {
            Assert.InRange(s.Price, 1m, 1000m);
            Assert.InRange(s.ReleaseYear, 1970, DateTime.UtcNow.Year);
        });
    }

    [Fact]
    public void Sneakers_HaveTextFieldsFilledIn()
    {
        Assert.All(SeedData.Sneakers(), s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Brand));
            Assert.False(string.IsNullOrWhiteSpace(s.Colorway));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }
}

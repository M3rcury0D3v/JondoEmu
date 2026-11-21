using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Server.Tests;

public sealed class TransportCatalogTests
{
    [Fact]
    public void Catalog_contains_all_three_categories()
    {
        var catalog = TransportCatalogManager.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, d => d.Category == TransportCategory.Miscellaneous);
        Assert.Contains(catalog, d => d.Category == TransportCategory.Zones);
        Assert.Contains(catalog, d => d.Category == TransportCategory.Dungeons);
    }

    [Fact]
    public void Miscellaneous_category_contains_essential_destinations()
    {
        var misc = TransportCatalogManager.GetDestinationsByCategory(TransportCategory.Miscellaneous).ToList();

        Assert.Contains(misc, d => d.Name.Contains("Astrub", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(misc, d => d.Name.Contains("Poutch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(misc, d => d.Name.Contains("Kolizeum", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Koliseo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(misc, d => d.Name.Contains("PvP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_filter_matches_name_and_coordinates()
    {
        var resultsByName = TransportCatalogManager.Filter(TransportCatalogManager.GetCatalog(), "astrub");
        Assert.NotEmpty(resultsByName);
        Assert.All(resultsByName, r => Assert.Contains("astrub", r.Name, StringComparison.OrdinalIgnoreCase));

        var resultsByCoords = TransportCatalogManager.Filter(TransportCatalogManager.GetCatalog(), "5,-18");
        Assert.NotEmpty(resultsByCoords);
        Assert.Contains(resultsByCoords, r => r.X == 5 && r.Y == -18);
    }

    [Fact]
    public void All_destinations_have_valid_coordinates_and_positive_levels()
    {
        var catalog = TransportCatalogManager.GetCatalog();

        foreach (var dest in catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(dest.Name), $"Destination map {dest.MapId} has empty name");
            Assert.True(dest.Level >= 1, $"Destination {dest.Name} has invalid level {dest.Level}");
            Assert.True(dest.ZoneBonus >= 0 && dest.ZoneBonus <= 100, $"Destination {dest.Name} has invalid zone bonus {dest.ZoneBonus}");
        }
    }
}

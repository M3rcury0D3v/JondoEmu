using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Server.Tests;

public sealed class TransportSecurityTests
{
    [Fact]
    public void Planner_handles_target_already_at_current_position()
    {
        var current = new AutopilotPoint(5, -18);
        var target = new AutopilotPoint(5, -18);
        var zaaps = new List<AutopilotPoint> { new AutopilotPoint(5, -18) };

        var plan = SmartAutopilotPlanner.Plan(current, target, zaaps);

        Assert.False(plan.UseZaap);
        Assert.Equal(0, plan.DirectDistance);
        Assert.Single(plan.Route);
        Assert.Equal(5, plan.Route[0].X);
        Assert.Equal(-18, plan.Route[0].Y);
    }

    [Fact]
    public void Catalog_search_handles_empty_or_whitespace_queries_without_exception()
    {
        var catalog = TransportCatalogManager.GetCatalog();

        var resNull = TransportCatalogManager.Filter(catalog, null!);
        var resEmpty = TransportCatalogManager.Filter(catalog, "");
        var resSpaces = TransportCatalogManager.Filter(catalog, "    ");

        Assert.Equal(catalog.Count, resNull.Count());
        Assert.Equal(catalog.Count, resEmpty.Count());
        Assert.Equal(catalog.Count, resSpaces.Count());
    }

    [Fact]
    public void Planner_respects_custom_minimum_zaap_distance()
    {
        var current = new AutopilotPoint(0, 0);
        var target = new AutopilotPoint(0, 8); // dist = 8
        var zaaps = new List<AutopilotPoint> { new AutopilotPoint(0, 7) }; // 1 map away from target

        // If minDistanceForZaap is set to 10, distance of 8 must fall back to direct walking
        var plan = SmartAutopilotPlanner.Plan(current, target, zaaps, minDistanceForZaap: 10);

        Assert.False(plan.UseZaap);
        Assert.Null(plan.ZaapPoint);
        Assert.Equal(8, plan.DirectDistance);
    }
}

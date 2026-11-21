using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Server.Tests;

public sealed class SmartAutopilotPlannerTests
{
    [Fact]
    public void Direct_walking_is_chosen_when_destination_is_close()
    {
        var current = new AutopilotPoint(0, 0, 100);
        var target = new AutopilotPoint(1, 2, 101); // Distance = 3 (< 5)
        var zaaps = new List<AutopilotPoint>
        {
            new AutopilotPoint(1, 1, 200)
        };

        var plan = SmartAutopilotPlanner.Plan(current, target, zaaps);

        Assert.False(plan.UseZaap);
        Assert.Null(plan.ZaapPoint);
        Assert.Equal(3, plan.DirectDistance);
        Assert.Equal(0, plan.TotalSavedDistance);
        Assert.Equal(4, plan.Route.Count); // [0,0], [1,0], [1,1], [1,2]
        Assert.Equal(target.X, plan.Route.Last().X);
        Assert.Equal(target.Y, plan.Route.Last().Y);
    }

    [Fact]
    public void Zaap_is_chosen_when_destination_is_far_and_zaap_is_within_walking_radius()
    {
        // User's exact scenario:
        // Current position: [0, 0]
        // Target position: 2 left, 5 up -> [-2, -5]. Direct distance = 7.
        // Zaap near target: [-1, -3]. Distance from zaap to target = 1 + 2 = 3 (<= 4 radius).
        var current = new AutopilotPoint(0, 0, 100);
        var target = new AutopilotPoint(-2, -5, 102);
        var zaaps = new List<AutopilotPoint>
        {
            new AutopilotPoint(10, 10, 201),  // Far zaap
            new AutopilotPoint(-1, -3, 202)   // Closer zaap (3 maps away from target)
        };

        var plan = SmartAutopilotPlanner.Plan(current, target, zaaps);

        Assert.True(plan.UseZaap);
        Assert.NotNull(plan.ZaapPoint);
        Assert.Equal(-1, plan.ZaapPoint.X);
        Assert.Equal(-3, plan.ZaapPoint.Y);
        Assert.Equal(7, plan.DirectDistance);
        Assert.Equal(3, plan.DistanceFromZaap);
        Assert.Equal(4, plan.TotalSavedDistance); // 7 - 3 = 4 maps saved

        // The walking route begins at the zaap and ends at the target
        Assert.Equal(plan.ZaapPoint.X, plan.Route.First().X);
        Assert.Equal(plan.ZaapPoint.Y, plan.Route.First().Y);
        Assert.Equal(target.X, plan.Route.Last().X);
        Assert.Equal(target.Y, plan.Route.Last().Y);
        Assert.Equal(4, plan.Route.Count); // [-1,-3] -> [-2,-3] -> [-2,-4] -> [-2,-5]
    }

    [Fact]
    public void Direct_walking_is_used_when_zaap_leaves_destination_too_far()
    {
        // Current: [0, 0], Target: [10, 10] (Distance = 20)
        // Nearest zaap is at [10, 3] -> distance to target = 7 (> maxZaapWalkingRadius of 4)
        var current = new AutopilotPoint(0, 0, 100);
        var target = new AutopilotPoint(10, 10, 103);
        var zaaps = new List<AutopilotPoint>
        {
            new AutopilotPoint(10, 3, 203) // 7 maps away from target
        };

        var plan = SmartAutopilotPlanner.Plan(current, target, zaaps);

        Assert.False(plan.UseZaap);
        Assert.Null(plan.ZaapPoint);
        Assert.Equal(20, plan.DirectDistance);
    }

    [Fact]
    public void Planner_handles_empty_zaap_list_gracefully()
    {
        var current = new AutopilotPoint(0, 0, 100);
        var target = new AutopilotPoint(8, 8, 104);
        var zaaps = Enumerable.Empty<AutopilotPoint>();

        var plan = SmartAutopilotPlanner.Plan(current, target, zaaps);

        Assert.False(plan.UseZaap);
        Assert.Null(plan.ZaapPoint);
        Assert.Equal(16, plan.DirectDistance);
        Assert.Equal(17, plan.Route.Count);
    }

    [Fact]
    public void Route_generation_produces_monotonic_step_by_step_path()
    {
        var start = new AutopilotPoint(5, -18);
        var end = new AutopilotPoint(2, -20);

        var route = SmartAutopilotPlanner.BuildLinearRoute(start, end);

        Assert.Equal(start.X, route[0].X);
        Assert.Equal(start.Y, route[0].Y);
        Assert.Equal(end.X, route.Last().X);
        Assert.Equal(end.Y, route.Last().Y);

        for (int i = 0; i < route.Count - 1; i++)
        {
            int stepDist = SmartAutopilotPlanner.ManhattanDistance(
                route[i].X, route[i].Y, route[i + 1].X, route[i + 1].Y);
            Assert.Equal(1, stepDist); // Exactly 1 map hop at a time
        }
    }
}

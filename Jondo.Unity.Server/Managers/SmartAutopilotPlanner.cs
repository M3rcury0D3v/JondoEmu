using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    public sealed record AutopilotPoint(int X, int Y, long MapId = 0);

    public sealed record AutopilotPlan(
        bool UseZaap,
        AutopilotPoint? ZaapPoint,
        int DirectDistance,
        int DistanceFromZaap,
        int TotalSavedDistance,
        IReadOnlyList<AutopilotPoint> Route
    );

    public static class SmartAutopilotPlanner
    {
        public const int DefaultMinDistanceForZaap = 5;
        public const int DefaultMaxZaapWalkingRadius = 4;

        public static int ManhattanDistance(int x1, int y1, int x2, int y2)
            => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

        public static AutopilotPlan Plan(
            AutopilotPoint current,
            AutopilotPoint target,
            IEnumerable<AutopilotPoint> availableZaaps,
            int minDistanceForZaap = DefaultMinDistanceForZaap,
            int maxZaapWalkingRadius = DefaultMaxZaapWalkingRadius)
        {
            int directDistance = ManhattanDistance(current.X, current.Y, target.X, target.Y);

            if (directDistance < minDistanceForZaap)
            {
                var directRoute = BuildLinearRoute(current, target);
                return new AutopilotPlan(false, null, directDistance, directDistance, 0, directRoute);
            }

            AutopilotPoint? bestZaap = null;
            int bestDistance = directDistance;

            foreach (var zaap in availableZaaps)
            {
                int distZaapToTarget = ManhattanDistance(zaap.X, zaap.Y, target.X, target.Y);
                if (distZaapToTarget <= maxZaapWalkingRadius && distZaapToTarget < bestDistance)
                {
                    bestDistance = distZaapToTarget;
                    bestZaap = zaap;
                }
            }

            if (bestZaap != null && (directDistance - bestDistance) >= 2)
            {
                var routeFromZaap = BuildLinearRoute(bestZaap, target);
                return new AutopilotPlan(
                    UseZaap: true,
                    ZaapPoint: bestZaap,
                    DirectDistance: directDistance,
                    DistanceFromZaap: bestDistance,
                    TotalSavedDistance: directDistance - bestDistance,
                    Route: routeFromZaap
                );
            }

            var fallbackRoute = BuildLinearRoute(current, target);
            return new AutopilotPlan(false, null, directDistance, directDistance, 0, fallbackRoute);
        }

        public static IReadOnlyList<AutopilotPoint> BuildLinearRoute(AutopilotPoint start, AutopilotPoint end)
        {
            var points = new List<AutopilotPoint>();
            int curX = start.X;
            int curY = start.Y;
            points.Add(new AutopilotPoint(curX, curY));

            while (curX != end.X || curY != end.Y)
            {
                if (curX < end.X) curX++;
                else if (curX > end.X) curX--;
                else if (curY < end.Y) curY++;
                else if (curY > end.Y) curY--;

                points.Add(new AutopilotPoint(curX, curY));
            }

            return points;
        }
    }
}

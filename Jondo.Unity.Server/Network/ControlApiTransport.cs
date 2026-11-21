using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;

namespace Jondo.Unity.Server.Network
{
    public static partial class ControlApi
    {
        private static Respuesta TransportCatalog(string body, long account)
        {
            var catalog = TransportCatalogManager.GetCatalog();
            return Bien(new
            {
                ok = true,
                destinations = catalog.Select(d => new
                {
                    category = d.Category,
                    order = d.Order,
                    name = d.Name,
                    mapId = d.MapId,
                    level = d.Level,
                    cost = d.Cost,
                    x = d.X,
                    y = d.Y,
                    bonus = d.ZoneBonus,
                    icon = d.IconName
                })
            });
        }

        private static Respuesta TransportTeleport(string body, long account)
        {
            var session = OwnCharacter(account);
            if (session == null || !session.IsInWorld)
                return Mal(404, "character-offline");

            if (session.State.IsInFight)
                return Mal(400, "in-fight");

            if (Jail.HoldNotice(session) != null)
                return Mal(400, "in-jail");

            long mapId = Numero(body, "mapId");
            if (mapId <= 0)
            {
                int x = (int)Numero(body, "x");
                int y = (int)Numero(body, "y");
                var match = MapLookup.AtCoordinates(x, y);
                if (match == null) return Mal(404, "unknown-map");
                mapId = match.Map.MapId;
            }

            int cell = (int)Numero(body, "cell");
            int targetCell = cell > 0 ? cell : TeleportHandler.MapCentre;
            int landing = TeleportHandler.ToMapAsync(session.Stream!, mapId, targetCell).GetAwaiter().GetResult();
            bool success = landing > 0;

            return success ? Bien(new { ok = true, mapId, cell = landing }) : Mal(500, "teleport-failed");
        }

        private static Respuesta OpenBank(string body, long account)
        {
            var session = OwnCharacter(account);
            if (session == null || !session.IsInWorld)
                return Mal(404, "character-offline");

            if (session.State.IsInFight)
                return Mal(400, "in-fight");

            if (session.Stream == null)
                return Mal(500, "invalid-session");

            using var context = SessionContext.Push(session);
            BankHandler.OpenAsync(session.Stream).GetAwaiter().GetResult();
            return Bien(new { ok = true });
        }

        private static Respuesta AutopilotPlan(string body, long account)
        {
            var session = OwnCharacter(account);
            if (session == null || !session.IsInWorld)
                return Mal(404, "character-offline");

            long currentMap = session.State.MapId;
            int curX = 0, curY = 0;
            if (MapManager.Maps.TryGetValue(currentMap, out var curInfo))
            {
                curX = curInfo.PosX;
                curY = curInfo.PosY;
            }

            int targetX = (int)Numero(body, "targetX");
            int targetY = (int)Numero(body, "targetY");
            long targetMap = Numero(body, "targetMap");

            if (targetMap > 0 && targetX == 0 && targetY == 0)
            {
                if (MapManager.Maps.TryGetValue(targetMap, out var tgtInfo))
                {
                    targetX = tgtInfo.PosX;
                    targetY = tgtInfo.PosY;
                }
            }

            var zaapPoints = new List<AutopilotPoint>();
            foreach (var wp in Interactives.Waypoints)
            {
                if (!wp.Activated) continue;
                if (MapManager.Maps.TryGetValue(wp.MapId, out var wpInfo))
                {
                    zaapPoints.Add(new AutopilotPoint(wpInfo.PosX, wpInfo.PosY, wp.MapId));
                }
            }

            var currentPoint = new AutopilotPoint(curX, curY, currentMap);
            var targetPoint = new AutopilotPoint(targetX, targetY, targetMap);

            var plan = SmartAutopilotPlanner.Plan(currentPoint, targetPoint, zaapPoints);

            return Bien(new
            {
                ok = true,
                useZaap = plan.UseZaap,
                zaapPoint = plan.ZaapPoint == null ? null : new { x = plan.ZaapPoint.X, y = plan.ZaapPoint.Y, mapId = plan.ZaapPoint.MapId },
                directDistance = plan.DirectDistance,
                distanceFromZaap = plan.DistanceFromZaap,
                savedDistance = plan.TotalSavedDistance,
                route = plan.Route.Select(p => new { x = p.X, y = p.Y, mapId = p.MapId })
            });
        }
    }
}

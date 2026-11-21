using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    public static class TransportCategory
    {
        public const int Miscellaneous = 0;
        public const int Zones = 1;
        public const int Dungeons = 2;
    }

    public sealed class TransportDestination
    {
        public int Category { get; init; }
        public int Order { get; init; }
        public string Name { get; init; } = "";
        public long MapId { get; init; }
        public int Level { get; init; }
        public int Cost { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int ZoneBonus { get; init; }
        public string IconName { get; init; } = "";
    }

    public static class TransportCatalogManager
    {
        private static readonly object _sync = new();
        private static List<TransportDestination>? _cachedCatalog;

        public static IReadOnlyList<TransportDestination> GetCatalog()
        {
            lock (_sync)
            {
                if (_cachedCatalog != null && _cachedCatalog.Count > 0)
                {
                    return _cachedCatalog;
                }

                _cachedCatalog = BuildCatalog();
                return _cachedCatalog;
            }
        }

        public static void InvalidateCache()
        {
            lock (_sync)
            {
                _cachedCatalog = null;
            }
        }

        public static IEnumerable<TransportDestination> GetDestinationsByCategory(int category)
            => GetCatalog().Where(d => d.Category == category);

        public static IEnumerable<TransportDestination> Filter(IEnumerable<TransportDestination> destinations, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return destinations;

            string clean = query.Trim().ToLowerInvariant();

            // Try matching coordinates: "5,-18" or "5 -18" or "[5,-18]"
            string noBrackets = clean.Replace("[", "").Replace("]", "").Replace(" ", ",");
            string[] parts = noBrackets.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int qX) && int.TryParse(parts[1], out int qY))
            {
                return destinations.Where(d => d.X == qX && d.Y == qY);
            }

            return destinations.Where(d =>
                d.Name.ToLowerInvariant().Contains(clean) ||
                $"{d.X},{d.Y}".Contains(clean) ||
                d.Level.ToString().Equals(clean));
        }

        private static List<TransportDestination> BuildCatalog()
        {
            var list = new List<TransportDestination>();
            int order = 0;

            // ─── 1. Miscellaneous (Varios / Hubs) ────────────────────────────────────
            var miscDefaults = new (string Name, long MapId, int X, int Y, string Icon)[]
            {
                ("Zaap Astrub", 191105026, 5, -18, "zaap_icon"),
                ("Map Poutch (Astrub)", 191104002, 5, -19, "poutch_icon"),
                ("Map Poutch (Village)", 191104003, 0, 0, "poutch_icon"),
                ("Map Poutch (Champion)", 191104004, -13, -29, "poutch_icon"),
                ("Map Kolizeum", 191104005, -13, -29, "kolizeum_icon"),
                ("Map PVP", 191104006, -1, 0, "pvp_icon"),
                ("Arène de Bonta", 191104007, -32, -59, "arena_icon"),
                ("Arène de Brâkmar", 191104008, -25, 36, "arena_icon"),
                ("Agence Immobilière d'Auralis", 191104009, -32, -56, "agency_icon"),
                ("Temple de Guilde", 191104010, 0, -8, "guild_icon"),
                ("Temple Alliance", 191104011, 13, 34, "alliance_icon")
            };

            foreach (var item in miscDefaults)
            {
                list.Add(new TransportDestination
                {
                    Category = TransportCategory.Miscellaneous,
                    Order = order++,
                    Name = item.Name,
                    MapId = item.MapId,
                    X = item.X,
                    Y = item.Y,
                    Level = 1,
                    Cost = 0,
                    ZoneBonus = 0,
                    IconName = item.Icon
                });
            }

            // ─── 2. Zones (Zonas / Regional Zaaps) ───────────────────────────────────
            if (Interactives.Waypoints != null && Interactives.Waypoints.Count > 0)
            {
                foreach (var wp in Interactives.Waypoints)
                {
                    if (!wp.Activated) continue;
                    long mapId = wp.MapId;
                    int x = 0, y = 0;
                    string name = $"Zone {wp.SubAreaId}";

                    if (MapManager.Maps.TryGetValue(mapId, out var mapInfo))
                    {
                        x = mapInfo.PosX;
                        y = mapInfo.PosY;
                        if (!string.IsNullOrWhiteSpace(mapInfo.Name)) name = mapInfo.Name;
                    }

                    int level = Math.Max(1, (int)(Math.Abs(wp.SubAreaId) % 200 + 1));
                    int bonus = (int)((Math.Abs(mapId) % 16) * 5 + 5);

                    list.Add(new TransportDestination
                    {
                        Category = TransportCategory.Zones,
                        Order = order++,
                        Name = name,
                        MapId = mapId,
                        X = x,
                        Y = y,
                        Level = level,
                        Cost = 100,
                        ZoneBonus = bonus,
                        IconName = "zaap_icon"
                    });
                }
            }
            else
            {
                var sampleZones = new (string Name, long MapId, int X, int Y, int Level, int Bonus)[]
                {
                    ("Incarnam (Camino de las Almas)", 153092352, 0, -3, 3, 5),
                    ("Incarnam (Campos)", 153092353, -1, -4, 4, 20),
                    ("Incarnam (Lago)", 153092354, -1, 0, 5, 5),
                    ("Incarnam (Prado)", 153092355, 1, -4, 6, 35),
                    ("Incarnam (Bosque)", 153092356, 1, -1, 7, 40),
                    ("Amakna (Pueblo de Amakna)", 88212480, 1, 0, 13, 80),
                    ("Astrub (Ciudad de Astrub)", 191105026, 3, -15, 13, 30),
                    ("Bahía de Sufokia (Sufokia)", 99615234, 13, 24, 13, 25)
                };

                foreach (var z in sampleZones)
                {
                    list.Add(new TransportDestination
                    {
                        Category = TransportCategory.Zones,
                        Order = order++,
                        Name = z.Name,
                        MapId = z.MapId,
                        X = z.X,
                        Y = z.Y,
                        Level = z.Level,
                        Cost = 100,
                        ZoneBonus = z.Bonus,
                        IconName = "zaap_icon"
                    });
                }
            }

            // ─── 3. Dungeons (Mazmorras) ─────────────────────────────────────────────
            if (DungeonManager.All != null && DungeonManager.All.Count > 0)
            {
                foreach (var kvp in DungeonManager.All)
                {
                    var d = kvp.Value;
                    if (d.EntranceMapId <= 0) continue;

                    int x = 0, y = 0;
                    if (MapManager.Maps.TryGetValue(d.EntranceMapId, out var mInfo))
                    {
                        x = mInfo.PosX;
                        y = mInfo.PosY;
                    }

                    int level = d.OptimalLevel > 0 ? d.OptimalLevel : (d.MinLevel > 0 ? d.MinLevel : 20);
                    int bonus = (int)((Math.Abs(d.Id) % 10) * 5 + 5);
                    string icon = d.Bosses.Count > 0 ? $"boss_{d.Bosses[0]}" : "dungeon_icon";

                    list.Add(new TransportDestination
                    {
                        Category = TransportCategory.Dungeons,
                        Order = order++,
                        Name = string.IsNullOrWhiteSpace(d.Name) ? $"Dungeon #{d.Id}" : d.Name,
                        MapId = d.EntranceMapId,
                        X = x,
                        Y = y,
                        Level = level,
                        Cost = 0,
                        ZoneBonus = bonus,
                        IconName = icon
                    });
                }
            }
            else
            {
                var sampleDungeons = new (string Name, long MapId, int X, int Y, int Level, int Bonus, string Icon)[]
                {
                    ("Cripta de Chadalid", 191105101, 5, -1, 10, 45, "boss_chadalid"),
                    ("Granero del Girasol Hambriento", 191105102, 7, -24, 20, 30, "boss_sunflower"),
                    ("Castillo de Arena", 191105103, 13, -28, 20, 30, "boss_sandcastle"),
                    ("Corte del Jalató Real", 191105104, 2, -34, 30, 45, "boss_royal_gobball"),
                    ("Mansión Encantada", 191105105, -13, -41, 40, 0, "boss_mansion"),
                    ("Mazmorra de los Escarahojas", 191105106, 1, 26, 40, 10, "boss_scaraleaf"),
                    ("Mazmorra de los Esqueletos", 191105107, 10, 15, 40, 30, "boss_skeletons"),
                    ("Mazmorra de los Tofus", 191105108, 5, 6, 40, 45, "boss_tofus"),
                    ("Escondrijo de Blatarata", 191105109, 3, -17, 40, 45, "boss_rat"),
                    ("Mazmorra de los Bworks", 191105110, -5, 10, 50, 5, "boss_bworks"),
                    ("Mazmorra de los Herreros", 191105111, 13, 21, 50, 30, "boss_smiths")
                };

                foreach (var sd in sampleDungeons)
                {
                    list.Add(new TransportDestination
                    {
                        Category = TransportCategory.Dungeons,
                        Order = order++,
                        Name = sd.Name,
                        MapId = sd.MapId,
                        X = sd.X,
                        Y = sd.Y,
                        Level = sd.Level,
                        Cost = 0,
                        ZoneBonus = sd.Bonus,
                        IconName = sd.Icon
                    });
                }
            }

            return list;
        }
    }
}

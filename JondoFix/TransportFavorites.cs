using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace JondoFix
{
    /// <summary>
    /// Persistent storage for the player's favorited teleport destinations.
    /// Stored as JSON under UserData/Preferences/TransportFavorites.json.
    /// </summary>
    public static class TransportFavorites
    {
        private static readonly HashSet<long> _favorites = new HashSet<long>();
        private static bool _loaded = false;

        private static string GetFilePath()
        {
            string gameDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.Combine(gameDir, "UserData", "Preferences");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return Path.Combine(dir, "TransportFavorites.json");
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = GetFilePath();
                if (!File.Exists(path)) return;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;

                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                {
                    _favorites.Clear();
                    foreach (var id in list) _favorites.Add(id);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TransportFavorites] Failed to load favorites: {ex.Message}");
            }
        }

        public static bool IsFavorite(long mapId)
        {
            EnsureLoaded();
            return _favorites.Contains(mapId);
        }

        public static bool Toggle(long mapId)
        {
            EnsureLoaded();
            bool result;
            if (_favorites.Contains(mapId))
            {
                _favorites.Remove(mapId);
                result = false;
            }
            else
            {
                _favorites.Add(mapId);
                result = true;
            }
            Save();
            return result;
        }

        public static void Save()
        {
            try
            {
                string path = GetFilePath();
                string json = JsonSerializer.Serialize(_favorites);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TransportFavorites] Failed to save favorites: {ex.Message}");
            }
        }
    }
}

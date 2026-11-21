using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Jondo.Unity.Server.Tests;

public sealed class TransportFavoritesTests
{
    private sealed class FavoritesStore
    {
        private readonly HashSet<long> _favorites = new HashSet<long>();

        public int Count => _favorites.Count;

        public bool IsFavorite(long mapId) => _favorites.Contains(mapId);

        public bool Toggle(long mapId)
        {
            if (_favorites.Contains(mapId))
            {
                _favorites.Remove(mapId);
                return false;
            }
            _favorites.Add(mapId);
            return true;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(_favorites);
        }

        public void LoadFromJson(string json)
        {
            _favorites.Clear();
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                {
                    foreach (var id in list) _favorites.Add(id);
                }
            }
            catch { }
        }
    }

    [Fact]
    public void Toggling_favorite_adds_and_removes_map_id()
    {
        var store = new FavoritesStore();
        Assert.False(store.IsFavorite(12345));

        bool added = store.Toggle(12345);
        Assert.True(added);
        Assert.True(store.IsFavorite(12345));
        Assert.Equal(1, store.Count);

        bool removed = store.Toggle(12345);
        Assert.False(removed);
        Assert.False(store.IsFavorite(12345));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Serialization_and_deserialization_preserves_favorites()
    {
        var store1 = new FavoritesStore();
        store1.Toggle(1001);
        store1.Toggle(1002);
        store1.Toggle(1003);

        string json = store1.ToJson();
        Assert.NotEmpty(json);

        var store2 = new FavoritesStore();
        store2.LoadFromJson(json);

        Assert.Equal(3, store2.Count);
        Assert.True(store2.IsFavorite(1001));
        Assert.True(store2.IsFavorite(1002));
        Assert.True(store2.IsFavorite(1003));
        Assert.False(store2.IsFavorite(9999));
    }

    [Fact]
    public void Corrupted_or_empty_json_does_not_throw_exception()
    {
        var store = new FavoritesStore();
        store.LoadFromJson("");
        store.LoadFromJson("   ");
        store.LoadFromJson("{ invalid json }");
        store.LoadFromJson("[ \"not a number\" ]");

        Assert.Equal(0, store.Count);
    }
}

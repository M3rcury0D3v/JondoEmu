using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Il2Cpp;
using Il2CppCore.Localization;
using Il2CppCore.Services.UI.Common;
using Il2CppCore.UILogic;
using Il2CppCore.UILogic.Components;
using Il2CppCore.UILogic.Components.Figma;
using Il2CppCore.UILogic.Components.Hyperlink;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;
using UnityEngine.UIElements;

namespace JondoFix
{
    /// <summary>
    /// Teleportation window (Pocket Teleporter) allowing players to travel across Miscellaneous,
    /// Zones, and Dungeons destinations.
    /// Toggleable via Ctrl + T and HUD menu button.
    /// </summary>
    public static class TransportUi
    {
        public sealed class DestinationEntry
        {
            public int Category { get; set; }
            public int Order { get; set; }
            public string Name { get; set; } = "";
            public long MapId { get; set; }
            public int Level { get; set; }
            public int Cost { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Bonus { get; set; }
            public string Icon { get; set; } = "";
        }

        private const int FigmaIconZaap = 336;
        private const int FigmaIconChest = 81;
        private const int FigmaIconPin = 290;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly ConcurrentQueue<Action> _toUnity = new ConcurrentQueue<Action>();

        private static bool _isOpen = false;
        private static WindowFigma _window;
        private static VisualElement _tree;
        private static VisualTreeAsset _rowTemplate;
        private static ScrollView _list;
        private static TextInput _search;
        private static DofusTabGroup _tabs;
        private static readonly List<DofusTab> _categoryTabs = new List<DofusTab>();
        private static CheckboxCustom _favoritesOnly;
        private static DofusButtonCustom _validateButton;
        private static DofusLabel _emptyLabel;

        private static int _activeCategory = 0; // 0 = Miscellaneous, 1 = Zones, 2 = Dungeons
        private static string _searchQuery = "";
        private static string _shownSearch = "";
        private static bool _showOnlyFavorites = false;
        private static DestinationEntry _selectedDestination;
        private static VisualElement _selectedRow;

        private static List<DestinationEntry> _catalog = new List<DestinationEntry>();
        private static bool _catalogLoaded = false;
        private static bool _isLoading = false;

        public static bool IsOpen => _isOpen && _window != null && _window.panel != null;

        private static string L(string es, string en, string fr)
        {
            string lang = JondoLanguage.Current ?? "en";
            return lang switch
            {
                "es" => es,
                "fr" => fr,
                _ => en
            };
        }

        private static Il2CppSystem.Action ConvertAction(Action action)
        {
            return DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { MelonLogger.Warning($"[TransportUi] Action error: {ex.Message}"); }
            }));
        }

        public static void Tick()
        {
            while (_toUnity.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { MelonLogger.Warning($"[TransportUi] Dispatch error: {ex.Message}"); }
            }

            // Keyboard shortcut Ctrl + T
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.T))
                {
                    Toggle();
                }
            }

            // Search input text polling (cleaner than IL2CPP value change delegates)
            if (IsOpen && _search != null)
            {
                string typed = _search.text ?? "";
                if (typed != _shownSearch)
                {
                    _shownSearch = typed;
                    _searchQuery = typed;
                    RenderList();
                }
            }
        }

        public static void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public static void Open()
        {
            if (IsOpen) return;

            var documents = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UIDocument>());
            VisualElement root = null;
            for (int i = 0; i < documents.Length; i++)
            {
                var doc = documents[i]?.TryCast<UIDocument>();
                if (doc != null && doc.rootVisualElement != null && doc.rootVisualElement.panel != null)
                {
                    root = doc.rootVisualElement;
                    break;
                }
            }

            if (root == null)
            {
                MelonLogger.Warning("[TransportUi] No active UIDocument root found to attach window.");
                return;
            }

            BuildWindow(root);
            _isOpen = true;

            if (!_catalogLoaded && !_isLoading)
            {
                FetchCatalogAsync();
            }
            else
            {
                RenderList();
            }
        }

        public static void Close()
        {
            if (_window != null)
            {
                try
                {
                    if (_tree != null) _tree.RemoveFromHierarchy();
                    else _window.RemoveFromHierarchy();
                }
                catch { }
            }
            _isOpen = false;
            _selectedDestination = null;
            _selectedRow = null;
        }

        private static void BuildWindow(VisualElement root)
        {
            _categoryTabs.Clear();

            DofusUISettings uiSettings = null;
            try { uiSettings = DofusUISettings.GetSettings(); } catch { }

            VisualTreeAsset windowTemplate = null;
            if (uiSettings != null)
            {
                try
                {
                    windowTemplate = uiSettings.InstantiateTemplate("Assets/UI/Taxi/zaapiSelection.uxml");
                    _rowTemplate = uiSettings.InstantiateTemplate("Assets/UI/Taxi/zaapListEntry.uxml");
                }
                catch { }
            }

            if (windowTemplate != null)
            {
                _tree = windowTemplate.CloneTree();
                _tree.style.position = new StyleEnum<Position>(Position.Absolute);
                _tree.style.left = new StyleLength(0f);
                _tree.style.top = new StyleLength(0f);
                _tree.style.right = new StyleLength(0f);
                _tree.style.bottom = new StyleLength(0f);

                _window = _tree.Q<WindowFigma>("ZaapSelection");
            }

            if (_window == null)
            {
                _window = new WindowFigma();
                _tree = _window;
            }

            _window.title = L("Teletransportador", "Teleporter", "Téléporteur");
            _window.shortcutValue = "Ctrl + T";
            _window.isMovable = true;
            _window.showCloseButton = true;

            float winWidth = 980f;
            float winHeight = 650f;
            _window.style.width = new StyleLength(winWidth);
            _window.style.height = new StyleLength(winHeight);
            _window.style.position = new StyleEnum<Position>(Position.Absolute);
            _window.style.left = new StyleLength(Mathf.Max(0f, (root.resolvedStyle.width - winWidth) / 2f));
            _window.style.top = new StyleLength(Mathf.Max(30f, (root.resolvedStyle.height - winHeight) / 2f));

            if (_window.closeButton != null)
            {
                _window.closeButton.RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                    new Action<ClickEvent>(e => { Close(); e.StopImmediatePropagation(); })));
            }

            // Search input
            _search = _tree.Q<TextInput>("inp_search");
            if (_search != null)
            {
                _search.placeholderText = L("Buscar un destino", "Search a destination", "Rechercher une destination");
            }

            // Tabs
            _tabs = _tree.Q<DofusTabGroup>("tabGroup");
            if (_tabs != null)
            {
                string[] tabLabels = new[]
                {
                    L("VARIOS", "MISCELLANEOUS", "DIVERS"),
                    L("ZONAS", "ZONES", "ZONES"),
                    L("MAZMORRAS", "DUNGEONS", "DONJONS")
                };

                for (int i = 0; i < _tabs.childCount; i++)
                {
                    var tab = _tabs[i].TryCast<DofusTab>();
                    if (tab != null) _categoryTabs.Add(tab);
                }

                for (int i = 0; i < _categoryTabs.Count && i < tabLabels.Length; i++)
                {
                    _categoryTabs[i].text = tabLabels[i];
                    int catIndex = i;
                    _categoryTabs[i].RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                        new Action<ClickEvent>(e =>
                        {
                            _activeCategory = catIndex;
                            RenderList();
                            e.StopImmediatePropagation();
                        })));
                }
            }

            // Scroll list
            var originalList = _tree.Q<ListView>("gd_zaap");
            if (originalList != null)
            {
                var parent = originalList.parent;
                int idx = parent.IndexOf(originalList);
                originalList.RemoveFromHierarchy();

                _list = new ScrollView(ScrollViewMode.Vertical);
                _list.name = "JondoTransportDestinations";
                _list.style.flexGrow = new StyleFloat(1f);
                _list.style.minHeight = new StyleLength(0f);
                parent.Insert(idx, _list);
            }
            else
            {
                _list = _tree.Q<ScrollView>("JondoTransportDestinations");
                if (_list == null)
                {
                    _list = new ScrollView(ScrollViewMode.Vertical);
                    _list.style.flexGrow = new StyleFloat(1f);
                    _window.Add(_list);
                }
            }

            // Empty label
            _emptyLabel = _tree.Q<DofusLabel>("lbl_noDestination");
            if (_emptyLabel != null)
            {
                _emptyLabel.text = L("No hay destinos disponibles.", "No destinations available.", "Aucune destination disponible.");
            }

            // Checkbox favorites only
            _favoritesOnly = _tree.Q<CheckboxCustom>("checkbox_showFavoriteZaap");
            if (_favoritesOnly != null)
            {
                string favText = L("Mostrar solo mis favoritos", "Show only my favorites", "Afficher únicamente mes favoris");
                _favoritesOnly.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
                _favoritesOnly.leftText = favText;
                _favoritesOnly.SetValueWithoutNotify(_showOnlyFavorites);
                _favoritesOnly.RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                    new Action<ClickEvent>(e =>
                    {
                        _showOnlyFavorites = !_showOnlyFavorites;
                        _favoritesOnly.SetValueWithoutNotify(_showOnlyFavorites);
                        RenderList();
                    })));
            }

            // Validate button
            _validateButton = _tree.Q<DofusButtonCustom>("btn_validate");
            if (_validateButton != null)
            {
                _validateButton.text = L("TELETRANSPORTARSE", "TELEPORT", "SE TÉLÉPORTER");
                _validateButton.SetEnabled(false);
                _validateButton.RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                    new Action<ClickEvent>(e =>
                    {
                        ExecuteTeleport();
                        e.StopImmediatePropagation();
                    })));
            }

            root.Add(_tree);
            _tree.BringToFront();
        }

        private static void RenderList()
        {
            if (_list == null) return;
            _list.Clear();
            _selectedDestination = null;
            _selectedRow = null;
            if (_validateButton != null) _validateButton.SetEnabled(false);

            // Update column header visibility
            var bonusHeader = _tree?.Q<VisualElement>("tch_cost");
            if (bonusHeader != null)
            {
                bonusHeader.style.display = new StyleEnum<DisplayStyle>(_activeCategory == 0 ? DisplayStyle.None : DisplayStyle.Flex);
            }

            var destinations = _catalog.Where(d => d.Category == _activeCategory);

            if (_showOnlyFavorites)
            {
                destinations = destinations.Where(d => TransportFavorites.IsFavorite(d.MapId));
            }

            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                string q = _searchQuery.Trim().ToLowerInvariant();
                destinations = destinations.Where(d =>
                    d.Name.ToLowerInvariant().Contains(q) ||
                    $"[{d.X}, {d.Y}]".Contains(q) ||
                    $"[{d.X},{d.Y}]".Contains(q) ||
                    $"{d.X} {d.Y}".Contains(q));
            }

            var list = destinations.OrderBy(d => d.Order).ThenBy(d => d.Level).ToList();

            if (_emptyLabel != null)
            {
                _emptyLabel.style.display = new StyleEnum<DisplayStyle>(list.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None);
                if (_showOnlyFavorites)
                {
                    _emptyLabel.text = L("No tienes favoritos en esta categoría.", "No favorites in this category.", "Aucun favori dans cet onglet.");
                }
                else
                {
                    _emptyLabel.text = L("No hay destinos disponibles.", "No destinations available.", "Aucune destination disponible.");
                }
            }

            foreach (var item in list)
            {
                var rowElement = CreateRowElement(item);
                if (rowElement != null)
                {
                    _list.Add(rowElement);
                }
            }
        }

        private static VisualElement CreateRowElement(DestinationEntry destination)
        {
            TemplateContainer container = null;
            if (_rowTemplate != null)
            {
                try { container = _rowTemplate.CloneTree(); } catch { }
            }

            VisualElement row = container != null ? container : new VisualElement();
            row.name = "TransportDestinationRow";
            row.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            row.style.alignItems = new StyleEnum<Align>(Align.Center);
            row.style.paddingLeft = new StyleLength(12f);
            row.style.paddingRight = new StyleLength(12f);
            row.style.paddingTop = new StyleLength(6f);
            row.style.paddingBottom = new StyleLength(6f);
            row.style.marginBottom = new StyleLength(3f);
            row.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.22f, 0.75f));
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
                row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = new StyleLength(4f);

            // Favorite star button
            var favBtn = new DofusIcon();
            favBtn.name = "icon_fav";
            favBtn.icon = (FigmaIcons)FigmaIconPin;
            favBtn.style.width = new StyleLength(20f);
            favBtn.style.height = new StyleLength(20f);
            favBtn.style.marginRight = new StyleLength(8f);
            favBtn.style.unityBackgroundImageTintColor = new StyleColor(
                TransportFavorites.IsFavorite(destination.MapId) ? new Color(1f, 0.82f, 0.1f, 1f) : new Color(0.5f, 0.55f, 0.65f, 0.5f));

            favBtn.RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                new Action<ClickEvent>(e =>
                {
                    bool isFav = TransportFavorites.Toggle(destination.MapId);
                    favBtn.style.unityBackgroundImageTintColor = new StyleColor(
                        isFav ? new Color(1f, 0.82f, 0.1f, 1f) : new Color(0.5f, 0.55f, 0.65f, 0.5f));
                    e.StopImmediatePropagation();
                })));

            // Destination Icon
            var destIcon = new DofusIcon();
            destIcon.icon = (FigmaIcons)FigmaIconZaap;
            destIcon.style.width = new StyleLength(32f);
            destIcon.style.height = new StyleLength(32f);
            destIcon.style.marginRight = new StyleLength(10f);

            // Destination Name
            var nameLabel = new DofusLabel();
            nameLabel.name = "lbl_name";
            nameLabel.text = destination.Name;
            nameLabel.style.flexGrow = new StyleFloat(1f);
            nameLabel.style.color = new StyleColor(Color.white);
            nameLabel.style.fontSize = new StyleLength(14f);

            // Coordinates Pin Button
            var coordContainer = new VisualElement();
            coordContainer.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            coordContainer.style.alignItems = new StyleEnum<Align>(Align.Center);
            coordContainer.style.width = new StyleLength(110f);

            var pinIcon = new DofusIcon();
            pinIcon.icon = (FigmaIcons)FigmaIconPin;
            pinIcon.style.width = new StyleLength(16f);
            pinIcon.style.height = new StyleLength(16f);
            pinIcon.style.marginRight = new StyleLength(4f);
            coordContainer.Add(pinIcon);

            var coordLabel = new DofusLabel();
            coordLabel.text = $"[{destination.X}, {destination.Y}]";
            coordLabel.style.color = new StyleColor(new Color(0.75f, 0.82f, 1f));
            coordContainer.Add(coordLabel);

            // Pin click places map flag
            coordContainer.RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                new Action<ClickEvent>(e =>
                {
                    PlaceMapFlag(destination.X, destination.Y, destination.MapId);
                    e.StopImmediatePropagation();
                })));

            // Level Label
            var levelLabel = new DofusLabel();
            levelLabel.name = "lbl_level";
            levelLabel.text = destination.Level.ToString();
            levelLabel.style.width = new StyleLength(60f);
            levelLabel.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            levelLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));

            // Zone Bonus Label (only shown on Zones and Dungeons tabs)
            VisualElement bonusContainer = null;
            if (_activeCategory != 0)
            {
                var bonusLabel = new DofusLabel();
                bonusLabel.name = "lbl_bonus";
                bonusLabel.text = $"+{destination.Bonus} %";
                bonusLabel.style.width = new StyleLength(110f);
                bonusLabel.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleRight);
                bonusLabel.style.color = new StyleColor(destination.Bonus > 0 ? new Color(0.4f, 0.95f, 0.4f) : Color.gray);
                bonusContainer = bonusLabel;
            }

            row.Add(favBtn);
            row.Add(destIcon);
            row.Add(nameLabel);
            row.Add(coordContainer);
            row.Add(levelLabel);
            if (bonusContainer != null) row.Add(bonusContainer);

            // Row click selection
            row.RegisterCallback<ClickEvent>(DelegateSupport.ConvertDelegate<EventCallback<ClickEvent>>(
                new Action<ClickEvent>(e =>
                {
                    if (_selectedRow != null)
                    {
                        _selectedRow.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.22f, 0.75f));
                    }
                    _selectedDestination = destination;
                    _selectedRow = row;
                    row.style.backgroundColor = new StyleColor(new Color(0.25f, 0.35f, 0.6f, 0.85f));
                    if (_validateButton != null) _validateButton.SetEnabled(true);

                    // Double click teleports
                    if (e.clickCount >= 2)
                    {
                        ExecuteTeleport();
                    }
                })));

            return row;
        }

        private static void PlaceMapFlag(int x, int y, long mapId)
        {
            try
            {
                var type = typeof(HyperlinkMapInformation);
                var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor != null)
                {
                    var pars = ctor.GetParameters();
                    var args = new object[pars.Length];
                    for (int i = 0; i < pars.Length; i++)
                    {
                        if (pars[i].ParameterType == typeof(int)) args[i] = (pars[i].Name == "x" || pars[i].Name == "posX") ? x : y;
                        else if (pars[i].ParameterType == typeof(long)) args[i] = mapId;
                    }
                    var instance = ctor.Invoke(args);
                    var clickMethod = type.GetMethod("OnHyperlinkLeftClick");
                    clickMethod?.Invoke(instance, new object[] { new Vector2(0, 0) });
                    MelonLogger.Msg($"[TransportUi] Placed flag at [{x}, {y}] map {mapId}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TransportUi] Flag placement error: {ex.Message}");
            }
        }

        private static async void FetchCatalogAsync()
        {
            _isLoading = true;
            try
            {
                string token = (Environment.GetEnvironmentVariable("JONDO_PLAYER_TOKEN")
                               ?? Environment.GetEnvironmentVariable("JONDO_CONTROL_TOKEN") ?? "").Trim();

                using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8888/api/transport/catalog");
                request.Content = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("destinations", out var destsArray))
                    {
                        var newCatalog = new List<DestinationEntry>();
                        foreach (var elem in destsArray.EnumerateArray())
                        {
                            newCatalog.Add(new DestinationEntry
                            {
                                Category = elem.GetProperty("category").GetInt32(),
                                Order = elem.GetProperty("order").GetInt32(),
                                Name = elem.GetProperty("name").GetString() ?? "",
                                MapId = elem.GetProperty("mapId").GetInt64(),
                                Level = elem.GetProperty("level").GetInt32(),
                                Cost = elem.GetProperty("cost").GetInt32(),
                                X = elem.GetProperty("x").GetInt32(),
                                Y = elem.GetProperty("y").GetInt32(),
                                Bonus = elem.GetProperty("bonus").GetInt32(),
                                Icon = elem.GetProperty("icon").GetString() ?? ""
                            });
                        }
                        _catalog = newCatalog;
                        _catalogLoaded = true;
                        _toUnity.Enqueue(RenderList);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TransportUi] Failed to fetch transport catalog: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private static async void ExecuteTeleport()
        {
            if (_selectedDestination == null) return;
            long mapId = _selectedDestination.MapId;
            Close();

            try
            {
                string token = (Environment.GetEnvironmentVariable("JONDO_PLAYER_TOKEN")
                               ?? Environment.GetEnvironmentVariable("JONDO_CONTROL_TOKEN") ?? "").Trim();

                using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8888/api/transport/teleport");
                request.Content = new StringContent(JsonSerializer.Serialize(new { token, mapId }), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    MelonLogger.Msg($"[TransportUi] Teleported successfully to map {mapId}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TransportUi] Teleport request failed: {ex.Message}");
            }
        }
    }
}

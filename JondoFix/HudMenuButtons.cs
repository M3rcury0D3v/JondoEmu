using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Il2CppCore.UILogic.Components.Figma;
using Il2CppCore.UILogic.Components.Slots;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.UIElements;

namespace JondoFix
{
    /// <summary>
    /// Injects the Portal (Teleporter) and Chest (Personal Bank) icon buttons into the client's
    /// bottom menu bar (III MENÚS) beside the team/heroes button.
    /// </summary>
    public static class HudMenuButtons
    {
        private const int FigmaIconZaap = 336;
        private const int FigmaIconChest = 81;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private static VisualElement _teleportButton;
        private static VisualElement _bankButton;
        private static float _nextScan = 0f;

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextScan) return;
            _nextScan = now + 1.5f;

            try
            {
                EnsureButtonsInstalled();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[HudMenuButtons] Scan failed: {ex.Message}");
            }
        }

        private static void EnsureButtonsInstalled()
        {
            var documents = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UIDocument>());
            VisualElement targetGrid = null;

            for (int i = 0; i < documents.Length; i++)
            {
                var root = documents[i]?.TryCast<UIDocument>()?.rootVisualElement;
                if (root == null || root.panel == null) continue;

                var grids = new List<VisualElement>();
                FindGridsUnder(root, grids);

                targetGrid = grids.FirstOrDefault(g => IsMenuGrid(g) && g.resolvedStyle.display != DisplayStyle.None);
                if (targetGrid != null) break;
            }

            if (targetGrid == null) return;

            // Find free places in the menu bar grid
            var slots = new List<VisualElement>();
            FindSlotsUnder(targetGrid, slots);
            slots = slots.Where(s => s.resolvedStyle.display != DisplayStyle.None && s.resolvedStyle.visibility == Visibility.Visible
                                     && s.worldBound.width > 4f)
                         .OrderBy(s => Math.Round(s.worldBound.y)).ThenBy(s => s.worldBound.x).ToList();

            if (slots.Count < 2) return;

            // Check if buttons already exist and are attached
            if (_teleportButton != null && _teleportButton.panel != null &&
                _bankButton != null && _bankButton.panel != null)
            {
                return;
            }

            // Find free slots after existing buttons
            var freeSlots = slots.Where(s => s.childCount <= 1 || s.name.Contains("free") || s.name.Contains("empty")).ToList();
            if (freeSlots.Count < 2)
            {
                // Fallback to taking the last slots
                freeSlots = slots.Skip(Math.Max(0, slots.Count - 4)).ToList();
            }

            if (freeSlots.Count >= 2)
            {
                var slot1 = freeSlots[0];
                var slot2 = freeSlots[1];

                if (_teleportButton == null || _teleportButton.panel == null)
                {
                    _teleportButton = CreateMenuButton("JondoTeleportMenuButton", (FigmaIcons)FigmaIconZaap, () =>
                    {
                        TransportUi.Toggle();
                    });
                    slot1.Add(_teleportButton);
                }

                if (_bankButton == null || _bankButton.panel == null)
                {
                    _bankButton = CreateMenuButton("JondoBankMenuButton", (FigmaIcons)FigmaIconChest, () =>
                    {
                        OpenBankAsync();
                    });
                    slot2.Add(_bankButton);
                }
            }
        }

        private static VisualElement CreateMenuButton(string name, FigmaIcons iconType, Action onClick)
        {
            var btn = new VisualElement();
            btn.name = name;
            btn.style.width = new StyleLength(Length.Percent(100f));
            btn.style.height = new StyleLength(Length.Percent(100f));
            btn.style.alignItems = new StyleEnum<Align>(Align.Center);
            btn.style.justifyContent = new StyleEnum<Justify>(Justify.Center);

            // Background frame
            btn.style.backgroundColor = new StyleColor(new Color(0.18f, 0.20f, 0.28f, 0.9f));
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = new StyleLength(4f);
            btn.style.borderTopWidth = btn.style.borderBottomWidth =
                btn.style.borderLeftWidth = btn.style.borderRightWidth = new StyleFloat(1f);
            btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = new StyleColor(new Color(0.35f, 0.38f, 0.5f, 0.8f));

            // Icon
            var icon = new DofusIcon();
            icon.icon = iconType;
            icon.style.width = new StyleLength(28f);
            icon.style.height = new StyleLength(28f);
            btn.Add(icon);

            // Hover light effect
            var hoverLight = new VisualElement();
            hoverLight.name = "HoverLight";
            hoverLight.style.position = new StyleEnum<Position>(Position.Absolute);
            hoverLight.style.left = new StyleLength(0f);
            hoverLight.style.top = new StyleLength(0f);
            hoverLight.style.right = new StyleLength(0f);
            hoverLight.style.bottom = new StyleLength(0f);
            hoverLight.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.15f));
            hoverLight.style.borderTopLeftRadius = hoverLight.style.borderTopRightRadius =
                hoverLight.style.borderBottomLeftRadius = hoverLight.style.borderBottomRightRadius = new StyleLength(4f);
            hoverLight.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
            btn.Add(hoverLight);

            btn.RegisterCallback<PointerEnterEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerEnterEvent>>(
                new Action<PointerEnterEvent>(_ => hoverLight.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex))));

            btn.RegisterCallback<PointerLeaveEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerLeaveEvent>>(
                new Action<PointerLeaveEvent>(_ => hoverLight.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None))));

            btn.RegisterCallback<PointerDownEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>(
                new Action<PointerDownEvent>(e =>
                {
                    try { e.StopImmediatePropagation(); } catch { }
                    onClick?.Invoke();
                })), TrickleDown.TrickleDown);

            return btn;
        }

        private static async void OpenBankAsync()
        {
            try
            {
                string token = (Environment.GetEnvironmentVariable("JONDO_PLAYER_TOKEN")
                               ?? Environment.GetEnvironmentVariable("JONDO_CONTROL_TOKEN") ?? "").Trim();

                using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8888/api/bank/open");
                request.Content = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    MelonLogger.Msg("[HudMenuButtons] Bank opened successfully.");
                }
                else
                {
                    MelonLogger.Warning($"[HudMenuButtons] Failed to open bank, status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[HudMenuButtons] Error requesting bank: {ex.Message}");
            }
        }

        private static void FindGridsUnder(VisualElement element, List<VisualElement> into)
        {
            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                var child = element.hierarchy.ElementAt(i);
                if (child.TryCast<SlotGrid>() != null) into.Add(child);
                else FindGridsUnder(child, into);
            }
        }

        private static void FindSlotsUnder(VisualElement element, List<VisualElement> into)
        {
            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                var child = element.hierarchy.ElementAt(i);
                if (child.name == "JondoTeleportMenuButton" || child.name == "JondoBankMenuButton") continue;
                if (child.TryCast<Slot>() != null) into.Add(child);
                else FindSlotsUnder(child, into);
            }
        }

        private static bool IsMenuGrid(VisualElement grid)
        {
            if (grid.name != null && grid.name.IndexOf("btn", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (var up = grid.hierarchy.parent; up != null; up = up.hierarchy.parent)
            {
                if (up.name != null && (up.name.IndexOf("Banner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        up.name.IndexOf("moreBtn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        up.name.IndexOf("additional", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

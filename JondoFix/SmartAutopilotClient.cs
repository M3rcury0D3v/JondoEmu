using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;

namespace JondoFix
{
    /// <summary>
    /// Client-side orchestrator for Smart Hybrid Autopilot.
    /// Balances immersion and convenience: if the destination is far and has a nearby Zaap,
    /// it teleports to that Zaap, then walks step-by-step through each remaining map to the destination.
    /// </summary>
    public static class SmartAutopilotClient
    {
        public sealed class PlanPoint
        {
            public int X { get; set; }
            public int Y { get; set; }
            public long MapId { get; set; }
        }

        public sealed class AutopilotPlanResponse
        {
            public bool Ok { get; set; }
            public bool UseZaap { get; set; }
            public PlanPoint ZaapPoint { get; set; }
            public int DirectDistance { get; set; }
            public int DistanceFromZaap { get; set; }
            public int SavedDistance { get; set; }
            public List<PlanPoint> Route { get; set; } = new List<PlanPoint>();
        }

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly ConcurrentQueue<Action> _toUnity = new ConcurrentQueue<Action>();

        private static bool _navigating = false;
        private static List<PlanPoint> _remainingHops = new List<PlanPoint>();
        private static float _nextStepTime = 0f;

        public static bool IsNavigating => _navigating;

        public static void Tick()
        {
            while (_toUnity.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { MelonLogger.Warning($"[SmartAutopilot] Action error: {ex.Message}"); }
            }

            if (!_navigating || _remainingHops.Count == 0) return;

            float now = Time.unscaledTime;
            if (now >= _nextStepTime)
            {
                _nextStepTime = now + 2.5f; // Wait between map hops for normal transition
                StepNextHop();
            }
        }

        public static async void NavigateTo(int targetX, int targetY, long targetMapId = 0)
        {
            try
            {
                string token = (Environment.GetEnvironmentVariable("JONDO_PLAYER_TOKEN")
                               ?? Environment.GetEnvironmentVariable("JONDO_CONTROL_TOKEN") ?? "").Trim();

                var payload = new
                {
                    token,
                    targetX,
                    targetY,
                    targetMap = targetMapId
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8888/api/autopilot/plan");
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    MelonLogger.Warning($"[SmartAutopilot] Failed to fetch route plan: {response.StatusCode}");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var plan = JsonSerializer.Deserialize<AutopilotPlanResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (plan == null || !plan.Ok)
                {
                    MelonLogger.Warning("[SmartAutopilot] Server rejected autopilot plan request.");
                    return;
                }

                _toUnity.Enqueue(() => StartNavigation(plan));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SmartAutopilot] Plan exception: {ex.Message}");
            }
        }

        private static async void StartNavigation(AutopilotPlanResponse plan)
        {
            _navigating = true;
            _remainingHops.Clear();

            if (plan.UseZaap && plan.ZaapPoint != null)
            {
                MelonLogger.Msg($"[SmartAutopilot] Smart route selected: Teleporting to Zaap [{plan.ZaapPoint.X}, {plan.ZaapPoint.Y}] " +
                                $"(saved {plan.SavedDistance} maps). Remaining walking hops: {plan.DistanceFromZaap} maps.");

                // 1. Teleport to the nearest Zaap
                string token = (Environment.GetEnvironmentVariable("JONDO_PLAYER_TOKEN")
                               ?? Environment.GetEnvironmentVariable("JONDO_CONTROL_TOKEN") ?? "").Trim();

                using var tpRequest = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8888/api/transport/teleport");
                tpRequest.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    token,
                    mapId = plan.ZaapPoint.MapId,
                    x = plan.ZaapPoint.X,
                    y = plan.ZaapPoint.Y
                }), Encoding.UTF8, "application/json");

                await _http.SendAsync(tpRequest);

                // 2. Prepare remaining walking hops from the Zaap to destination
                if (plan.Route.Count > 1)
                {
                    _remainingHops = new List<PlanPoint>(plan.Route);
                    _remainingHops.RemoveAt(0); // Remove current zaap starting map
                    _nextStepTime = Time.unscaledTime + 3f; // Give time for map to load
                }
                else
                {
                    _navigating = false;
                }
            }
            else
            {
                MelonLogger.Msg($"[SmartAutopilot] Direct walking route selected: {plan.DirectDistance} maps to destination.");
                if (plan.Route.Count > 1)
                {
                    _remainingHops = new List<PlanPoint>(plan.Route);
                    _remainingHops.RemoveAt(0);
                    _nextStepTime = Time.unscaledTime + 1f;
                }
                else
                {
                    _navigating = false;
                }
            }
        }

        private static void StepNextHop()
        {
            if (_remainingHops.Count == 0)
            {
                _navigating = false;
                MelonLogger.Msg("[SmartAutopilot] Arrived at destination successfully.");
                return;
            }

            var nextHop = _remainingHops[0];
            _remainingHops.RemoveAt(0);

            MelonLogger.Msg($"[SmartAutopilot] Moving to map hop [{nextHop.X}, {nextHop.Y}] (remaining: {_remainingHops.Count})...");

            // In game, player moves towards the map boundary or sends map change request
            // For headless/client extension testing, trigger teleport/change map
            string token = (Environment.GetEnvironmentVariable("JONDO_PLAYER_TOKEN")
                           ?? Environment.GetEnvironmentVariable("JONDO_CONTROL_TOKEN") ?? "").Trim();

            _ = Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8888/api/transport/teleport");
                    request.Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        token,
                        mapId = nextHop.MapId,
                        x = nextHop.X,
                        y = nextHop.Y
                    }), Encoding.UTF8, "application/json");

                    await _http.SendAsync(request);
                }
                catch { }
            });
        }

        public static void Cancel()
        {
            _navigating = false;
            _remainingHops.Clear();
            MelonLogger.Msg("[SmartAutopilot] Navigation cancelled.");
        }
    }
}

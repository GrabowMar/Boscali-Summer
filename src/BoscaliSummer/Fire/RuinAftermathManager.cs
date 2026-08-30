using System.Collections.Generic;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Stores every ruin for the mission but gives particle systems only to the nearest
    /// bounded subset. This keeps the aftermath persistent without making a destroyed city
    /// simulate hundreds of transparent plumes at once.
    /// </summary>
    internal sealed class RuinAftermathManager : MonoBehaviour
    {
        private sealed class RuinSite
        {
            public GlobalPosition Position;
            public Vector2 HalfExtents;
            public float Born;
            public bool Desired;
            public FuelDepotSmokePool.Visual Smoke;
        }

        public static RuinAftermathManager Instance { get; private set; }

        private readonly List<RuinSite> ruins = new List<RuinSite>(128);
        private readonly FuelDepotSmokePool smokePool = new FuelDepotSmokePool();
        private readonly CollapseBurstPool collapsePool = new CollapseBurstPool();
        private float nextSelection;
        private float nextVisualTick;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene() => Clear();

        internal void RegisterRuin(
            GlobalPosition position, Vector2 halfExtents, float ageSeconds = 0f,
            bool broadcast = false, bool collapseBurst = true)
        {
            for (int i = 0; i < ruins.Count; i++)
                if ((ruins[i].Position - position).sqrMagnitude < 64f) return;
            if (ruins.Count >= Plugin.Settings.MaximumPersistentRuins) return;

            halfExtents.x = Mathf.Clamp(halfExtents.x, 3f, 32f);
            halfExtents.y = Mathf.Clamp(halfExtents.y, 3f, 32f);
            var site = new RuinSite
            {
                Position = position,
                HalfExtents = halfExtents,
                Born = Time.timeSinceLevelLoad - Mathf.Max(0f, ageSeconds)
            };
            ruins.Add(site);
            if (collapseBurst && ageSeconds < 2f) collapsePool.Emit(position, halfExtents);
            if (broadcast) ModNet.BroadcastRuin(position, halfExtents);
            nextSelection = 0f;
        }

        internal void SendSnapshot(Mirage.INetworkPlayer player)
        {
            if (!IsServer() || player == null) return;
            float now = Time.timeSinceLevelLoad;
            for (int i = 0; i < ruins.Count; i++)
                ModNet.SendRuin(player, ruins[i].Position, ruins[i].HalfExtents,
                    Mathf.Max(0f, now - ruins[i].Born));
        }

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            collapsePool.Update(now);
            Camera camera = Camera.main;
            if (camera == null) return;
            if (now >= nextSelection)
            {
                nextSelection = now + 0.5f;
                SelectVisuals(camera.transform.position);
            }
            if (now < nextVisualTick) return;
            nextVisualTick = now + 0.25f;

            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind()
                : Vector3.zero;
            float hotSeconds = Plugin.Settings.HotRuinSeconds;
            for (int i = 0; i < ruins.Count; i++)
            {
                RuinSite site = ruins[i];
                if (site.Smoke == null) continue;
                float age = Mathf.Max(0f, now - site.Born);
                float distance = Vector3.Distance(camera.transform.position, site.Position.ToLocalPosition());
                float distanceScale = distance < 1200f ? 1f : distance < 3000f ? 0.68f : 0.46f;
                float ageScale;
                if (age < hotSeconds)
                    ageScale = Mathf.Lerp(1f, 0.52f, Mathf.Clamp01(age / Mathf.Max(hotSeconds, 1f)));
                else
                    ageScale = 0.25f + Mathf.PerlinNoise(
                        site.Position.x * 0.007f + site.Position.z * 0.011f,
                        now * 0.018f) * 0.16f;
                site.Smoke.ExternalIntensity = ageScale * distanceScale;
                site.Smoke.SetPosition(site.Position);
                site.Smoke.SetPhase(age, 1f, wind);
            }
        }

        private void SelectVisuals(Vector3 cameraPosition)
        {
            for (int i = 0; i < ruins.Count; i++) ruins[i].Desired = false;
            int budget = Mathf.Min(Plugin.Settings.MaximumRuinSmokeVisuals, ruins.Count);
            float maximumDistanceSq = 6000f * 6000f;
            for (int slot = 0; slot < budget; slot++)
            {
                int best = -1;
                float bestDistance = maximumDistanceSq;
                for (int i = 0; i < ruins.Count; i++)
                {
                    RuinSite site = ruins[i];
                    if (site.Desired) continue;
                    float distance = (cameraPosition - site.Position.ToLocalPosition()).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = i;
                }
                if (best < 0) break;
                ruins[best].Desired = true;
            }

            for (int i = 0; i < ruins.Count; i++)
            {
                RuinSite site = ruins[i];
                if (!site.Desired && site.Smoke != null)
                {
                    smokePool.Release(site.Smoke);
                    site.Smoke = null;
                }
                else if (site.Desired && site.Smoke == null && !GameManager.IsHeadless)
                {
                    site.Smoke = smokePool.Acquire(
                        site.Position, site.HalfExtents,
                        FuelDepotSmokePool.SmokeProfile.Ruin);
                }
            }
        }

        private void Clear()
        {
            for (int i = 0; i < ruins.Count; i++) smokePool.Release(ruins[i].Smoke);
            ruins.Clear();
            smokePool.Clear();
            collapsePool.Clear();
            nextSelection = nextVisualTick = 0f;
        }

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }
}

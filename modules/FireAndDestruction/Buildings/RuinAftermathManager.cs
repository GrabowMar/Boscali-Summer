using System.Collections.Generic;
using BoscaliSummer.Modules.FireAndDestruction.Configuration;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using BoscaliSummer.Modules.FireAndDestruction.Networking;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Stores every ruin for the mission but gives particle systems only to the nearest
    /// bounded subset. This keeps the aftermath persistent without making a destroyed city
    /// simulate hundreds of transparent plumes at once.
    /// </summary>
    internal sealed class RuinAftermathManager : MonoBehaviour, ISceneService
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
        private readonly FuelDepotSmokePool smokePool = new FuelDepotSmokePool(24);
        private readonly CollapseBurstPool collapsePool = new CollapseBurstPool();
        private readonly RuinDebrisPool debrisPool = new RuinDebrisPool();
        private float nextSelection;
        private float nextVisualTick;

        private static FireAndDestructionSettings Fire => Plugin.Settings.FireAndDestruction;

        private void Awake() { Instance = this; debrisPool.Landing = collapsePool.EmitLanding; }
        private void Start() { if (!GameManager.IsHeadless) collapsePool.Warm(); }

        private void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene() { Clear(); if (isActiveAndEnabled) Start(); }
        internal void LandingDust(GlobalPosition position) => collapsePool.EmitLanding(position, Vector3.up, 3f);
        internal void SectionDust(GlobalPosition position, float size) => collapsePool.EmitLanding(position, Vector3.up, size);

        internal void RegisterRuin(
            GlobalPosition position, Vector2 halfExtents, float ageSeconds = 0f,
            bool broadcast = false, bool collapseBurst = true, bool genericDebris = true)
        {
            for (int i = 0; i < ruins.Count; i++)
                if ((ruins[i].Position - position).sqrMagnitude < 64f) return;
            if (ruins.Count >= Fire.MaximumPersistentRuins) return;

            halfExtents.x = Mathf.Clamp(halfExtents.x, 3f, 32f);
            halfExtents.y = Mathf.Clamp(halfExtents.y, 3f, 32f);
            var site = new RuinSite
            {
                Position = position,
                HalfExtents = halfExtents,
                Born = Time.timeSinceLevelLoad - Mathf.Max(0f, ageSeconds)
            };
            ruins.Add(site);
            if (genericDebris) debrisPool.Place(position, halfExtents, collapseBurst && ageSeconds < 2f);
            if (collapseBurst && ageSeconds < 2f) collapsePool.Emit(position, halfExtents);
            if (broadcast) ModNet.BroadcastRuin(position, halfExtents);
            nextSelection = 0f;
        }

        internal GameObject AttachFacade(
            GlobalPosition position, List<RuinDebrisPool.FacadePiece> pieces, int buildingId, bool nativeRubble = false) =>
            debrisPool.AttachShell(position, pieces, buildingId, nativeRubble);

        /// <summary>
        /// A shot near a wreck. The slab eases over a third of a second. A blast that
        /// lands on the wreck also kicks the collapse dust and a new hole in the facade.
        /// </summary>
        internal void Poke(Vector3 point, float power)
        {
            if (GameManager.IsHeadless) return;
            if (BuildingCarver.Instance != null && BuildingCarver.Instance.Poke(point, power))
            {
                collapsePool.EmitImpact(point.ToGlobalPosition(), Vector3.up, 6f);
                return;
            }
            if (!debrisPool.Nudge(point, power)) return;
            if (power >= HitEscalation.MinBlastPower)
                collapsePool.EmitImpact(point.ToGlobalPosition(), Vector3.up, 6f);
        }

        internal void SendSnapshot(Mirage.INetworkPlayer player)
        {
            if (!GameAccess.IsServer() || player == null) return;
            float now = Time.timeSinceLevelLoad;
            for (int i = 0; i < ruins.Count; i++)
                ModNet.SendRuin(player, ruins[i].Position, ruins[i].HalfExtents,
                    Mathf.Max(0f, now - ruins[i].Born));
        }

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            collapsePool.Update(now);
            debrisPool.Tick(now);
            if (now < nextSelection && now < nextVisualTick) return;
            if (ruins.Count == 0) return;

            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
            if (camera == null) return;
            Vector3 camPos = camera.transform.position;
            if (now >= nextSelection)
            {
                nextSelection = now + 0.5f;
                SelectVisuals(camPos);
                debrisPool.ApplyAttention(camPos);
            }
            if (now < nextVisualTick) return;
            nextVisualTick = now + 0.25f;

            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind()
                : Vector3.zero;
            float hotSeconds = Fire.HotRuinSeconds;
            for (int i = 0; i < ruins.Count; i++)
            {
                RuinSite site = ruins[i];
                if (site.Smoke == null) continue;
                float age = Mathf.Max(0f, now - site.Born);
                float distanceSq = (camPos - site.Position.ToLocalPosition()).sqrMagnitude;
                float distanceScale = distanceSq < 1200f * 1200f
                    ? 1f : distanceSq < 3000f * 3000f ? 0.68f : 0.46f;
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
            int budget = Mathf.Min(Fire.MaximumRuinSmokeVisuals, ruins.Count);
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

            int acquireBudget = 2;
            for (int i = 0; i < ruins.Count; i++)
            {
                RuinSite site = ruins[i];
                if (!site.Desired && site.Smoke != null)
                {
                    smokePool.Release(site.Smoke);
                    site.Smoke = null;
                }
                else if (site.Desired && site.Smoke == null && !GameManager.IsHeadless && acquireBudget > 0)
                {
                    site.Smoke = smokePool.Acquire(
                        site.Position, site.HalfExtents,
                        FuelDepotSmokePool.SmokeProfile.Ruin);
                    if (site.Smoke != null) acquireBudget--;
                }
            }
        }

        private void Clear()
        {
            for (int i = 0; i < ruins.Count; i++) smokePool.Release(ruins[i].Smoke);
            ruins.Clear();
            smokePool.Clear();
            collapsePool.Clear();
            debrisPool.Clear();
            nextSelection = nextVisualTick = 0f;
        }
    }
}

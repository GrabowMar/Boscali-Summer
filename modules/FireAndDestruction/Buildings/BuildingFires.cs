using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Cosmetic, client-local fires in breaches and collapse stubs. Reuses the module's flame
    /// pool and vanilla fuel-depot smoke copies; only the nearest few sites get visuals, only
    /// the nearest two flicker a light. Never touches native fire, HP or the wire.
    /// </summary>
    internal sealed class BuildingFires
    {
        private sealed class Site
        {
            internal GlobalPosition Position;
            internal float Born, Life, Size;
            internal FireVisualPool.Visual Flame;
            internal FuelDepotSmokePool.Visual Smoke;
        }

        // ponytail: 16 remembered sites, 5 drawn; raise only if dense city fights demand it.
        private const int MaxSites = 16, MaxDrawn = 5;
        private readonly List<Site> sites = new List<Site>(MaxSites);
        private readonly FireVisualPool flames = new FireVisualPool();
        private readonly FuelDepotSmokePool smoke = new FuelDepotSmokePool(MaxDrawn);
        private float nextSelect, nextTick;

        internal int Count => sites.Count;
        internal int Drawn { get { int n = 0; foreach (Site s in sites) if (s.Flame != null) n++; return n; } }

        /// <summary>Size 0..1 scales flame lifetime, smoke volume and burn time.</summary>
        internal void Ignite(GlobalPosition position, float size)
        {
            foreach (Site s in sites)
                if ((s.Position - position).sqrMagnitude < 36f) { s.Size = Mathf.Max(s.Size, size); s.Life = Mathf.Max(s.Life, Time.timeSinceLevelLoad - s.Born + 60f); return; }
            if (sites.Count >= MaxSites)
            {
                Site oldest = sites[0];
                foreach (Site s in sites) if (s.Born < oldest.Born) oldest = s;
                Drop(oldest);
            }
            sites.Add(new Site { Position = position, Born = Time.timeSinceLevelLoad, Life = 70f + size * 110f, Size = size });
            nextSelect = 0f;
        }

        internal void Update(float now)
        {
            for (int i = sites.Count - 1; i >= 0; i--) if (now - sites[i].Born > sites[i].Life) Drop(sites[i]);
            if (sites.Count == 0) return;
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
            if (camera == null) return;
            Vector3 eye = camera.transform.position;
            if (now >= nextSelect) { nextSelect = now + 0.5f; Select(eye); }
            if (now < nextTick) return;
            nextTick = now + 0.1f;
            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null ? NetworkSceneSingleton<LevelInfo>.i.GetWind() : Vector3.zero;
            Site nearA = null, nearB = null; float dA = float.MaxValue, dB = float.MaxValue;
            foreach (Site s in sites)
            {
                if (s.Flame == null) continue;
                float age = now - s.Born, left = 1f - age / s.Life;
                s.Flame.SetPhase(age * (1.5f + s.Size), left, wind);
                if (s.Smoke != null)
                {
                    s.Smoke.ExternalIntensity = (0.35f + s.Size * 0.5f) * Mathf.Clamp01(left * 3f);
                    s.Smoke.SetPosition(s.Position);
                    s.Smoke.SetPhase(age, left, wind);
                }
                float d = (s.Position.ToLocalPosition() - eye).sqrMagnitude;
                if (d < dA) { nearB = nearA; dB = dA; nearA = s; dA = d; }
                else if (d < dB) { nearB = s; dB = d; }
            }
            foreach (Site s in sites) s.Flame?.SetLight(s == nearA || s == nearB);
        }

        private void Select(Vector3 eye)
        {
            sites.Sort((a, b) => (a.Position.ToLocalPosition() - eye).sqrMagnitude.CompareTo((b.Position.ToLocalPosition() - eye).sqrMagnitude));
            for (int i = 0; i < sites.Count; i++)
            {
                Site s = sites[i];
                bool want = i < MaxDrawn && (s.Position.ToLocalPosition() - eye).sqrMagnitude < 3000f * 3000f;
                if (!want) { Hide(s); continue; }
                if (s.Flame == null)
                {
                    s.Flame = flames.Acquire(s.Position, false);
                    s.Flame?.SetPosition(s.Position);
                }
                if (s.Smoke == null) s.Smoke = smoke.Acquire(s.Position, new Vector2(2f + s.Size * 3f, 2f + s.Size * 3f));
            }
        }

        private void Hide(Site s)
        {
            if (s.Flame != null) { flames.Release(s.Flame); s.Flame = null; }
            if (s.Smoke != null) { smoke.Release(s.Smoke); s.Smoke = null; }
        }

        private void Drop(Site s) { Hide(s); sites.Remove(s); }

        internal void Clear()
        {
            foreach (Site s in sites) Hide(s);
            sites.Clear();
            flames.Clear();
            smoke.Clear();
        }
    }
}

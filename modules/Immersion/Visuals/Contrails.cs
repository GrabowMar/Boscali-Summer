using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Contrails (2026-10-27): every running jet nozzle above <see cref="FloorAlt"/> (sea level datum) draws a ribbon that
    /// widens from engine size to a broad band and drifts with the mission wind, then fades over <see cref="Life"/> seconds.
    /// One particle system per nozzle in Datum space (floating-origin safe), one ribbon particle per <see cref="Spacing"/> m,
    /// at most <see cref="MaxEmitters"/> live trails. A trail that stops (descent, engine off, destroyed) is left to fade and a
    /// new one starts on return, so a ribbon never bridges the gap. Wind is set at birth only.
    /// ponytail: altitude band only, no temperature/humidity test; read Weather's air column if contrails should vary by day.
    /// </summary>
    internal sealed class Contrails
    {
        private const float FloorAlt = 7500f, FullAlt = 9000f, CeilingAlt = 16000f, Reach = 40000f, Spacing = 40f, Life = 90f;
        private const int MaxEmitters = 24, MaxFading = 48, MaxPerTrail = 400;

        private static readonly AccessTools.FieldRef<JetNozzle, float> Rpm = Field<float>("rpmRatio");
        private static readonly AccessTools.FieldRef<JetNozzle, Transform> Exhaust = Field<Transform>("thrustTransform");
        private static Material material;

        private sealed class Trail
        {
            public ParticleSystem System;
            public GlobalPosition Last;
            public bool Fresh = true;
            public int Seen;
        }

        private readonly Dictionary<int, Trail> byNozzle = new Dictionary<int, Trail>();
        private readonly List<int> live = new List<int>();
        private readonly List<ParticleSystem> fading = new List<ParticleSystem>();
        private readonly Dictionary<int, JetNozzle[]> nozzles = new Dictionary<int, JetNozzle[]>();
        private int frame;

        internal int Emitters => live.Count;

        internal void Tick(Vector3 eye, bool enabled, float light)
        {
            frame++;
            if (enabled)
            {
                List<Aircraft> aircraft = UnitRegistry.allAircraft;
                for (int i = 0; aircraft != null && i < aircraft.Count; i++)
                {
                    Aircraft plane = aircraft[i];
                    if (plane == null) continue;
                    Vector3 at = plane.transform.position;
                    float alt = at.y - Datum.LocalSeaY;
                    if (alt < FloorAlt || alt > CeilingAlt || (at - eye).sqrMagnitude > Reach * Reach) continue;
                    float strength = Mathf.InverseLerp(FloorAlt, FullAlt, alt);
                    JetNozzle[] jets = Nozzles(plane);
                    for (int j = 0; j < jets.Length; j++) Step(jets[j], strength, light);
                }
            }
            // Anything not fed this frame stops emitting and fades on its own.
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Trail trail = byNozzle[live[i]];
                if (trail.Seen == frame) continue;
                if (trail.System != null) { trail.System.Stop(false, ParticleSystemStopBehavior.StopEmitting); fading.Add(trail.System); }
                byNozzle.Remove(live[i]);
                live.RemoveAt(i);
            }
            while (fading.Count > MaxFading) { if (fading[0] != null) Object.Destroy(fading[0].gameObject); fading.RemoveAt(0); }
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                ParticleSystem s = fading[i];
                if (s != null && s.particleCount > 0) continue;
                if (s != null) Object.Destroy(s.gameObject);
                fading.RemoveAt(i);
            }
        }

        private JetNozzle[] Nozzles(Aircraft plane)
        {
            int id = plane.GetInstanceID();
            if (!nozzles.TryGetValue(id, out JetNozzle[] jets)) nozzles[id] = jets = plane.GetComponentsInChildren<JetNozzle>(true);
            return jets;
        }

        private void Step(JetNozzle nozzle, float strength, float light)
        {
            if (nozzle == null || !nozzle.isActiveAndEnabled || (Rpm != null && Rpm(nozzle) < .2f)) return;
            int id = nozzle.GetInstanceID();
            if (!byNozzle.TryGetValue(id, out Trail trail))
            {
                if (live.Count >= MaxEmitters) return;
                ParticleSystem system = Create();
                if (system == null) return;
                byNozzle[id] = trail = new Trail { System = system };
                live.Add(id);
            }
            trail.Seen = frame;
            Transform exhaust = Exhaust != null ? Exhaust(nozzle) : null;
            Vector3 at = (exhaust != null ? exhaust : nozzle.transform).position;
            GlobalPosition global = at.ToGlobalPosition();
            if (!trail.Fresh && FastMath.Distance(global, trail.Last) < Spacing) return;
            trail.Fresh = false;
            trail.Last = global;
            Vector3 wind = LevelInfo.i != null ? LevelInfo.i.GetWind(global) : Vector3.zero;
            Transform datum = Datum.origin;
            trail.System.Emit(new ParticleSystem.EmitParams
            {
                position = datum.InverseTransformPoint(at),
                velocity = datum.InverseTransformDirection(wind),
                startSize = 2.5f,
                startLifetime = Life,
                startColor = new Color(light, light, Mathf.Min(1f, light * 1.03f), .55f * strength),
                applyShapeToPosition = false,
            }, 1);
        }

        private static ParticleSystem Create()
        {
            Material mat = Material();
            if (mat == null) return null;
            var go = new GameObject("BoscaliContrail") { hideFlags = HideFlags.DontSave };
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaxPerTrail;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.startSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            var shape = system.shape;
            shape.enabled = false;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.12f, 9f), new Keyframe(1f, 16f)));
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .01f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var trails = system.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ribbonCount = 1;
            trails.lifetime = new ParticleSystem.MinMaxCurve(1f);
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = true;
            trails.worldSpace = false;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.trailMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            system.Play();
            return system;
        }

        /// <summary>The shared smoke puff material with a band texture: soft across the ribbon, even along it.</summary>
        private static Material Material()
        {
            if (material != null) return material;
            Material puff = SmokeFx.Puff;
            if (puff == null) return null;
            const int h = 32;
            var band = new Texture2D(4, h, TextureFormat.RGBA32, false) { name = "BoscaliContrailBand", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[4 * h];
            for (int y = 0; y < h; y++)
            {
                float a = Mathf.Pow(Mathf.Sin(Mathf.PI * (y + .5f) / h), 1.6f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            band.SetPixels32(px);
            band.Apply(false, true);
            material = new Material(puff) { name = "BoscaliContrail", hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", band);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", band);
            return material;
        }

        private static AccessTools.FieldRef<JetNozzle, T> Field<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<JetNozzle, T>(name); }
            catch (System.Exception e) { Plugin.Logger?.LogWarning("[Immersion] JetNozzle." + name + " not found; contrails fall back: " + e.Message); return null; }
        }

        internal void Reset()
        {
            foreach (Trail trail in byNozzle.Values) if (trail.System != null) Object.Destroy(trail.System.gameObject);
            for (int i = 0; i < fading.Count; i++) if (fading[i] != null) Object.Destroy(fading[i].gameObject);
            byNozzle.Clear();
            live.Clear();
            fading.Clear();
            nozzles.Clear();
        }
    }
}

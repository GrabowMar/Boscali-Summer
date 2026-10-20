using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// The sea (2026-10-19). Swaps the shader of the game's own water material (<c>WaterMat</c> on <c>LevelInfo.ocean</c>) for
    /// <c>Boscali/Ocean</c> and feeds it every frame: sixteen Gerstner waves from <see cref="SeaState"/>, which follow the mission wind or the
    /// local storm (cover and rain from <see cref="IWeatherView"/>) and build over ~45 s; ships keep a calm ring so hulls do not cut through
    /// crests; low aircraft leave a wash (<see cref="SeaSpray"/>). The swap is in place, so LevelInfo keeps writing the map textures and the
    /// floating-origin offset; releasing restores the vanilla shader and the underlay plane. Wave phases and texture offsets are kept in
    /// doubles relative to an anchor on a 2 km grid near the camera, carried across anchor moves, so nothing slides or loses precision far
    /// from the map centre. Client-local and cosmetic: the game's physics and splashes still use the flat sea.
    /// </summary>
    // After WeatherManager (100): the sea copies RenderSettings fog and ambient into its material, and LevelInfo rewrites them with vanilla's
    // values once a second before RainAtmosphere puts the overcast back; read earlier, the sea flashed blue and unfogged for one frame each
    // second (2026-10-19).
    [DefaultExecutionOrder(101)]
    internal sealed class OceanDressing : MonoBehaviour, ISceneService, IClientEffect
    {
        // Live-tunable through the bridge while a look pass runs (static on purpose).
        internal static float ForcedWind = -1f;
        internal static float ReflectionGain = 1f, ScatterGain = 0.6f, GustContrast = 0.45f;
        internal static float DisplaceStart = 900f, DisplaceEnd = 2200f;
        // Linear. Deep sea is dark blue (0.01, 0.16, 0.27 drew a swimming-pool cyan from altitude, live check 2026-10-19).
        internal static Color CalmDeep = new Color(0.006f, 0.075f, 0.17f), StormDeep = new Color(0.03f, 0.065f, 0.08f);
        internal static Color ScatterColour = new Color(0.1f, 0.55f, 0.48f), FoamTint = new Color(0.93f, 0.96f, 0.98f);

        private const int Waves = SeaState.WaveCount, CalmSlots = 8;
        private const double Anchor = 2048.0, Tau = Math.PI * 2.0;
        private static readonly AccessTools.FieldRef<LevelInfo, ReflectionProbe> ProbeOf =
            AccessTools.FieldRefAccess<LevelInfo, ReflectionProbe>("reflectionProbe");

        private static readonly int WavesId = Shader.PropertyToID("_Waves"), PhaseId = Shader.PropertyToID("_WavePhase"),
            AnchorId = Shader.PropertyToID("_OceanAnchor"), DispId = Shader.PropertyToID("_OceanDisp"), WindId = Shader.PropertyToID("_OceanWind"),
            SeaId = Shader.PropertyToID("_OceanSea"), Sea2Id = Shader.PropertyToID("_OceanSea2"), ShoreId = Shader.PropertyToID("_OceanShore"),
            DeepId = Shader.PropertyToID("_OceanDeep"), ScatterId = Shader.PropertyToID("_OceanScatter"), FoamTintId = Shader.PropertyToID("_OceanFoamTint"),
            AmbientId = Shader.PropertyToID("_OceanAmbient"), FogColourId = Shader.PropertyToID("_OceanFogColor"), FogId = Shader.PropertyToID("_OceanFog"),
            CalmId = Shader.PropertyToID("_OceanCalm"), WashId = Shader.PropertyToID("_OceanWash"), WashDirId = Shader.PropertyToID("_OceanWashDir"),
            SkyId = Shader.PropertyToID("_OceanSky");

        // Texture layers, each a tile size (m), a strength and a drift speed (m/s) along the wind turned by an angle (rad): ripples A and B,
        // foam cells, streaks (sampled 5x longer along the wind), gust patches (drifting with the wind itself), and coarse ripples C that keep
        // the sea textured from 0.5 to 5 km (the fine two mip flat there; live check 2026-10-19).
        // Ripples and streaks use wind-aligned coordinates; foam and gusts use global xz.
        private static readonly int[] LayerIds =
        {
            Shader.PropertyToID("_RippleA"), Shader.PropertyToID("_RippleB"), Shader.PropertyToID("_FoamUV"),
            Shader.PropertyToID("_StreakUV"), Shader.PropertyToID("_GustUV"), Shader.PropertyToID("_RippleC"),
        };
        private static readonly float[] Tile = { 11f, 37f, 46f, 140f, 2400f, 170f };
        private static readonly float[] Weight = { 1f, 0.6f, 1f, 1f, 1f, 0.8f };
        private static readonly float[] Drift = { 0.9f, 1.6f, 1.2f, 3f, -1f, 2.5f };
        private static readonly float[] Turn = { 0f, 0.7f, 0f, 0f, 0f, -0.5f };
        private static readonly bool[] WindAligned = { true, true, false, true, false, true };
        private const int StreakLayer = 3;

        private WeatherSettings settings;
        private IWeatherView weather;
        private ManualLogSource logger;
        private readonly SeaSpray spray = new SeaSpray();

        private Material water;
        private Shader vanillaShader;
        private int vanillaQueue;
        private Transform underlay;
        private Vector3 underlayHome;
        private bool attached, failed;

        private readonly float[] amplitude = new float[Waves];
        private readonly float[] spread = new float[Waves];
        private readonly double[] phase = new double[Waves];
        private readonly Vector2[] direction = new Vector2[Waves];
        private readonly Vector4[] waves = new Vector4[Waves], wavePhase = new Vector4[Waves];
        private readonly Vector4[] calm = new Vector4[CalmSlots], calmGlobal = new Vector4[CalmSlots];
        private readonly float[] calmDistance = new float[CalmSlots];
        private readonly Vector4[] wash = new Vector4[4], washDir = new Vector4[4];
        private readonly double[] offsetU = new double[6], offsetV = new double[6];
        private readonly Vector3[] up = { Vector3.up };
        private readonly Color[] skyIrradiance = new Color[1];
        private double anchorX, anchorZ;
        private bool anchored, headed;
        private float seaWind = -1f, baseDeg, swellDeg, nextShips, nextLight, renderTime, probeMips;
        private int calmCount;
        internal float WaveHeight { get; private set; }

        public string EffectId => "ocean";

        internal void Configure(WeatherSettings weatherSettings, IWeatherView view, ManualLogSource log)
        {
            settings = weatherSettings;
            weather = view;
            logger = log;
        }

        public void ResetForScene()
        {
            ReleaseFx();
            seaWind = -1f;
            headed = anchored = false;
            calmCount = 0;
            nextShips = nextLight = 0f;
        }

        private void OnDestroy() => ReleaseFx();

        public void ReleaseFx()
        {
            Release();
            spray.Reset();
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["oceanActive"] = attached;
            state["oceanSeaWind"] = seaWind;
            state["oceanWaveHeight"] = WaveHeight;
            state["oceanShips"] = calmCount;
            state["sprayParticles"] = spray.Particles;
            state["sprayWashes"] = spray.WashCount;
        }

        private void LateUpdate()
        {
            if (Application.isBatchMode || settings == null) return;
            LevelInfo level = LevelInfo.i;
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (!settings.Enabled.Value || level == null || camera == null || GameManager.gameState == GameState.Encyclopedia)
            {
                ReleaseFx();
                return;
            }
            float dt = Time.deltaTime;
            renderTime = (renderTime + dt) % 1000f; // render-only
            spray.Tick(camera, Datum.LocalSeaY, settings.SeaSprayEnabled.Value, dt);
            if (!settings.OceanWavesEnabled.Value || !Attach(level))
            {
                Release();
                return;
            }
            Drive(level, camera, dt);
        }

        private bool Attach(LevelInfo level)
        {
            if (attached) return water != null;
            if (failed) return false;
            Shader shader = CanopyShaderBundle.GetOceanShader();
            if (shader == null)
            {
                failed = true;
                logger?.LogWarning("[Weather] Ocean shader unavailable; the game's own water stays.");
                return false;
            }
            MeshRenderer renderer = level.ocean != null ? level.ocean.GetComponent<MeshRenderer>() : null;
            if (renderer == null || renderer.sharedMaterial == null) return false;
            water = renderer.sharedMaterial;
            // Borrow the game's own water textures by name before the swap hides the Shader Graph's slots.
            Texture ripple = null, foam = null, streak = null, gust = null;
            foreach (string name in water.GetTexturePropertyNames())
            {
                Texture t = water.GetTexture(name);
                if (t == null) continue;
                if (t.name == "waves_n") ripple = t;
                else if (t.name == "whitecaps") foam = t;
                else if (t.name == "currents") streak = t;
                else if (t.name == "waves_displacement") gust = t;
            }
            vanillaShader = water.shader;
            vanillaQueue = water.renderQueue;
            water.shader = shader;
            water.renderQueue = vanillaQueue;
            if (ripple != null) water.SetTexture("_RippleNormal", ripple);
            if (foam != null) water.SetTexture("_FoamTex", foam);
            if (streak != null) water.SetTexture("_StreakTex", streak);
            if (gust != null) water.SetTexture("_GustTex", gust);
            underlay = level.ocean.transform.Find("oceanUnderlay");
            if (underlay != null) underlayHome = underlay.localPosition;
            attached = true;
            nextLight = 0f;
            logger?.LogInfo($"[Weather] Ocean attached (ripples {ripple != null}, foam {foam != null}, streaks {streak != null}, gusts {gust != null}).");
            return true;
        }

        private void Release()
        {
            if (!attached) return;
            if (water != null && vanillaShader != null)
            {
                water.shader = vanillaShader;
                water.renderQueue = vanillaQueue;
            }
            if (underlay != null) underlay.localPosition = underlayHome;
            attached = false;
            water = null;
            underlay = null;
            WaveHeight = 0f;
        }

        private void Drive(LevelInfo level, Camera camera, float dt)
        {
            GlobalPosition eye = camera.transform.position.ToGlobalPosition();
            Vector3 wind = level.windVelocity;
            float windSpeed = new Vector2(wind.x, wind.z).magnitude;

            float target = ForcedWind >= 0f ? ForcedWind
                : weather != null && weather.TrySample(eye.x, eye.z, out WeatherViewSample sky) ? SeaState.SeaWind(windSpeed, sky.Cover, sky.Rain01)
                : SeaState.SeaWindFromConditions(windSpeed, level.conditions);
            seaWind = ForcedWind >= 0f ? target : SeaState.Ease(seaWind, target, dt);

            // Waves turn toward the wind slowly (12 degrees a minute) about the anchor; the swell keeps the heading it opened with.
            float windDeg = windSpeed > 0.3f ? Mathf.Atan2(wind.x, wind.z) * Mathf.Rad2Deg : baseDeg;
            if (!headed)
            {
                baseDeg = windDeg;
                swellDeg = windDeg + 32f;
                for (int i = 0; i < Waves; i++)
                {
                    spread[i] = SeaState.DirectionOffset(i);
                    phase[i] = WeatherMath.HashRange(0x5EA5u, i, 1, 0, 0f, (float)Tau);
                }
                headed = true;
            }
            else baseDeg = Mathf.MoveTowardsAngle(baseDeg, windDeg, 0.2f * dt);
            for (int i = 0; i < Waves; i++)
            {
                float deg = (i < SeaState.SwellCount ? swellDeg : baseDeg) + spread[i];
                direction[i] = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));
            }

            MoveAnchor(eye);

            SeaState.Amplitudes(seaWind, amplitude);
            WaveHeight = SeaState.SignificantHeight(amplitude);
            float steep = SeaState.Steepness(seaWind), leanSum = 0f, crest = 0f;
            for (int i = 0; i < Waves; i++)
            {
                double k = Tau / SeaState.Wavelength(i);
                phase[i] = Math.IEEERemainder(phase[i] - Math.Sqrt(9.81 * k) * dt, Tau);
                leanSum += steep * (float)k * amplitude[i];
                crest += amplitude[i];
            }
            // Never let the train fold over: the summed lean stays under 0.95.
            float q = leanSum > 0.95f ? steep * 0.95f / leanSum : steep;
            for (int i = 0; i < Waves; i++)
            {
                double length = SeaState.Wavelength(i);
                waves[i] = new Vector4(direction[i].x, direction[i].y, (float)(Tau / length), amplitude[i]);
                wavePhase[i] = new Vector4((float)phase[i], q, (float)length, 0f);
            }

            DriftTextures(dt);
            if (Time.unscaledTime >= nextShips) { nextShips = Time.unscaledTime + 0.5f; FindShips(camera); }
            for (int i = 0; i < CalmSlots; i++)
                calm[i] = i < calmCount
                    ? new Vector4((float)(calmGlobal[i].x - anchorX), (float)(calmGlobal[i].y - anchorZ), calmGlobal[i].z, calmGlobal[i].w)
                    : Vector4.zero;
            for (int i = 0; i < 4; i++)
            {
                if (i < spray.WashCount)
                {
                    Vector4 w = spray.Wash[i];
                    wash[i] = new Vector4((float)(w.x - anchorX), (float)(w.y - anchorZ), w.z, w.w);
                    washDir[i] = spray.WashDir[i];
                }
                else { wash[i] = Vector4.zero; washDir[i] = new Vector4(0f, 1f, 1f, 0f); }
            }

            if (Time.unscaledTime >= nextLight) { nextLight = Time.unscaledTime + 0.5f; ReadLighting(level); }
            Upload(level, crest);
        }

        /// <summary>Re-anchors on the 2 km grid and carries every phase and offset across, so the sea does not jump.</summary>
        private void MoveAnchor(GlobalPosition eye)
        {
            double ax = Math.Round(eye.x / Anchor) * Anchor, az = Math.Round(eye.z / Anchor) * Anchor;
            if (!anchored)
            {
                anchorX = ax; anchorZ = az; anchored = true;
                Array.Clear(offsetU, 0, offsetU.Length);
                Array.Clear(offsetV, 0, offsetV.Length);
                return;
            }
            double dx = ax - anchorX, dz = az - anchorZ;
            if (dx == 0 && dz == 0) return;
            for (int i = 0; i < Waves; i++)
                phase[i] = Math.IEEERemainder(phase[i] + Tau / SeaState.Wavelength(i) * (direction[i].x * dx + direction[i].y * dz), Tau);
            Vector2 along = Along(StreakLayer);
            for (int t = 0; t < Tile.Length; t++)
            {
                if (WindAligned[t])
                {
                    Vector2 basis = t == StreakLayer ? along : Along(t);
                    offsetU[t] += (basis.x * dx + basis.y * dz) * (t == StreakLayer ? 0.2 : 1.0) / Tile[t];
                    offsetV[t] += (-basis.y * dx + basis.x * dz) / Tile[t];
                }
                else { offsetU[t] += dx / Tile[t]; offsetV[t] += dz / Tile[t]; }
                offsetU[t] -= Math.Floor(offsetU[t]);
                offsetV[t] -= Math.Floor(offsetV[t]);
            }
            anchorX = ax; anchorZ = az;
        }

        private Vector2 Along(int layer)
        {
            float rad = (baseDeg * Mathf.Deg2Rad) + Turn[layer];
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        /// <summary>Scrolls each texture layer downwind: ripples and foam with the surface drift, gust patches with the wind itself.</summary>
        private void DriftTextures(float dt)
        {
            for (int t = 0; t < Tile.Length; t++)
            {
                float speed = Drift[t] >= 0f ? Drift[t] * Mathf.Lerp(0.5f, 1.5f, SeaState.Storminess(seaWind)) : Mathf.Max(seaWind, 2f) * 0.8f;
                Vector2 dir = Along(t);
                if (WindAligned[t]) offsetU[t] -= speed * dt * (t == StreakLayer ? 0.2 : 1.0) / Tile[t];
                else { offsetU[t] -= dir.x * speed * dt / Tile[t]; offsetV[t] -= dir.y * speed * dt / Tile[t]; }
                offsetU[t] -= Math.Floor(offsetU[t]);
                offsetV[t] -= Math.Floor(offsetV[t]);
            }
        }

        /// <summary>The eight nearest ships within 4 km keep a calm ring: flat inside 0.9 of their radius, full waves by 1.9.</summary>
        private void FindShips(Camera camera)
        {
            calmCount = 0;
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            Vector3 eye = camera.transform.position;
            for (int i = 0; i < units.Count; i++)
            {
                if (!(units[i] is Ship ship) || ship == null) continue;
                Vector3 at = ship.transform.position;
                float d2 = (at - eye).sqrMagnitude;
                if (d2 > 4000f * 4000f) continue;
                int slot = calmCount;
                if (calmCount < CalmSlots) calmCount++;
                else
                {
                    slot = 0;
                    for (int s = 1; s < CalmSlots; s++) if (calmDistance[s] > calmDistance[slot]) slot = s;
                    if (calmDistance[slot] <= d2) continue;
                }
                GlobalPosition g = at.ToGlobalPosition();
                calmGlobal[slot] = new Vector4(g.x, g.z, ship.maxRadius * 0.9f, ship.maxRadius * 1.9f);
                calmDistance[slot] = d2;
            }
        }

        private void ReadLighting(LevelInfo level)
        {
            RenderSettings.ambientProbe.Evaluate(up, skyIrradiance);
            ReflectionProbe probe = ProbeOf(level);
            Texture sky = probe == null ? null : probe.realtimeTexture != null ? probe.realtimeTexture : probe.texture;
            if (sky == null) return;
            water.SetTexture(SkyId, sky);
            probeMips = Mathf.Log(Mathf.Max(sky.width, 1), 2f);
        }

        private void Upload(LevelInfo level, float crest)
        {
            float storm = SeaState.Storminess(seaWind);
            float coverage = SeaState.Whitecaps(seaWind);
            Vector2 windDir = Along(0);
            Light main = WeatherLighting.Source(level);

            water.SetVectorArray(WavesId, waves);
            water.SetVectorArray(PhaseId, wavePhase);
            water.SetVectorArray(CalmId, calm);
            water.SetVectorArray(WashId, wash);
            water.SetVectorArray(WashDirId, washDir);
            water.SetVector(AnchorId, new Vector4((float)anchorX, (float)anchorZ, 0f, 0f));
            water.SetVector(DispId, new Vector4(DisplaceStart, DisplaceEnd, renderTime, 0f));
            water.SetVector(WindId, new Vector4(windDir.x, windDir.y, SeaState.Ripples(seaWind), GustContrast));
            // Breaking cores are a fifth of the whitecap coverage (Monahan's stage A); the rest is the foam lace they leave.
            water.SetVector(SeaId, new Vector4(SeaState.Roughness(seaWind), SeaState.WhitecapSigma(coverage * 0.2f), SeaState.WhitecapSigma(coverage), coverage));
            water.SetVector(Sea2Id, new Vector4(SeaState.Streaks(seaWind), Mathf.Lerp(0.25f, 1f, storm), probeMips, ReflectionGain));
            for (int t = 0; t < Tile.Length; t++)
                water.SetVector(LayerIds[t], new Vector4((float)offsetU[t], (float)offsetV[t], 1f / Tile[t], Weight[t]));
            water.SetVector(ShoreId, new Vector4(0.55f, 0.92f, 0.6f, 0.35f));
            water.SetColor(DeepId, Color.Lerp(CalmDeep, StormDeep, storm));
            water.SetColor(ScatterId, new Color(ScatterColour.r, ScatterColour.g, ScatterColour.b, ScatterGain * Mathf.Lerp(0.5f, 1f, storm)));
            water.SetColor(FoamTintId, FoamTint);
            water.SetVector(AmbientId, new Vector4(skyIrradiance[0].r, skyIrradiance[0].g, skyIrradiance[0].b, 1f));
            // The shader works in linear light: the raw sRGB fog colour fogged the far sea paler and greyer than the terrain beside it,
            // a grey band along the horizon (live check 2026-10-19).
            water.SetColor(FogColourId, RenderSettings.fogColor.linear);
            water.SetVector(FogId, new Vector4(RenderSettings.fogDensity, RenderSettings.fogMode == FogMode.ExponentialSquared ? 1f : 0f,
                RenderSettings.fog ? 1f : 0f, main != null && main.cookie != null ? 1f : 0f));
            // The opaque underlay sits 2 m under the sea; sink it under the deepest trough or it cuts the troughs off.
            if (underlay != null) underlay.localPosition = underlayHome + Vector3.down * Mathf.Min(crest + 0.5f, 10f);
        }
    }
}

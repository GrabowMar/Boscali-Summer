using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.QoL.Configuration;
using BoscaliSummer.Modules.QoL.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.QoL.Visuals
{
    /// <summary>
    /// The mod's night-vision look, on top of the game's own goggles (the N key and vanilla's volume swap stay as
    /// they are). While the goggles are on it drives the overrides of vanilla's night-vision volume and runs
    /// <see cref="NvgPass"/>: a white-phosphor picture with a metered, gating tube gain, and in the Fused look an
    /// orange glow on everything the game counts as hot. Local presentation only.
    /// Fails closed: no shaders means a white tint on vanilla's own gain; the Vanilla setting touches nothing.
    /// </summary>
    internal sealed class NightVisionLook : MonoBehaviour, ISceneService
    {
        // Statics, not constants: the look is tuned live through the bridge.
        /// <summary>The average frame brightness AUTO gain aims for, before the game's exposure.</summary>
        internal static float TargetLuminance = 0.12f;
        /// <summary>Stops of gain AUTO and HIGH may add over vanilla's maximum.</summary>
        internal static float MaxBoost = 1.5f;
        internal static float HeatBrightness = 1.5f, SignalGrain = 0.35f, NoiseFloor = 0.004f, Saturation = -25f;

        private const float ScanInterval = 0.25f, HeatRange = 12000f;
        private const int MaxHotUnits = 40, MaxCachedUnits = 200;
        /// <summary>Radius over distance below which a mesh is skipped: about three pixels at 1080p.</summary>
        private const float MinAngularSize = 0.0025f;
        private static readonly Color Phosphor = new Color(0.80f, 0.90f, 1f), HeatColor = new Color(1f, 0.36f, 0.04f);

        private readonly Dictionary<Unit, NvgPass.HotRenderer[]> renderers = new Dictionary<Unit, NvgPass.HotRenderer[]>();
        private readonly List<(float Distance, Unit Unit, float Heat)> candidates = new List<(float, Unit, float)>(64);
        private readonly HashSet<Renderer> seen = new HashSet<Renderer>();
        private QoLSettings settings;
        private NvgPass pass;
        private Material phosphorMaterial, heatMaterial;
        private NvgGain gain;
        private Volume volume;
        private ColorAdjustments color;
        private Bloom bloom;
        private FilmGrain grain;
        private Vignette vignette;
        private Color savedFilter;
        private float savedSaturation, savedExposure, savedThreshold, savedGrain, savedVignette, nextScan;
        private int appliedFrame, lastStep;
        private float settleUntil;

        internal static NightVisionLook Live { get; private set; }
        internal QoLSettings Settings => settings;

        public void Configure(QoLSettings config) => settings = config;

        private void Awake() => Live = this;

        /// <summary>Automation: what the tube is doing right now.</summary>
        internal Dictionary<string, object> Readout()
        {
            GameAccess.TryGetNightVisionState(out bool selected, out bool active);
            return new Dictionary<string, object>
            {
                { "ok", true }, { "look", settings != null ? settings.NightVisionLook.Value.ToString() : "none" },
                { "selected", selected }, { "active", active }, { "ownsGain", NightVisionLink.OwnsGain },
                { "gating", NightVisionLink.Gating }, { "gainStep", NightVisionLink.GainStep }, { "legend", NightVisionLink.Legend },
                { "ev", gain.Ev }, { "metered", pass != null && pass.Metered }, { "mean", pass != null ? pass.Mean : 0f },
                { "peak", pass != null ? pass.Peak : 0f }, { "passAge", pass != null ? Time.frameCount - pass.ExecutedFrame : -1 },
                { "hot", pass != null ? pass.Hot.Count : 0 }, { "draws", pass != null ? pass.Draws : 0 },
                { "exposure", color != null ? color.postExposure.value : 0f }, { "saturation", color != null ? color.saturation.value : 0f },
            };
        }

        public void ResetForScene()
        {
            Release();
            renderers.Clear();
        }

        private void OnDestroy()
        {
            Release();
            pass?.Dispose();
            pass = null;
            if (phosphorMaterial != null) Destroy(phosphorMaterial);
            if (heatMaterial != null) Destroy(heatMaterial);
            NvgShaderBundle.Release();
            if (Live == this) Live = null;
        }

        private void Update()
        {
            NvgLook look = settings != null ? settings.NightVisionLook.Value : NvgLook.Vanilla;
            if (look == NvgLook.Vanilla || !GameAccess.TryGetNightVisionState(out _, out bool active) || !active ||
                !GameAccess.TryGetNightVisionRig(out Volume rig, out float min, out float max))
            {
                Release();
                return;
            }
            if (rig != volume)
            {
                Release();
                if (!Take(rig)) return;
                gain.Start(min);
            }

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera world = cameras != null ? cameras.mainCamera : null;
            // Shaders present and the pass really running (a grace period covers the first frames after switch-on).
            bool live = world != null && EnsurePass(world) &&
                (Time.frameCount - appliedFrame < 30 || pass.ExecutedFrame >= Time.frameCount - 30);
            if (pass != null) pass.Active = world != null && phosphorMaterial != null;
            NightVisionLink.OwnsGain = live;
            if (!live)
            {
                // Vanilla keeps its own gain; only the colour changes.
                NightVisionLink.Gating = false;
                color.colorFilter.value = Phosphor;
                color.saturation.value = -90f;
                return;
            }

            float top = max + MaxBoost;
            int step = NightVisionLink.GainStep;
            float wanted = step == 0 && !pass.Metered ? min
                : NvgGain.Wanted(step, NvgGain.Metered(pass.Mean, pass.Peak), TargetLuminance, min, top);
            gain.Step(wanted, Time.unscaledDeltaTime);
            // A hand on the gain knob is not the tube gating: only a drop AUTO made by itself lights the lamp.
            if (step != lastStep) { lastStep = step; settleUntil = Time.unscaledTime + 1.5f; }
            if (step != 0 || Time.unscaledTime < settleUntil) gain.Baseline = gain.Ev;
            NightVisionLink.Gating = step == 0 && gain.Gating;
            float level = Mathf.InverseLerp(min, top, gain.Ev);

            // The tint is the pass's job now, so the cockpit and the thermal orange keep some colour.
            color.postExposure.value = gain.Ev;
            color.colorFilter.value = Color.white;
            color.saturation.value = Saturation;
            bloom.threshold.value = Mathf.Lerp(1.5f, 0.35f, level);
            if (grain != null) grain.intensity.value = Mathf.Lerp(0.2f, 0.6f, level);
            if (vignette != null) vignette.intensity.value = 0.42f;

            pass.Tint = Phosphor;
            pass.Noise = new Vector2(SignalGrain, NoiseFloor);
            pass.DrawHeat = look == NvgLook.Fused && heatMaterial != null;
            // Divided by the gain, so a hot unit looks as bright on a moonless night as at dusk.
            pass.HeatColor = HeatColor * (HeatBrightness / Mathf.Pow(2f, gain.Ev));
            if (!pass.DrawHeat) pass.Hot.Clear();
            else if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                Scan(world.transform.position);
            }
        }

        private bool EnsurePass(Camera world)
        {
            if (phosphorMaterial == null)
            {
                Shader shader = NvgShaderBundle.GetPhosphor();
                if (shader == null) return false;
                phosphorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                Shader heatShader = NvgShaderBundle.GetHeat();
                if (heatShader != null) heatMaterial = new Material(heatShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            pass ??= new NvgPass();
            pass.Bind(world, phosphorMaterial, heatMaterial);
            return true;
        }

        private bool Take(Volume rig)
        {
            VolumeProfile profile = rig.profile;
            if (profile == null || !profile.TryGet(out color) || !profile.TryGet(out bloom)) return false;
            profile.TryGet(out grain);
            profile.TryGet(out vignette);
            volume = rig;
            savedFilter = color.colorFilter.value;
            savedSaturation = color.saturation.value;
            savedExposure = color.postExposure.value;
            savedThreshold = bloom.threshold.value;
            if (grain != null) savedGrain = grain.intensity.value;
            if (vignette != null) savedVignette = vignette.intensity.value;
            appliedFrame = Time.frameCount;
            return true;
        }

        /// <summary>Hand the volume back exactly as vanilla left it.</summary>
        private void Release()
        {
            NightVisionLink.OwnsGain = NightVisionLink.Gating = false;
            pass?.Rest();
            if (volume == null) return;
            if (color != null)
            {
                color.colorFilter.value = savedFilter;
                color.saturation.value = savedSaturation;
                color.postExposure.value = savedExposure;
            }
            if (bloom != null) bloom.threshold.value = savedThreshold;
            if (grain != null) grain.intensity.value = savedGrain;
            if (vignette != null) vignette.intensity.value = savedVignette;
            volume = null;
            color = null; bloom = null; grain = null; vignette = null;
        }

        /// <summary>The nearest hot units, four times a second: the game's infrared sources decide what is hot.</summary>
        private void Scan(Vector3 eye)
        {
            pass.Hot.Clear();
            candidates.Clear();
            GameManager.GetLocalAircraft(out Aircraft own);
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || unit == own || unit is Building) continue;
                float distance = (unit.transform.position - eye).sqrMagnitude;
                if (distance > HeatRange * HeatRange) continue;
                float infrared = 0f;
                List<IRSource> sources = GameAccess.GetIrSources(unit);
                if (sources != null)
                    for (int s = 0; s < sources.Count; s++)
                        if (sources[s] != null && !sources[s].flare) infrared += sources[s].intensity;
                float heat = NvgGain.Heat01(infrared, unit is GroundVehicle || unit is Ship);
                if (heat > 0f) candidates.Add((distance, unit, heat));
            }
            if (candidates.Count > MaxHotUnits)
            {
                candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                candidates.RemoveRange(MaxHotUnits, candidates.Count - MaxHotUnits);
            }
            foreach ((float squared, Unit unit, float heat) in candidates)
            {
                NvgPass.HotRenderer[] parts = RenderersOf(unit);
                // An aircraft is some 150 small meshes: only the ones a few pixels across are worth a draw call.
                float smallest = Mathf.Sqrt(squared) * MinAngularSize;
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p].Size < smallest) continue;
                    parts[p].Heat = heat;
                    pass.Hot.Add(parts[p]);
                }
            }
        }

        private void Collect(Component root, List<NvgPass.HotRenderer> found)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && seen.Add(renderer))
                    found.Add(new NvgPass.HotRenderer { Renderer = renderer, Submeshes = renderer.sharedMaterials.Length, Size = renderer.bounds.extents.magnitude });
        }

        private NvgPass.HotRenderer[] RenderersOf(Unit unit)
        {
            if (renderers.TryGetValue(unit, out NvgPass.HotRenderer[] parts)) return parts;
            if (renderers.Count >= MaxCachedUnits) renderers.Clear();
            // An aircraft's parts are separate bodies, not children of the unit: walk them too. Inactive ones are
            // kept (other detail levels); the pass skips whatever is not drawn this frame.
            var found = new List<NvgPass.HotRenderer>();
            seen.Clear();
            Collect(unit, found);
            if (unit.partLookup != null)
                foreach (UnitPart part in unit.partLookup) if (part != null) Collect(part, found);
            parts = found.ToArray();
            renderers[unit] = parts;
            return parts;
        }
    }
}

using System;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Retunes the vanilla cloud deck from the weather model — storm darkening, the discrete
    /// weather-set feather, turbulent shear, deck thickness, advection, anvil spread and fog —
    /// and draws no geometry of its own.
    ///
    /// <para>Everything here is presentation and client-local: nothing is networked, nothing is
    /// written back to <c>LevelInfo</c>, and a client with a different sky setting still flies
    /// the host's weather. The deck is never hidden, no <c>Renderer</c> or <c>GameObject</c> is
    /// disabled, and the <c>CloudLayer</c> component keeps running, because it owns the sun and
    /// moon cloud cookies, the occlusion, the fog and the lighting the rest of the module reads.</para>
    ///
    /// <para>Writes go to material and particle-system instances the game itself owns: the
    /// <c>layerMaterial</c> is the clone <c>CloudLayer.Start</c> makes with
    /// <c>MaterialHelper.CloneMaterial</c>, and the three puff materials are the
    /// <c>ParticleSystemRenderer.material</c> instances that same <c>Start</c> caches. Only the
    /// live instances are written; the shipped <c>Material</c>/<c>WeatherSet</c> assets are read,
    /// never mutated.</para>
    ///
    /// <para>All writes happen in <see cref="LateUpdate"/>. Vanilla writes its deck in
    /// <c>CloudLayer.Update</c> and its 1 Hz <c>UpdateWeatherSets</c> from a UniTask loop whose
    /// runner is injected at the top of the Update phase, and <c>LevelInfo</c> writes the fog from
    /// its own <c>Update</c>; every one of them therefore runs before this, so the last word each
    /// frame is this module's.</para>
    ///
    /// <para>A missing <c>CloudLayer</c> is a bounded number of cheap probes and then a latch
    /// until the next scene; a missing mission or a switched-off module gives the uncontested
    /// values back immediately and lets vanilla rewrite the rest inside a second.</para>
    /// </summary>
    internal sealed class VanillaClouds : MonoBehaviour, ISceneService
    {
        /// <summary>Frames the binding is retried for, then the module latches until the next scene.</summary>
        private const int MaxAttempts = 8;

        /// <summary>
        /// How far ahead of the reader the storm field is sampled for the "it is coming" darkening.
        /// A look judgement: roughly a minute of flight at transit speeds, so the deck leads the
        /// aircraft into the cell rather than only darkening once the aircraft is under it.
        /// </summary>
        private const float LookAheadMeters = 15000f;

        /// <summary>Deck brightness left at full storm darkness. Look judgement, not a derivation.</summary>
        private const float StormLightFloor = 0.35f;

        /// <summary>How far the scatter colour is pulled to grey at full storm darkness.</summary>
        private const float StormDesaturation = 0.5f;

        /// <summary>Deck-plane emissive left at full storm darkness. Look judgement.</summary>
        private const float DeckEmissiveFloor = 0.45f;

        /// <summary>
        /// How strongly the blended weather-set coverage thins the near puffs. This is the
        /// feather: opacity trades against the discrete set's particle count so the visible
        /// density ramps instead of stepping at a set boundary. Look judgement; 0 disables it.
        /// </summary>
        private const float FeatherGain = 0.30f;

        /// <summary>Below this stir the puff noise is switched off entirely (it would not read).</summary>
        private const float NoiseOnset = 0.05f;

        /// <summary>Noise displacement at rest and at full stir, as a fraction of the deck thickness.</summary>
        private const float NoiseStrengthMin = 0.02f;
        private const float NoiseStrengthMax = 0.22f;

        /// <summary>Horizon-band particles are an order of magnitude larger, so their noise must be too.</summary>
        private const float DistantNoiseScale = 3f;

        /// <summary>Noise frequency and scroll speed in world units and seconds. Look judgement.</summary>
        private const float NoiseFrequency = 0.04f;
        private const float NoiseFrequencyRise = 0.10f;
        private const float NoiseScrollSpeed = 0.08f;

        /// <summary>Gust speed that counts as a full gust, metres per second.</summary>
        private const float GustReference = 25f;

        /// <summary>At and above this LCL the deck keeps its vanilla thickness, metres.</summary>
        private const float LclThinAt = 3000f;

        /// <summary>Deck thickness multiplier when the LCL sits on the surface. Look judgement.</summary>
        private const float DeckThicknessGain = 1.6f;

        /// <summary>How fast the deck yaws onto the veering wind, degrees per second.</summary>
        private const float YawDegreesPerSecond = 1.2f;

        /// <summary>How much of the front's travel heading is mixed into the deck yaw, 0..1.</summary>
        private const float FrontYawWeight = 0.5f;

        /// <summary>Band expansion at full convection: 1 + this, so up to 1.8x. Look judgement.</summary>
        private const float AnvilGain = 0.8f;

        /// <summary>Convection floor once the population organises into a cluster or a line.</summary>
        private const float AnvilOrganisedFloor = 0.7f;

        /// <summary>
        /// Fog density at the visibility distance: three e-foldings, so the deck is ~95%
        /// extinguished at <c>Visibility</c> metres. Derived from Beer-Lambert, not taste.
        /// </summary>
        private const float FogExtinction = 3f;

        /// <summary>Visibility floor for the fog term, so a zero reading cannot make soup.</summary>
        private const float MinFogVisibility = 250f;

        private static readonly int ScatterColorId = Shader.PropertyToID("_ScatterColor");
        private static readonly int OpacityBoostId = Shader.PropertyToID("_OpacityBoost");
        private static readonly int CloudEmissiveId = Shader.PropertyToID("_CloudEmissive");

        // The one reflection seam into the vanilla sky, following WeatherCloudAccess: bound once
        // per process, resolved to the live layer once per scene. Everything here is private
        // serialized state of CloudLayer; the names are a compatibility surface, so a missing
        // one disables the module with a warning instead of throwing per frame.
        private static AccessTools.FieldRef<CloudLayer, MeshRenderer> cloudRendererRef;
        private static AccessTools.FieldRef<CloudLayer, ParticleSystem> cloudSystemRef;
        private static AccessTools.FieldRef<CloudLayer, ParticleSystem> distantSystemRef;
        private static AccessTools.FieldRef<CloudLayer, ParticleSystem> flyThroughSystemRef;
        private static AccessTools.FieldRef<CloudLayer, Material> cloudMaterialRef;
        private static AccessTools.FieldRef<CloudLayer, Material> layerMaterialRef;
        private static AccessTools.FieldRef<CloudLayer, WeatherSet[]> weatherSetsRef;
        private static AccessTools.FieldRef<CloudLayer, float> layerThicknessRef;
        private static bool seamsBound;
        private static bool seamsFailed;

        private WeatherManager manager;
        private ManualLogSource log;

        // Live scene binding. Null means the module is not retuning anything.
        private CloudLayer layer;
        private Transform deckTransform;
        private ParticleSystem.MainModule distantMain;
        private ParticleSystem.NoiseModule nearNoise;
        private ParticleSystem.NoiseModule distantNoise;
        private Material nearMaterial;
        private Material distantMaterial;
        private Material flyMaterial;
        private Material deckMaterial;
        private WeatherSet[] sets;

        // Vanilla baselines, cached once per scene so a release can hand them back.
        private float baseDeckEmissive;
        private float baseLayerThickness;
        private Quaternion baseDeckRotation;
        private float baseWindHeading;
        private float advectionYaw;

        private bool active;
        private bool touched;
        private bool nearNoiseOn;
        private bool distantNoiseOn;
        private int attempts;
        private bool warned;
        private bool announced;
        private float lastFogWeight = 1f;

        public void Configure(WeatherManager weatherManager, ManualLogSource logger)
        {
            manager = weatherManager;
            log = logger;
        }

        /// <summary>
        /// Idempotent: gives the uncontested values back, forgets the scene's objects and re-arms
        /// the bounded binding for the next scene. Contested values (scatter colour, band size,
        /// fog) are left for vanilla, which rewrites them on its next tick anyway.
        /// </summary>
        public void ResetForScene()
        {
            Restore();
            layer = null;
            deckTransform = null;
            nearMaterial = null;
            distantMaterial = null;
            flyMaterial = null;
            deckMaterial = null;
            sets = null;
            baseDeckEmissive = 0f;
            baseLayerThickness = 0f;
            baseDeckRotation = Quaternion.identity;
            baseWindHeading = 0f;
            advectionYaw = 0f;
            active = false;
            touched = false;
            nearNoiseOn = false;
            distantNoiseOn = false;
            attempts = 0;
            warned = false;
            announced = false;
            lastFogWeight = 1f;
        }

        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            // A headless host has no sky to present and no camera to hang it on.
            if (Application.isBatchMode) return;
            if (manager == null || !manager.Enabled)
            {
                Restore();
                return;
            }

            if (!active && !TryResolve()) return;

            WeatherSnapshot snapshot = manager.Snapshot;
            if (!snapshot.Available)
            {
                // No mission: vanilla owns its sky until the model has one again.
                Restore();
                return;
            }

            Apply(in snapshot);
        }

        /// <summary>
        /// Bind the private fields once, then find the live layer. The layer's fields are
        /// assigned in <c>Start</c>, which trails the first service tick by a frame or two, so
        /// the probe is retried a bounded number of times and then latched until the next scene.
        /// </summary>
        private bool TryResolve()
        {
            if (!seamsBound && !seamsFailed) BindSeams();
            if (!seamsBound) return false;
            if (attempts >= MaxAttempts) return false;
            attempts++;

            CloudLayer found = UnityEngine.Object.FindObjectOfType<CloudLayer>(true);
            if (found == null)
            {
                Warn("no CloudLayer in the scene");
                return false;
            }

            MeshRenderer deck = cloudRendererRef(found);
            ParticleSystem near = cloudSystemRef(found);
            ParticleSystem band = distantSystemRef(found);
            ParticleSystem wisps = flyThroughSystemRef(found);
            Material nearMat = cloudMaterialRef(found);
            Material deckMat = layerMaterialRef(found);
            WeatherSet[] weather = weatherSetsRef(found);
            if (deck == null || near == null || band == null || wisps == null || nearMat == null ||
                deckMat == null || weather == null || weather.Length == 0)
            {
                Warn("CloudLayer has not finished initialising");
                return false;
            }

            // The renderers' own materials are the instances CloudLayer caches in Start; touching
            // them here returns the same instances rather than second clones.
            Material bandMat = MaterialOf(band);
            Material wispMat = MaterialOf(wisps);

            layer = found;
            deckTransform = deck.transform;
            deckMaterial = deckMat;
            distantMain = band.main;
            nearNoise = near.noise;
            distantNoise = band.noise;
            nearMaterial = nearMat;
            distantMaterial = bandMat;
            flyMaterial = wispMat;
            sets = weather;

            baseDeckRotation = deck.transform.rotation;
            baseDeckEmissive = deckMat.GetFloat(CloudEmissiveId);
            baseLayerThickness = layerThicknessRef(found);

            active = true;
            if (!announced)
            {
                announced = true;
                log?.LogInfo("Weather clouds: retuning the vanilla deck — " + weather.Length +
                             " weather sets, layer material '" + deckMat.name + "' with emissive " +
                             baseDeckEmissive.ToString("0.##") + ", deck thickness " +
                             baseLayerThickness.ToString("0") + " m.");
            }
            return true;
        }

        private static Material MaterialOf(ParticleSystem system)
        {
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            return renderer != null ? renderer.material : null;
        }

        private bool BindSeams()
        {
            try
            {
                cloudRendererRef = FieldRef<CloudLayer, MeshRenderer>("cloudRenderer");
                cloudSystemRef = FieldRef<CloudLayer, ParticleSystem>("cloudSystem");
                distantSystemRef = FieldRef<CloudLayer, ParticleSystem>("distantCloudSystem");
                flyThroughSystemRef = FieldRef<CloudLayer, ParticleSystem>("flyThroughSystem");
                cloudMaterialRef = FieldRef<CloudLayer, Material>("cloudMaterial");
                layerMaterialRef = FieldRef<CloudLayer, Material>("layerMaterial");
                weatherSetsRef = FieldRef<CloudLayer, WeatherSet[]>("weatherSets");
                layerThicknessRef = FieldRef<CloudLayer, float>("layerThickness");
                seamsBound = true;
                return true;
            }
            catch (Exception e)
            {
                // A renamed field is a game update, not a transient state: give up until the
                // process restarts rather than probing every scene or every frame.
                seamsFailed = true;
                attempts = MaxAttempts;
                Warn("CloudLayer fields unavailable: " + e.Message);
                return false;
            }
        }

        private static AccessTools.FieldRef<TInstance, TField> FieldRef<TInstance, TField>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(TInstance), name) ??
                throw new MissingFieldException(typeof(TInstance).FullName, name);
            return AccessTools.FieldRefAccess<TInstance, TField>(field);
        }

        private void Apply(in WeatherSnapshot snapshot)
        {
            // The layer going away without a scene event is the canary for a dead binding; latch
            // off until the next scene rather than writing through destroyed objects.
            if (layer == null)
            {
                ResetForScene();
                return;
            }

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (level == null || cameras == null) return;

            Vector3 position = cameras.transform.position;
            float conditions = Mathf.Clamp01(level.conditions);
            float dark = Darkness(in snapshot, position);
            // The veer is measured from the sky the scene opened with, so the deck starts exactly
            // where vanilla put it.
            if (!touched) baseWindHeading = snapshot.Live.WindHeading;

            WriteScatter(level, position, dark);
            WriteDeck(dark);
            WriteFeather(conditions);
            WriteShear(in snapshot);
            WriteThickness(in snapshot, level);
            WriteAdvection(in snapshot);
            WriteBand(in snapshot, conditions);
            WriteFog(in snapshot, position);
            touched = true;
        }

        /// <summary>
        /// The storm cue that replaces the deleted towers: how much of the deck belongs to a
        /// storm, 0..1, from the cell under the reader, the cell the weather is carrying in and
        /// the front's own activity. Smoothed again here because both inputs are already smooth
        /// but their maximum can crease where they cross.
        /// </summary>
        private float Darkness(in WeatherSnapshot snapshot, Vector3 position)
        {
            float x = position.x;
            float z = position.z;
            float dark = WeatherRegimes.Clamp01(snapshot.CellInfluence);

            // The same cell buffer the rain, the radar and the HUD read, sampled ahead along the
            // direction the weather travels: a front moves along its normal, and in a uniform air
            // mass the cells advect with the synced mean wind.
            float headingX;
            float headingZ;
            if (snapshot.Front.Present)
            {
                headingX = snapshot.Front.NormalX;
                headingZ = snapshot.Front.NormalZ;
            }
            else
            {
                float radians = snapshot.Live.WindHeading * Mathf.Deg2Rad;
                headingX = Mathf.Sin(radians);
                headingZ = Mathf.Cos(radians);
            }

            StormField.StrongestAt(
                manager.CellBuffer, manager.CellCount,
                x + headingX * LookAheadMeters, z + headingZ * LookAheadMeters, out float ahead);
            if (ahead > dark) dark = ahead;

            if (snapshot.Front.Present)
            {
                float frontal = snapshot.Front.InfluenceAt(x, z);
                if (frontal > dark) dark = frontal;
            }

            return dark * dark * (3f - 2f * dark);
        }

        /// <summary>
        /// The lighting the deck scatters, darkened and desaturated under a storm. Vanilla writes
        /// this same value once a second; this reproduces its inputs and lands after it, so the
        /// base is the game's own current sun and daylight rather than a cached colour.
        /// </summary>
        private void WriteScatter(LevelInfo level, Vector3 position, float dark)
        {
            if (nearMaterial == null) return;
            Color scatter = level.GetSunColor() * level.GetDaylightFactor(position);
            float grey = 0.3f * scatter.r + 0.59f * scatter.g + 0.11f * scatter.b;
            Color weathered = Color.Lerp(scatter, new Color(grey, grey, grey, scatter.a), dark * StormDesaturation);
            float light = Mathf.Lerp(1f, StormLightFloor, dark);
            weathered.r *= light;
            weathered.g *= light;
            weathered.b *= light;

            nearMaterial.SetColor(ScatterColorId, weathered);
            if (distantMaterial != null) distantMaterial.SetColor(ScatterColorId, weathered);
            if (flyMaterial != null) flyMaterial.SetColor(ScatterColorId, weathered);
        }

        /// <summary>
        /// The masked deck plane has no scatter colour; its brightness is the emissive term, and
        /// CloudLayer never writes it after load, so this is uncontested between the two of us.
        /// </summary>
        private void WriteDeck(float dark)
        {
            if (deckMaterial == null || baseDeckEmissive <= 0f) return;
            deckMaterial.SetFloat(CloudEmissiveId, baseDeckEmissive * Mathf.Lerp(1f, DeckEmissiveFloor, dark));
        }

        /// <summary>
        /// Feather the discrete sets. <c>WeatherSet</c> carries one interpolatable property —
        /// <c>coverage</c> — so the neighbouring sets are blended and the near puffs' opacity is
        /// traded against the chosen set's particle count, which is what actually steps at a set
        /// boundary. The mask, cookie and particle-sampler textures are read straight from the
        /// chosen set: they are textures and cannot be interpolated at runtime, so the pattern
        /// still swaps while the density ramps. The opacity base mirrors the vanilla
        /// <c>1 - CloudDetail</c> write, which the game only repeats when the graphics setting
        /// changes, so this write does not fight it.
        /// </summary>
        private void WriteFeather(float conditions)
        {
            if (nearMaterial == null || sets == null) return;
            int count = sets.Length;
            int index = Mathf.Clamp(Mathf.FloorToInt(conditions * count), 0, count - 1);
            int next = Mathf.Min(index + 1, count - 1);
            float weight = Mathf.Clamp01(conditions * count - index);
            float coverage = WeatherState.Lerp(sets[index].coverage, sets[next].coverage, weight);

            float detail = PlayerSettings.graphics != null ? PlayerSettings.graphics.CloudDetail : 1f;
            float baseBoost = Mathf.Clamp01(1f - detail);
            nearMaterial.SetFloat(
                OpacityBoostId,
                Mathf.Clamp01(baseBoost + FeatherGain * (1f - Mathf.Clamp01(coverage))));
        }

        /// <summary>
        /// Shear and gusts tear the puff edges. The noise module is untouched by vanilla, so the
        /// only contest is with itself: it is switched on at the onset threshold and off below it,
        /// never toggled per frame, and its displacement is scaled by the deck thickness because
        /// that is the only puff-scale length this module already knows.
        /// </summary>
        private void WriteShear(in WeatherSnapshot snapshot)
        {
            float shear = WeatherRegimes.Clamp01(snapshot.Live.Turbulence * 0.6f + snapshot.Atmosphere.Shear * 0.4f);
            float gust = WeatherRegimes.Clamp01(snapshot.Atmosphere.GustSpeed / GustReference);
            float stir = WeatherRegimes.Clamp01(shear * 0.7f + gust * 0.3f);

            if (stir > NoiseOnset)
            {
                float scale = baseLayerThickness > 0f ? baseLayerThickness : 300f;
                float strength = scale * Mathf.Lerp(NoiseStrengthMin, NoiseStrengthMax, stir);
                if (!nearNoiseOn)
                {
                    nearNoise.enabled = true;
                    nearNoiseOn = true;
                }
                nearNoise.strength = new ParticleSystem.MinMaxCurve(strength);
                nearNoise.frequency = NoiseFrequency + NoiseFrequencyRise * stir;
                nearNoise.scrollSpeed = new ParticleSystem.MinMaxCurve(NoiseScrollSpeed * (0.5f + stir));
                nearNoise.damping = true;
                nearNoise.octaveCount = 2;

                if (!distantNoiseOn)
                {
                    distantNoise.enabled = true;
                    distantNoiseOn = true;
                }
                distantNoise.strength = new ParticleSystem.MinMaxCurve(strength * DistantNoiseScale);
                distantNoise.frequency = NoiseFrequency;
                distantNoise.scrollSpeed = new ParticleSystem.MinMaxCurve(NoiseScrollSpeed);
                distantNoise.damping = true;
            }
            else
            {
                DisableNoise();
            }
        }

        private void DisableNoise()
        {
            if (nearNoiseOn)
            {
                nearNoise.enabled = false;
                nearNoiseOn = false;
            }
            if (distantNoiseOn)
            {
                distantNoise.enabled = false;
                distantNoiseOn = false;
            }
        }

        /// <summary>
        /// The deck thickens as the condensation level drops. The base itself is vanilla's:
        /// <c>CloudLayer.Update</c> reads <c>LevelInfo.cloudHeight</c> into <c>layerHeight</c>
        /// every frame, so this only ever writes <c>layerThickness</c>, the vertical spread of
        /// each puff around that base, and never the base the model drives.
        /// </summary>
        private void WriteThickness(in WeatherSnapshot snapshot, LevelInfo level)
        {
            if (layer == null || baseLayerThickness <= 0f) return;
            float lcl = snapshot.Atmosphere.Available ? snapshot.Atmosphere.Lcl : level.cloudHeight;
            float low = WeatherRegimes.Clamp01(1f - lcl / LclThinAt);
            layerThicknessRef(layer) = baseLayerThickness * Mathf.Lerp(1f, DeckThicknessGain, low);
        }

        /// <summary>
        /// Advection: vanilla already integrates the wind into the deck's scroll offset, so this
        /// adds the turn — a bounded world-space yaw relative to the deck's own resting rotation,
        /// tracking how far the synced mean wind has veered since the scene loaded and weighted
        /// towards the front's travel direction while one is overhead. Working relative to the
        /// vanilla rotation keeps whatever orientation the deck plane was authored with.
        /// </summary>
        private void WriteAdvection(in WeatherSnapshot snapshot)
        {
            if (deckTransform == null) return;
            float heading = snapshot.Live.WindHeading;
            if (snapshot.Front.Present)
            {
                float travel = WeatherState.WrapHeading(
                    Mathf.Atan2(snapshot.Front.NormalX, snapshot.Front.NormalZ) * Mathf.Rad2Deg);
                heading = WeatherState.LerpAngle(
                    heading, travel, WeatherRegimes.Clamp01(snapshot.Front.Activity) * FrontYawWeight);
            }

            float target = WeatherState.WrapHeading(heading - baseWindHeading);
            advectionYaw = Mathf.MoveTowardsAngle(advectionYaw, target, YawDegreesPerSecond * Time.deltaTime);
            deckTransform.rotation = Quaternion.AngleAxis(advectionYaw, Vector3.up) * baseDeckRotation;
        }

        /// <summary>
        /// Anvil and cirrus spread, using the vanilla distant band scaled up. Vanilla sizes it
        /// from conditions once a second; the convection factor on top is what grows it as CAPE
        /// and shear build, and it collapses to vanilla when no cell is up.
        /// </summary>
        private void WriteBand(in WeatherSnapshot snapshot, float conditions)
        {
            if (layer == null) return;
            float convection = snapshot.Atmosphere.Available
                ? WeatherRegimes.Clamp01(snapshot.Atmosphere.Cape * 0.6f + snapshot.Atmosphere.Shear * 0.4f)
                : 0f;
            if (StormModes.IsOrganised(snapshot.StormMode) && convection < AnvilOrganisedFloor)
                convection = AnvilOrganisedFloor;
            if (snapshot.CellCount <= 0 && !snapshot.Front.Present) convection = 0f;

            float sizeFactor = conditions * 1.8f;
            float spread = 1f + AnvilGain * convection;
            distantMain.startSize = new ParticleSystem.MinMaxCurve(1200f * sizeFactor * spread, 1800f * sizeFactor * spread);
        }

        /// <summary>
        /// The model's visibility thickens the vanilla fog. The fog is shared: <c>LevelInfo</c>
        /// rewrites density from the time of day and conditions once a second, and
        /// <c>CameraStateManager</c> owns it underwater, so this only ever adds on top of
        /// whatever vanilla last wrote — the previous write is divided back out first — never
        /// thins it, and stays out of the water entirely. <c>LevelInfo.UpdateFogDensity</c> is an
        /// empty stub in this build, so there is no other cloud-fog hook to feed.
        /// </summary>
        private void WriteFog(in WeatherSnapshot snapshot, Vector3 position)
        {
            if (!RenderSettings.fog || position.y < Datum.LocalSeaY)
            {
                lastFogWeight = 1f;
                return;
            }
            if (!snapshot.Atmosphere.Available) return;

            float visibility = Mathf.Max(snapshot.Atmosphere.Visibility, MinFogVisibility);
            float current = RenderSettings.fogDensity;
            float vanilla = lastFogWeight > 0.01f ? current / lastFogWeight : current;
            if (float.IsNaN(vanilla) || vanilla < 0f) vanilla = current;

            float wanted = Mathf.Max(vanilla, FogExtinction / visibility);
            RenderSettings.fogDensity = wanted;
            lastFogWeight = vanilla > 1e-7f ? wanted / vanilla : 1f;
        }

        /// <summary>
        /// Hand the uncontested values back. Contested ones are deliberately not touched: vanilla
        /// rewrites the scatter colour, the band size and the fog within a second of its next
        /// tick, and writing them twice would be a second copy of vanilla's arithmetic to keep in
        /// sync with a game update.
        /// </summary>
        private void Restore()
        {
            if (!touched) return;
            if (deckMaterial != null && baseDeckEmissive > 0f)
                deckMaterial.SetFloat(CloudEmissiveId, baseDeckEmissive);
            if (layer != null && baseLayerThickness > 0f)
                layerThicknessRef(layer) = baseLayerThickness;
            if (deckTransform != null)
                deckTransform.rotation = baseDeckRotation;
            if (layer != null) DisableNoise();
            if (nearMaterial != null)
            {
                float detail = PlayerSettings.graphics != null ? PlayerSettings.graphics.CloudDetail : 1f;
                nearMaterial.SetFloat(OpacityBoostId, Mathf.Clamp01(1f - detail));
            }
            advectionYaw = 0f;
            touched = false;
        }

        private void Warn(string reason)
        {
            if (warned || attempts < MaxAttempts) return;
            warned = true;
            log?.LogWarning("Weather clouds unavailable: " + reason + ".");
        }
    }
}

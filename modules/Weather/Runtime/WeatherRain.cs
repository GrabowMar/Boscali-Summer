using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The rain front's presentation: rain locked to the canopy glass, falling rain, and the
    /// weather audio. One MonoBehaviour owns all three, reads the snapshot once per 10 Hz tick and
    /// hands the scalars down, so no part ever queries the manager itself.
    ///
    /// <para>Rain on the glass is <see cref="GlassRain"/>: it draws on the real cockpit panes and
    /// refracts the world through the droplets. <see cref="CanopyRainOverlay"/> is the fallback for
    /// when there is no glass to lock to — an observer, a parachute, an unrecognised cockpit — and
    /// it is torn down the moment the glass layer takes over, so the two can never compete for the
    /// canvas.</para>
    ///
    /// <para>Client-local and cosmetic: nothing here is networked, patched or authoritative. The
    /// three gates (<c>RainEffects</c>, <c>RainOnCanopy</c>, <c>RainAudio</c>) and
    /// <c>RainEffectDensity</c> switch parts on and off, and a cleared sky tears a part down rather
    /// than idling it. Everything created here is destroyed by <see cref="ResetForScene"/>, which
    /// is idempotent and safe to call from a scene load, a disable or a destroy.</para>
    /// </summary>
    internal sealed class WeatherRain : MonoBehaviour, ISceneService
    {
        /// <summary>The snapshot is read at 10 Hz; the drops and the overlay advance every frame.</summary>
        private const float TickInterval = 0.1f;

        /// <summary>Standing seed for this cockpit's droplet pattern; local, cosmetic, fixed.</summary>
        private const int DropletSeed = 0x5241494E;

        /// <summary>Below this the mean wind is a breeze, not storm airflow worth hearing.</summary>
        private const float WindThreshold = 2f;

        private readonly RainSystem particles = new RainSystem();
        private readonly GlassRain glass = new GlassRain(DropletSeed);
        private readonly CanopyRainOverlay canopy = new CanopyRainOverlay();
        private readonly RainAudio audio = new RainAudio();

        private WeatherSettings settings;
        private WeatherManager manager;
        private ManualLogSource log;

        private float nextTick;
        private bool materialWarned;
        private bool audioWarned;
        private bool overlayAnnounced;
        private Aircraft lastAircraft;
        private bool canopySeen;

        public bool ParticlesActive => particles.Active;

        /// <summary>The glass layer is locked to the canopy and built.</summary>
        public bool GlassActive => glass.Created;

        /// <summary>The screen-space fallback is drawing because there is no glass to lock to.</summary>
        public bool OverlayActive => canopy.Active;

        public bool AudioActive => audio.Active;

        /// <summary>The snapshot's precipitation at the reader, as read on the last tick.</summary>
        public float RainIntensity { get; private set; }

        public void Configure(WeatherSettings weatherSettings, WeatherManager weatherManager, ManualLogSource logger)
        {
            settings = weatherSettings;
            manager = weatherManager;
            log = logger;
        }

        public void ResetForScene()
        {
            particles.Teardown();
            glass.Reset();
            canopy.Teardown();
            audio.Teardown();
            materialWarned = false;
            audioWarned = false;
            overlayAnnounced = false;
            lastAircraft = null;
            canopySeen = false;
            RainIntensity = 0f;
            nextTick = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            // A headless host has no camera and no listener; never build presentation there.
            if (Application.isBatchMode) return;
            if (settings == null || manager == null || !settings.Enabled.Value)
            {
                ResetForScene();
                return;
            }
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + TickInterval;
                Refresh();
            }
            glass.Advance(Time.unscaledDeltaTime);
            canopy.Advance(Time.unscaledDeltaTime);
        }

        private void Refresh()
        {
            WeatherSnapshot snapshot = manager.Snapshot;
            bool available = snapshot.Available;
            float density = Mathf.Clamp01(settings.RainEffectDensity.Value);
            float intensity = available ? snapshot.RainIntensity : 0f;
            PrecipitationKind kind = available ? snapshot.Precipitation : PrecipitationKind.None;
            RainIntensity = intensity;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Vector3 cameraVelocity = cameras != null ? cameras.cameraVelocity : Vector3.zero;
            Vector3 cameraPosition = cameras != null ? cameras.transform.position : Vector3.zero;
            float altitude = cameraPosition.y - Datum.LocalSeaY;
            bool cockpit = cameras != null && cameras.currentState == cameras.cockpitState;
            GameManager.GetLocalAircraft(out Aircraft local);
            bool canopyGone = CanopyGone(local);
            float airspeed = Airspeed(local);
            // The synced mean wind the module already read for the panel: a client never samples
            // the per-position wind, and the glass only needs the sideways component of this.
            Vector3 wind = new Vector3(snapshot.LocalWindX, snapshot.LocalWindY, snapshot.LocalWindZ);

            if (!settings.RainEffects.Value || density <= 0f) particles.Teardown();
            else if (particles.Created || intensity >= RainSystem.MinIntensity)
            {
                if (particles.TryCreate())
                    particles.Tick(intensity, density, altitude, wind, cameraVelocity, cameraPosition);
                else WarnMissingMaterial();
            }

            // The glass is the effect; the overlay is what is left when there is no glass to lock
            // to. A cleared sky or a failed build tears the glass down, a head outside the cockpit
            // only hides it, and the moment it is built the overlay goes so the two cannot fight
            // over canvas sorting.
            bool canopyWanted = settings.RainOnCanopy.Value && density > 0f &&
                                intensity >= RainSystem.MinIntensity;
            if (!canopyWanted) glass.Release();
            else if (glass.Created || glass.TryCreate(log))
                glass.Apply(intensity, density, kind, cockpit && !canopyGone, altitude, airspeed, wind);

            if (!settings.RainOnCanopy.Value || density <= 0f || glass.Created)
            {
                canopy.Teardown();
            }
            else if (canopy.Created || intensity >= RainSystem.MinIntensity)
            {
                if (!overlayAnnounced && cockpit && !canopyGone && intensity >= RainSystem.MinIntensity)
                {
                    overlayAnnounced = true;
                    log?.LogInfo("Weather: no cockpit glass resolved for canopy rain; drawing the " +
                                 "screen-space overlay instead.");
                }
                if (canopy.TryCreate())
                    canopy.Apply(intensity, density, cockpit, canopyGone, altitude, airspeed + snapshot.Live.WindSpeed);
            }

            // Audio is not throttled by the effect-density knob: that is a presentation budget
            // for particles and glass, not a volume control.
            bool windWorthHearing = available && snapshot.Live.WindSpeed >= WindThreshold;
            if (!settings.RainAudio.Value) audio.Teardown();
            else if (audio.Created || intensity >= RainSystem.MinIntensity || windWorthHearing)
            {
                if (audio.TryCreate(transform))
                    audio.Tick(intensity, kind, airspeed, snapshot.Live.WindSpeed, snapshot.Live.Turbulence);
                else WarnAudio();
            }
        }

        /// <summary>
        /// The aircraft's own speed. The cockpit body first, then the airframe, and only then the
        /// camera — an unreadable one degrades to no speed scaling rather than throwing.
        /// </summary>
        private static float Airspeed(Aircraft local)
        {
            if (local != null)
            {
                // CockpitRB dereferences the cockpit part, which a non-flying aircraft need not have.
                Rigidbody body = local.cockpit != null ? local.CockpitRB() : null;
                if (body == null) body = local.rb;
                if (body != null) return body.velocity.magnitude;
            }
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return 0f;
            Vector3 velocity = cameras.cameraVelocity;
            return float.IsNaN(velocity.x) ? 0f : velocity.magnitude;
        }

        /// <summary>
        /// Vanilla exposes no public jettison flag, so the glass is tracked by identity: an
        /// ejection unparents the Canopy, and a canopy this scene has seen before and cannot
        /// find now is gone. Never having resolved one keeps the overlay on rather than
        /// silently disabling the feature on an aircraft layout we do not recognise.
        ///
        /// <para>A different aircraft is a different cockpit, so the glass layer is released and
        /// allowed to resolve again — that is also the only place the build retries are reset.</para>
        /// </summary>
        private bool CanopyGone(Aircraft local)
        {
            if (local != lastAircraft)
            {
                lastAircraft = local;
                canopySeen = false;
                glass.Reset();
                overlayAnnounced = false;
            }
            if (local == null) return false;
            if (local.GetComponentInChildren<Canopy>() != null)
            {
                canopySeen = true;
                return false;
            }
            return canopySeen;
        }

        private void WarnMissingMaterial()
        {
            if (materialWarned) return;
            materialWarned = true;
            log?.LogWarning(
                "Weather: no vanilla particle material is available for rain; falling rain is " +
                "disabled until a scene resolves GameAssets.contactSmoke.");
        }

        private void WarnAudio()
        {
            if (audioWarned || !audio.Failed) return;
            audioWarned = true;
            log?.LogWarning("Weather: rain audio synthesis failed; weather audio is disabled.");
        }
    }
}

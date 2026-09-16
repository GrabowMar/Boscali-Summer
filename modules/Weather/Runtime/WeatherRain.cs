using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The rain front's presentation: falling rain, rain on the canopy glass and the weather
    /// audio. One MonoBehaviour owns all three, reads the snapshot once per 10 Hz tick and hands
    /// the scalars down, so no part ever queries the manager itself.
    ///
    /// Client-local and cosmetic: nothing here is networked, patched or authoritative. The three
    /// gates (<c>RainEffects</c>, <c>RainOnCanopy</c>, <c>RainAudio</c>) and
    /// <c>RainEffectDensity</c> switch parts on and off, and a cleared sky tears a part down
    /// rather than idling it. Everything created here is destroyed by <see cref="ResetForScene"/>,
    /// which is idempotent and safe to call from a scene load, a disable or a destroy.
    /// </summary>
    internal sealed class WeatherRain : MonoBehaviour, ISceneService
    {
        /// <summary>The snapshot is read at 10 Hz; the overlay scroll advances every frame.</summary>
        private const float TickInterval = 0.1f;

        /// <summary>Clamp for the mean wind used as rain tilt; above this the tilt is already flat.</summary>
        private const float MaxWindSpeed = 40f;

        /// <summary>Below this the mean wind is a breeze, not airflow worth a second noise bed.</summary>
        private const float WindThreshold = 2f;

        private readonly RainSystem particles = new RainSystem();
        private readonly CanopyRainOverlay canopy = new CanopyRainOverlay();
        private readonly RainAudio audio = new RainAudio();

        private WeatherSettings settings;
        private WeatherManager manager;
        private ManualLogSource log;

        private float nextTick;
        private bool materialWarned;
        private bool audioWarned;
        private Aircraft lastAircraft;
        private bool canopySeen;

        public bool ParticlesActive => particles.Active;

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
            canopy.Teardown();
            audio.Teardown();
            materialWarned = false;
            audioWarned = false;
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
            canopy.Advance(Time.unscaledDeltaTime);
        }

        private void Refresh()
        {
            WeatherSnapshot snapshot = manager.Snapshot;
            float density = Mathf.Clamp01(settings.RainEffectDensity.Value);
            float intensity = snapshot.Available ? snapshot.RainIntensity : 0f;
            RainIntensity = intensity;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Vector3 cameraVelocity = cameras != null ? cameras.cameraVelocity : Vector3.zero;
            Vector3 cameraPosition = cameras != null ? cameras.transform.position : Vector3.zero;
            float altitude = cameraPosition.y - Datum.LocalSeaY;
            bool cockpit = cameras != null && cameras.currentState == cameras.cockpitState;
            Vector3 wind = WindVector(snapshot);
            float airspeed = cameraVelocity.magnitude;

            if (!settings.RainEffects.Value || density <= 0f) particles.Teardown();
            else if (particles.Created || intensity >= RainSystem.MinIntensity)
            {
                if (particles.TryCreate())
                    particles.Tick(intensity, density, altitude, wind, cameraVelocity, cameraPosition);
                else WarnMissingMaterial();
            }

            if (!settings.RainOnCanopy.Value || density <= 0f) canopy.Teardown();
            else if (canopy.Created || intensity >= RainSystem.MinIntensity)
            {
                if (canopy.TryCreate())
                    canopy.Apply(intensity, density, cockpit, CanopyGone(), altitude, airspeed + snapshot.Live.WindSpeed);
            }

            // Audio is not throttled by the effect-density knob: that is a presentation budget
            // for particles and glass, not a volume control.
            bool windWorthHearing = snapshot.Available && snapshot.Live.WindSpeed >= WindThreshold;
            if (!settings.RainAudio.Value) audio.Teardown();
            else if (audio.Created || intensity >= RainSystem.MinIntensity || windWorthHearing)
            {
                if (audio.TryCreate(transform))
                    audio.Tick(intensity, airspeed, snapshot.Live.WindSpeed, snapshot.Live.Turbulence);
                else WarnAudio();
            }
        }

        /// <summary>
        /// The synced mean wind as a world vector — the module rule for anything a client can
        /// see, because only the server rotates the vanilla wind zone.
        /// </summary>
        private static Vector3 WindVector(WeatherSnapshot snapshot)
        {
            float speed = Mathf.Clamp(snapshot.Live.WindSpeed, 0f, MaxWindSpeed);
            float radians = snapshot.Live.WindHeading * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * speed;
        }

        /// <summary>
        /// Vanilla exposes no public jettison flag, so the glass is tracked by identity: an
        /// ejection unparents the Canopy, and a canopy this scene has seen before and cannot
        /// find now is gone. Never having resolved one keeps the overlay on rather than
        /// silently disabling the feature on an aircraft layout we do not recognise.
        /// </summary>
        private bool CanopyGone()
        {
            GameManager.GetLocalAircraft(out Aircraft local);
            if (local != lastAircraft)
            {
                lastAircraft = local;
                canopySeen = false;
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

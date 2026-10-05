using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Camera-relative procedural rain emitter for combat flight simulators.
    /// Orientates along apparent relative wind velocity and uses stretched billboards.
    /// 100% C# code; requires 0 external asset files.
    /// </summary>
    internal sealed class ProceduralRainEmitter : MonoBehaviour, IClientEffect
    {
        private const float RainTerminalVelocity = 9.0f; // m/s

        private ParticleSystem ps;
        private ParticleSystemRenderer psRenderer;
        private Material rainMaterial;
        private Texture2D streakTexture;
        private Camera targetCamera;
        private Transform simulationFrame;
        private Vector3 previousCameraPosition;
        private bool positioned;
        private Color fogTint = new Color(0.62f, 0.66f, 0.72f, 1f);

        public string EffectId => "streaks";

        public FxBudget Budget => new FxBudget(0, 1, 0, true);

        public void ReleaseFx()
        {
            if (ps != null) Destroy(ps);
            ps = null;
        }

        public void DescribeFx(System.Collections.Generic.IDictionary<string, object> state)
        {
            state["fx.streaks.alive"] = ps != null ? ps.particleCount : 0;
        }

        /// <summary>Live diagnostics for the automation readout (sim report).</summary>
        internal int AliveParticles => ps != null ? ps.particleCount : -1;
        internal bool Playing => ps != null && ps.isPlaying;
        internal float EmissionNow => ps != null ? ps.emission.rateOverTime.constant : -1f;
        internal float ApparentSpeedNow { get; private set; }
        internal string ShaderNow =>
            rainMaterial != null && rainMaterial.shader != null ? rainMaterial.shader.name : "none";

        public void Initialize(Camera cam)
        {
            if (!FxBus.Register(this)) return;
            targetCamera = cam;
            streakTexture = RainStreakMaterial.CreateTexture();
            rainMaterial = RainStreakMaterial.CreateMaterial(streakTexture);
            if (rainMaterial == null) return;

            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            psRenderer = gameObject.GetComponent<ParticleSystemRenderer>();

            // Stretched billboard aligned to relative velocity: thin, and longer with speed.
            psRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            psRenderer.cameraVelocityScale = 0.0f;
            // Short photographic exposure: a moving drop reads as a streak, not a spark.
            psRenderer.velocityScale = 1f / 120f;
            psRenderer.lengthScale = 2f;
            psRenderer.sharedMaterial = rainMaterial;
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            // Screen-space clamp: drops passing the lens can never balloon into view-filling blobs.
            psRenderer.maxParticleSize = 0.025f;

            // Main module
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = RainVisualMath.MaxParticles;
            // Relative velocity belongs in a translating camera frame. In world simulation
            // subtracting ownship speed here counted the camera's travel a second time.
            var frame = new GameObject("RainSimulationFrame");
            frame.transform.SetParent(transform.parent, false);
            simulationFrame = frame.transform;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = simulationFrame;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = new Color(0.72f, 0.80f, 0.92f, 0.30f);
            // Camera-relative motion changes for every live drop during an orbit or turn,
            // rather than only for newly emitted drops. Native simulation owns the update.
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            // Emission box shape: upstream plane
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(18f, 14f, 0.2f);

            // Fade in and out over particle lifetime
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = grad;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
        }

        /// <summary>Fog colour the streaks are tinted from, so they sit inside the haze.</summary>
        public void SetTint(Color fog)
        {
            fogTint = fog;
        }

        public void UpdateRain(
            Vector3 aircraftVelocity, Vector3 worldWind, float rainIntensity, Camera currentCam,
            float density = 1f, float gust = 1f, float lightLevel = 1f)
        {
            if (currentCam != targetCamera) { positioned = false; if (ps != null) ps.Clear(); }
            if (currentCam != null) targetCamera = currentCam;
            if (targetCamera == null || ps == null) return;

            Vector3 position = targetCamera.transform.position;
            Vector3 travel = position - previousCameraPosition;
            // Render-space snaps shorter than a rebase would spike the velocity; resync instead.
            float snapLimit = Mathf.Max(ApparentSpeedNow, 6f) * Mathf.Max(Time.deltaTime, 0.001f) * 4f + 3f;
            bool snapped = positioned && travel.sqrMagnitude > snapLimit * snapLimit;
            if (positioned && !snapped && travel.sqrMagnitude < 10000f && Time.deltaTime > 0f)
                aircraftVelocity = travel / Time.deltaTime;
            else if (positioned) ps.Clear(); // teleport / floating-origin relocation
            simulationFrame.position = position;
            psRenderer.bounds = new Bounds(position, Vector3.one * 120f);
            simulationFrame.rotation = Quaternion.identity;
            previousCameraPosition = position;
            positioned = true;

            // Calculate apparent relative wind velocity: V_rel = (V_wind - 9j) - V_aircraft
            Vector3 rainWorldVelocity = worldWind - (Vector3.up * RainTerminalVelocity);
            Vector3 apparentVelocity = rainWorldVelocity - aircraftVelocity;
            float apparentSpeed = apparentVelocity.magnitude;
            ApparentSpeedNow = apparentSpeed;
            var velocity = ps.velocityOverLifetime;
            velocity.x = apparentVelocity.x;
            velocity.y = apparentVelocity.y;
            velocity.z = apparentVelocity.z;
            // Drops already in flight retain correct camera motion while a shower ends.
            if (rainIntensity <= 0.02f)
            {
                if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            if (!ps.isPlaying) ps.Play();
            // Hovering in matching wind collapses V_rel; fall straight down instead of
            // feeding LookRotation a degenerate vector.
            Vector3 streamDir = apparentSpeed > 0.5f ? apparentVelocity / apparentSpeed : Vector3.down;

            // Position upstream of camera along incoming wind vector so aircraft never outruns the box
            float boxLength = Mathf.Clamp(16f + apparentSpeed * 0.09f, 16f, 45f);
            float leadDistance = boxLength * 0.45f;

            // Bias the bounded volume toward the view without changing upstream travel distance.
            Vector3 viewBias = Vector3.ProjectOnPlane(targetCamera.transform.forward, streamDir) * 7f;
            transform.position = targetCamera.transform.position - (streamDir * leadDistance) + viewBias;
            Vector3 planeUp = Vector3.ProjectOnPlane(targetCamera.transform.up, streamDir);
            if (planeUp.sqrMagnitude < 0.001f)
                planeUp = Vector3.ProjectOnPlane(targetCamera.transform.forward, streamDir);
            transform.rotation = Quaternion.LookRotation(streamDir, planeUp);
            RainVisualMath.ViewCoverage(targetCamera.fieldOfView, targetCamera.aspect, out float width, out float height);
            var shape = ps.shape;
            shape.scale = new Vector3(width, height, 0.2f);
            psRenderer.velocityScale = Mathf.Min(1f / 120f, 0.85f / Mathf.Max(1f, apparentSpeed + 1.2f));

            // Speed and lifetime matching to keep active particle count bounded
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 1.2f);
            float lifetime = boxLength / Mathf.Max(6f, apparentSpeed);
            main.startLifetime = lifetime;

            // Streaks take the fog colour so they read as part of the haze, kept under bloom.
            float alpha = RainVisualMath.StreakAlpha(rainIntensity);
            RainSkyMath.StreakColor(fogTint.r, fogTint.g, fogTint.b, Mathf.Clamp(lightLevel, 0.04f, 1f),
                out float r, out float g, out float b);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(r, g, b, alpha * 0.75f), new Color(r, g, b, alpha));

            // Emission rate preserves spatial particle density across speeds, clamped so
            // density and gusts can never overflow the particle budget.
            var emission = ps.emission;
            float rate = RainVisualMath.EmissionRate(apparentSpeed, rainIntensity, density, gust);
            int budget = Mathf.Max(64, Mathf.RoundToInt(
                RainVisualMath.MaxParticles * FxBus.Scales.Particles));
            emission.rateOverTime = RainVisualMath.ClampRateToBudget(rate, lifetime, budget);
        }

        private void OnDestroy()
        {
            FxBus.Unregister(this);
            if (simulationFrame != null) Destroy(simulationFrame.gameObject);
            if (rainMaterial != null) Destroy(rainMaterial);
            if (streakTexture != null) Destroy(streakTexture);
        }
    }
}

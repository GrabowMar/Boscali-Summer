using BoscaliSummer.Features.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>
    /// Camera-relative procedural rain emitter for combat flight simulators.
    /// Orientates along apparent relative wind velocity and uses stretched billboards.
    /// 100% C# code; requires 0 external asset files.
    /// </summary>
    internal sealed class ProceduralRainEmitter : MonoBehaviour
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

        public void Initialize(Camera cam)
        {
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
            psRenderer.velocityScale = 0.009f;
            psRenderer.lengthScale = 1.6f;
            psRenderer.sharedMaterial = rainMaterial;
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            // Screen-space clamp: drops passing the lens can never balloon into view-filling blobs.
            psRenderer.maxParticleSize = 0.10f;

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
            main.startSize = 0.032f;
            main.startColor = new Color(0.72f, 0.80f, 0.92f, 0.30f);

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

        /// <summary>True while the system is alive; the owner may tear down once false.</summary>
        public bool IsEmitting => ps != null && ps.IsAlive();

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
            if (positioned && travel.sqrMagnitude < 10000f && Time.deltaTime > 0f)
                aircraftVelocity = travel / Time.deltaTime;
            else if (positioned) ps.Clear(); // teleport / floating-origin relocation
            simulationFrame.position = position;
            simulationFrame.rotation = Quaternion.identity;
            previousCameraPosition = position;
            positioned = true;

            if (rainIntensity <= 0.02f)
            {
                if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            if (!ps.isPlaying) ps.Play();

            // Calculate apparent relative wind velocity: V_rel = (V_wind - 9j) - V_aircraft
            Vector3 rainWorldVelocity = worldWind - (Vector3.up * RainTerminalVelocity);
            Vector3 apparentVelocity = rainWorldVelocity - aircraftVelocity;
            float apparentSpeed = apparentVelocity.magnitude;
            // Hovering in matching wind collapses V_rel; fall straight down instead of
            // feeding LookRotation a degenerate vector.
            Vector3 streamDir = apparentSpeed > 0.5f ? apparentVelocity / apparentSpeed : Vector3.down;

            // Position upstream of camera along incoming wind vector so aircraft never outruns the box
            float boxLength = Mathf.Clamp(16f + apparentSpeed * 0.09f, 16f, 45f);
            float leadDistance = boxLength * 0.45f;

            transform.position = targetCamera.transform.position - (streamDir * leadDistance);
            transform.rotation = Quaternion.LookRotation(streamDir);

            // Speed and lifetime matching to keep active particle count bounded
            var main = ps.main;
            main.startSpeed = apparentSpeed;
            float lifetime = boxLength / Mathf.Max(6f, apparentSpeed);
            main.startLifetime = lifetime;

            // Streaks take the fog colour so they read as part of the haze, kept under bloom.
            float alpha = RainVisualMath.StreakAlpha(rainIntensity);
            RainSkyMath.StreakColor(fogTint.r, fogTint.g, fogTint.b, Mathf.Clamp(lightLevel, 0.04f, 1f),
                out float r, out float g, out float b);
            main.startColor = new Color(r, g, b, alpha);

            // Emission rate preserves spatial particle density across speeds, clamped so
            // density and gusts can never overflow the particle budget.
            var emission = ps.emission;
            float rate = RainVisualMath.EmissionRate(apparentSpeed, rainIntensity, density, gust);
            emission.rateOverTime = RainVisualMath.ClampRateToBudget(rate, lifetime, RainVisualMath.MaxParticles);
        }

        private void OnDestroy()
        {
            if (simulationFrame != null) Destroy(simulationFrame.gameObject);
            if (rainMaterial != null) Destroy(rainMaterial);
            if (streakTexture != null) Destroy(streakTexture);
        }
    }
}

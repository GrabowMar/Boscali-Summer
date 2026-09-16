using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Falling rain around the aircraft: one stretched-billboard <see cref="ParticleSystem"/> in
    /// a box around the camera, simulated in Datum space so a fast jet flies through the volume
    /// instead of dragging it along.
    ///
    /// The material is borrowed read-only from the live vanilla smoke prefab — no shader, no
    /// bundle, nothing vanilla mutated or instantiated. The emitter is bounded so it can never
    /// ask for more than <see cref="MaxParticles"/>: 520 particles a second over a 2.4 s
    /// lifetime tops out near 1250. Below <see cref="MinIntensity"/> or above the icing ceiling
    /// the whole system is off — no emission, no renderer, nothing to simulate.
    /// </summary>
    internal sealed class RainSystem
    {
        /// <summary>Hard ceiling; Unity drops emission past it, so the rate stays well under it.</summary>
        private const int MaxParticles = 2000;

        /// <summary>Below this the shower is not worth a system; the owner tears it down.</summary>
        public const float MinIntensity = 0.02f;

        private const float VolumeRadius = 260f;
        private const float VolumeHeight = 320f;
        private const float LifetimeSeconds = 2.4f;

        /// <summary>
        /// Full-intensity emission. Ceiling is MaxParticles / LifetimeSeconds (~830/s); 520/s
        /// leaves headroom for the tail of a shower that is already fading out.
        /// </summary>
        private const float MaxEmissionRate = 520f;

        private const float MinStartSize = 0.22f;
        private const float MaxStartSize = 0.55f;
        private const float FallSpeedMin = 16f;
        private const float FallSpeedMax = 42f;

        /// <summary>Clamped so a teleport or a stale velocity cannot smear one particle across the sky.</summary>
        private const float MaxRelativeSpeed = 400f;

        /// <summary>Streak tuning: lengthScale stretches along motion, velocityScale turns speed into length.</summary>
        private const float StreakLengthScale = 2.2f;
        private const float StreakVelocityScale = 0.025f;
        private const float StreakOpacity = 0.5f;

        /// <summary>Above this it is ice, not rain: the volume fades out over the taper.</summary>
        private const float IceCeilingMetres = 12000f;
        private const float IceTaperMetres = 1800f;

        private ParticleSystem system;
        private ParticleSystemRenderer renderer;
        private ParticleSystem.MainModule main;
        private ParticleSystem.EmissionModule emission;
        private ParticleSystem.ShapeModule shape;
        private ParticleSystem.VelocityOverLifetimeModule velocity;
        private bool emitting;

        public bool Active => emitting;

        public bool Created => system != null;

        /// <summary>
        /// Build the emitter once. False when the vanilla material or the Datum has not resolved
        /// yet, which the owner retries on its next tick rather than failing the feature.
        /// </summary>
        public bool TryCreate()
        {
            if (system != null) return true;
            if (Datum.origin == null) return false;
            Material material = VanillaParticleMaterial();
            if (material == null) return false;

            var host = new GameObject("BoscaliRainParticles", typeof(ParticleSystem));
            host.transform.SetParent(Datum.origin, false);
            system = host.GetComponent<ParticleSystem>();
            renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = StreakLengthScale;
            renderer.velocityScale = StreakVelocityScale;
            renderer.freeformStretching = true;
            renderer.rotateWithStretchDirection = true;
            renderer.sharedMaterial = material;

            main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = LifetimeSeconds;
            // Start speed stays zero: a Box shape emits along +Z, and all motion belongs in
            // velocityOverLifetime, which is the only place the relative wind is expressible.
            main.startSpeed = 0f;
            main.startSize = MinStartSize;
            main.startColor = new Color(0.78f, 0.84f, 0.92f, StreakOpacity);
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;

            emission = system.emission;
            emission.rateOverTime = 0f;

            shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // A Box emits from its whole volume unless an Edge/Shell mode is selected.
            shape.box = new Vector3(VolumeRadius * 2f, VolumeHeight, VolumeRadius * 2f);

            velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            host.SetActive(false);
            return true;
        }

        /// <summary>
        /// One 10 Hz update from scalars the owner already read: intensity and density gate the
        /// emission, altitude fades it out, and (wind - camera velocity) is the velocity rain
        /// actually has relative to the aircraft, which is what the streaks must point along.
        /// </summary>
        public void Tick(float intensity, float density, float altitude,
                         Vector3 windVelocity, Vector3 cameraVelocity, Vector3 position)
        {
            if (system == null) return;
            float effective = Mathf.Clamp01(intensity) * Mathf.Clamp01(density) * AltitudeFade(altitude);
            if (effective < MinIntensity)
            {
                SetEmitting(false);
                return;
            }

            system.transform.position = position;

            Vector3 relative = windVelocity - cameraVelocity;
            float fallSpeed = Mathf.Lerp(FallSpeedMin, FallSpeedMax, Mathf.Clamp01(intensity));
            velocity.x = Mathf.Clamp(relative.x, -MaxRelativeSpeed, MaxRelativeSpeed);
            velocity.y = Mathf.Clamp(relative.y, -MaxRelativeSpeed, MaxRelativeSpeed) - fallSpeed;
            velocity.z = Mathf.Clamp(relative.z, -MaxRelativeSpeed, MaxRelativeSpeed);
            emission.rateOverTime = MaxEmissionRate * effective;
            main.startSize = Mathf.Lerp(MinStartSize, MaxStartSize, effective);
            SetEmitting(true);
        }

        public void Teardown()
        {
            emitting = false;
            if (system != null) Object.Destroy(system.gameObject);
            system = null;
            renderer = null;
        }

        private void SetEmitting(bool on)
        {
            if (emitting == on) return;
            emitting = on;
            if (system == null) return;
            if (on)
            {
                system.gameObject.SetActive(true);
                system.Play();
            }
            else
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.gameObject.SetActive(false);
            }
        }

        private static float AltitudeFade(float altitude) =>
            Mathf.Clamp01((IceCeilingMetres - altitude) / IceTaperMetres);

        private static Material VanillaParticleMaterial()
        {
            GameAssets assets = GameAssets.i;
            GameObject prefab = assets != null ? assets.contactSmoke : null;
            if (prefab == null) return null;
            ParticleSystemRenderer source = prefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            return source != null ? source.sharedMaterial : null;
        }
    }
}

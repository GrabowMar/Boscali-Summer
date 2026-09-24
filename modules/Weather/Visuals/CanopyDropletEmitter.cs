using BoscaliSummer.Features.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>
    /// Droplets adhered to the canopy glass: a small local-space particle system that
    /// samples a verified glass submesh. Used only when the shader is unavailable;
    /// unreadable native geometry fails closed rather than emitting a box in the cockpit.
    /// </summary>
    internal sealed class CanopyDropletEmitter : MonoBehaviour
    {
        internal const int MaxDroplets = 256;
        private const float MaxEmission = 140f;

        private ParticleSystem ps;
        private ParticleSystemRenderer psRenderer;
        private Material dropMaterial;
        private Texture2D streakTexture;

        public void Initialize()
        {
            streakTexture = RainStreakMaterial.CreateTexture();
            dropMaterial = RainStreakMaterial.CreateMaterial(streakTexture);
            if (dropMaterial == null) return;

            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            psRenderer = gameObject.GetComponent<ParticleSystemRenderer>();

            psRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            psRenderer.cameraVelocityScale = 0.0f;
            psRenderer.velocityScale = 0.01f;
            psRenderer.lengthScale = 1.0f;
            psRenderer.sharedMaterial = dropMaterial;
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            psRenderer.maxParticleSize = 0.05f;

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaxDroplets;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSize = 0.02f;
            main.startColor = new Color(0.78f, 0.86f, 0.95f, 0.30f);
            main.gravityModifier = 0f;

            // Explicit local-space drift: never depend on shape emission-direction defaults.
            var drift = ps.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.Local;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.1f, 0.7f, 0.05f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = grad;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
        }

        /// <summary>True while the system is alive; the owner may tear down once false.</summary>
        public bool IsEmitting => ps != null && ps.IsAlive();

        public void UpdateDrops(CanopySurface surface, float rainIntensity, float airspeedMs)
        {
            if (ps == null) return;
            Renderer glass = surface.Renderer;
            if (glass != null) gameObject.layer = glass.gameObject.layer;
            // Unity mesh particle emission requires CPU-readable geometry. No box-shaped
            // spray inside the cockpit when native glass cannot provide a surface.
            if (glass == null || !glass.enabled || (surface.Lod != null && !glass.isVisible) ||
                surface.Mesh == null || !surface.Mesh.isReadable || rainIntensity <= 0.02f)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }

            if (!ps.isPlaying) ps.Play();

            // Track the glass without touching the vanilla hierarchy.
            transform.position = glass.transform.position;
            transform.rotation = glass.transform.rotation;
            transform.localScale = glass.transform.lossyScale;

            // Only the selected glass triangles; never the opaque frame or a bounds box.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = surface.Mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.useMeshMaterialIndex = true;
            shape.meshMaterialIndex = surface.Submesh;

            // Fallback beads stay on the surface; only the shader can follow curved runoff.
            var main = ps.main;
            main.startSpeed = 0f;
            main.startLifetime = 1.6f;
            var drift = ps.velocityOverLifetime;
            drift.enabled = false; // remain on the sampled surface; shader handles runoff
            float alpha = RainVisualMath.StreakAlpha(rainIntensity) * 0.9f;
            main.startColor = new Color(0.78f, 0.86f, 0.95f, alpha);

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Min(MaxEmission, MaxEmission * rainIntensity);
        }

        private void OnDestroy()
        {
            if (dropMaterial != null) Destroy(dropMaterial);
            if (streakTexture != null) Destroy(streakTexture);
        }
    }
}

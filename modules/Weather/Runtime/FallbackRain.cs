using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>
    /// Rain without the shader bundle: one bounded Shuriken box of stretched billboards on the
    /// shipped URP particle shader. Particles live in Datum space with world velocity wind + fall
    /// (never minus the camera — the camera's own motion supplies the relative movement), and
    /// the renderer stretches them by their velocity plus the camera's, which is what makes a
    /// fast pass read as a sheet. The box leads the camera along its relative velocity so its
    /// upwind face stays outside the view.
    /// </summary>
    internal sealed class FallbackRain
    {
        private const float BoxSize = 60f;
        private const int MaxParticles = 9000;
        private const float Lifetime = 1.5f;

        private readonly GameObject root;
        private readonly ParticleSystem system;
        private readonly ParticleSystemRenderer renderer;
        private readonly Material material;
        private readonly Texture2D streak;
        private ParticleSystem.MainModule main;
        private ParticleSystem.EmissionModule emission;
        private ParticleSystem.VelocityOverLifetimeModule velocity;

        public FallbackRain(Transform parent, ManualLogSource log)
        {
            root = new GameObject("BoscaliRainFallback");
            Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.DontSave;
            system = root.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaxParticles;
            main.startLifetime = Lifetime;
            main.startSpeed = 0f;
            main.startSize = 0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(BoxSize, BoxSize, BoxSize);

            velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            streak = new Texture2D(8, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BoscaliRainStreak" };
            var pixels = new Color32[8 * 64];
            for (int y = 0; y < 64; y++)
            {
                float along = Mathf.Sin(y / 63f * Mathf.PI);
                for (int x = 0; x < 8; x++)
                {
                    float across = 1f - Mathf.Abs((x - 3.5f) / 3.5f);
                    byte a = (byte)(255f * along * across);
                    pixels[y * 8 + x] = new Color32(255, 255, 255, a);
                }
            }
            streak.SetPixels32(pixels);
            streak.Apply(false, true);

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "BoscaliRainFallback" };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", streak);
            material.mainTexture = streak;
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent + 50;

            renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.02f;
            renderer.cameraVelocityScale = 0.02f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            log?.LogInfo("[Weather] Fallback rain built.");
        }

        public void Tick(Camera camera, Vector3 cameraVelocity, Vector3 rainVelocity, float density, Color tint)
        {
            if (density <= 0.002f)
            {
                Stop();
                return;
            }
            Vector3 relative = rainVelocity - cameraVelocity;
            Vector3 lead = relative.sqrMagnitude > 1f ? -relative.normalized * BoxSize * 0.3f : Vector3.zero;
            root.transform.SetPositionAndRotation(camera.transform.position + lead, Quaternion.identity);

            velocity.x = rainVelocity.x;
            velocity.y = rainVelocity.y;
            velocity.z = rainVelocity.z;
            emission.rateOverTime = MaxParticles / Lifetime * Mathf.Clamp01(density) * 0.9f;
            tint.a = 0.22f;
            main.startColor = tint;
            if (!system.isPlaying) system.Play(true);
        }

        public void Stop()
        {
            if (system != null && system.isPlaying) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            if (streak != null) Object.Destroy(streak);
        }
    }
}

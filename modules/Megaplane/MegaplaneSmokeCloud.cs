using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Groza soot, impact dust and spray use the same four textured billows and floating-origin space.</summary>
    internal static class MegaplaneSmokeCloud
    {
        private static Material material;

        internal static Vector3 Wind
        {
            get
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                Vector3 wind = level != null ? level.windVelocity : Vector3.zero;
                wind.y = 0f;
                return Vector3.ClampMagnitude(wind, 22f);
            }
        }

        internal static ParticleSystem Create(Transform parent, string name, int budget, uint seed)
        {
            if (Datum.origin == null || SmokeFx.Puff == null) return null;
            if (material == null)
            {
                const string resource = "BoscaliSummer.Megaplane.BS_Megaplane_WreckSmoke.png";
                var pixels = EmbeddedResources.ReadAll(typeof(MegaplaneSmokeCloud).Assembly, resource, 512 * 1024);
                var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                    { name = "Groza smoke billows", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                if (pixels == null || !ImageConversion.LoadImage(atlas, pixels, true))
                {
                    Object.Destroy(atlas);
                    return null;
                }
                material = new Material(SmokeFx.Puff) { name = "Groza smoke billows" };
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", atlas);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", atlas);
            }
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.useAutoRandomSeed = false;
            system.randomSeed = seed | 1u;
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = 0f;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, .999f);
            var growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, .78f), new Keyframe(.17f, 1.48f), new Keyframe(1f, 3.5f)));
            var turn = system.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-.24f, .24f);
            var fading = system.colorOverLifetime;
            fading.enabled = true;
            var opacity = new Gradient();
            opacity.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.25f, 0f), new GradientAlphaKey(1f, .035f),
                    new GradientAlphaKey(.75f, .42f), new GradientAlphaKey(0f, 1f) });
            fading.color = opacity;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = .85f;
            go.SetActive(true);
            system.Play();
            return system;
        }

        internal static void Emit(ParticleSystem cloud, Vector3 world, Vector3 velocity, float size, float life, Color color, float rotation)
        {
            cloud.Emit(new ParticleSystem.EmitParams {
                position = Datum.origin.InverseTransformPoint(world), velocity = velocity, startSize = size,
                startLifetime = life, startColor = color, rotation = rotation, applyShapeToPosition = false
            }, 1);
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    internal static class DestructionAssets
    {
        private static AssetBundle bundle;
        private static bool attempted;
        private static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        private static UniversalAdditionalCameraData depthCamera;
        private static int depthUsers;
        private static CameraOverrideOption previousDepth;
        internal static bool AcquireDepth()
        {
#if UNITY_EDITOR
            Camera camera = Camera.main;
#else
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
#endif
            if (camera == null) return false;
            if (depthCamera == null)
            {
                depthCamera = camera.GetUniversalAdditionalCameraData();
                previousDepth = depthCamera.requiresDepthOption;
            }
            depthCamera.requiresDepthTexture = true; depthUsers++; return true;
        }
        internal static void ReleaseDepth()
        {
            if (depthUsers > 0) depthUsers--;
            if (depthUsers != 0 || depthCamera == null) return;
            depthCamera.requiresDepthOption = previousDepth; depthCamera = null;
        }
        internal static Shader Shader(string name)
        {
            Shader loaded = UnityEngine.Shader.Find(name);
            if (loaded != null && loaded.isSupported) return loaded;
            if (shaders.TryGetValue(name, out loaded)) return loaded;
            if (!attempted)
            {
                attempted = true;
                try
                {
                    byte[] bytes = EmbeddedResources.ReadAll(typeof(DestructionAssets).Assembly,
                        "BoscaliSummer.Destruction.structure.bundle", 8 * 1024 * 1024);
                    if (bytes != null) bundle = AssetBundle.LoadFromMemory(bytes);
                    if (bundle != null)
                        foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                        {
                            Report("Structural shader loaded: " + shader.name + ", supported=" + shader.isSupported, false);
                            if (shader.isSupported) shaders[shader.name] = shader;
                        }
                }
                catch (Exception error) { Debug.LogError("Structural destruction assets: " + error.Message); }
            }
            loaded = UnityEngine.Shader.Find(name);
            if (loaded != null && loaded.isSupported) return loaded;
            if (shaders.TryGetValue(name, out loaded)) return loaded;
            Report("Structural shader unavailable: " + name, true);
            return null;
        }
        internal static Texture2D ConcreteTexture()
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float aggregate = FractureGeometry.Rand((uint)(x * 8191 + y), 17);
                    float coarse = Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
                    float shade = 0.28f + coarse * 0.22f + aggregate * 0.12f;
                    if (aggregate > 0.91f) shade *= 0.63f;
                    pixels[y * size + x] = (Color32)new Color(shade, shade * 0.98f, shade * 0.93f, 1f);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Concrete aggregate", wrapMode = TextureWrapMode.Repeat };
            texture.SetPixels32(pixels); texture.Apply(true, true); return texture;
        }
        private static void Report(string message, bool error)
        {
#if UNITY_EDITOR
            if (error) Debug.LogError(message); else Debug.Log(message);
#else
            if (error) Plugin.Logger.LogError(message); else Plugin.Logger.LogInfo(message);
#endif
        }
    }
}

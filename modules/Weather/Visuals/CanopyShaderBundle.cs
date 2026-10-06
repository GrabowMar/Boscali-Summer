using System;
using System.IO;
using System.Reflection;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Loads the embedded canopy-rain shader bundle (weatherrain.bundle, built by
    /// tools/Compile-WeatherAssets.ps1) exactly once via AssetBundle.LoadFromMemory.
    /// Byte-capped and fail-closed: null when the resource, bundle, or shader is missing.
    /// </summary>
    internal static class CanopyShaderBundle
    {
        private const string ResourceName = "BoscaliSummer.Weather.weatherrain.bundle";
        private const string ShaderName = "Boscali/CanopyRain";
        private const string UpdateShaderName = "Hidden/BoscaliCanopyDroplets";
        private const int MaxBundleBytes = 4 * 1024 * 1024;

        private static AssetBundle bundle;
        private static Shader shader;
        private static Shader terrainShader;
        private static Shader updateShader;
        private static Shader flightCloudShader;
        private static Shader flightCloudCompositeShader;
        private static bool attempted;
        private static bool prewarmed;

        /// <summary>
        /// Compiles our small bundle shaders on an 8x8 target so first use does not hitch.
        /// Returns milliseconds spent, or -1 when nothing was warmed. WarmupAllShaders was measured
        /// at 61 s - never again; this warms only what we own.
        /// </summary>
        internal static long Prewarm()
        {
            if (prewarmed) return 0;
            prewarmed = true;
            GetShader();
            GetTerrainShader();
            GetUpdateShader();
            GetFlightCloudShader();
            GetFlightCloudCompositeShader();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int warmed = 0;
            warmed += WarmOne(shader) ? 1 : 0;
            warmed += WarmOne(terrainShader) ? 1 : 0;
            warmed += WarmOne(updateShader) ? 1 : 0;
            warmed += WarmOne(flightCloudShader) ? 1 : 0;
            warmed += WarmOne(flightCloudCompositeShader) ? 1 : 0;
            watch.Stop();
            return warmed > 0 ? watch.ElapsedMilliseconds : -1;
        }

        private static bool WarmOne(Shader shader)
        {
            if (shader == null || !shader.isSupported) return false;
            RenderTexture src = null, dst = null;
            Material material = null;
            try
            {
                src = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
                dst = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
                material = new Material(shader);
                Graphics.Blit(src, dst, material);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (src != null) RenderTexture.ReleaseTemporary(src);
                if (dst != null) RenderTexture.ReleaseTemporary(dst);
                if (material != null) UnityEngine.Object.Destroy(material);
            }
        }

        internal static Shader GetTerrainShader()
        {
            GetShader();
            return terrainShader;
        }

        internal static Shader GetUpdateShader()
        {
            GetShader();
            return updateShader;
        }

        internal static Shader GetFlightCloudShader()
        {
            GetShader();
            return flightCloudShader;
        }

        /// <summary>Upsamples the half-resolution clouds over the scene (WeatherCloudPass).</summary>
        internal static Shader GetFlightCloudCompositeShader()
        {
            GetShader();
            return flightCloudCompositeShader;
        }

        internal static Shader GetShader()
        {
            if (shader != null) return shader;
            if (attempted) return null;
            attempted = true;

            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(CanopyShaderBundle).Assembly, ResourceName, MaxBundleBytes);
                if (bytes == null) return null;
                bundle = AssetBundle.LoadFromMemory(bytes);

                if (bundle == null) return null;
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                if (shaders != null)
                {
                    for (int i = 0; i < shaders.Length; i++)
                    {
                        if (shaders[i] != null && shaders[i].name == ShaderName && shaders[i].isSupported)
                        {
                            shader = shaders[i];
                        }
                        if (shaders[i] != null && shaders[i].name == "Boscali/TerrainRain" && shaders[i].isSupported)
                            terrainShader = shaders[i];
                        if (shaders[i] != null && shaders[i].name == UpdateShaderName && shaders[i].isSupported)
                            updateShader = shaders[i];
                        if (shaders[i] != null && shaders[i].name == "Boscali/FlightCloud" && shaders[i].isSupported)
                            flightCloudShader = shaders[i];
                        if (shaders[i] != null && shaders[i].name == "Boscali/FlightCloudComposite" && shaders[i].isSupported)
                            flightCloudCompositeShader = shaders[i];
                    }
                }
                if (shader != null || terrainShader != null || updateShader != null ||
                    flightCloudShader != null)
                    return shader;
                bundle.Unload(true);
                bundle = null;
            }
            catch (Exception)
            {
                shader = null;
            }

            return shader;
        }
    }
}

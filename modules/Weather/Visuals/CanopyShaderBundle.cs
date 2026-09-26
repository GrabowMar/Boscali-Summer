using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Visuals
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
        private const int MaxBundleBytes = 4 * 1024 * 1024;

        private static AssetBundle bundle;
        private static Shader shader;
        private static Shader terrainShader;
        private static Texture2D dropletMask;
        private static Texture2D rivuletMask;

        internal static Shader GetTerrainShader()
        {
            GetShader();
            return terrainShader;
        }

        internal static Texture2D GetDropletMask()
        {
            GetShader();
            return dropletMask;
        }

        internal static Texture2D GetRivuletMask()
        {
            GetShader();
            return rivuletMask;
        }
        private static bool attempted;

        internal static Shader GetShader()
        {
            if (shader != null) return shader;
            if (attempted) return null;
            attempted = true;

            try
            {
                Assembly asm = typeof(CanopyShaderBundle).Assembly;
                using (Stream s = asm.GetManifestResourceStream(ResourceName))
                {
                    if (s == null || s.Length == 0 || s.Length > MaxBundleBytes) return null;
                    byte[] bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != bytes.Length) return null;
                    bundle = AssetBundle.LoadFromMemory(bytes);
                }

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
                    }
                }
                Texture2D[] textures = bundle.LoadAllAssets<Texture2D>();
                if (textures != null)
                {
                    for (int i = 0; i < textures.Length; i++)
                    {
                        if (textures[i] == null) continue;
                        if (textures[i].name.IndexOf("droplet", StringComparison.OrdinalIgnoreCase) >= 0)
                            dropletMask = textures[i];
                        else if (textures[i].name.IndexOf("rivulet", StringComparison.OrdinalIgnoreCase) >= 0)
                            rivuletMask = textures[i];
                    }
                }
                if (shader != null || terrainShader != null) return shader;
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

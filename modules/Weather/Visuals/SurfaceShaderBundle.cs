using System;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    // Optional surface assets are separate from the weather bundle; missing/unsupported
    // shaders leave native terrain and the previous custom ocean available.
    internal static class SurfaceShaderBundle
    {
        private static AssetBundle bundle;
        private static Shader terrain, ocean;
        private static Texture2D detail;
        private static bool attempted;

        internal static Shader Terrain { get { Load(); return terrain; } }
        internal static Shader Ocean { get { Load(); return ocean; } }
        internal static Texture2D Detail { get { Load(); return detail; } }

        private static void Load()
        {
            if (attempted || Application.isBatchMode) return;
            attempted = true;
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(SurfaceShaderBundle).Assembly,
                    "BoscaliSummer.Weather.surfaces.bundle", 2 * 1024 * 1024);
                if (bytes == null) return;
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) return;
                foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                {
                    if (shader == null || !shader.isSupported) continue;
                    if (shader.name == "Boscali/TerrainDetail") terrain = shader;
                    if (shader.name == "Boscali/Ocean") ocean = shader;
                }
                foreach (Texture2D texture in bundle.LoadAllAssets<Texture2D>())
                    if (texture != null && texture.name == "terrain-detail" && texture.width == 256 &&
                        texture.height == 256 && texture.mipmapCount > 1) detail = texture;
                if (detail == null) terrain = null;
                if (terrain != null || ocean != null) return;
                bundle.Unload(true);
                bundle = null;
            }
            catch (Exception error)
            {
                terrain = ocean = null;
                detail = null;
                if (bundle != null) bundle.Unload(true);
                bundle = null;
                Debug.LogWarning("[BoscaliSummer] Surface assets unavailable: " + error.Message);
            }
        }
    }
}

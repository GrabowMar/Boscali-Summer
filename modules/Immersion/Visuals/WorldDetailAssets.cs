using System;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    internal static class WorldDetailAssets
    {
        private static bool attempted;
        private static AssetBundle bundle;
        private static Shader shader;
        private static Texture2D atlas;

        internal static bool TryGet(out Shader detailShader, out Texture2D detailAtlas)
        {
            if (!attempted && !Application.isBatchMode)
            {
                attempted = true;
                try
                {
                    var assembly = typeof(WorldDetailAssets).Assembly;
                    byte[] bytes = EmbeddedResources.ReadAll(assembly, "BoscaliSummer.WorldDetail.bundle", 512 * 1024);
                    if (bytes != null) bundle = AssetBundle.LoadFromMemory(bytes);
                    if (bundle != null)
                        foreach (Shader item in bundle.LoadAllAssets<Shader>())
                            if (item != null && item.name == "Boscali/WorldDetail" && item.isSupported) shader = item;
                    bytes = EmbeddedResources.ReadAll(assembly, "BoscaliSummer.WorldDetail.png", 256 * 1024);
                    if (shader != null && bytes != null)
                    {
                        atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true, false)
                        {
                            name = "Boscali World Detail Atlas", hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 1
                        };
                        if (!ImageConversion.LoadImage(atlas, bytes, true) || atlas.width != 256 || atlas.height != 256 ||
                            atlas.mipmapCount != 9 || atlas.isReadable)
                        { UnityEngine.Object.Destroy(atlas); atlas = null; }
                    }
                }
                catch (Exception error)
                {
                    if (atlas != null) UnityEngine.Object.Destroy(atlas);
                    atlas = null; shader = null;
                    Debug.LogWarning("[BoscaliSummer] World detail unavailable: " + error.Message);
                }
                if (shader == null || atlas == null)
                {
                    shader = null;
                    if (bundle != null) bundle.Unload(true);
                    bundle = null;
                }
            }
            detailShader = shader;
            detailAtlas = atlas;
            return shader != null && atlas != null;
        }
    }
}

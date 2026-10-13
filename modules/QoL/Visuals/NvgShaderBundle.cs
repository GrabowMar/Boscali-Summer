using System;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.QoL.Visuals
{
    /// <summary>The two night-vision shaders, from the embedded nightvision.bundle (tools/Compile-NightVisionAssets.ps1). Null when it is missing or unsupported.</summary>
    internal static class NvgShaderBundle
    {
        private static AssetBundle bundle;
        private static Shader phosphor, heat;
        private static bool attempted;

        internal static Shader GetPhosphor() { Load(); return phosphor; }
        internal static Shader GetHeat() { Load(); return heat; }

        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null; phosphor = heat = null; attempted = false;
        }

        private static void Load()
        {
            if (attempted) return;
            attempted = true;
            try
            {
                byte[] data = EmbeddedResources.ReadAll(typeof(NvgShaderBundle).Assembly, "BoscaliSummer.QoL.nightvision.bundle", 512 * 1024);
                if (data == null) return;
                bundle = AssetBundle.LoadFromMemory(data);
                if (bundle == null) return;
                foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                {
                    if (!shader.isSupported) continue;
                    if (shader.name == "Boscali/NvgPhosphor") phosphor = shader;
                    if (shader.name == "Boscali/NvgHeat") heat = shader;
                }
            }
            catch (Exception) { phosphor = heat = null; }
        }
    }
}

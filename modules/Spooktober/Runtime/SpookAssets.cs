using System;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>The embedded spooktober.bundle (tools/Compile-SpooktoberAssets.ps1), loaded once. <see cref="RadGhost"/> is null
    /// when the bundle is missing or the shader is unsupported here (logged once): the rad look is then embers only.</summary>
    internal static class SpookAssets
    {
        private const string Resource = "BoscaliSummer.Spooktober.spooktober.bundle";
        private static AssetBundle bundle;
        private static Shader radGhost;
        private static bool attempted;

        public static Shader RadGhost { get { Load(); return radGhost; } }

        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null; radGhost = null; attempted = false;
        }

        private static void Load()
        {
            if (attempted) return;
            attempted = true;
            try
            {
                byte[] data = EmbeddedResources.ReadAll(typeof(SpookAssets).Assembly, Resource, 512 * 1024);
                bundle = data != null ? AssetBundle.LoadFromMemory(data) : null;
                if (bundle != null)
                    foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                        if (shader.name == "Boscali/RadGhost" && shader.isSupported) radGhost = shader;
            }
            catch (Exception error) { Debug.LogError("[Spooktober] spooktober.bundle: " + error.Message); }
            if (radGhost == null) Debug.LogWarning("[Spooktober] Boscali/RadGhost unavailable (tools/Compile-SpooktoberAssets.ps1): rad look is embers only");
        }
    }
}

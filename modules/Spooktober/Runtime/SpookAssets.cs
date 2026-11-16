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
        private const string ParticleResource = "BoscaliSummer.Spooktober.hell-particles.bundle";
        private static AssetBundle bundle;
        private static AssetBundle particleBundle;
        private static GameObject portalParticles;
        private static Shader radGhost, hellfire, hellPortal, hellMatter, hellSky, ashSurface, hellInfection;
        private static bool attempted;
        private static bool particlesAttempted;

        public static Shader RadGhost { get { Load(); return radGhost; } }
        /// <summary>Hell's Army units (solid charred body, lava cracks); null when missing, like <see cref="RadGhost"/>.</summary>
        public static Shader Hellfire { get { Load(); return hellfire; } }
        /// <summary>The hell portal's vortex disc; null when missing (the portal is then its embers only).</summary>
        public static Shader HellPortal { get { Load(); return hellPortal; } }
        internal static Shader HellMatter { get { Load(); return hellMatter; } }
        internal static Shader HellSky { get { Load(); return hellSky; } }
        internal static Shader AshSurface { get { Load(); return ashSurface; } }
        internal static Shader HellInfection { get { Load(); return hellInfection; } }
        /// <summary>The RQL-authored prefab contains only native Unity particle components, with materials assigned by the portal.</summary>
        internal static GameObject PortalParticles { get { LoadParticles(); return portalParticles; } }

        internal static void Release()
        {
            if (bundle != null) bundle.Unload(true);
            if (particleBundle != null) particleBundle.Unload(true);
            particleBundle = null; portalParticles = null; particlesAttempted = false;
            bundle = null; radGhost = null; hellfire = null; hellPortal = null; hellMatter = null; hellSky = null; ashSurface = null; hellInfection = null; attempted = false;
        }

        private static void LoadParticles()
        {
            if (particlesAttempted) return;
            particlesAttempted = true;
            try
            {
                byte[] data = EmbeddedResources.ReadAll(typeof(SpookAssets).Assembly, ParticleResource, 512 * 1024);
                particleBundle = data != null ? AssetBundle.LoadFromMemory(data) : null;
                portalParticles = particleBundle != null ? particleBundle.LoadAsset<GameObject>("assets/hellparticles/hellportalparticles.prefab") : null;
            }
            catch (Exception error) { Debug.LogWarning("[Spooktober] hell-particles.bundle: " + error.Message); }
            if (portalParticles == null) Debug.LogWarning("[Spooktober] Authored portal particles unavailable; using the small native ash fallback");
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
                    {
                        if (!shader.isSupported) continue;
                        if (shader.name == "Boscali/RadGhost") radGhost = shader;
                        else if (shader.name == "Boscali/Hellfire") hellfire = shader;
                        else if (shader.name == "Boscali/HellPortal") hellPortal = shader;
                        else if (shader.name == "Boscali/HellMatter") hellMatter = shader;
                        else if (shader.name == "Boscali/HellSky") hellSky = shader;
                        else if (shader.name == "Boscali/SootedWasteland") ashSurface = shader;
                        else if (shader.name == "Boscali/HellInfection") hellInfection = shader;
                    }
            }
            catch (Exception error) { Debug.LogError("[Spooktober] spooktober.bundle: " + error.Message); }
            if (radGhost == null) Debug.LogWarning("[Spooktober] Boscali/RadGhost unavailable (tools/Compile-SpooktoberAssets.ps1): rad look is embers only");
            if (hellfire == null || hellPortal == null) Debug.LogWarning("[Spooktober] Boscali/Hellfire or HellPortal unavailable: hell look is embers only");
        }
    }
}

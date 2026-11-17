using System;
using BepInEx.Logging;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Effects.Runtime
{
    /// <summary>Owns the optional cinematic streak shader without changing native tonemapping.</summary>
    internal sealed class CinematicAssets : IDisposable
    {
        private AssetBundle bundle;
        private Shader streak;
        private bool attempted;

        internal Shader Streak(ManualLogSource logger)
        {
            if (attempted) return streak;
            attempted = true;
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(CinematicAssets).Assembly,
                    "BoscaliSummer.Effects.cinematic.bundle", 4 * 1024 * 1024);
                if (bytes == null) throw new InvalidOperationException("embedded cinematic.bundle is missing");
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) throw new InvalidOperationException("cinematic.bundle could not be loaded");
                foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                    if (shader.name == "Hidden/Boscali/CinematicStreak" && shader.isSupported)
                        return streak = shader;
                throw new InvalidOperationException("cinematic streak shader is missing or unsupported");
            }
            catch (Exception error)
            {
                logger?.LogWarning("[Effects] Cinematic light streaks unavailable: " + error.Message);
                if (bundle != null) bundle.Unload(true);
                bundle = null;
                return null;
            }
        }

        public void Dispose()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            streak = null;
            attempted = false;
        }
    }
}

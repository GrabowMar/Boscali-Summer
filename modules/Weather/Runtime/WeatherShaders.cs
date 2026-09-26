using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The Weather module's own shaders, shipped as an AssetBundle embedded in the plugin
    /// (built from tools/WeatherShaders with tools/build-weather-shaders.ps1). Loaded once per
    /// process and never unloaded — materials reference these shaders for the whole session.
    ///
    /// <para>Fails closed: a missing resource, a bundle Unity refuses, or a shader this GPU
    /// cannot run sets <see cref="Available"/> false with one log line, and every renderer
    /// falls back to vanilla shaders or turns itself off.</para>
    /// </summary>
    internal static class WeatherShaders
    {
        public const string RainField = "Boscali/RainField";
        public const string RainShaft = "Boscali/RainShaft";
        public const string CanopyGlass = "Boscali/CanopyGlass";
        public const string DropSplat = "Boscali/DropSplat";
        public const string DropDecay = "Boscali/DropDecay";
        public const string Lightning = "Boscali/Lightning";
        public const string Splash = "Boscali/Splash";

        private const string ResourceName = "BoscaliSummer.Weather.boscali_weather.bundle";

        private static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
        private static bool attempted;
        private static AssetBundle bundle;

        public static bool Available { get; private set; }

        /// <summary>A shader from the bundle that this GPU supports, or null.</summary>
        public static Shader Get(string name, ManualLogSource log)
        {
            EnsureLoaded(log);
            if (!Available) return null;
            return shaders.TryGetValue(name, out Shader shader) ? shader : null;
        }

        private static void EnsureLoaded(ManualLogSource log)
        {
            if (attempted) return;
            attempted = true;
            try
            {
                byte[] bytes;
                using (Stream stream = typeof(WeatherShaders).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        log?.LogWarning("[Weather] Shader bundle resource is missing; rain falls back to vanilla shaders.");
                        return;
                    }
                    bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = stream.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }

                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null)
                {
                    log?.LogWarning("[Weather] Unity refused the shader bundle; rain falls back to vanilla shaders.");
                    return;
                }

                foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                {
                    if (shader == null) continue;
                    if (!shader.isSupported)
                    {
                        log?.LogWarning("[Weather] Shader " + shader.name + " is not supported on this GPU; skipped.");
                        continue;
                    }
                    shaders[shader.name] = shader;
                }
                Available = shaders.Count > 0;
                log?.LogInfo("[Weather] Loaded " + shaders.Count + " weather shader(s) from the embedded bundle.");
            }
            catch (Exception e)
            {
                Available = false;
                log?.LogWarning("[Weather] Could not load the shader bundle (" + e.GetType().Name + ": " + e.Message +
                                "); rain falls back to vanilla shaders.");
            }
        }
    }
}

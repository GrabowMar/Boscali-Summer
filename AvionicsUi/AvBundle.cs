using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// The shared avionics UI bundle: FUI fonts, the icon font, the UI shaders and a noise texture.
    /// Loaded once from the embedding plugin's resources. Everything here fails closed: a missing,
    /// corrupt or unsupported asset is null, it is logged once, and callers fall back to
    /// vanilla fonts and no effects.
    /// </summary>
    public static class AvBundle
    {
        public const string ResourceName = "NOAvionics.avionics-ui.bundle";
        private const int MaxBytes = 4 * 1024 * 1024;

        private static bool attempted;
        private static AssetBundle bundle;
        private static readonly Dictionary<string, TMP_FontAsset> Fonts = new Dictionary<string, TMP_FontAsset>(5);
        private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>(3);
        private static Texture2D noise;

        public static bool Available { get; private set; }
        public static Texture2D Noise => noise;
        public static Texture2D Illustration(string name) => bundle != null ? bundle.LoadAsset<Texture2D>(name) : null;

        public static void Engraving(RectTransform parent, float opacity)
        {
            Texture2D texture = Illustration("contour-engraving");
            if (texture == null) return;
            var go = new GameObject("Decorative engraving", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            AvLay.Fill((RectTransform)go.transform, 1f);
            var image = go.AddComponent<RawImage>();
            image.texture = texture;
            image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
            image.raycastTarget = false;
        }

        public static void Load(Action<string> log = null)
        {
            if (attempted) return;
            byte[] bytes = null;
            try
            {
                using (Stream s = typeof(AvBundle).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (s != null && s.Length > 0 && s.Length <= MaxBytes)
                    {
                        bytes = new byte[s.Length];
                        int read = 0;
                        while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    }
                }
            }
            catch (Exception e) { log?.Invoke("avionics-ui.bundle unreadable: " + e.Message); }
            if (bytes == null) { attempted = true; log?.Invoke("avionics-ui.bundle not embedded; using vanilla fonts, no UI effects"); return; }
            LoadFromBytes(bytes, log);
        }

        /// <summary>Load from raw bytes. Public so offline Unity checks (another assembly) can exercise the failure paths.</summary>
        public static void LoadFromBytes(byte[] bytes, Action<string> log)
        {
            if (attempted && bundle != null) return;
            attempted = true;
            try
            {
                // Reuse only this asset revision; other mods can carry the older shared bundle.
                foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                    if (loaded != null && loaded.name == "boscali-mfd-ui.bundle") { bundle = loaded; break; }
                if (bundle == null) bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) { log?.Invoke("avionics-ui.bundle failed to load; using vanilla fonts, no UI effects"); return; }

                foreach (TMP_FontAsset f in bundle.LoadAllAssets<TMP_FontAsset>()) if (f != null) Fonts[f.name] = f;
                foreach (Shader s in bundle.LoadAllAssets<Shader>())
                    if (s != null && s.isSupported) Shaders[s.name] = s;
                    else if (s != null) log?.Invoke("UI shader unsupported on this GPU: " + s.name);
                noise = bundle.LoadAsset<Texture2D>("noise64");

                // Bundled TMP materials reference a bundled copy of the SDF shader; use the game's own.
                Shader sdf = UnityEngine.Shader.Find("TextMeshPro/Distance Field");
                if (sdf != null) foreach (TMP_FontAsset f in Fonts.Values) if (f.material != null) f.material.shader = sdf;

                Available = Fonts.Count > 0 || Shaders.Count > 0;
                log?.Invoke("avionics-ui.bundle loaded: " + Fonts.Count + " fonts, " + Shaders.Count + " shaders");
            }
            catch (Exception e) { Available = false; log?.Invoke("avionics-ui.bundle error: " + e.Message); }
        }

        public static TMP_FontAsset Font(string assetName) =>
            assetName != null && Fonts.TryGetValue(assetName, out TMP_FontAsset f) ? f : null;

        public static Shader Shader(string name) =>
            name != null && Shaders.TryGetValue(name, out Shader s) ? s : null;

        public static void ResetForTests()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null; attempted = false; Available = false; noise = null;
            Fonts.Clear(); Shaders.Clear();
        }
    }
}

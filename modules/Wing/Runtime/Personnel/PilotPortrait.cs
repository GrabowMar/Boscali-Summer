using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    internal static class PilotPortrait
    {
        private const int MaxCachedPortraits = 256;
        private static readonly Dictionary<PortraitSelection, Sprite> portraits = new Dictionary<PortraitSelection, Sprite>();
        private static byte[] layers;
        private static bool loadAttempted;

        /// <summary>Renders a semantic selection. Atlas tile IDs never escape the compositor.</summary>
        public static Sprite ForSelection(PortraitSelection selection)
        {
            selection = PilotPortraitGenerator.Normalize(selection);
            if (portraits.TryGetValue(selection, out Sprite portrait)) return portrait;
            // Consumers borrow these sprites. At the ceiling, fail closed instead of evicting an image still displayed.
            if (portraits.Count >= MaxCachedPortraits || !LoadLayers()) return null;

            string key = $"{(int)selection.Body}_{selection.Face}_{selection.Hair}_{selection.Uniform}_{selection.Accessory}_{selection.Backdrop}";
            portrait = Create(key, PilotPortraitGenerator.Compose(selection, layers));
            portraits.Add(selection, portrait);
            return portrait;
        }

        private static Texture2D previewTexture;
        private static Sprite previewSprite;

        /// <summary>The studio's preview (R7): one texture drawn over in place, so stepping through looks never grows the cache.</summary>
        public static Sprite Preview(PortraitSelection selection)
        {
            if (!LoadLayers()) return null;
            byte[] pixels = PilotPortraitGenerator.Compose(PilotPortraitGenerator.Normalize(selection), layers);
            if (previewTexture == null || previewSprite == null ||
                previewTexture.width != PilotPortraitGenerator.Width || previewTexture.height != PilotPortraitGenerator.Height)
            {
                DestroyPreview();
                previewTexture = new Texture2D(PilotPortraitGenerator.Width, PilotPortraitGenerator.Height, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "WingCommand_Pilot_Preview", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                previewSprite = UnityEngine.Sprite.Create(previewTexture, new Rect(0, 0, previewTexture.width, previewTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                previewSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            previewTexture.LoadRawTextureData(pixels);
            previewTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return previewSprite;
        }

        public static Sprite For(WingPilot pilot)
        {
            Sprite file = pilot != null ? FromFile(pilot.Callsign) : null;
            if (file != null) return file;
            if (pilot != null && pilot.HasCustomPortrait)
                return ForSelection(pilot.PortraitSelection.Value);

            int faction = pilot != null && pilot.PortraitFaction >= 0 ? pilot.PortraitFaction : PortraitFactions.Local;
            return ForIdentity(pilot == null ? "WingCommand" : pilot.Name + "|" + pilot.Callsign, PortraitRole.Pilot, faction);
        }

        /// <summary>A service role changes clothing/equipment while the identity retains its face, body, hair and scene.</summary>
        public static Sprite ForIdentity(string identity, PortraitRole role, int faction = -1) =>
            ForSelection(PilotPortraitGenerator.Select(identity, role, faction));

        private static Sprite Create(string key, byte[] pixels)
        {
            var texture = new Texture2D(PilotPortraitGenerator.Width, PilotPortraitGenerator.Height,
                                        TextureFormat.RGBA32, mipChain: false)
            {
                name = "WingCommand_Pilot_" + key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.LoadRawTextureData(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite portrait = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                                                        new Vector2(0.5f, 0.5f), 100f);
            portrait.name = texture.name;
            portrait.hideFlags = HideFlags.HideAndDontSave;
            return portrait;
        }

        private static bool LoadLayers()
        {
            if (loadAttempted) return layers != null;
            loadAttempted = true;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                using (Stream stream = typeof(PilotPortrait).Assembly.GetManifestResourceStream("WingCommand.PilotLayers.png"))
                using (var bytes = new MemoryStream())
                {
                    if (stream == null) throw new InvalidDataException("Embedded portrait layers missing.");
                    stream.CopyTo(bytes);
                    if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false) ||
                        texture.width != PilotPortraitGenerator.AtlasWidth || texture.height != PilotPortraitGenerator.AtlasHeight)
                        throw new InvalidDataException("Invalid portrait atlas dimensions.");
                    // LoadImage can change PNG storage to ARGB32; the compositor needs RGBA.
                    Color32[] pixels = texture.GetPixels32();
                    if (pixels.Length != PilotPortraitGenerator.AtlasWidth * PilotPortraitGenerator.AtlasHeight)
                        throw new InvalidDataException("Invalid portrait atlas pixel count.");
                    var loadedLayers = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        loadedLayers[i * 4] = pixels[i].r;
                        loadedLayers[i * 4 + 1] = pixels[i].g;
                        loadedLayers[i * 4 + 2] = pixels[i].b;
                        loadedLayers[i * 4 + 3] = pixels[i].a;
                    }
                    layers = loadedLayers;
                }
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilot] Could not load portrait layers: " + e.Message);
            }
            finally
            {
                DestroyObject(texture);
            }
            return layers != null;
        }

        // ---------------------------------------------------------------- the PNG portrait (Pilots/Portraits/<CALLSIGN>.png)

        private sealed class FilePortrait
        {
            public DateTime Stamp;
            public float CheckedAt;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, FilePortrait> files = new Dictionary<string, FilePortrait>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The pilot's PNG portrait when Pilots/Portraits holds one under the callsign; it replaces the generated face
        /// everywhere. The disk is looked at once per 2 s per callsign, so callers may ask every frame.</summary>
        public static Sprite FromFile(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return null;
            if (!files.TryGetValue(callsign, out FilePortrait f))
            {
                if (files.Count >= MaxCachedPortraits) return null;
                files.Add(callsign, f = new FilePortrait { CheckedAt = float.NegativeInfinity });
            }
            if (Time.unscaledTime - f.CheckedAt < 2f) return f.Sprite;
            f.CheckedAt = Time.unscaledTime;
            string path = WingSavedPilots.PortraitPath(callsign);
            DateTime stamp;
            try { stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default; }
            catch (Exception) { stamp = default; }
            if (stamp == f.Stamp) return f.Sprite;
            f.Stamp = stamp;
            if (f.Sprite != null)
            {
                DestroyObject(f.Sprite.texture);
                DestroyObject(f.Sprite);
                f.Sprite = null;
            }
            if (stamp == default) return null;
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "WingCommand_PilotFile_" + callsign, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), markNonReadable: true))
                {
                    DestroyObject(texture);
                    return null;
                }
                f.Sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                f.Sprite.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilot] Could not read portrait " + path + ": " + e.Message);
            }
            return f.Sprite;
        }

        /// <summary>Forget the cached file for a callsign so the next ask reads the disk now (after LOAD / CLEAR / a rename).</summary>
        public static void Recheck(string callsign)
        {
            if (!string.IsNullOrEmpty(callsign) && files.TryGetValue(callsign, out FilePortrait f)) f.CheckedAt = float.NegativeInfinity;
        }

        /// <summary>An image file as a portrait PNG: centre-cropped to the portrait's 4:5 and scaled to 256x320. Null when it is not
        /// an image Unity can read.</summary>
        public static byte[] ToPortraitPng(byte[] image)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            RenderTexture rt = null;
            Texture2D target = null;
            try
            {
                if (image == null || !ImageConversion.LoadImage(source, image, markNonReadable: false)) return null;
                int w = PilotPortraitGenerator.Width, h = PilotPortraitGenerator.Height;
                float aspect = (float)w / h, have = (float)source.width / source.height;
                Vector2 scale = have > aspect ? new Vector2(aspect / have, 1f) : new Vector2(1f, have / aspect);
                Vector2 offset = new Vector2((1f - scale.x) * 0.5f, (1f - scale.y) * 0.5f);
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                source.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, rt, scale, offset);
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                target = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                target.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                target.Apply(false);
                RenderTexture.active = was;
                return ImageConversion.EncodeToPNG(target);
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                DestroyObject(source);
                DestroyObject(target);
            }
        }

        /// <summary>The generated portrait of a selection as PNG bytes (SAVE PNG when no file portrait is on file).</summary>
        public static byte[] SelectionPng(PortraitSelection selection)
        {
            if (!LoadLayers()) return null;
            var texture = new Texture2D(PilotPortraitGenerator.Width, PilotPortraitGenerator.Height, TextureFormat.RGBA32, mipChain: false);
            try
            {
                texture.LoadRawTextureData(PilotPortraitGenerator.Compose(PilotPortraitGenerator.Normalize(selection), layers));
                texture.Apply(false);
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                DestroyObject(texture);
            }
        }

        public static void Reset()
        {
            foreach (FilePortrait f in files.Values)
            {
                if (f.Sprite == null) continue;
                DestroyObject(f.Sprite.texture);
                DestroyObject(f.Sprite);
            }
            files.Clear();
            foreach (Sprite portrait in portraits.Values)
            {
                if (portrait == null) continue;
                DestroyObject(portrait.texture);
                DestroyObject(portrait);
            }
            portraits.Clear();
            DestroyPreview();
            layers = null;
            loadAttempted = false;
        }

        private static void DestroyPreview()
        {
            DestroyObject(previewTexture);
            DestroyObject(previewSprite);
            previewTexture = null;
            previewSprite = null;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}

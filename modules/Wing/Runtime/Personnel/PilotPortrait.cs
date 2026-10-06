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

        public static Sprite Sprite => For(null);

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

        public static void Reset()
        {
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

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// Event art, loaded first from loose PNGs a player drops into
    /// <c>BepInEx/plugins/BoscaliSummer/Events/</c>, then cropped from one bundled atlas.
    /// Everything still renders without art
    /// (the vector glyph is the fallback), so this is decoration, never a dependency: a
    /// missing file, an oversized poster or a bad header just means the glyph shows.
    ///
    /// <para>Bounded and cached like the radio icon cache: PNG signature plus header
    /// dimensions checked before decode, one texture and sprite per key, hard byte and
    /// pixel ceilings, cleared on scene reset. The atlas is the only bundled texture.</para>
    /// </summary>
    internal static class EventArtCache
    {
        internal const int MaximumFileBytes = 2 * 1024 * 1024;
        internal const int MaximumDimension = 1024;

        private const int MaximumEntries = 64;

        private sealed class Entry
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Thumbs =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static Sprite[] tiles;
        private static bool atlasAttempted;

        private static string root;
        private static bool prepared;

        /// <summary>
        /// The poster for the first key that has one, falling back to <c>default.png</c>.
        /// Returns null when the player has drawn nothing, and then the caller draws the
        /// vector mark instead.
        /// </summary>
        public static Sprite Get(string artKey, string fallbackKey)
        {
            Sprite sprite = Load(artKey);
            if (sprite != null) return sprite;
            sprite = AtlasTile(artKey);
            if (sprite != null) return sprite;
            sprite = Load(fallbackKey);
            return sprite != null ? sprite : Load("default");
        }

        /// <summary>
        /// A small square, centre-cropped copy of the poster for <see cref="Get"/>, for row badges
        /// (a tile of the shared atlas cannot be shown by texture alone). Null when there is no art
        /// or the GPU copy is unavailable; bounded and cleared with the rest of the cache.
        /// </summary>
        public static Texture Thumb(string artKey, string fallbackKey)
        {
            string id = artKey + "|" + fallbackKey;
            if (Thumbs.TryGetValue(id, out Texture2D cached)) return cached;
            if (Thumbs.Count >= MaximumEntries) return null;

            Texture2D thumb = null;
            try
            {
                Sprite sprite = Get(artKey, fallbackKey);
                Texture2D source = sprite != null ? sprite.texture : null;
                if (source != null && source.format == TextureFormat.RGBA32 &&
                    (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0)
                {
                    Rect area = sprite.textureRect;
                    int side = Mathf.FloorToInt(Mathf.Min(area.width, area.height));
                    if (side > 0)
                    {
                        int x = Mathf.RoundToInt(area.x + (area.width - side) * 0.5f);
                        int y = Mathf.RoundToInt(area.y + (area.height - side) * 0.5f);
                        thumb = new Texture2D(side, side, TextureFormat.RGBA32, false, false)
                        {
                            name = "BoscaliEvents.Thumb",
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp
                        };
                        Graphics.CopyTexture(source, 0, 0, x, y, side, side, thumb, 0, 0, 0, 0);
                    }
                }
            }
            catch
            {
                if (thumb != null) UnityEngine.Object.Destroy(thumb);
                thumb = null;
            }
            Thumbs[id] = thumb;
            return thumb;
        }

        public static void Clear()
        {
            foreach (Texture2D thumb in Thumbs.Values)
                if (thumb != null) UnityEngine.Object.Destroy(thumb);
            Thumbs.Clear();
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                    if (tiles[i] != null) UnityEngine.Object.Destroy(tiles[i]);
                tiles = null;
            }
            foreach (Entry entry in Entries.Values)
            {
                if (entry == null) continue;
                if (entry.Sprite != null) UnityEngine.Object.Destroy(entry.Sprite);
                if (entry.Texture != null) UnityEngine.Object.Destroy(entry.Texture);
            }
            Entries.Clear();
            atlasAttempted = false;
        }

        private static Sprite AtlasTile(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (tiles == null && !atlasAttempted)
            {
                atlasAttempted = true;
                Entry atlas = Read("event_atlas");
                if (atlas?.Texture == null) return null;
                const int columns = 4;
                const int rows = 4;
                int width = atlas.Texture.width / columns;
                int height = atlas.Texture.height / rows;
                tiles = new Sprite[columns * rows];
                for (int i = 0; i < tiles.Length; i++)
                {
                    int column = i % columns;
                    int row = i / columns;
                    tiles[i] = Sprite.Create(atlas.Texture,
                        new Rect(column * width, (rows - row - 1) * height, width, height),
                        new Vector2(0.5f, 0.5f), 100f);
                }
                Entries["event_atlas"] = atlas;
            }
            if (tiles == null) return null;
            return tiles[TileIndex(key)];
        }

        private static int TileIndex(string key)
        {
            switch (key)
            {
                case "monsoon_season": case "air_corridor_closure": return 0;
                case "munitions_crisis": case "depot_refit": case "stocktaking_hold":
                case "captured_depot": return 1;
                case "industrial_surge": case "parts_standardization": return 2;
                case "partisan_supply_raid": case "rail_embargo": return 3;
                case "emergency_withdrawal": return 4;
                case "ceasefire_rumors": case "diplomatic_sanctions":
                case "press_censorship": case "quartermaster_audit":
                case "insurance_premium_hike": case "currency_devaluation": return 5;
                case "dockworker_strike": case "harbor_insurance_spike": return 6;
                case "salvage_boom": case "scrap_drive": return 7;
                case "allied_intervention": case "volunteer_logistics_corps":
                case "merchant_fleet_charter": return 8;
                case "fuel_depot_fire": case "fuel_rationing": return 9;
                case "homefront_rally": case "radio_relay_lease": return 10;
                case "forward_workshop": case "veteran_contractor_influx": return 11;
                case "ceasefire_ultimatum": case "holiday_stand_down": return 12;
                case "emergency_appropriation": case "war_bond_drive":
                case "emergency_procurement": case "local_donations":
                case "strategic_reserve_release": return 13;
                case "dust_storm": case "storm_grounding": return 14;
                default: return 15;
            }
        }

        private static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Entries.TryGetValue(key, out Entry cached)) return cached?.Sprite;
            if (Entries.Count >= MaximumEntries) return null;

            Entry entry = Read(key);
            Entries[key] = entry;
            return entry?.Sprite;
        }

        private static Entry Read(string key)
        {
            try
            {
                try
                {
                    PrepareFolder();
                    if (!string.IsNullOrEmpty(root))
                    {
                        string path = Path.Combine(root, key + ".png");
                        if (File.Exists(path))
                        {
                            var info = new FileInfo(path);
                            if (info.Length > 0 && info.Length <= MaximumFileBytes)
                            {
                                Entry loose = Decode(File.ReadAllBytes(path));
                                if (loose != null) return loose;
                            }
                        }
                    }
                }
                catch { /* An unreadable override must not hide bundled art. */ }

                byte[] data = EmbeddedResources.ReadAll(
                    typeof(EventArtCache).Assembly, "BoscaliSummer.EventsArt." + key + ".png", MaximumFileBytes);
                return data == null ? null : Decode(data);
            }
            catch
            {
                return null;
            }
        }

        private static Entry Decode(byte[] data)
        {
            try
            {
                return PngSprites.TryLoad(data, MaximumDimension, "BoscaliEvents.Art",
                    out Texture2D texture, out Sprite sprite) ? new Entry { Texture = texture, Sprite = sprite } : null;
            }
            catch
            {
                return null;
            }
        }

        private static void PrepareFolder()
        {
            if (prepared) return;
            prepared = true;
            root = Path.Combine(Paths.PluginPath, "BoscaliSummer", "Events");
            try
            {
                Directory.CreateDirectory(root);
            }
            catch
            {
                // A read-only plugins folder still renders glyphs; nothing to report.
            }
        }
    }
}

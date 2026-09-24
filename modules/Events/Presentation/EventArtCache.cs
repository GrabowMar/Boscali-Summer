using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace BoscaliSummer.Features.Events.Presentation
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

        private static readonly byte[] Signature =
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
        };

        private sealed class Entry
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
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

        public static void Clear()
        {
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

                using (Stream source = typeof(EventArtCache).Assembly.GetManifestResourceStream(
                           "BoscaliSummer.EventsArt." + key + ".png"))
                {
                    if (source == null || source.Length <= 0 || source.Length > MaximumFileBytes) return null;
                    var data = new byte[(int)source.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int chunk = source.Read(data, read, data.Length - read);
                        if (chunk <= 0) return null;
                        read += chunk;
                    }
                    return Decode(data);
                }
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
                if (!IsSupported(data, out int width, out int height)) return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "BoscaliEvents.Art",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(texture, data, true) ||
                    texture.width != width || texture.height != height)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Sprite sprite = Sprite.Create(
                    texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                if (sprite == null)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                sprite.name = "BoscaliEvents.ArtSprite";
                return new Entry { Texture = texture, Sprite = sprite };
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

        private static bool IsSupported(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (data == null || data.Length < 24 || data.Length > MaximumFileBytes) return false;

            for (int i = 0; i < Signature.Length; i++)
                if (data[i] != Signature[i]) return false;

            if (data[12] != (byte)'I' || data[13] != (byte)'H' ||
                data[14] != (byte)'D' || data[15] != (byte)'R')
                return false;

            uint rawWidth = ReadBigEndianUInt32(data, 16);
            uint rawHeight = ReadBigEndianUInt32(data, 20);
            if (rawWidth == 0 || rawHeight == 0 ||
                rawWidth > MaximumDimension || rawHeight > MaximumDimension)
                return false;

            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static uint ReadBigEndianUInt32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) |
            ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) |
            data[offset + 3];
    }
}

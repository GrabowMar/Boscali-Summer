using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>How a picture is drawn: not at all (the class glyph instead), as the mod's own alpha mask on the ordinary UI material, or through the game's additive material.</summary>
    internal enum ArtKind : byte { None, Mask, Additive }

    /// <summary>
    /// The pictures of the stores side: the game's own line art for a weapon (<c>WeaponInfo.weaponIcon</c>) and for a countermeasure (<c>Countermeasure.displayImage</c>). The feed's domain types (<see cref="StoreStation.Icon"/>,
    /// <see cref="CmStation.Icon"/>) carry a picture as an integer handle, 0 for none, so they stay free of Unity; this table, filled by the runtime feed when it first meets a sprite, says which sprite a handle is.
    /// The pictures are light lines on an OPAQUE black ground, made for the game's additive material. The board's own way is an alpha mask: each sprite is baked ONCE (<see cref="Bake"/>, one per 10 Hz tick) into a white texture
    /// whose alpha is the brightness of the picture (<see cref="StoreMask"/>), kept in a cache of at most <see cref="StoreMaskCache{T}.Capacity"/> and drawn on the ordinary UI material, tinted like every other ink; its ink box is
    /// measured while it is baked and is what the picture is cropped to. Until a picture is baked the board shows the class glyph. A sprite that cannot be baked (a bake that throws, a picture too big) is remembered and falls
    /// back to the game's additive material (<see cref="Material"/>, taken from the native weapon panel; the crop is then <see cref="StoreArtBounds"/>'s table by the sprite's name), and without that to the glyph.
    /// </summary>
    internal static class StoreIcons
    {
        private struct Entry
        {
            public Sprite Sprite;
            public ArtBounds Ink;
        }

        /// <summary>A baked picture: the white mask and where its ink is (fractions of the picture, y down).</summary>
        private sealed class Baked
        {
            public Texture2D Texture;
            public ArtBounds Ink;
        }

        private static readonly List<Entry> entries = new List<Entry>(80);
        private static readonly Dictionary<Sprite, int> ids = new Dictionary<Sprite, int>(80);
        private static readonly StoreMaskCache<Baked> masks = new StoreMaskCache<Baked>();
        /// <summary>The sprites (by instance id) whose bake failed: they are not tried again.</summary>
        private static readonly HashSet<int> failed = new HashSet<int>();
        /// <summary>The handles the header has asked for and nothing has baked yet, oldest first (a loadout has a handful: HERO's weapon and the jammer).</summary>
        private static readonly int[] queue = new int[8];
        private static int queued;

        /// <summary>The game's additive picture material; null until the board has found it (and after it is destroyed). Only the fallback for a sprite that cannot be baked.</summary>
        public static Material Material;

        /// <summary>The handle of <paramref name="sprite"/> (made on first sight, the same afterwards); 0 for none. Cheap and allocation-free for a sprite already seen.</summary>
        public static int Id(Sprite sprite)
        {
            if (sprite == null) return 0;
            if (ids.TryGetValue(sprite, out int id)) return id;
            if (!StoreArtBounds.TryGet(sprite.name, out ArtBounds ink)) ink = ArtBounds.Whole;
            entries.Add(new Entry { Sprite = sprite, Ink = ink });
            id = entries.Count;
            ids[sprite] = id;
            return id;
        }

        private static Sprite SpriteOf(int id) => id > 0 && id <= entries.Count ? entries[id - 1].Sprite : null;

        /// <summary>The sprite behind a handle; null for 0, an unknown handle, a sprite the game has since destroyed or one packed into an atlas (its rect is not on its own texture, so the crop table would not fit).</summary>
        public static Sprite Get(int id)
        {
            Sprite sprite = SpriteOf(id);
            return sprite != null && !sprite.packed && sprite.texture != null ? sprite : null;
        }

        /// <summary>
        /// How the picture of <paramref name="id"/> is drawn, and what it is drawn from: the ink box (fractions of the picture, y down) and the picture's size in pixels. A picture not baked yet is queued for
        /// <see cref="Bake"/> and shows as the glyph (<see cref="ArtKind.None"/>) until it is; one that failed is drawn through <see cref="Material"/> when there is one.
        /// </summary>
        public static ArtKind Pick(int id, out ArtBounds ink, out float width, out float height)
        {
            ink = ArtBounds.Whole;
            width = height = 0f;
            Sprite sprite = SpriteOf(id);
            if (sprite == null || sprite.texture == null) return ArtKind.None;
            int key = sprite.GetInstanceID();
            if (masks.TryGet(key, out Baked baked))
            {
                ink = baked.Ink;
                width = baked.Texture.width;
                height = baked.Texture.height;
                return ArtKind.Mask;
            }
            if (!failed.Contains(key))
            {
                Queue(id);
                return ArtKind.None;
            }
            sprite = Get(id);
            if (sprite == null || Material == null) return ArtKind.None;
            ink = entries[id - 1].Ink;
            width = sprite.rect.width;
            height = sprite.rect.height;
            return ArtKind.Additive;
        }

        /// <summary>The baked mask of a picture <see cref="Pick"/> said is <see cref="ArtKind.Mask"/>; null otherwise.</summary>
        public static Texture2D MaskOf(int id)
        {
            Sprite sprite = SpriteOf(id);
            return sprite != null && masks.TryGet(sprite.GetInstanceID(), out Baked baked) ? baked.Texture : null;
        }

        private static void Queue(int id)
        {
            for (int i = 0; i < queued; i++) if (queue[i] == id) return;
            if (queued < queue.Length) queue[queued++] = id;
        }

        /// <summary>
        /// Bake the oldest queued picture, ONE per call (the board calls it once per 10 Hz tick, so a frame never bakes two): a RenderTexture the size of the sprite's rect, the sprite's part of its texture blitted into it (an
        /// atlas-packed sprite works), read back, rewritten as the white mask (<see cref="StoreMask.Convert"/>) and kept in the cache. A bake that throws, or a picture the mask cannot take, is remembered as failed.
        /// </summary>
        public static void Bake()
        {
            while (queued > 0)
            {
                int id = queue[0];
                Array.Copy(queue, 1, queue, 0, --queued);
                Sprite sprite = SpriteOf(id);
                if (sprite == null || sprite.texture == null) continue;
                int key = sprite.GetInstanceID();
                if (failed.Contains(key) || masks.TryGet(key, out _)) continue;
                Baked baked = null;
                try { baked = BakeOne(sprite); }
                catch (Exception e) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.StoreMask", e); }
                if (baked == null) failed.Add(key);
                else Release(masks.Add(key, baked));
                return;
            }
        }

        private static Baked BakeOne(Sprite sprite)
        {
            Texture source = sprite.texture;
            Rect part = sprite.packed ? sprite.textureRect : sprite.rect;
            int w = Mathf.RoundToInt(part.width), h = Mathf.RoundToInt(part.height);
            if (w < 1 || h < 1 || Math.Max(w, h) > StoreMask.MaxSide) return null;
            RenderTexture previous = RenderTexture.active, temporary = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "BoscaliHud_StoreMask_" + sprite.name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            bool ok = false;
            try
            {
                Graphics.Blit(source, temporary, new Vector2(part.width / source.width, part.height / source.height), new Vector2(part.x / source.width, part.y / source.height));
                RenderTexture.active = temporary;
                texture.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                byte[] pixels = texture.GetRawTextureData();
                if (!StoreMask.Convert(pixels, w, h, out ArtBounds ink)) return null;
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, false);
                ok = true;
                return new Baked { Texture = texture, Ink = ink };
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (!ok) UnityEngine.Object.Destroy(texture);
            }
        }

        private static void Release(Baked baked)
        {
            if (baked == null || baked.Texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(baked.Texture);
            else UnityEngine.Object.DestroyImmediate(baked.Texture);      // the offline check runs in the editor outside play mode
        }

        /// <summary>Scene change or teardown: destroy every baked texture and forget the queue and the failures (the sprites of the next scene are others).</summary>
        public static void Clear()
        {
            masks.Clear(Release);
            failed.Clear();
            queued = 0;
        }
    }

    /// <summary>One picture the stores header wants on the board: the sprite's handle (0: this slot is unused), where it goes and which part of the picture it shows (<see cref="ArtPlace"/>), the tone and alpha it is tinted to and which way it is drawn (<see cref="ArtKind"/>: the mask, else the additive fallback).</summary>
    internal readonly struct ArtSlot
    {
        public readonly int Icon;
        public readonly ArtPlace Place;
        public readonly ScopeTone Tone;
        public readonly float Alpha;
        public readonly bool Mask;

        public ArtSlot(int icon, in ArtPlace place, ScopeTone tone, float alpha, bool mask) { Icon = icon; Place = place; Tone = tone; Alpha = alpha; Mask = mask; }
    }
}

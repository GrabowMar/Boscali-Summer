using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The pictures of the stores side: the game's own line art for a weapon (<c>WeaponInfo.weaponIcon</c>) and for a countermeasure (<c>Countermeasure.displayImage</c>). The feed's domain types (<see cref="StoreStation.Icon"/>,
    /// <see cref="CmStation.Icon"/>) carry a picture as an integer handle, 0 for none, so they stay free of Unity; this table, filled by the runtime feed when it first meets a sprite, says which sprite a handle is and the box
    /// its ink lives in (<see cref="StoreArtBounds"/> by the sprite's name, looked up once). The pictures are white lines on an OPAQUE black ground, made for the game's additive material: with any other it paints a black plate,
    /// so the board draws a picture only with <see cref="Material"/> (the one the native weapon panel draws its own with, taken by the board when it builds) and shows the class glyph when there is none.
    /// </summary>
    internal static class StoreIcons
    {
        private struct Entry
        {
            public Sprite Sprite;
            public ArtBounds Ink;
        }

        private static readonly List<Entry> entries = new List<Entry>(80);
        private static readonly Dictionary<Sprite, int> ids = new Dictionary<Sprite, int>(80);

        /// <summary>The game's additive picture material; null until the board has found it (and after it is destroyed): no pictures are drawn then.</summary>
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

        /// <summary>The sprite behind a handle; null for 0, an unknown handle, a sprite the game has since destroyed or one packed into an atlas (its rect is not on its own texture, so the crop table would not fit).</summary>
        public static Sprite Get(int id)
        {
            Sprite sprite = id > 0 && id <= entries.Count ? entries[id - 1].Sprite : null;
            return sprite != null && !sprite.packed && sprite.texture != null ? sprite : null;
        }

        /// <summary>The box the ink of the handle's sprite lives in (the whole sprite for one the table does not know).</summary>
        public static ArtBounds Ink(int id) => id > 0 && id <= entries.Count ? entries[id - 1].Ink : ArtBounds.Whole;
    }

    /// <summary>One picture the stores header wants on the board: the sprite's handle (0: this slot is unused), where it goes and which part of the sprite it shows (<see cref="ArtPlace"/>), and the tone and alpha it is tinted to.</summary>
    internal readonly struct ArtSlot
    {
        public readonly int Icon;
        public readonly ArtPlace Place;
        public readonly ScopeTone Tone;
        public readonly float Alpha;

        public ArtSlot(int icon, in ArtPlace place, ScopeTone tone, float alpha) { Icon = icon; Place = place; Tone = tone; Alpha = alpha; }
    }
}

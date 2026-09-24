using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// What a primitive is allowed to know about the room that hosts it: its inks, its stroke sprite
    /// and its type personality. A primitive never picks a colour or a sprite of its own, so the same
    /// instrument looks native in every room.
    /// </summary>
    internal struct Skin
    {
        /// <summary>Text and marks.</summary>
        public Color Ink;
        /// <summary>The empty part of a track.</summary>
        public Color Track;
        /// <summary>The filled part of a track.</summary>
        public Color Fill;
        /// <summary>Accent: the now cursor, a target tick, the newest sample.</summary>
        public Color Mark;
        /// <summary>A pip or chevron glyph.</summary>
        public Sprite Glyph;
        /// <summary>A pattern for fills that must not rely on colour (hatch, dash); null draws flat.</summary>
        public Sprite Pattern;
        public int FontSize;
        public float Tracking;
        public FontStyles Style;
        /// <summary>Monospaced figures (terminal and typewriter rooms).</summary>
        public bool Mono;
        public bool Dashed;

        public string Text(string value) => Mono && !string.IsNullOrEmpty(value) ? "<mspace=0.6em>" + value : value ?? "";
    }
}

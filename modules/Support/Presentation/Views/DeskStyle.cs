using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// Tactical mission-control surfaces and typography, resolved from the shared stylesheet.
    /// Semantic status colours repeat written state; they never replace it.
    /// </summary>
    internal static class DeskStyle
    {
        public const float EntranceSeconds = 0.40f;

        // Type personality.
        public const float Stencil = 18f;
        public const float StencilSmall = 13f;
        public const float StencilTracking = 2f;
        public const float Typewriter = 12f;
        public const float TypewriterSmall = 11f;
        public const string Mono = "<mspace=0.6em>";

        // Geometry.
        public const float Gutter = 16f;
        public const float BannerHeight = 22f;
        public const float HeaderHeight = 48f;
        public const float TagGap = 12f;
        public const float FolderWidth = 488f;
        public const float TimelineHeight = 138f;
        public const float TokenSize = 36f;

        public static Color Map { get; private set; }
        public static Color MapInk { get; private set; }
        public static Color Paper { get; private set; }
        public static Color Ink { get; private set; }
        public static Color Khaki { get; private set; }
        public static Color Stamp { get; private set; }
        public static Color Banner { get; private set; }
        public static Color BannerInk { get; private set; }
        public static Color Tape { get; private set; }

        /// <summary>Resolve the palette from the stylesheet. Called on every room build.</summary>
        public static void Resolve()
        {
            Map = RoomPaint.Fill("room-specops-map", AvTheme.Ground);
            MapInk = RoomPaint.Ink("room-specops-map", AvTheme.TextPrimary);
            Paper = RoomPaint.Fill("room-specops-paper", AvTheme.SurfaceRaised);
            Ink = RoomPaint.Ink("room-specops-ink", AvTheme.TextPrimary);
            Khaki = RoomPaint.Ink("room-specops-khaki", AvTheme.Dim);
            Stamp = RoomPaint.Ink("room-specops-stamp", AvTheme.RailCaution);
            Banner = RoomPaint.Fill("room-specops-banner", AvTheme.SurfaceRaised);
            BannerInk = RoomPaint.Ink("room-specops-banner", AvTheme.TextPrimary);
            Tape = RoomPaint.Fill("room-specops-tape", AvTheme.SurfaceRaised);
        }

        /// <summary>A stencil title: heavy, widely tracked caps.</summary>
        public static TMP_Text Title(RectTransform parent, string text, Rect area, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text label = AvKit.Label(parent, text, area, color, size, FontStyles.Bold | FontStyles.UpperCase, align);
            label.characterSpacing = StencilTracking;
            return label;
        }

        /// <summary>Typewriter body: monospaced, plain weight. Set text through <see cref="Type"/>.</summary>
        public static TMP_Text Body(RectTransform parent, Rect area, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            TMP_Text label = AvKit.Label(parent, "", area, color, size, FontStyles.Normal, align, wrap);
            label.richText = true;
            return label;
        }

        private static readonly System.Collections.Generic.Dictionary<TMP_Text, string> typed =
            new System.Collections.Generic.Dictionary<TMP_Text, string>(128);

        /// <summary>Typewriter text; rewritten only when it changed.</summary>
        public static void Type(TMP_Text label, string text)
        {
            if (text == null) text = "";
            if (typed.TryGetValue(label, out string shown) && shown == text) return;
            typed[label] = text;
            label.text = text.Length == 0 ? "" : Mono + text;
        }

        /// <summary>Forget typed labels from a previous build.</summary>
        public static void ForgetTyped() => typed.Clear();

        public static Skin Dossier() => new Skin
        {
            Ink = Ink,
            Track = Khaki.WithAlpha(0.3f),
            Fill = Ink,
            Mark = Stamp,
            Glyph = OpsSprites.Glyph(OpsSprites.G.Team),
            Pattern = OpsSprites.Guard,
            FontSize = 11,
            Tracking = 0f,
            Style = FontStyles.Normal,
            Mono = true
        };
    }
}

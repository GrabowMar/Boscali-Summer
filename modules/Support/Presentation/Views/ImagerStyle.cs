using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// SPACE › IMAGER, the sensor operator: no panels, the feed fills the room, symbology is drawn
    /// straight on it in targeting-pod style — monochrome high-contrast ink with a dark halo. The feed
    /// acquires with a brief scan line. Colours come from the <c>.room-imager-*</c> classes.
    /// </summary>
    internal static class ImagerStyle
    {
        public const float EntranceSeconds = 0.22f;

        public const float Osd = 13f;
        public const float Corner = 14f;
        public const float Small = 11f;
        public const float Tracking = 2f;

        public static Color Ink { get; private set; }
        public static Color Dim { get; private set; }
        public static Color Halo { get; private set; }
        public static Color Pod { get; private set; }

        public static void Resolve()
        {
            Ink = RoomPaint.Ink("room-imager-ink", AvTheme.TextPrimary);
            Dim = RoomPaint.Ink("room-imager-dim", AvTheme.Dim);
            Halo = RoomPaint.Fill("room-imager-halo", AvTheme.Ground);
            Pod = RoomPaint.Fill("room-imager-pod", AvTheme.Ground);
        }

        /// <summary>Pod symbology: bold tracked ink with a dark outline so it reads on any image.</summary>
        public static TMP_Text Symbol(RectTransform parent, string text, Rect area, float size,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool dim = false)
        {
            TMP_Text label = AvKit.Label(parent, text, area, dim ? Dim : Ink, size, FontStyles.Bold, align);
            label.characterSpacing = Tracking;
            label.outlineWidth = 0.18f;
            label.outlineColor = (Color32)Halo.WithAlpha(1f);
            return label;
        }

        public static Skin Symbology() => new Skin
        {
            Ink = Ink,
            Track = Ink.WithAlpha(0.25f),
            Fill = Ink,
            Mark = Ink,
            Glyph = OpsSprites.Triangle,
            Pattern = OpsSprites.Scan,
            FontSize = 11,
            Tracking = Tracking,
            Style = FontStyles.Bold,
            Mono = false,
            Dashed = false
        };
    }
}

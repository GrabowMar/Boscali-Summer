using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// SPACE › STATION, the flight-control front wall: deep blue-black with a blueprint grid, star
    /// specks and the Earth's limb glowing along the bottom. Tall, thin, widely tracked caps; one
    /// display-size GET clock. Blueprint strokes draw in, then modules pop onto their ports.
    /// Colours come from the <c>.room-space-*</c> classes.
    /// </summary>
    internal static class StationStyle
    {
        public const float EntranceSeconds = 0.36f;

        // Type personality: thin and tracked; nothing bold except the callsign.
        public const float Display = 25f;
        public const float DisplayTracking = 4f;
        public const float Callsign = 26f;
        public const float Label = 11f;
        public const float LabelTracking = 3f;
        public const float BodyTracking = 3f;

        public static Color Surface { get; private set; }
        public static Color Ink { get; private set; }
        public static Color Dim { get; private set; }
        public static Color Line { get; private set; }
        public static Color Limb { get; private set; }
        public static Color LimbSky { get; private set; }
        public static Color Console { get; private set; }
        public static Color ConsoleEdge { get; private set; }

        public static void Resolve()
        {
            Surface = RoomPaint.Fill("room-space-surface", AvTheme.Ground);
            Ink = RoomPaint.Ink("room-space-ink", AvTheme.TextPrimary);
            Dim = RoomPaint.Ink("room-space-dim", AvTheme.Dim);
            Line = RoomPaint.Ink("room-space-line", AvTheme.RailInfo);
            Limb = RoomPaint.Ink("room-space-limb", AvTheme.RailInfo);
            LimbSky = RoomPaint.Fill("room-space-limb", AvTheme.Surface);
            Console = RoomPaint.Fill("room-space-console", AvTheme.Surface);
            ConsoleEdge = AvStyleHost.Resolve(AvStyleHost.Style("room-space-console").Border, AvTheme.Hairline);
        }

        /// <summary>Tracked caps in the wall's thin voice.</summary>
        public static TMP_Text Text(RectTransform parent, string text, Rect area, float size, Color color,
            float tracking = BodyTracking, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            TMP_Text label = AvKit.Label(parent, text, area, color, size,
                (bold ? FontStyles.Bold : FontStyles.Normal) | FontStyles.UpperCase, align);
            label.characterSpacing = tracking;
            return label;
        }

        public static Skin Telemetry() => new Skin
        {
            Ink = Ink,
            Track = Line.WithAlpha(0.22f),
            Fill = Line,
            Mark = Limb,
            Glyph = null,
            Pattern = OpsSprites.Blueprint,
            FontSize = 11,
            Tracking = LabelTracking,
            Style = FontStyles.Normal,
            Mono = false
        };
    }
}

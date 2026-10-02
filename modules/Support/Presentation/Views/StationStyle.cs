using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>
    /// Expeditionary Command orbital console: cream instrumentation on dark olive surfaces.
    /// Engineering retains its blueprint and module ports; tasking adds equipment imagery.
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
            ConsoleEdge = RoomPaint.Edge("room-space-console", AvTheme.Hairline);
        }

        /// <summary>Tracked caps in the wall's thin voice.</summary>
        public static TMP_Text Text(RectTransform parent, string text, Rect area, float size, Color color,
            float tracking = BodyTracking, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            TMP_Text label = Chrome.Label(parent, text, area, color, size,
                (bold ? FontStyles.Bold : FontStyles.Normal) | FontStyles.UpperCase, align);
            label.characterSpacing = tracking;
            return label;
        }

        /// <summary>Portal corner brackets around a rectangle (Y negative downward).</summary>
        public static UnityEngine.UI.Image Corners(RectTransform parent, Rect area, Color color) =>
            Chrome.Panel(parent, area, color, OpsSprites.Brackets);

        /// <summary>Hazard-stripe fill: the tiled hatch tinted by the fill colour.</summary>
        public static UnityEngine.UI.Image Hazard(UnityEngine.UI.Image fill)
        {
            fill.sprite = OpsSprites.Guard;
            fill.type = UnityEngine.UI.Image.Type.Tiled;
            return fill;
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

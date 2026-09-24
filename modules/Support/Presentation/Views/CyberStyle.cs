using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// CYBER › CONSOLE, the network-ops terminal: near-black with a faint hex lattice, tiled into
    /// panes with title bars and 1 px gutters, everything monospaced, one large block INFOCON
    /// glyph. Panes boot with a few typed lines. Colours come from the <c>.room-cyber-*</c> classes.
    /// </summary>
    internal static class CyberStyle
    {
        public const float EntranceSeconds = 0.30f;
        public const string Mono = "<mspace=0.6em>";

        public const float Block = 64f;
        public const float Body = 15f;
        public const float Small = 13f;
        public const float Micro = 12f;
        public const float TitleBar = 28f;
        public const float Gutter = 1f;
        public const float LinePitch = 20f;

        public static Color Surface { get; private set; }
        public static Color Pane { get; private set; }
        public static Color PaneEdge { get; private set; }
        public static Color Bar { get; private set; }
        public static Color Title { get; private set; }
        public static Color Ink { get; private set; }
        public static Color Dim { get; private set; }
        public static Color Lattice { get; private set; }
        public static Color Accent { get; private set; }
        public static Color BlockInk { get; private set; }

        public static void Resolve()
        {
            Surface = RoomPaint.Fill("room-cyber-surface", AvTheme.Ground);
            Pane = RoomPaint.Fill("room-cyber-pane", AvTheme.Surface);
            PaneEdge = AvStyleHost.Resolve(AvStyleHost.Style("room-cyber-pane").Border, AvTheme.Hairline);
            Bar = RoomPaint.Fill("room-cyber-bar", AvTheme.SurfaceRaised);
            Title = RoomPaint.Ink("room-cyber-title", AvTheme.Accent);
            Ink = RoomPaint.Ink("room-cyber-ink", AvTheme.TextPrimary);
            Dim = RoomPaint.Ink("room-cyber-dim", AvTheme.Dim);
            Lattice = RoomPaint.Ink("room-cyber-lattice", AvTheme.Hairline);
            Accent = RoomPaint.Ink("room-cyber-accent", AvTheme.RailInfo);
            BlockInk = RoomPaint.Ink("room-cyber-block", AvTheme.TextPrimary);
            typed.Clear();
        }

        /// <summary>A monospaced terminal line. Set through <see cref="Type"/>.</summary>
        public static TMP_Text Line(RectTransform parent, Rect area, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            TMP_Text label = AvKit.Label(parent, "", area, color, size, FontStyles.Normal, align, wrap);
            label.richText = true;
            return label;
        }

        private static readonly System.Collections.Generic.Dictionary<TMP_Text, string> typed =
            new System.Collections.Generic.Dictionary<TMP_Text, string>(256);

        /// <summary>Monospaced text; rewritten only when it changed.</summary>
        public static void Type(TMP_Text label, string text)
        {
            if (text == null) text = "";
            if (typed.TryGetValue(label, out string shown) && shown == text) return;
            typed[label] = text;
            label.text = text.Length == 0 ? "" : Mono + text;
        }

        /// <summary>A text bar: <c>[#####-----]</c>, the terminal's own gauge.</summary>
        public static string Bar10(float fraction)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fraction) * 10f), 0, 10);
            return Bars[filled];
        }

        private static readonly string[] Bars =
        {
            "[----------]", "[#---------]", "[##--------]", "[###-------]", "[####------]", "[#####-----]",
            "[######----]", "[#######---]", "[########--]", "[#########-]", "[##########]"
        };

        public static Skin Terminal() => new Skin
        {
            Ink = Ink,
            Track = Lattice,
            Fill = Ink,
            Mark = Accent,
            Glyph = OpsSprites.Cursor,
            Pattern = OpsSprites.Dash,
            FontSize = 12,
            Tracking = 0f,
            Style = FontStyles.Normal,
            Mono = true,
            Dashed = true
        };
    }
}

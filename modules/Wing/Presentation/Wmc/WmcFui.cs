using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The WMC's FUI kit (spec 2026-09-28 FUI §kit), Boscali's heavy instrument look for every page: section headers with a
    /// tick, glyph, note and hairline; cards with a rail and corner brackets; icon buttons; tapes with scale ticks; state badges;
    /// key/value lines. Rects are in the parent's top-left space (y down, negative), as every WMC builder.</summary>
    internal static class WmcFui
    {
        public const float HeaderH = 20f, HeaderStep = 24f, Corner = 7f, IconSize = 12f;

        /// <summary>A section header: tick, glyph, title in the info colour, a muted right-hand note, and a hairline under it.
        /// Returns the note (null when <paramref name="note"/> is null and <paramref name="withNote"/> is false).</summary>
        public static TMP_Text Header(RectTransform p, Rect r, Glyph glyph, string title, string note = null, bool withNote = false,
            string tickClass = "info")
        {
            Image tick = WmcDraw.Rule(p, new Rect(r.x, r.y - 8f, 8f, 2f), AvTheme.RailInfo);
            tick.raycastTarget = false;
            WmcUi.SetRail(tick, tickClass);
            float tx = r.x + 12f;
            if (glyph != Glyph.None)
            {
                Icon(p, new Rect(tx, r.y - 2f, IconSize, IconSize), glyph, TickColor(tickClass));
                tx += IconSize + 5f;
            }
            TMP_Text t = WmcDraw.Label(p, new Rect(tx, r.y, r.width * 0.6f - (tx - r.x), 16f), title, AvTextRole.Head, "section-title", AvTheme.RailInfo);
            TMP_Text n = null;
            if (note != null || withNote)
            {
                n = WmcKit.Text(p, new Rect(r.x + r.width * 0.45f, r.y, r.width * 0.55f, 16f), "row-sub", TextAlignmentOptions.MidlineRight);
                n.text = note ?? "";
                n.color = AvStyleHost.FuiColor("ink-muted", AvTheme.Disabled);
            }
            WmcDraw.Rule(p, new Rect(r.x, r.y - HeaderH + 2f, r.width, 1f), AvStyleHost.FuiColor("hairline", AvTheme.Hairline).WithAlpha(0.6f)).raycastTarget = false;
            return n;
        }

        private static Color TickColor(string cls) =>
            cls == "danger" ? AvTheme.Alert : cls == "warn" || cls == "armed" ? AvTheme.Warning : cls == "live" || cls == "ready" ? AvTheme.Accent
            : cls == "inert" || cls == "locked" ? AvTheme.RailInert : AvTheme.RailInfo;

        /// <summary>A card: surface fill, hairline outline, corner brackets top-left and bottom-right, and a 3 px left rail when
        /// <paramref name="rail"/> is set. Returns the rail (null without one).</summary>
        public static Image Card(RectTransform p, Rect r, string rail = null, bool corners = true)
        {
            Image fill = WmcDraw.Fill(p, r, AvStyleHost.FuiColor("surface", AvTheme.Surface).WithAlpha(0.92f));
            fill.raycastTarget = false;
            WmcDraw.Outline(p, r, AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            if (corners) Corners(p, r, AvStyleHost.FuiColor("info", AvTheme.RailInfo));
            if (rail == null) return null;
            Image railImage = WmcDraw.Rule(p, new Rect(r.x, r.y, 3f, r.height), AvStyleHost.FuiColor("info", AvTheme.RailInfo));
            WmcUi.SetRail(railImage, rail);
            return railImage;
        }

        /// <summary>L-brackets at the top-left and bottom-right corners (Boscali's card marks).</summary>
        public static void Corners(RectTransform p, Rect r, Color c, float len = Corner)
        {
            Rule(p, new Rect(r.x, r.y, len, 1f), c);
            Rule(p, new Rect(r.x, r.y, 1f, len), c);
            Rule(p, new Rect(r.x + r.width - len, r.y - r.height + 1f, len, 1f), c);
            Rule(p, new Rect(r.x + r.width - 1f, r.y - r.height + len, 1f, len), c);
        }

        private static void Rule(RectTransform p, Rect r, Color c) => WmcDraw.Rule(p, r, c).raycastTarget = false;

        /// <summary>A drawn glyph in <paramref name="r"/> (hand-drawn strokes, unchanged: <see cref="AvVector"/>/<see cref="AvStrokes"/>
        /// are the kit's shared low-level mesh helpers in both v1 and v2, not a v1-only primitive).</summary>
        public static WmcIcon Icon(RectTransform p, Rect r, Glyph g, Color c)
        {
            RectTransform rt = WmcDraw.Container(p, "Glyph", r);
            var icon = new WmcIcon(AvVector.Create(rt, "GlyphMesh", 72), r.width, r.height);
            icon.Set(g, c);
            return icon;
        }

        /// <summary>A state word in a 1 px outline over a faint wash of the state colour.</summary>
        public static WmcBadge Badge(RectTransform p, Rect r)
        {
            Image wash = WmcDraw.Fill(p, r, Color.clear);
            wash.raycastTarget = false;
            AvFrame edge = WmcDraw.Outline(p, r, AvStyleHost.FuiColor("frame", AvTheme.Frame));
            TMP_Text t = WmcKit.Text(p, new Rect(r.x + 2f, r.y, r.width - 4f, r.height), "row-sub", TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            return new WmcBadge(wash, edge, t);
        }

        /// <summary>A state class ("live", "warn", "danger", "info", "inert", or a rail class) as a colour.</summary>
        public static Color StateColor(string state) => TickColor(state);
    }

    /// <summary>A drawn glyph: redrawn only when the glyph changes; the colour is the graphic's tint.</summary>
    internal sealed class WmcIcon
    {
        private static readonly float[] Scratch = new float[WmcGlyphs.MaxFloats];
        private static readonly Rgba White = new Rgba(1f, 1f, 1f, 1f);
        private readonly AvVector vector;
        private readonly float w, h;
        private Glyph shown = (Glyph)255;

        public WmcIcon(AvVector vector, float w, float h)
        {
            this.vector = vector;
            this.w = w;
            this.h = h;
        }

        public Color Color
        {
            get => vector.color;
            set { if (vector.color != value) vector.color = value; }
        }

        public void SetVisible(bool on)
        {
            if (vector.gameObject.activeSelf != on) vector.gameObject.SetActive(on);
        }

        public void Set(Glyph g, Color c)
        {
            Color = c;
            if (g == shown) return;
            shown = g;
            AvQuadBuffer b = vector.Buffer;
            b.Clear();
            int n = WmcGlyphs.Segments(g, Scratch);
            float hx = w * 0.5f, hy = h * 0.5f, line = Mathf.Max(1.1f, w / 10f);
            for (int i = 0; i < n; i++)
            {
                int k = i * 4;
                AvStrokes.Line(b, hx + Scratch[k] * (hx - 0.5f), hy + Scratch[k + 1] * (hy - 0.5f),
                    hx + Scratch[k + 2] * (hx - 0.5f), hy + Scratch[k + 3] * (hy - 0.5f), line, White);
            }
            vector.Commit();
        }
    }

    /// <summary>A tape's fill: width by fraction, colour by state; repainted only on change.</summary>
    internal sealed class WmcTape
    {
        private readonly Image fill;
        private readonly float width;
        private int key = int.MinValue;

        public WmcTape(Image fill, float width)
        {
            this.fill = fill;
            this.width = width;
        }

        public void Set(float fraction, Color c)
        {
            float f = WingRows.Bar(fraction);
            int k = Mathf.RoundToInt(f * 1000f) * 31 + c.GetHashCode();
            if (k == key) return;
            key = k;
            RectTransform rt = fill.rectTransform;
            rt.sizeDelta = new Vector2(width * f, rt.sizeDelta.y);
            fill.color = c;
        }
    }

    /// <summary>A state badge; <see cref="Set"/> repaints only on change.</summary>
    internal sealed class WmcBadge
    {
        private readonly Image wash;
        private readonly AvFrame edge;
        private readonly TMP_Text text;
        private string shown;
        private Color color;

        public WmcBadge(Image wash, AvFrame edge, TMP_Text text)
        {
            this.wash = wash;
            this.edge = edge;
            this.text = text;
        }

        public void Set(string word, string state)
        {
            Color c = WmcFui.StateColor(state);
            if (word == shown && c == color) return;
            shown = word;
            color = c;
            text.text = word;
            text.color = c;
            wash.color = c.WithAlpha(0.08f);
            edge.Paint(Color.clear, c.WithAlpha(0.7f));
        }

        public void SetVisible(bool on)
        {
            if (text.gameObject.activeSelf == on) return;
            text.gameObject.SetActive(on);
            wash.gameObject.SetActive(on);
            edge.gameObject.SetActive(on);
        }
    }

    /// <summary>The WMC's cues (spec FUI §motion, game-feel tiers): a punch on an order press, a blink on a new urgent alert, a one-
    /// refresh rail flash on a state change. <c>Wmc/ReduceMotion</c> turns each into its steady state (through
    /// <see cref="AvReveal.Snap"/> for the punch).</summary>
    internal static class WmcMotion
    {
        public const float PunchFrom = 0.94f, PunchSeconds = 0.12f, BlinkSeconds = 3f, BlinkHz = 2f;
        private static AvReveal reveal;

        public static bool Reduced => WingSettings.Instance != null && WingSettings.Instance.ReduceMotion.Value;

        /// <summary>The driver lives on the panel root (built once).</summary>
        public static void Attach(GameObject root)
        {
            if (root != null && reveal == null) reveal = root.AddComponent<AvReveal>();
        }

        public static void Punch(Component target)
        {
            if (reveal == null || target == null) return;
            AvReveal.Snap = Reduced;
            reveal.Punch((RectTransform)target.transform, PunchFrom, PunchSeconds);
        }
    }
}

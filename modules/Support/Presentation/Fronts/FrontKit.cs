using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>Repaint callbacks of every static part of one window; <see cref="Apply"/> re-reads the live style sheet roles.</summary>
    internal sealed class FrontSkin
    {
        private readonly List<Action> parts = new List<Action>(64);

        public void Add(Action restyle) { parts.Add(restyle); restyle(); }

        public void Apply() { for (int i = 0; i < parts.Count; i++) parts[i](); }
    }

    /// <summary>Absolute-placement helpers of the front windows (panel pixels, origin top-left). Colours come from kit roles only.</summary>
    internal static class FrontKit
    {
        public const float HeaderH = 22f;

        /// <summary>A fixed-size mono line in a box; call <see cref="Set"/> to change it.</summary>
        public static TMP_Text Mono(RectTransform p, string name, float x, float y, float w, float h, float size, TextAlignmentOptions a,
            bool strong = false, float tracking = 0f)
        {
            TMP_Text t = C2Kit.Mono(p, name, size, a, strong, tracking);
            AvLay.Place(t.rectTransform, x, y, w, h);
            t.color = AvInk.Ink;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A fixed-size condensed (heading face) line.</summary>
        public static TMP_Text Cond(RectTransform p, string name, float x, float y, float w, float h, float size, TextAlignmentOptions a,
            AvTextRole role = AvTextRole.Head)
        {
            TMP_Text t = C2Kit.Cond(p, name, role, size, a);
            AvLay.Place(t.rectTransform, x, y, w, h);
            t.color = AvInk.Ink;
            t.raycastTarget = false;
            return t;
        }

        public static void Set(TMP_Text t, string s, Color c)
        {
            AvText.Set(t, s);
            if (t.color != c) t.color = c;
        }

        public static Image Solid(RectTransform p, string name, float x, float y, float w, float h, Color c)
        {
            Image i = AvLay.Solid(p, name, c);
            AvLay.Place(i.rectTransform, x, y, w, h);
            return i;
        }

        public static AvFrame Panel(RectTransform p, string name, float x, float y, float w, float h, float chamfer = 0f, float bracket = 0f)
        {
            AvFrame f = AvFrame.Add(p, name, chamfer > 0f ? AvChamfer.Diagonal(chamfer) : default(AvChamfer));
            f.Bracket = bracket;
            AvLay.Place(f.rectTransform, x, y, w, h);
            return f;
        }

        /// <summary>m:ss.</summary>
        public static string Clock(float seconds) => BoscaliSummer.Modules.Support.Domain.Fronts.FrontRules.Clock(seconds);

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        /// <summary>An X of half-size <paramref name="r"/> centred on <paramref name="p"/>.</summary>
        public static void Cross(FrontVector g, Vector2 p, float r, Color k)
        {
            g.Line(p + new Vector2(-r, -r), p + new Vector2(r, r), 2f, k);
            g.Line(p + new Vector2(-r, r), p + new Vector2(r, -r), 2f, k);
        }

        /// <summary>Four corner brackets around a square of half-size <paramref name="r"/> centred on <paramref name="p"/> (the selection mark).</summary>
        public static void Brackets(FrontVector g, Vector2 p, float r, Color k)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector2 q = p + new Vector2(sx * r, sy * r);
                    g.Line(q, q + new Vector2(-sx * 6f, 0f), 1.8f, k);
                    g.Line(q, q + new Vector2(0f, -sy * 6f), 1.8f, k);
                }
        }

        /// <summary>The ink a state word wears (ready green, caution amber, danger red, info neutral).</summary>
        public static Color Tone(AvState s) => s == AvState.Inert ? AvInk.Muted : s == AvState.Info ? AvInk.Key : OpsInk.Word(s);
    }

    /// <summary>How a box is dressed: the shared rail stays <see cref="Plain"/>; each room wears its own console so the three rooms read as three places.</summary>
    internal enum FrontLook
    {
        Plain,
        /// <summary>ORBIT, mission control: dark label plates on steel bezels, an amber console-position tag.</summary>
        Console,
        /// <summary>NETWORK, intrusion terminal: phosphor title bar on green-black glass with scanlines.</summary>
        Terminal,
        /// <summary>SHADOW, dossier: a manila folder tab and folder edge on a khaki board, square paper corners.</summary>
        Folder,
    }

    /// <summary>
    /// A titled box: chamfered frame with brackets, a header band (icon, stencil title, right-hand meta) and a body rect.
    /// The same chrome frames every section of the rail and every pane of a room; a room passes its <see cref="FrontLook"/>.
    /// </summary>
    internal sealed class FrontBox
    {
        private const float TagH = 14f;
        private static Texture2D scan;

        private readonly AvFrame frame;
        private readonly Image band, rule, rail, tagPlate;
        private readonly TMP_Text title, meta, icon, tag;
        private readonly FrontVector dress;
        private readonly FrontLook look;
        private readonly float textX;
        private Color metaTone;
        private bool laidOut;

        public RectTransform Rect { get; }
        public float Width { get; }
        public float Height { get; }

        public FrontBox(RectTransform parent, FrontSkin skin, string name, float x, float y, float w, float h, string heading, AvIcon glyph = AvIcon.None,
            FrontLook look = FrontLook.Plain)
        {
            Width = w; Height = h; this.look = look;
            Rect = AvLay.Child(parent, "Box " + name);
            AvLay.Place(Rect, x, y, w, h);
            bool paper = look == FrontLook.Folder;
            frame = FrontKit.Panel(Rect, "Frame", 0f, 0f, w, h, paper ? 0f : 6f, paper ? 0f : 7f);
            if (look == FrontLook.Terminal) Scanlines(Rect, w, h);
            if (look != FrontLook.Plain)
            {
                dress = FrontVector.Add(Rect, "Dress");
                AvLay.Place(dress.rectTransform, 0f, 0f, w, h);
            }
            band = FrontKit.Solid(Rect, "Band", 1f, 1f, w - 2f, FrontKit.HeaderH - 1f, Color.clear);
            rail = FrontKit.Solid(Rect, "Rail", 1f, 1f, 3f, FrontKit.HeaderH - 1f, Color.clear);
            rule = FrontKit.Solid(Rect, "Rule", 1f, FrontKit.HeaderH, w - 2f, paper ? 2f : 1f, Color.clear);
            textX = 10f;
            if (glyph != AvIcon.None)
            {
                icon = AvIcons.Make(Rect, glyph, 14f, AvInk.Dim);
                AvLay.Place(icon.rectTransform, 8f, 4f, 14f, 14f);
                textX = 28f;
            }
            if (look == FrontLook.Console) tagPlate = FrontKit.Solid(Rect, "TagPlate", textX, 4f, 0f, TagH, Color.clear);
            if (look != FrontLook.Plain)
                tag = FrontKit.Mono(Rect, "Tag", textX, 0f, 0f, FrontKit.HeaderH, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            title = FrontKit.Cond(Rect, "Title", textX, 0f, w * 0.55f, FrontKit.HeaderH, 12.5f, TextAlignmentOptions.MidlineLeft);
            title.characterSpacing = 3f;
            title.text = heading;
            meta = FrontKit.Mono(Rect, "Meta", w * 0.45f - 8f, 0f, w * 0.55f, FrontKit.HeaderH, 10.5f, TextAlignmentOptions.MidlineRight);
            Layout();
            skin.Add(Restyle);
        }

        public void SetMeta(string text, Color tone) { metaTone = tone; FrontKit.Set(meta, text, tone); }

        /// <summary>
        /// The look's tag beside the title: ORBIT a console position on an amber plate ("FDO"), NETWORK the terminal it runs on ("tty1"),
        /// SHADOW the file number typed on the folder tab ("FILE 07-S"). Ignored on a plain box.
        /// </summary>
        public FrontBox Tagged(string text)
        {
            if (tag == null) return this;
            tag.text = text ?? "";
            laidOut = false;
            Layout();
            return this;
        }

        // TMP mis-measures while the window is still hidden (built inactive): the first Restyle after Show lays the header out,
        // and a box measured once keeps its layout while its room is switched off.
        private void Layout()
        {
            if (laidOut || !Rect.gameObject.activeInHierarchy) return;
            laidOut = true;
            float x = textX;
            if (tag != null && tag.text.Length > 0)
            {
                float tw = C2Kit.Width(tag, tag.text) + 2f;
                if (look == FrontLook.Console)
                {
                    AvLay.Place(tagPlate.rectTransform, x - 3f, 4f, tw + 6f, TagH);
                    AvLay.Place(tag.rectTransform, x, 0f, tw, FrontKit.HeaderH);
                    x += tw + 12f;
                }
                else if (look == FrontLook.Terminal)
                {
                    AvLay.Place(tag.rectTransform, x, 0f, tw, FrontKit.HeaderH);
                    x += tw + 8f;
                }
            }
            float titleW = C2Kit.Width(title, title.text) + 2f;
            AvLay.Place(title.rectTransform, x, 0f, Mathf.Max(titleW, Width * 0.55f - (x - textX)), FrontKit.HeaderH);
            if (look != FrontLook.Folder) return;

            // The folder tab carries the title and the typed file number; its slanted end is drawn by the dress.
            float tabEnd = x + titleW + 10f;
            if (tag != null && tag.text.Length > 0)
            {
                float tw = C2Kit.Width(tag, tag.text) + 2f;
                AvLay.Place(tag.rectTransform, tabEnd + 4f, 0f, tw, FrontKit.HeaderH);
                tabEnd += tw + 14f;
            }
            AvLay.Place(band.rectTransform, 1f, 1f, tabEnd - 1f, FrontKit.HeaderH - 1f);
            dress.Clear();
            dress.Tri(new Vector2(tabEnd, 1f), new Vector2(tabEnd + 12f, FrontKit.HeaderH), new Vector2(tabEnd, FrontKit.HeaderH), RoomInk.Manila);
            dress.Flush();
        }

        private void Restyle()
        {
            Layout();
            switch (look)
            {
                case FrontLook.Console:
                    frame.Paint(RoomInk.ConsoleBody, RoomInk.Bezel.WithAlpha(0.9f));
                    frame.BracketColor = RoomInk.Bezel;
                    band.color = RoomInk.Plate;
                    rail.color = RoomInk.Telemetry;
                    rule.color = RoomInk.Bezel.WithAlpha(0.8f);
                    title.color = RoomInk.Engrave;
                    tagPlate.color = RoomInk.Telemetry;
                    tag.color = RoomInk.Plate.WithAlpha(1f);
                    if (icon != null) icon.color = RoomInk.Telemetry;
                    Bezel();
                    break;
                case FrontLook.Terminal:
                    frame.Paint(RoomInk.Crt, RoomInk.PhosphorDim.WithAlpha(0.55f));
                    frame.BracketColor = RoomInk.PhosphorDim;
                    band.color = RoomInk.CrtBar;
                    rail.color = RoomInk.Phosphor;
                    rule.color = RoomInk.PhosphorDim.WithAlpha(0.7f);
                    title.color = RoomInk.Phosphor;
                    tag.color = RoomInk.PhosphorDim;
                    if (icon != null) icon.color = RoomInk.Phosphor;
                    break;
                case FrontLook.Folder:
                    frame.Paint(RoomInk.Board, RoomInk.Khaki.WithAlpha(0.8f));
                    frame.BracketColor = Color.clear;
                    band.color = RoomInk.Manila;
                    rail.color = RoomInk.Grease;
                    rule.color = RoomInk.Manila.WithAlpha(0.85f);
                    title.color = RoomInk.Typewriter;
                    tag.color = RoomInk.Typewriter;
                    if (icon != null) icon.color = RoomInk.Typewriter;
                    break;
                default:
                    frame.Paint(AvInk.Surface.WithAlpha(0.62f), AvInk.Hairline);
                    frame.BracketColor = AvInk.Frame.WithAlpha(0.8f);
                    band.color = AvInk.Raised.WithAlpha(0.7f);
                    rail.color = AvInk.Select;
                    rule.color = AvInk.Frame.WithAlpha(0.55f);
                    title.color = AvInk.Ink;
                    if (icon != null) icon.color = AvInk.Dim;
                    break;
            }
            Color quiet = look == FrontLook.Terminal ? RoomInk.PhosphorDim : look == FrontLook.Console ? RoomInk.Engrave.WithAlpha(0.75f) : AvInk.Dim;
            if (meta != null) meta.color = metaTone.a > 0f ? metaTone : quiet;
        }

        /// <summary>The console bezel: an inner hairline and two screw heads at the foot of the plate.</summary>
        private void Bezel()
        {
            dress.Clear();
            dress.Frame(3f, FrontKit.HeaderH + 3f, Width - 6f, Height - FrontKit.HeaderH - 6f, 1f, RoomInk.Bezel.WithAlpha(0.35f));
            for (int s = 0; s < 2; s++)
            {
                var c = new Vector2(s == 0 ? 7f : Width - 7f, Height - 7f);
                dress.Disc(c, 2.6f, RoomInk.Bezel, 14);
                dress.Line(c + new Vector2(-1.6f, -1.6f), c + new Vector2(1.6f, 1.6f), 0.9f, RoomInk.Plate.WithAlpha(1f));
            }
            dress.Flush();
        }

        /// <summary>CRT scanlines over the terminal glass (under the pane's own content): one faint phosphor row in three.</summary>
        private static void Scanlines(RectTransform box, float w, float h)
        {
            if (scan == null)
            {
                scan = new Texture2D(1, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                Color line = RoomInk.Phosphor.WithAlpha(0.045f);
                scan.SetPixels(new[] { line, Color.clear, Color.clear });
                scan.Apply(false, true);
            }
            var go = new GameObject("Scanlines", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(box, false);
            var raw = go.AddComponent<RawImage>();
            raw.texture = scan;
            raw.raycastTarget = false;
            float bh = h - FrontKit.HeaderH - 2f;
            raw.uvRect = new Rect(0f, 0f, 1f, bh / 3f);
            AvLay.Place(raw.rectTransform, 1f, FrontKit.HeaderH + 1f, w - 2f, bh);
        }
    }

    /// <summary>A flat progress bar: dark well, coloured fill, optional end ticks. Fill and colour are set per paint.</summary>
    internal sealed class FrontBar
    {
        private readonly Image well, fill;
        private readonly float w, h;
        private float shown = -1f;
        private Color tone = Color.clear;

        public FrontBar(RectTransform parent, FrontSkin skin, float x, float y, float width, float height)
        {
            w = width; h = height;
            well = FrontKit.Solid(parent, "BarWell", x, y, width, height, Color.clear);
            fill = FrontKit.Solid(parent, "BarFill", x + 1f, y + 1f, 0f, height - 2f, Color.clear);
            skin.Add(() => well.color = AvInk.Ground.WithAlpha(0.92f));
        }

        public void Set(float fraction, Color color)
        {
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Abs(fraction - shown) > 0.0005f)
            {
                shown = fraction;
                fill.rectTransform.sizeDelta = new Vector2(Mathf.Round((w - 2f) * fraction), h - 2f);
            }
            if (tone != color) { tone = color; fill.color = color; }
        }

        public void Show(bool on)
        {
            if (well.gameObject.activeSelf != on) { well.gameObject.SetActive(on); fill.gameObject.SetActive(on); }
        }
    }
}

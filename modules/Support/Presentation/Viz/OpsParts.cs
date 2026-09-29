using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// One labelled instrument bar: NAME · bar · mono value · state word. Replaces the double-printed
    /// resource rows: the name is printed once, the figure once, the state as a word.
    /// </summary>
    internal sealed class MeterRow : AvPart
    {
        private const float H = 26f, LabelW = 74f, ValueW = 104f, WordW = 88f, Gap = 8f, BarH = 8f;
        private readonly OpsCanvas bar;
        private readonly TMP_Text label, value, word;
        private float fill = -1f, width;
        private AvState state = AvState.Info;
        private bool dirty = true;

        public MeterRow(RectTransform parent, string labelText)
        {
            Rect = AvLay.Child(parent, "Meter " + labelText);
            bar = OpsCanvas.Add(Rect, "Bar");
            label = OpsText.Line(Rect, "Label", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            value = OpsText.Line(Rect, "Value", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
            word = OpsText.Line(Rect, "Word", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            label.text = labelText;
            Restyle();
        }

        public void Set(float fill01, string valueText, string wordText, AvState st)
        {
            fill01 = Mathf.Clamp01(float.IsNaN(fill01) ? 0f : fill01);
            OpsText.Set(value, valueText);
            bool stateChanged = st != state;
            state = st;
            bool wordChanged = OpsText.Set(word, AvStates.Glyph(st) + (wordText ?? ""));
            if (stateChanged || wordChanged) RestyleText();
            if (stateChanged || Mathf.Abs(fill01 - fill) > 0.002f) dirty = true;
            fill = fill01;
            Paint();
        }

        public void SetLabel(string text) => OpsText.Set(label, text);

        public override float Measure(float w) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place((RectTransform)bar.transform, 0f, 0f, s.W, H);
            OpsText.Place(label, 0f, 0f, LabelW, H);
            OpsText.Place(word, s.W - WordW, 0f, WordW, H);
            OpsText.Place(value, s.W - WordW - Gap - ValueW, 0f, ValueW, H);
            dirty = true;
            Paint();
        }

        public override void Restyle() { RestyleText(); dirty = true; Paint(); }

        private void RestyleText()
        {
            label.color = OpsInk.Dim;
            value.color = OpsInk.Ink;
            word.color = OpsInk.Word(state);
        }

        private void Paint()
        {
            if (!dirty || width <= 0f) return;
            dirty = false;
            float x = LabelW + Gap, w = Mathf.Max(20f, width - WordW - Gap - ValueW - Gap - x), y = (H - BarH) * 0.5f;
            bar.Begin();
            bar.Box(x, y, w, BarH, OpsInk.A(OpsInk.Hairline, 0.55f));
            float f = Mathf.Max(0f, fill);
            if (f > 0f)
            {
                Color c = OpsInk.Rail(state == AvState.Inert ? AvState.Info : state);
                bar.BoxH(x, y, Mathf.Max(2f, w * f), BarH, OpsInk.A(c, 0.55f), c);
            }
            for (int i = 1; i < 4; i++) bar.Box(x + w * i * 0.25f - 0.5f, y, 1f, BarH, OpsInk.A(OpsInk.Sunken, 0.9f));
            bar.End();
        }
    }

    /// <summary>
    /// A compact banner: a state rail, one headline word-line and an optional wrapped body, with an
    /// optional control on the right (ABORT while armed). The one empty-state card and the one
    /// advice line of a page. Height follows the wrapped text.
    /// </summary>
    internal sealed class BriefCard : AvPart
    {
        private const float PadX = 12f, PadY = 8f, ControlW = 92f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text head, body;
        private AvControl control;
        private AvState state = AvState.Inert;

        public BriefCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Brief");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            head = OpsText.Block(Rect, "Head", AvTextRole.Label);
            body = OpsText.Block(Rect, "Body", AvTextRole.ProseSmall);
            Restyle();
        }

        public AvControl AddControl(AvControl.Spec spec)
        {
            control = AvControl.Make(Rect, spec);
            control.Rect.gameObject.SetActive(false);
            return control;
        }

        public AvControl Control => control;

        public void ShowControl(bool shown)
        {
            if (control == null || control.Rect.gameObject.activeSelf == shown) return;
            control.Rect.gameObject.SetActive(shown);
            Changed();
        }

        public void Set(string headline, string text, AvState tone)
        {
            bool grew = OpsText.Set(head, headline);
            grew |= OpsText.Set(body, text);
            if (tone != state) { state = tone; Restyle(); }
            if (grew) Changed();
        }

        private bool ControlOn => control != null && control.Rect.gameObject.activeSelf;
        private float TextW(float w) => w - PadX - 10f - (ControlOn ? ControlW + 8f : 0f);

        public override float Measure(float width)
        {
            float w = TextW(width);
            float h = PadY + AvText.Height(head, w) + (body.text.Length > 0 ? 2f + AvText.Height(body, w) : 0f) + PadY;
            return Mathf.Max(ControlOn ? 40f : 32f, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W), hh = AvText.Height(head, w);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            OpsText.Place(head, PadX, PadY, w, hh);
            OpsText.Place(body, PadX, PadY + hh + 2f, w, body.text.Length > 0 ? AvText.Height(body, w) : 0f);
            if (control != null) AvLay.Place(control.Rect, s.W - ControlW - 8f, (s.H - 26f) * 0.5f, ControlW, 26f);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert), Color.clear);
            rail.color = OpsInk.Rail(state);
            head.color = state == AvState.Inert ? OpsInk.Ink : OpsInk.Word(state);
            body.color = OpsInk.Dim;
            control?.Restyle();
        }
    }

    /// <summary>
    /// A small newest-first tape written from a caller-owned ring: mono timestamp column, message that
    /// wraps, alarms marked with a danger glyph and colour. Rows without text take no space.
    /// </summary>
    internal sealed class LogTape : AvPart
    {
        private const float TimeW = 72f, Gap = 8f;
        private readonly TMP_Text[] stamps, messages;
        private readonly bool[] alarms;
        private readonly string[] shown;
        private readonly bool terminal;

        public LogTape(RectTransform parent, int count, bool terminal = false)
        {
            Rect = AvLay.Child(parent, "Log");
            this.terminal = terminal;
            stamps = new TMP_Text[count];
            messages = new TMP_Text[count];
            alarms = new bool[count];
            shown = new string[count];
            for (int i = 0; i < count; i++)
            {
                stamps[i] = OpsText.Line(Rect, "Time" + i, AvTextRole.DataSmall, TextAlignmentOptions.TopLeft);
                messages[i] = OpsText.Block(Rect, "Line" + i, terminal ? AvTextRole.DataSmall : AvTextRole.ProseSmall);
            }
            Restyle();
        }

        public void Write(string[] source)
        {
            bool changed = false;
            for (int i = 0; i < messages.Length; i++)
            {
                string v = source != null && i < source.Length ? source[i] : null;
                if (shown[i] == v) continue;
                shown[i] = v;
                changed = true;
                Split(v, out string stamp, out string message, out bool alarm);
                alarms[i] = alarm;
                stamps[i].text = stamp;
                messages[i].text = alarm ? AvStates.Glyph(AvState.Danger) + message : message;
            }
            if (!changed) return;
            Restyle();
            Changed();
        }

        private static void Split(string line, out string stamp, out string message, out bool alarm)
        {
            stamp = ""; message = ""; alarm = false;
            if (string.IsNullOrEmpty(line)) return;
            int cut = line.IndexOf("  ", StringComparison.Ordinal);
            stamp = cut > 0 ? line.Substring(0, cut) : "";
            message = cut > 0 ? line.Substring(cut + 2) : line;
            if (message.StartsWith("!! ", StringComparison.Ordinal)) { alarm = true; message = message.Substring(3); }
        }

        private float RowH(int i, float w) => messages[i].text.Length == 0 ? 0f : Mathf.Max(18f, AvText.Height(messages[i], w - TimeW - Gap) + 2f);

        public override float Measure(float width)
        {
            float h = 0f;
            for (int i = 0; i < messages.Length; i++) h += RowH(i, width);
            return h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f;
            for (int i = 0; i < messages.Length; i++)
            {
                float rh = RowH(i, s.W);
                OpsText.Place(stamps[i], 0f, y + 1f, TimeW, Mathf.Max(0f, rh - 1f));
                OpsText.Place(messages[i], TimeW + Gap, y + 1f, s.W - TimeW - Gap, Mathf.Max(0f, rh - 1f));
                y += rh;
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < messages.Length; i++)
            {
                stamps[i].color = OpsInk.Muted;
                messages[i].color = alarms[i] ? OpsInk.Word(AvState.Danger) : i == 0 ? OpsInk.Ink : OpsInk.Dim;
            }
        }
    }

    /// <summary>
    /// The station's fitted modules as a two-column roster: state rail, code, name, state word.
    /// Pooled to <see cref="MaxCells"/>; only the first <c>count</c> cells take space.
    /// </summary>
    internal sealed class RosterGrid : AvPart
    {
        public const int MaxCells = 16;
        private const float CellH = 26f, Gap = 6f, CodeW = 34f, StateW = 74f;
        private readonly Image[] backs = new Image[MaxCells], rails = new Image[MaxCells];
        private readonly TMP_Text[] codes = new TMP_Text[MaxCells], names = new TMP_Text[MaxCells], states = new TMP_Text[MaxCells];
        private readonly AvState[] tones = new AvState[MaxCells];
        private int count;

        public RosterGrid(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Roster");
            for (int i = 0; i < MaxCells; i++)
            {
                backs[i] = AvLay.Solid(Rect, "Back" + i, Color.clear);
                rails[i] = AvLay.Solid(Rect, "Rail" + i, Color.clear);
                codes[i] = OpsText.Line(Rect, "Code" + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
                names[i] = OpsText.Line(Rect, "Name" + i, AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
                states[i] = OpsText.Line(Rect, "State" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
                Show(i, false);
            }
            Restyle();
        }

        public void Set(int index, string code, string name, string state, AvState tone)
        {
            if (index < 0 || index >= MaxCells) return;
            codes[index].text = code ?? "";
            names[index].text = name ?? "";
            states[index].text = AvStates.Glyph(tone) + (state ?? "");
            if (tones[index] != tone) { tones[index] = tone; RestyleCell(index); }
        }

        public void SetCount(int n)
        {
            n = Mathf.Clamp(n, 0, MaxCells);
            if (n == count) return;
            count = n;
            for (int i = 0; i < MaxCells; i++) Show(i, i < count);
            for (int i = 0; i < count; i++) RestyleCell(i);
            Changed();
        }

        private void Show(int i, bool on)
        {
            backs[i].gameObject.SetActive(on); rails[i].gameObject.SetActive(on);
            codes[i].gameObject.SetActive(on); names[i].gameObject.SetActive(on); states[i].gameObject.SetActive(on);
        }

        private int Rows => (count + 1) / 2;

        public override float Measure(float width) => Rows == 0 ? 0f : Rows * CellH + (Rows - 1) * Gap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float cw = (s.W - Gap) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x = (i % 2) * (cw + Gap), y = (i / 2) * (CellH + Gap);
                AvLay.Place(backs[i].rectTransform, x, y, cw, CellH);
                AvLay.Place(rails[i].rectTransform, x, y, 2f, CellH);
                OpsText.Place(codes[i], x + 10f, y, CodeW, CellH);
                OpsText.Place(names[i], x + 10f + CodeW + 4f, y, cw - 10f - CodeW - 4f - StateW - 6f, CellH);
                OpsText.Place(states[i], x + cw - StateW - 6f, y, StateW, CellH);
            }
        }

        public override void Restyle() { for (int i = 0; i < MaxCells; i++) RestyleCell(i); }

        private void RestyleCell(int i)
        {
            backs[i].color = OpsInk.Inert;
            rails[i].color = OpsInk.Rail(tones[i]);
            codes[i].color = OpsInk.Key;
            names[i].color = OpsInk.Ink;
            states[i].color = OpsInk.Word(tones[i]);
        }
    }

    /// <summary>
    /// The one ACTIONS-page unit: an icon plate, the ability's name, the reason or readiness in words,
    /// the cost in mono with its unit and up to two controls. Replaces the plain list row so every
    /// ACTIONS page reads as the same set of tiles. Height follows the wrapped text.
    /// </summary>
    internal sealed class ActionTile : AvPart
    {
        private const float PadX = 10f, PadY = 8f, Plate = 34f, ValueW = 62f, TrailW = 60f, TrailH = 26f, Min = 52f;
        private readonly AvFrame frame, plate;
        private readonly Image rail;
        private readonly TMP_Text icon, name, sub, value, unit;
        private readonly List<AvControl> trailing = new List<AvControl>(2);
        private AvState state = AvState.Info;
        private bool hover, armed, dim;
        private AvIcon glyph = AvIcon.None;

        public ActionTile(RectTransform parent, AvIcon iconKind)
        {
            Rect = AvLay.Child(parent, "Tile");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            plate = AvFrame.Add(Rect, "Plate", AvChamfer.Diagonal(6f));
            glyph = iconKind;
            icon = AvIcons.Make(Rect, iconKind, AvGridTokens.IconTool, Color.white);
            name = OpsText.Block(Rect, "Name", AvTextRole.Label);
            sub = OpsText.Block(Rect, "Sub", AvTextRole.ProseSmall);
            value = OpsText.Line(Rect, "Value", AvTextRole.DataStrong, TextAlignmentOptions.TopRight);
            unit = OpsText.Line(Rect, "Unit", AvTextRole.Micro, TextAlignmentOptions.TopRight);
            AvHit hit = AvHit.On(frame);
            hit.Hover = h => { hover = h; Restyle(); };
            Restyle();
        }

        /// <summary>Hover help shown in the console footer.</summary>
        public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

        public bool Armed { get => armed; set { if (armed == value) return; armed = value; Restyle(); } }

        /// <summary>Muted name: the ability cannot be used right now.</summary>
        public bool Dim { get => dim; set { if (dim == value) return; dim = value; Restyle(); } }

        public void Set(string n, string s, string v, string u, AvState st, AvIcon kind)
        {
            bool grew = OpsText.Set(name, n);
            grew |= OpsText.Set(sub, s);
            bool valueChanged = OpsText.Set(value, v);
            OpsText.Set(unit, u);
            grew |= valueChanged && (value.text.Length == 0 || string.IsNullOrEmpty(v));
            if (kind != glyph) { glyph = kind; AvIcons.Set(icon, kind, AvGridTokens.IconTool); }
            if (st != state) { state = st; Restyle(); }
            if (grew) Changed();
        }

        public AvControl AddTrailing(AvControl.Spec spec)
        {
            if (trailing.Count >= 2) return null;
            AvControl c = AvControl.Make(Rect, spec);
            trailing.Add(c);
            return c;
        }

        private float TextW(float w) => w - PadX - Plate - 12f - PadX - (value.text.Length > 0 ? ValueW + 4f : 0f) - trailing.Count * (TrailW + 4f);

        public override float Measure(float width)
        {
            float w = TextW(width);
            float h = PadY + AvText.Height(name, w) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, w) : 0f) + PadY;
            return Mathf.Max(Min, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W), nh = AvText.Height(name, w), x0 = PadX + Plate + 12f;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(plate.rectTransform, PadX + 2f, (s.H - Plate) * 0.5f, Plate, Plate);
            AvLay.Place(icon.rectTransform, PadX + 2f + (Plate - 20f) * 0.5f, (s.H - 20f) * 0.5f, 20f, 20f);
            OpsText.Place(name, x0, PadY, w, nh);
            OpsText.Place(sub, x0, PadY + nh + 2f, w, sub.text.Length > 0 ? AvText.Height(sub, w) : 0f);
            float x = s.W - PadX;
            for (int i = trailing.Count - 1; i >= 0; i--)
            {
                x -= TrailW;
                AvLay.Place(trailing[i].Rect, x, (s.H - TrailH) * 0.5f, TrailW, TrailH);
                x -= 4f;
            }
            if (value.text.Length > 0)
            {
                OpsText.Place(value, x - ValueW, PadY, ValueW, 18f);
                OpsText.Place(unit, x - ValueW, PadY + 18f, ValueW, 15f);
            }
        }

        public override void Restyle()
        {
            string st = armed ? "armed" : hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            Color hue = OpsInk.Rail(state);
            rail.color = armed ? OpsInk.Select : hue;
            plate.Paint(OpsInk.A(hue, 0.14f), OpsInk.A(hue, 0.6f));
            icon.color = dim && state == AvState.Inert ? OpsInk.Muted : OpsInk.Word(state == AvState.Inert ? AvState.Info : state);
            name.color = dim ? OpsInk.Muted : OpsInk.Ink;
            sub.color = OpsInk.Dim;
            value.color = dim ? OpsInk.Muted : OpsInk.Word(state == AvState.Inert ? AvState.Info : state);
            unit.color = OpsInk.Muted;
            foreach (AvControl c in trailing) c.Restyle();
        }
    }
}

using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Words for a stance's four combat settings (TARGETS · RANGE · RADAR · WEAPONS) in the width of a stance slot.</summary>
    internal static class StanceWords
    {
        public static readonly string[] Targets = { "HOLD", "AIR", "GND", "BOTH", "COVER" };
        public static readonly string[] Reach = { "6K", "12K" };
        public static readonly string[] Radar = { "ON", "SILENT", "OFF" };
        public static readonly string[] Weapons = { "AUTO", "MSL", "GUN", "NO A-G" };

        private static string Pick(string[] words, byte v) => v < words.Length ? words[v] : "?";

        /// <summary>"AIR · 12K · SILENT" (HOLD · 6K · ON for a quiet stance); a weapons limit is added: "· MSL".</summary>
        public static string Summary(byte[] axes)
        {
            if (axes == null || axes.Length < Stance.AxisCount) return "";
            string s = Pick(Targets, axes[(int)DoctrineAxis.Targets]) + " · " + Pick(Reach, axes[(int)DoctrineAxis.Reach]) + " · "
                + Pick(Radar, axes[(int)DoctrineAxis.Radar]);
            byte w = axes[(int)DoctrineAxis.Weapons];
            return w == 0 ? s : s + " · " + Pick(Weapons, w);
        }
    }

    /// <summary>ORDERS' six stance slots (mockup board/orders.html .slots): 3 × 2 plates, each the wing-key hint (W+1 … W+6), the stance's
    /// name and its settings in a line. The slot the scope flies is lit; with settings changed from it, an amber * follows its name.
    /// Kit gap: a plate with three lines of text is not a kit control, so it is drawn from kit primitives.</summary>
    internal sealed class WmcStanceSlots : AvPart
    {
        public const int Count = StanceBook.Slots;
        private const float CellH = 40f, Gap = 3f;

        private sealed class Slot
        {
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Hint, Name, Detail;
            public bool On, Edited, Hover, Filled;
        }

        private readonly Slot[] slots = new Slot[Count];
        private readonly Action<int> pick;

        public WmcStanceSlots(RectTransform parent, Action<int> picked, Action<int, AvHit> bind)
        {
            Rect = AvLay.Child(parent, "StanceSlots");
            pick = picked;
            for (int i = 0; i < Count; i++)
            {
                int k = i;
                var s = new Slot();
                s.Frame = AvFrame.Add(Rect, "Slot" + i, AvChamfer.Diagonal(4f));
                AvHit hit = AvHit.On(s.Frame);
                hit.Hover = h => { s.Hover = h; Style(s); };
                hit.Click = e => pick(k);
                bind?.Invoke(k, hit);
                s.Rail = AvLay.Solid(Rect, "SlotRail" + i, Color.clear);
                s.Hint = Text("Hint" + i, AvTextRole.DataSmall, TextAlignmentOptions.TopLeft);
                s.Hint.text = "W+" + (i + 1);
                s.Name = Text("Name" + i, AvTextRole.Label, TextAlignmentOptions.TopLeft);
                s.Detail = Text("Detail" + i, AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft);
                slots[i] = s;
            }
            Restyle();
        }

        public Transform Target(int i) => slots[i].Frame.transform;

        public void SetHelp(int i, string tip) => AvHelpTip.Attach(slots[i].Frame.gameObject, tip);

        private TMP_Text Text(string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(Rect, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        public override float Measure(float width) => 2f * CellH + Gap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, 3, Gap);
            for (int i = 0; i < Count; i++)
            {
                float x = i % 3 * (w + Gap), y = i / 3 * (CellH + Gap);
                Slot c = slots[i];
                AvLay.Place(c.Frame.rectTransform, x, y, w, CellH);
                AvLay.Place(c.Rail.rectTransform, x, y, 2f, CellH);
                AvLay.Place(c.Hint.rectTransform, x + 7f, y + 3f, 30f, 14f);
                AvLay.Place(c.Name.rectTransform, x + 38f, y + 2f, w - 42f, 17f);
                AvLay.Place(c.Detail.rectTransform, x + 38f, y + 20f, w - 42f, 16f);
            }
        }

        /// <summary>Slot <paramref name="i"/>: its stance's name and settings, whether it is the one flown (<paramref name="on"/>) and
        /// whether the scope has changed settings from it.</summary>
        public void Set(int i, string name, string detail, bool filled, bool on, bool edited)
        {
            Slot s = slots[i];
            WmcKit.Set(s.Name, filled ? name + (on && edited ? " *" : "") : "—");
            WmcKit.Set(s.Detail, detail);
            if (s.On == on && s.Edited == edited && s.Filled == filled) return;
            s.On = on;
            s.Edited = edited;
            s.Filled = filled;
            Style(s);
        }

        private void Style(Slot s)
        {
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo), caution = AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
            Color accent = AvStyleHost.FuiColor("select", AvTheme.Accent);
            Color surface = AvStyleHost.FuiColor("surface", AvTheme.Surface);
            Color back = s.On ? Color.Lerp(surface, s.Edited ? caution : accent, 0.16f) : s.Hover ? Color.Lerp(surface, Color.white, 0.07f) : surface;
            Color border = s.On ? (s.Edited ? caution : accent) : AvStyleHost.FuiColor("frame", AvTheme.Frame);
            s.Frame.Paint(back, border);
            s.Rail.color = s.On ? (s.Edited ? caution : accent) : Color.clear;
            s.Hint.color = key;
            s.Name.color = s.Filled ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary) : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            s.Detail.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        }

        public override void Restyle()
        {
            foreach (Slot s in slots) Style(s);
        }
    }

    /// <summary>One FINE-TUNE row (mockup .sg-r): the setting's name (an amber * when it differs from the stance) and its choices as a
    /// strip. The current choice is latched; the stance's own value, when the scope has moved off it, is outlined (armed) so RESET's
    /// result reads before it is pressed. Like <see cref="AvSegmented"/>, with that outline.</summary>
    internal sealed class WmcTuneRow : AvPart
    {
        private const float RowH = 26f, LabelW = 78f;
        private readonly TMP_Text label;
        private readonly AvControl[] options;
        private readonly string name;
        private bool edited;

        public AvControl[] Options => options;

        public WmcTuneRow(RectTransform parent, string labelText, string[] choices, Action<int> set)
        {
            Rect = AvLay.Child(parent, "Tune " + labelText);
            name = labelText;
            label = AvText.Make(Rect, "Label", AvTextRole.Micro, labelText, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(label, false);
            options = new AvControl[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                int k = i;
                options[i] = AvControl.Make(Rect, new AvControl.Spec(choices[i], () => set(k)));
                options[i].SingleLine();
            }
            Restyle();
        }

        public override float Measure(float width) => RowH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(label.rectTransform, 0f, 0f, LabelW, s.H);
            float w = AvFlowMath.ColumnWidth(s.W - LabelW, options.Length, 2f);
            for (int i = 0; i < options.Length; i++) AvLay.Place(options[i].Rect, LabelW + i * (w + 2f), 2f, w, s.H - 4f);
        }

        /// <summary>The current choice (-1: mixed), the stance's own (-1: none, or the same) and whether this setting is edited.</summary>
        public void Show(int current, int baseValue, bool isEdited)
        {
            for (int i = 0; i < options.Length; i++)
            {
                options[i].Latched = i == current;
                options[i].Armed = isEdited && i == baseValue && i != current;
            }
            if (edited == isEdited) return;
            edited = isEdited;
            label.text = isEdited ? name + " *" : name;
            Restyle();
        }

        public override void Restyle()
        {
            label.color = edited ? AvStyleHost.FuiColor("caution", AvTheme.RailCaution) : AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            foreach (AvControl o in options) o.Restyle();
        }
    }

    /// <summary>The FINE-TUNE box's frame (mockup .tune): a sunken card the header, the status line and the four rows sit in.</summary>
    internal sealed class WmcTuneBox : AvPart
    {
        private const float Pad = 6f, HeadH = 24f, StatusH = 16f, RowH = 26f, Gap = 2f;
        private readonly AvFrame frame;
        private readonly TMP_Text title, status;
        public readonly AvControl Reset, Save, Edit;
        public readonly WmcTuneRow[] Rows;
        private bool edited;

        public WmcTuneBox(RectTransform parent, Action reset, Action save, Action edit, WmcTuneRow[] rows)
        {
            Rect = AvLay.Child(parent, "TuneBox");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            title = AvText.Make(Rect, "Title", AvTextRole.Micro, "FINE-TUNE", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            status = AvText.Make(Rect, "Status", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(status, false);
            Reset = AvControl.Make(Rect, new AvControl.Spec("RESET", reset, AvButtonStyle.Quiet));
            Save = AvControl.Make(Rect, new AvControl.Spec("SAVE AS…", save, AvButtonStyle.Quiet));
            Edit = AvControl.Make(Rect, new AvControl.Spec("EDIT ›", edit, AvButtonStyle.Quiet));
            foreach (AvControl b in new[] { Reset, Save, Edit }) b.SingleLine();
            Rows = rows;
            foreach (WmcTuneRow r in rows) r.Rect.SetParent(Rect, false);
            Restyle();
        }

        public override float Measure(float width) => Pad + HeadH + StatusH + Rows.Length * (RowH + Gap) + Pad - Gap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = s.W - 2f * Pad, y = Pad;
            float bw = 60f, sw = 78f;
            AvLay.Place(title.rectTransform, Pad + 2f, y, 80f, HeadH);
            AvLay.Place(Edit.Rect, s.W - Pad - bw, y + 2f, bw, HeadH - 4f);
            AvLay.Place(Save.Rect, s.W - Pad - bw - 3f - sw, y + 2f, sw, HeadH - 4f);
            AvLay.Place(Reset.Rect, s.W - Pad - bw - 3f - sw - 3f - bw, y + 2f, bw, HeadH - 4f);
            y += HeadH;
            AvLay.Place(status.rectTransform, Pad + 2f, y, w - 4f, StatusH);
            y += StatusH;
            foreach (WmcTuneRow r in Rows)
            {
                r.Place(new AvSlot(Pad, y, w, RowH));
                y += RowH + Gap;
            }
        }

        public void SetStatus(string text, bool isEdited)
        {
            WmcKit.Set(status, text);
            if (edited == isEdited) return;
            edited = isEdited;
            Restyle();
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.FuiColor("ground", AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            title.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            status.color = edited ? AvStyleHost.FuiColor("caution", AvTheme.RailCaution) : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            Reset.Restyle();
            Save.Restyle();
            Edit.Restyle();
            if (Rows != null) foreach (WmcTuneRow r in Rows) r.Restyle();
        }
    }
}

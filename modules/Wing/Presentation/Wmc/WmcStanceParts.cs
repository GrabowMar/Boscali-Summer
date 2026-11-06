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
    internal static class StanceSlotWords
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
        private const float CellH = 40f, SlimH = 22f, Gap = 3f;

        private sealed class Slot
        {
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Hint, Name, Detail;
            public bool On, Hover, Filled;
        }

        private readonly Slot[] slots;
        private readonly Action<int> pick;
        private readonly bool book;
        private int shown;

        /// <summary><paramref name="isBook"/>: BEHAVIOUR's stance book — <paramref name="count"/> plates of which the first
        /// <see cref="SetShown"/> take room, each with its own hint (<see cref="SetHint"/>), no slim row.</summary>
        public WmcStanceSlots(RectTransform parent, Action<int> picked, Action<int, AvHit> bind, int count = Count, bool isBook = false)
        {
            Rect = AvLay.Child(parent, "StanceSlots");
            pick = picked;
            book = isBook;
            shown = count;
            slots = new Slot[count];
            for (int i = 0; i < count; i++)
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

        public void SetHelp(int i, string tip) => AvHelpTip.Attach(slots[i].Frame.gameObject, tip);

        private TMP_Text Text(string name, AvTextRole role, TextAlignmentOptions align) => WmcSheet.Text(Rect, name, role, align);

        // The second row is one line tall while all three of its slots are empty: the order keys below sit 18 px higher.
        private bool SlimRow => !book && !slots[3].Filled && !slots[4].Filled && !slots[5].Filled;

        public override float Measure(float width)
        {
            if (!book) return CellH + Gap + (SlimRow ? SlimH : CellH);
            int rows = (shown + 2) / 3;
            return rows == 0 ? 0f : rows * (CellH + Gap) - Gap;
        }

        /// <summary>The first <paramref name="n"/> plates show (the book). True when that changed (the caller relayouts).</summary>
        public bool SetShown(int n)
        {
            n = Mathf.Clamp(n, 0, slots.Length);
            if (n == shown) return false;
            shown = n;
            for (int i = 0; i < slots.Length; i++)
            {
                Slot s = slots[i];
                foreach (Component part in new Component[] { s.Frame, s.Rail, s.Hint, s.Name, s.Detail })
                    if (part.gameObject.activeSelf != (i < n)) part.gameObject.SetActive(i < n);
            }
            return true;
        }

        /// <summary>Plate <paramref name="i"/>'s key column: the hint ("W+2", or nothing).</summary>
        public void SetHint(int i, string hint) => AvText.Set(slots[i].Hint, hint);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, 3, Gap);
            for (int i = 0; i < slots.Length; i++)
            {
                float x = i % 3 * (w + Gap), y = i / 3 * (CellH + Gap);
                bool slim = i >= 3 && SlimRow;
                float h = slim ? SlimH : CellH;
                Slot c = slots[i];
                AvLay.Place(c.Frame.rectTransform, x, y, w, h);
                AvLay.Place(c.Rail.rectTransform, x, y, 2f, h);
                AvLay.Place(c.Hint.rectTransform, x + 6f, y + 2f, 34f, 18f);
                if (!book && c.Name.gameObject.activeSelf == slim) c.Name.gameObject.SetActive(!slim);
                AvLay.Place(c.Name.rectTransform, x + 42f, y + 2f, w - 46f, 18f);
                AvLay.Place(c.Detail.rectTransform, x + 42f, y + (slim ? 2f : 20f), w - 46f, 18f);
            }
        }

        /// <summary>Slot <paramref name="i"/>: its stance's name and settings, and whether it is the one flown (<paramref name="on"/>).</summary>
        public void Set(int i, string name, string detail, bool filled, bool on)
        {
            Slot s = slots[i];
            AvText.Set(s.Name, filled ? name : "—");
            AvText.Set(s.Detail, detail);
            if (s.On == on && s.Filled == filled) return;
            bool slim = SlimRow;
            s.On = on;
            s.Filled = filled;
            Style(s);
            if (slim != SlimRow) Changed();
        }

        private void Style(Slot s)
        {
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            Color accent = AvStyleHost.FuiColor("select", AvTheme.Accent);
            Color surface = AvStyleHost.FuiColor("surface", AvTheme.Surface);
            Color back = s.On ? Color.Lerp(surface, accent, 0.16f) : s.Hover ? Color.Lerp(surface, Color.white, 0.07f) : surface;
            Color border = s.On ? accent : AvStyleHost.FuiColor("frame", AvTheme.Frame);
            s.Frame.Paint(back, border);
            s.Rail.color = s.On ? accent : Color.clear;
            s.Hint.color = key;
            s.Name.color = s.Filled ? AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary) : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            s.Detail.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
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
            label = WmcSheet.Text(Rect, "Label", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, labelText);
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
}

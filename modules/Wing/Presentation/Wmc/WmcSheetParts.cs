using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain.Pure;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // The "one sheet" parts SUPPLY and LOADOUT share (spec 2026-10-04 §SUPPLY × LOADOUT): dense 24–26 px rows, header bars that carry
    // their own controls, wrapping chip rows, a strip block for INBOUND / ADOPT / HANGAR, a station map and a store picker. Built from
    // AvPart + AvFrame/AvText/AvLay (kit gaps); every text box is sized from its text so nothing clips or shrinks below the floor.

    /// <summary>Small helpers the sheet parts share.</summary>
    internal static class WmcSheet
    {
        public const float RowH = 26f, RowGap = 2f, CtlH = 22f;

        public static TMP_Text Text(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        /// <summary>Sets <paramref name="full"/> and, when it is wider than <paramref name="avail"/>, cuts it a character at a time
        /// (never with an ellipsis) until it fits: a slot too small for its words cuts them, it never spills.</summary>
        public static void SetFit(TMP_Text t, string full, float avail)
        {
            full = full ?? "";
            if (t.text != full) t.text = full;
            if (avail < 8f) return;
            string s = full;
            for (int guard = 0; guard < 96 && s.Length > 1 && AvText.Width(t) > avail; guard++)
            {
                s = s.Substring(0, s.Length - 1).TrimEnd(' ', '·');
                t.text = s;
            }
        }

        /// <summary>A state class ("ok", "warn", "bad", "info", "inert" or a rail word) as a colour.</summary>
        public static Color Level(string level) =>
            level == "ok" ? WmcState.Color("ready") : level == "bad" ? WmcState.Color("danger") : level == "warn" ? WmcState.Color("caution")
            : level == "info" ? WmcState.Color("info") : level == "inert" ? AvInk.Dim : WmcState.Color(level);

        /// <summary>A button's width from its label (the kit shrinks a long label, but not below the floor).</summary>
        public static float ChipW(string label) => Mathf.Max(44f, (label ?? "").Length * 7.2f + 14f);

        public static void PaintRow(AvFrame frame, Image rail, AvState state, string interaction)
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), interaction);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert), r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            if (rail != null) rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
        }

        public static AvControl Chevron(RectTransform parent, Action click, bool left, string help) =>
            Make(parent, new AvControl.Spec("", click, AvButtonStyle.Quiet, left ? AvIcon.ChevronLeft : AvIcon.ChevronRight), help);

        public static AvControl Make(RectTransform parent, AvControl.Spec spec, string help)
        {
            AvControl c = AvControl.Make(parent, spec);
            if (help != null) c.Help = help;
            return c;
        }
    }

    /// <summary>A section header that carries controls on its right (the airframe pager, the base mode, the fuel steps): the kit's
    /// section on the left (title, glyph, caption), the controls on the right, one rule under both.</summary>
    internal sealed class WmcHeadBar : AvPart
    {
        private const float MinH = 26f;
        private readonly AvSection section;
        private readonly AvControl[] controls;
        private readonly float[] widths;
        private readonly Image rule;
        private float secW, secH, ctlW;

        public WmcHeadBar(RectTransform parent, AvIcon icon, string title, params AvControl.Spec[] specs)
        {
            Rect = AvLay.Child(parent, "Head " + title);
            section = new AvSection(Rect, icon, title, "") { Parent = this };
            controls = new AvControl[specs.Length];
            widths = new float[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                controls[i] = AvControl.Make(Rect, specs[i]);
                widths[i] = string.IsNullOrEmpty(specs[i].Label) ? 30f : WmcSheet.ChipW(specs[i].Label);
                ctlW += widths[i] + 2f;
            }
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            Restyle();
        }

        public AvControl Control(int i) => controls[i];

        public void SetCaption(string text) => section.SetCaption(text);

        public override float Measure(float width)
        {
            secW = Mathf.Max(40f, width - (controls.Length > 0 ? ctlW + 4f : 0f));
            secH = section.Measure(secW);
            return controls.Length > 0 ? Mathf.Max(MinH, secH) : secH;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            secW = Mathf.Max(40f, s.W - (controls.Length > 0 ? ctlW + 4f : 0f));
            secH = section.Measure(secW);
            section.Place(new AvSlot(0f, s.H - secH, secW, secH));
            float x = s.W;
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                x -= widths[i];
                AvLay.Place(controls[i].Rect, x, (s.H - WmcSheet.CtlH) * 0.5f - 1f, widths[i], WmcSheet.CtlH);
                x -= 2f;
            }
            // The kit's rule stops at the section; this one carries on under the controls.
            AvLay.Place(rule.rectTransform, secW, s.H - 1f, Mathf.Max(0f, s.W - secW), 1f);
        }

        public override void Restyle()
        {
            section.Restyle();
            rule.color = AvStyleHost.FuiBorder("section", AvTheme.Hairline);
            if (controls != null) foreach (AvControl c in controls) c.Restyle();
        }
    }

    /// <summary>A row of chips that wraps: a left key, a choice chip per option (one latched) and fixed action chips after them
    /// (EDIT ›, NEW, COPY, DELETE). Chip widths come from their labels; a line that would overrun starts the next.</summary>
    internal sealed class WmcChipRow : AvPart
    {
        private const float H = 24f, Pitch = 26f, KeyW = 62f, Gap = 2f;
        private readonly TMP_Text key;
        private readonly AvControl[] choices, actions;
        private readonly float[] widths, xs, actionExtra;
        private readonly int[] lines;
        private readonly bool hasKey;

        public WmcChipRow(RectTransform parent, string keyText, int maxChoices, Action<int> pick, params AvControl.Spec[] actionSpecs)
        {
            Rect = AvLay.Child(parent, "ChipRow " + keyText);
            hasKey = !string.IsNullOrEmpty(keyText);
            if (hasKey)
            {
                key = WmcSheet.Text(Rect, "Key", AvTextRole.Micro);
                key.text = keyText;
            }
            choices = new AvControl[maxChoices];
            for (int i = 0; i < maxChoices; i++)
            {
                int slot = i;
                choices[i] = AvControl.Make(Rect, new AvControl.Spec("", () => pick?.Invoke(slot), AvButtonStyle.Default));
                choices[i].gameObject.SetActive(false);
            }
            actions = new AvControl[actionSpecs.Length];
            actionExtra = new float[actionSpecs.Length];
            for (int i = 0; i < actionSpecs.Length; i++)
            {
                actions[i] = AvControl.Make(Rect, actionSpecs[i]);
                actionExtra[i] = actionSpecs[i].Icon != AvIcon.None ? 24f : 0f;
            }
            widths = new float[maxChoices + actionSpecs.Length];
            xs = new float[widths.Length];
            lines = new int[widths.Length];
            Restyle();
        }

        public AvControl Choice(int i) => choices[i];

        public AvControl Action(int i) => actions[i];

        public int MaxChoices => choices.Length;

        /// <summary>The first <paramref name="labels"/>.Count chips show (the rest hide); <paramref name="selected"/> is latched.</summary>
        public void SetChoices(IList<string> labels, int selected)
        {
            int n = Mathf.Min(labels.Count, choices.Length);
            for (int i = 0; i < choices.Length; i++)
            {
                bool on = i < n;
                if (choices[i].gameObject.activeSelf != on) choices[i].gameObject.SetActive(on);
                if (!on) continue;
                if (choices[i].Label != labels[i]) choices[i].Label = labels[i];
                choices[i].Latched = i == selected;
            }
            Changed();
        }

        private int Layout(float width, bool place)
        {
            int n = 0;
            for (int i = 0; i < choices.Length + actions.Length; i++)
            {
                AvControl c = i < choices.Length ? choices[i] : actions[i - choices.Length];
                if (!c.gameObject.activeSelf) continue;
                widths[n] = Mathf.Min(width - (hasKey ? KeyW : 0f), WmcSheet.ChipW(c.Label) + (i >= choices.Length ? actionExtra[i - choices.Length] : 0f));
                n++;
            }
            int used = ChipFlow.Place(widths, n, hasKey ? KeyW : 0f, width, Gap, xs, lines);
            if (!place) return used;
            if (hasKey) AvLay.Place(key.rectTransform, 0f, 0f, KeyW - 4f, H);
            n = 0;
            for (int i = 0; i < choices.Length + actions.Length; i++)
            {
                AvControl c = i < choices.Length ? choices[i] : actions[i - choices.Length];
                if (!c.gameObject.activeSelf) continue;
                AvLay.Place(c.Rect, xs[n], lines[n] * Pitch, widths[n], H);
                n++;
            }
            return used;
        }

        public override float Measure(float width) => Layout(width, false) * Pitch - (Pitch - H);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            Layout(s.W, true);
        }

        public override void Restyle()
        {
            if (key != null) key.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            if (choices == null) return;
            foreach (AvControl c in choices) c.Restyle();
            foreach (AvControl c in actions) c.Restyle();
        }
    }

    /// <summary>SUPPLY's airframe list as one-line rows (silhouette, code, name, price, reason or READY): pooled, each rebound only
    /// when its key changes, one inert line that says why when the list is empty. Paging lives in the header bar above it.</summary>
    internal sealed class WmcAirRows : AvPart
    {
        private sealed class Row
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail, Icon;
            public TMP_Text Code, Name, Price, Reason;
            public string CodeFull, NameFull, PriceFull, ReasonFull, Level = "";
            public int Key = int.MinValue;
            public bool Selected, Enabled = true, Hover, Shown;
            public AvState State = AvState.Inert;
        }

        private readonly Row[] rows;
        private readonly TMP_Text emptyText;
        private bool empty;
        private float width;

        public WmcAirRows(RectTransform parent, WmcControls ids, int count, string prefix, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "AirRows");
            rows = new Row[count];
            for (int i = 0; i < count; i++)
            {
                rows[i] = Build(i, pick);
                ids.Add(prefix + i, rows[i].Root);
            }
            emptyText = AvText.Make(Rect, "EmptyText", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            emptyText.gameObject.SetActive(false);
            Restyle();
        }

        public int PerPage => rows.Length;

        private Row Build(int slot, Action<int> pick)
        {
            var r = new Row { Root = AvLay.Child(Rect, "Row" + slot) };
            r.Frame = AvFrame.Add(r.Root, "Frame", default(AvChamfer));
            AvLay.Fill(r.Frame.rectTransform);
            r.Rail = AvLay.Solid(r.Root, "Rail", Color.clear);
            r.Icon = AvLay.Solid(r.Root, "Icon", Color.white);
            r.Icon.preserveAspect = true;
            r.Code = WmcSheet.Text(r.Root, "Code", AvTextRole.Label);
            r.Name = WmcSheet.Text(r.Root, "Name", AvTextRole.ProseSmall);
            r.Price = WmcSheet.Text(r.Root, "Price", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
            r.Reason = WmcSheet.Text(r.Root, "Reason", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
            AvHit hit = AvHit.On(r.Frame);
            hit.Hover = h => { r.Hover = h; Paint(r); };
            hit.Click = e => pick(slot);
            r.Root.gameObject.SetActive(false);
            return r;
        }

        public bool NeedsBind(int slot, int key)
        {
            Row r = rows[slot];
            if (r.Key == key && r.Shown) return false;
            r.Key = key;
            return true;
        }

        /// <summary><paramref name="level"/> colours the reason ("ok", "warn", "bad"); <paramref name="railClass"/> the rail.</summary>
        public void Bind(int slot, Sprite icon, string code, string name, string price, string reason, string level, string railClass,
            bool selected, bool enabled, string tip)
        {
            Row r = rows[slot];
            if (!r.Shown)
            {
                r.Shown = true;
                r.Root.gameObject.SetActive(true);
            }
            r.Icon.sprite = icon;
            r.Icon.enabled = icon != null;
            r.CodeFull = code;
            r.NameFull = name;
            r.PriceFull = price;
            r.ReasonFull = reason;
            r.Level = level ?? "";
            r.State = WmcState.Of(railClass);
            r.Selected = selected;
            r.Enabled = enabled;
            AvHit hit = r.Frame.GetComponent<AvHit>();
            if (hit != null) hit.Interactable = enabled;
            AvHelpTip.Attach(r.Frame.gameObject, tip);
            Fit(r);
            Paint(r);
            Changed();
        }

        public void Hide(int slot)
        {
            Row r = rows[slot];
            r.Key = int.MinValue;
            if (!r.Shown) return;
            r.Shown = false;
            r.Root.gameObject.SetActive(false);
            Changed();
        }

        /// <summary>One inert line over an empty list saying why; null hides it.</summary>
        public void ShowEmpty(string text)
        {
            bool on = text != null;
            if (on) AvText.Set(emptyText, text);
            if (on == empty) return;
            empty = on;
            emptyText.gameObject.SetActive(on);
            Changed();
        }

        private int ShownCount()
        {
            int n = 0;
            foreach (Row r in rows)
                if (r.Shown) n++;
            return n;
        }

        public override float Measure(float w)
        {
            if (empty) return Mathf.Max(WmcSheet.RowH, AvText.Height(emptyText, w - 16f) + 10f);
            int n = ShownCount();
            return n == 0 ? 0f : n * (WmcSheet.RowH + WmcSheet.RowGap) - WmcSheet.RowGap;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(emptyText.rectTransform, 8f, 5f, Mathf.Max(0f, s.W - 16f), AvText.Height(emptyText, s.W - 16f));
            int at = 0;
            foreach (Row r in rows)
            {
                if (!r.Shown) continue;
                AvLay.Place(r.Root, 0f, at * (WmcSheet.RowH + WmcSheet.RowGap), s.W, WmcSheet.RowH);
                at++;
                Fit(r);
            }
        }

        private void Fit(Row r)
        {
            float w = width > 0f ? width : 400f;
            const float priceW = 66f, reasonW = 84f;
            AvText.Set(r.Code, r.CodeFull);
            float codeW = Mathf.Clamp(AvText.Width(r.Code) + 4f, 46f, 90f);
            float x = 32f;
            AvLay.Place(r.Rail.rectTransform, 0f, 0f, 3f, WmcSheet.RowH);
            AvLay.Place(r.Icon.rectTransform, 8f, (WmcSheet.RowH - 18f) * 0.5f, 18f, 18f);
            AvLay.Place(r.Code.rectTransform, x, 0f, codeW, WmcSheet.RowH);
            float nameX = x + codeW + 6f, right = w - 8f;
            WmcSheet.SetFit(r.Reason, r.ReasonFull, 120f);
            float rw = Mathf.Clamp(AvText.Width(r.Reason) + 4f, 30f, 120f);
            AvLay.Place(r.Reason.rectTransform, right - rw, 0f, rw, WmcSheet.RowH);
            right -= rw + 8f;
            AvText.Set(r.Price, r.PriceFull);
            float pw = Mathf.Clamp(AvText.Width(r.Price) + 4f, 20f, priceW + 24f);
            AvLay.Place(r.Price.rectTransform, right - pw, 0f, pw, WmcSheet.RowH);
            right -= pw + 8f;
            float nw = Mathf.Max(0f, right - nameX);
            WmcSheet.SetFit(r.Name, r.NameFull, nw);
            AvLay.Place(r.Name.rectTransform, nameX, 0f, nw, WmcSheet.RowH);
        }

        private void Paint(Row r)
        {
            WmcSheet.PaintRow(r.Frame, r.Rail, r.State, !r.Enabled ? "disabled" : r.Selected ? "armed" : r.Hover ? "hover" : null);
            r.Frame.Bracket = r.Selected ? 6f : 0f;
            r.Frame.BracketColor = AvStyleHost.FuiColor("select", AvTheme.Accent);
            r.Frame.SetVerticesDirty();
            r.Code.color = !r.Enabled ? AvTheme.Disabled : AvInk.Ink;
            r.Name.color = AvInk.Dim;
            r.Price.color = AvInk.Ink;
            r.Reason.color = r.Level.Length == 0 ? AvInk.Dim : WmcSheet.Level(r.Level);
            r.Icon.color = r.Selected ? Color.white : r.Enabled ? AvTheme.Friendly : AvTheme.Dim;
        }

        public override void Restyle()
        {
            if (rows == null) return;
            foreach (Row r in rows) Paint(r);
            emptyText.color = AvInk.Dim;
        }
    }

    /// <summary>Slim strips for what is on the way or ready to take: a state tag, a bold word, a dim line, an optional progress bar, a
    /// number, and up to a few buttons on the right. SUPPLY's INBOUND rows, ADOPT row and HANGAR row are one of these each.</summary>
    internal sealed class WmcStripBlock : AvPart
    {
        private sealed class Strip
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail, Track, Fill;
            public TMP_Text Tag, Main, Sub, Num;
            public string TagFull, MainFull, SubFull, NumFull;
            public AvControl[] Buttons;
            public AvState State = AvState.Info, TagState = AvState.Info, NumState = AvState.Info;
            public float Bar = -1f;
            public bool Shown;
        }

        private readonly Strip[] strips;
        private readonly float[] buttonW;
        private readonly AvControl pagerPrev, pagerNext;
        private readonly TMP_Text pagerRange;
        private float width;
        private bool paged;

        /// <summary>A block of <paramref name="rowCount"/> strips; with <paramref name="turn"/> it also has a pager line (◂ range ▸) that
        /// shows while <see cref="SetPaging"/> says there is more than a page; <paramref name="onButton"/> gets (row, button) for a click.</summary>
        public WmcStripBlock(RectTransform parent, string name, int rowCount, Action<int> turn, Action<int, int> onButton, params AvControl.Spec[] buttonSpecs)
        {
            Rect = AvLay.Child(parent, name);
            if (turn != null)
            {
                pagerPrev = WmcSheet.Chevron(Rect, () => turn(-1), true, "Previous page");
                pagerNext = WmcSheet.Chevron(Rect, () => turn(1), false, "Next page");
                pagerRange = WmcSheet.Text(Rect, "Range", AvTextRole.DataSmall, TextAlignmentOptions.Center);
                pagerPrev.gameObject.SetActive(false);
                pagerNext.gameObject.SetActive(false);
            }
            strips = new Strip[rowCount];
            buttonW = new float[buttonSpecs.Length];
            for (int k = 0; k < buttonSpecs.Length; k++) buttonW[k] = Mathf.Max(56f, WmcSheet.ChipW(buttonSpecs[k].Label));
            for (int i = 0; i < rowCount; i++)
            {
                var s = new Strip { Root = AvLay.Child(Rect, "Strip" + i) };
                s.Frame = AvFrame.Add(s.Root, "Frame", default(AvChamfer));
                AvLay.Fill(s.Frame.rectTransform);
                s.Rail = AvLay.Solid(s.Root, "Rail", Color.clear);
                s.Track = AvLay.Solid(s.Root, "BarTrack", Color.clear);
                s.Fill = AvLay.Solid(s.Root, "BarFill", Color.clear);
                s.Tag = WmcSheet.Text(s.Root, "Tag", AvTextRole.Micro);
                s.Main = WmcSheet.Text(s.Root, "Main", AvTextRole.Label);
                s.Sub = WmcSheet.Text(s.Root, "Sub", AvTextRole.ProseSmall);
                s.Num = WmcSheet.Text(s.Root, "Num", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                s.Buttons = new AvControl[buttonSpecs.Length];
                for (int k = 0; k < buttonSpecs.Length; k++)
                {
                    int row = i, button = k;
                    AvControl.Spec b = buttonSpecs[k];
                    s.Buttons[k] = AvControl.Make(s.Root, new AvControl.Spec(b.Label, () => onButton?.Invoke(row, button), b.Style, b.Icon, b.IconRight));
                }
                s.Root.gameObject.SetActive(false);
                strips[i] = s;
            }
            Restyle();
        }

        public int Capacity => strips.Length;

        public AvControl Button(int row, int k) => strips[row].Buttons[k];

        public AvControl PagerPrev => pagerPrev;

        public AvControl PagerNext => pagerNext;

        /// <summary>Shows the pager line when there is more than one page; <paramref name="range"/> is its centre word.</summary>
        public void SetPaging(int page, int pages, string range)
        {
            if (pagerPrev == null) return;
            bool on = pages > 1;
            pagerPrev.gameObject.SetActive(on);
            pagerNext.gameObject.SetActive(on);
            pagerRange.gameObject.SetActive(on);
            pagerPrev.Interactable = page > 0;
            pagerNext.Interactable = page < pages - 1;
            AvText.Set(pagerRange, range);
            if (on != paged)
            {
                paged = on;
                Changed();
            }
        }

        public int ShownCount
        {
            get
            {
                int n = 0;
                foreach (Strip s in strips)
                    if (s.Shown) n++;
                return n;
            }
        }

        /// <summary><paramref name="bar"/> is 0..1, or negative for no bar; <paramref name="tagState"/> colours the tag, <paramref name="state"/>
        /// the rail.</summary>
        public void Set(int i, string tag, AvState tagState, string main, string sub, float bar, string number, AvState numState, AvState state)
        {
            Strip s = strips[i];
            if (!s.Shown)
            {
                s.Shown = true;
                s.Root.gameObject.SetActive(true);
            }
            s.TagFull = tag;
            s.TagState = tagState;
            s.MainFull = main;
            s.SubFull = sub;
            s.NumFull = number;
            s.NumState = numState;
            s.Bar = bar;
            s.State = state;
            Fit(s);
            Paint(s);
            Changed();
        }

        public void Hide(int i)
        {
            Strip s = strips[i];
            if (!s.Shown) return;
            s.Shown = false;
            s.Root.gameObject.SetActive(false);
            Changed();
        }

        public override float Measure(float w)
        {
            int n = ShownCount;
            return n == 0 ? 0f : n * (WmcSheet.RowH + WmcSheet.RowGap) - WmcSheet.RowGap + (paged ? WmcSheet.RowH + 4f : 0f);
        }

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            width = slot.W;
            int at = 0;
            foreach (Strip s in strips)
            {
                if (!s.Shown) continue;
                AvLay.Place(s.Root, 0f, at * (WmcSheet.RowH + WmcSheet.RowGap), slot.W, WmcSheet.RowH);
                at++;
                Fit(s);
            }
            if (paged)
            {
                float y = at * (WmcSheet.RowH + WmcSheet.RowGap) + 2f;
                AvLay.Place(pagerPrev.Rect, 0f, y, 60f, WmcSheet.RowH);
                AvLay.Place(pagerNext.Rect, slot.W - 60f, y, 60f, WmcSheet.RowH);
                AvLay.Place(pagerRange.rectTransform, 64f, y, slot.W - 128f, WmcSheet.RowH);
            }
        }

        private void Fit(Strip s)
        {
            float w = width > 0f ? width : 400f, h = WmcSheet.RowH;
            AvLay.Place(s.Rail.rectTransform, 0f, 0f, 2f, h);
            float x = 10f, right = w - 8f;
            for (int k = s.Buttons.Length - 1; k >= 0; k--)
            {
                AvLay.Place(s.Buttons[k].Rect, right - buttonW[k], (h - WmcSheet.CtlH) * 0.5f, buttonW[k], WmcSheet.CtlH);
                right -= buttonW[k] + 3f;
            }
            AvText.Set(s.Num, s.NumFull);
            float nw = s.Num.text.Length == 0 ? 0f : Mathf.Clamp(AvText.Width(s.Num) + 4f, 20f, 90f);
            if (nw > 0f)
            {
                AvLay.Place(s.Num.rectTransform, right - nw, 0f, nw, h);
                right -= nw + 6f;
            }
            bool bar = s.Bar >= 0f;
            s.Track.enabled = s.Fill.enabled = bar;
            if (bar)
            {
                const float bw = 54f;
                AvLay.Place(s.Track.rectTransform, right - bw, (h - 4f) * 0.5f, bw, 4f);
                AvLay.Place(s.Fill.rectTransform, right - bw, (h - 4f) * 0.5f, bw * Mathf.Clamp01(s.Bar), 4f);
                right -= bw + 6f;
            }
            AvText.Set(s.Tag, s.TagFull);
            float tw = s.Tag.text.Length == 0 ? 0f : AvText.Width(s.Tag) + 4f;
            AvLay.Place(s.Tag.rectTransform, x, 0f, tw, h);
            x += tw > 0f ? tw + 8f : 0f;
            AvText.Set(s.Main, s.MainFull);
            float mw = Mathf.Min(AvText.Width(s.Main) + 4f, Mathf.Max(0f, (right - x) * 0.55f));
            WmcSheet.SetFit(s.Main, s.MainFull, mw);
            mw = Mathf.Max(0f, Mathf.Min(AvText.Width(s.Main) + 4f, right - x));
            AvLay.Place(s.Main.rectTransform, x, 0f, mw, h);
            x += mw > 0f ? mw + 8f : 0f;
            float sw = Mathf.Max(0f, right - x);
            WmcSheet.SetFit(s.Sub, s.SubFull, sw);
            AvLay.Place(s.Sub.rectTransform, x, 0f, sw, h);
        }

        private void Paint(Strip s)
        {
            WmcSheet.PaintRow(s.Frame, s.Rail, s.State, null);
            s.Tag.color = WmcState.Color(AvStates.Class(s.TagState));
            s.Main.color = AvInk.Ink;
            s.Sub.color = AvInk.Dim;
            s.Num.color = s.NumState == AvState.Info ? AvInk.Ink : WmcState.Color(AvStates.Class(s.NumState));
            s.Track.color = AvStyleHost.FuiFill("metric-track", AvTheme.Hairline);
            s.Fill.color = WmcState.Color(AvStates.Class(s.TagState));
        }

        public override void Restyle()
        {
            if (strips == null) return;
            foreach (Strip s in strips)
            {
                Paint(s);
                foreach (AvControl c in s.Buttons) c.Restyle();
            }
            if (pagerPrev == null) return;
            pagerRange.color = AvInk.Dim;
            pagerPrev.Restyle();
            pagerNext.Restyle();
        }
    }

    /// <summary>One row: a key, an optional icon or swatch, a pick word and its position, and ◂ ▸ (LOADOUT's AIRFRAME and LIVERY).
    /// With <c>splitArrows</c> the arrows flank the word (◂ word ▸); without, they follow it.</summary>
    internal sealed class WmcPickRow : AvPart
    {
        private const float KeyW = 66f, ArrowW = 34f, H = 26f;
        private readonly TMP_Text key, pick, pos;
        private readonly AvFrame frame;
        private readonly Image icon, swatch;
        private readonly bool splitArrows;
        private string pickFull = "", posFull = "";
        private float width;

        public WmcPickRow(RectTransform parent, string keyText, bool splitArrows, Action<int> step, string help)
        {
            Rect = AvLay.Child(parent, "PickRow " + keyText);
            this.splitArrows = splitArrows;
            key = WmcSheet.Text(Rect, "Key", AvTextRole.Micro);
            key.text = keyText;
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            icon = AvLay.Solid(Rect, "Icon", Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            swatch = AvLay.Solid(Rect, "Swatch", Color.clear);
            swatch.enabled = false;
            pick = WmcSheet.Text(Rect, "Pick", AvTextRole.Label);
            pos = WmcSheet.Text(Rect, "Pos", AvTextRole.DataSmall);
            Prev = WmcSheet.Chevron(Rect, () => step(-1), true, help);
            Next = WmcSheet.Chevron(Rect, () => step(1), false, help);
            Restyle();
        }

        public AvControl Prev { get; }

        public AvControl Next { get; }

        public string Word => pickFull;

        public string Position => posFull;

        /// <summary><paramref name="swatchColor"/> (alpha 0 for none) and <paramref name="sprite"/> (null for none) lead the word.</summary>
        public void Set(string word, string position, Sprite sprite, Color swatchColor)
        {
            pickFull = word ?? "";
            posFull = position ?? "";
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            swatch.color = swatchColor;
            swatch.enabled = swatchColor.a > 0f;
            Fit();
        }

        public void SetEnabled(bool on, string help)
        {
            Prev.Interactable = Next.Interactable = on;
            Prev.Help = Next.Help = help;
        }

        public override float Measure(float w) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Fit();
        }

        private void Fit()
        {
            float w = width > 0f ? width : 400f;
            AvLay.Place(key.rectTransform, 0f, 0f, KeyW - 4f, H);
            float x = KeyW;
            if (splitArrows)
            {
                AvLay.Place(Prev.Rect, x, 1f, ArrowW, H - 2f);
                x += ArrowW + 2f;
            }
            float boxW = w - x - (splitArrows ? ArrowW + 2f : 2f * (ArrowW + 2f));
            AvLay.Place(frame.rectTransform, x, 0f, boxW, H);
            float tx = x + 8f;
            if (icon.enabled)
            {
                AvLay.Place(icon.rectTransform, tx, 3f, 20f, 20f);
                tx += 26f;
            }
            if (swatch.enabled)
            {
                AvLay.Place(swatch.rectTransform, tx, 6f, 26f, 14f);
                tx += 32f;
            }
            AvText.Set(pos, posFull);
            float pw = posFull.Length == 0 ? 0f : AvText.Width(pos) + 4f;
            float avail = Mathf.Max(0f, x + boxW - 8f - tx - (pw > 0f ? pw + 6f : 0f));
            WmcSheet.SetFit(pick, pickFull, avail);
            float ww = Mathf.Min(avail, AvText.Width(pick) + 4f);
            AvLay.Place(pick.rectTransform, tx, 0f, ww, H);
            AvLay.Place(pos.rectTransform, tx + ww + 6f, 0f, pw, H);
            float ax = x + boxW + 2f;
            if (!splitArrows) AvLay.Place(Prev.Rect, ax, 1f, ArrowW, H - 2f);
            AvLay.Place(Next.Rect, splitArrows ? ax : ax + ArrowW + 2f, 1f, ArrowW, H - 2f);
        }

        public override void Restyle()
        {
            key.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            AvStyle r = AvStyleHost.FuiStyle("row info");
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert), r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            pick.color = AvInk.Ink;
            pos.color = AvInk.Dim;
            icon.color = AvTheme.Friendly;
            if (Prev != null)
            {
                Prev.Restyle();
                Next.Restyle();
            }
        }
    }

    /// <summary>LOADOUT's HARDPOINTS: a small top-view station map (the airframe's outline and numbered station boxes) beside the station
    /// table (the number, the store or what blocks it or EMPTY, its mass and ✕ to clear). The map and the table select together.</summary>
    internal sealed class WmcStationBoard : AvPart
    {
        public const int MaxStations = 24, RowsPerPage = 8;
        private const float BoxSize = 16f, RowMin = 22f, RowGap = 2f, Clear = 24f, MassW = 54f, NumW = 20f;

        // The generic top view (x right, y down), the mockup's outline.
        private static readonly float[] OutlineX = { 0, 7, 11, 14, 96, 100, 16, 15, 46, 46, 12, 8, -8, -12, -46, -46, -15, -16, -100, -96, -14, -11, -7 };
        private static readonly float[] OutlineY = { -118, -90, -52, -30, 18, 32, 22, 54, 82, 92, 86, 98, 98, 86, 92, 82, 54, 22, 32, 18, -30, -52, -90 };

        private sealed class Row
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Num, Store, Mass;
            public AvControl Clear;
            public string StoreFull, MassFull, StateClass = "inert";
            public bool Shown, CanClear, Enabled = true, Hover;
        }

        private sealed class Box
        {
            public RectTransform Root;
            public AvFrame Frame;
            public TMP_Text Label;
            public int Station = -1;
        }

        private readonly Row[] rows = new Row[RowsPerPage];
        private readonly Box[] boxes = new Box[MaxStations * 2];
        private readonly RectTransform mapRoot;
        private readonly AvVector outline;
        private readonly Action<int> select;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private readonly string[] states = new string[MaxStations];
        private readonly int[] pylons = new int[MaxStations];
        private readonly string[] stationStore = new string[MaxStations], stationMass = new string[MaxStations];
        private readonly bool[] canClear = new bool[MaxStations], enabled = new bool[MaxStations];
        private readonly StationMapMath.Spot[] spots = new StationMapMath.Spot[MaxStations * 2];
        private int count, page, selected = -1;
        private float width, mapW, mapH, tableH;

        public WmcStationBoard(RectTransform parent, WmcControls ids, Action<int> onSelect, Action<int> onClear, Action<int> onPage)
        {
            Rect = AvLay.Child(parent, "StationBoard");
            select = onSelect;
            mapRoot = AvLay.Child(Rect, "Map");
            outline = AvVector.Create(mapRoot, "Outline", 96);
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = new Box { Root = AvLay.Child(mapRoot, "Box" + i) };
                b.Frame = AvFrame.Add(b.Root, "Frame", default(AvChamfer));
                AvLay.Fill(b.Frame.rectTransform);
                b.Label = WmcSheet.Text(b.Root, "N", AvTextRole.DataSmall, TextAlignmentOptions.Center);
                AvLay.Fill(b.Label.rectTransform);
                AvHit hit = AvHit.On(b.Frame);
                Box self = b;
                hit.Click = e => { if (self.Station >= 0) select(self.Station); };
                b.Root.gameObject.SetActive(false);
                boxes[i] = b;
            }
            for (int i = 0; i < RowsPerPage; i++)
            {
                int slot = i;
                var r = new Row { Root = AvLay.Child(Rect, "Row" + i) };
                r.Frame = AvFrame.Add(r.Root, "Frame", default(AvChamfer));
                AvLay.Fill(r.Frame.rectTransform);
                r.Rail = AvLay.Solid(r.Root, "Rail", Color.clear);
                r.Num = WmcSheet.Text(r.Root, "Num", AvTextRole.DataSmall, TextAlignmentOptions.Center);
                r.Store = AvText.Make(r.Root, "Store", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft, true);
                r.Mass = WmcSheet.Text(r.Root, "Mass", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                r.Clear = AvControl.Make(r.Root, new AvControl.Spec("", () => onClear(page * RowsPerPage + slot), AvButtonStyle.Quiet, AvIcon.X));
                r.Clear.Help = "Empty this station.";
                AvHit hit = AvHit.On(r.Frame);
                hit.Hover = h => { r.Hover = h; Paint(r); };
                hit.Click = e => select(page * RowsPerPage + slot);
                r.Root.gameObject.SetActive(false);
                rows[i] = r;
                ids.Add("lo.hp" + i, r.Root);
                ids.Add("lo.hp" + i + ".clear", r.Clear);
            }
            prev = WmcSheet.Chevron(Rect, () => onPage(-1), true, "Previous stations");
            next = WmcSheet.Chevron(Rect, () => onPage(1), false, "Next stations");
            range = WmcSheet.Text(Rect, "Range", AvTextRole.DataSmall, TextAlignmentOptions.Center);
            ids.Add("lo.hp.prev", prev);
            ids.Add("lo.hp.next", next);
            Restyle();
        }

        public int Count => count;

        public int Page => page;

        public int Selected => selected;

        public int Pages => Mathf.Max(1, (count + RowsPerPage - 1) / RowsPerPage);

        public int Shown
        {
            get
            {
                int n = 0;
                foreach (Row r in rows)
                    if (r.Shown) n++;
                return n;
            }
        }

        /// <summary>How many stations, and each one's pylon count (a pair is two boxes on the map).</summary>
        public void SetStations(int n, IList<int> pylonCounts)
        {
            count = Mathf.Clamp(n, 0, MaxStations);
            for (int i = 0; i < count; i++)
            {
                pylons[i] = pylonCounts != null && i < pylonCounts.Count ? pylonCounts[i] : 1;
                if (stationStore[i] == null) stationStore[i] = "";
                if (stationMass[i] == null) stationMass[i] = "";
                if (states[i] == null) states[i] = "inert";
            }
            page = Mathf.Clamp(page, 0, Pages - 1);
            Rebuild();
        }

        public void SetPage(int p)
        {
            page = Mathf.Clamp(p, 0, Pages - 1);
            Rebuild();
        }

        public void SetSelected(int station)
        {
            selected = station;
            Rebuild();
        }

        /// <summary>One station's words. <paramref name="stateClass"/> is "ready" (flies), "caution" (a store that will not fly),
        /// "inert" (empty or blocked).</summary>
        public void Bind(int station, string store, string mass, string stateClass, bool clearable, bool canPick, string tip)
        {
            if (station < 0 || station >= MaxStations) return;
            stationStore[station] = store ?? "";
            stationMass[station] = mass ?? "";
            states[station] = stateClass;
            canClear[station] = clearable;
            enabled[station] = canPick;
            int slot = station - page * RowsPerPage;
            if (slot >= 0 && slot < RowsPerPage) AvHelpTip.Attach(rows[slot].Frame.gameObject, tip);
        }

        /// <summary>Redraws rows and map from what <see cref="Bind"/> last stored.</summary>
        public void Rebuild()
        {
            int first = page * RowsPerPage;
            for (int i = 0; i < RowsPerPage; i++)
            {
                Row r = rows[i];
                int st = first + i;
                bool on = st < count;
                if (r.Shown != on)
                {
                    r.Shown = on;
                    r.Root.gameObject.SetActive(on);
                }
                if (!on) continue;
                r.StoreFull = stationStore[st];
                r.MassFull = stationMass[st];
                r.StateClass = states[st];
                r.CanClear = canClear[st];
                r.Enabled = enabled[st];
                AvText.Set(r.Num, (st + 1).ToString());
                AvText.Set(r.Store, r.StoreFull);
                AvText.Set(r.Mass, r.MassFull);
                r.Clear.gameObject.SetActive(r.CanClear);
                r.Clear.Interactable = true;
                AvHit hit = r.Frame.GetComponent<AvHit>();
                if (hit != null) hit.Interactable = r.Enabled;
                Paint(r);
            }
            bool paged = Pages > 1;
            prev.gameObject.SetActive(paged);
            next.gameObject.SetActive(paged);
            range.gameObject.SetActive(paged);
            prev.Interactable = page > 0;
            next.Interactable = page < Pages - 1;
            AvText.Set(range, count == 0 ? "" : (first + 1) + "–" + Mathf.Min(count, first + RowsPerPage) + " OF " + count);
            LayoutMap();
            Changed();
        }

        private float RowHeight(Row r, float w) => Mathf.Max(RowMin, AvText.Height(r.Store, StoreW(w)) + 8f);

        private float StoreW(float w) => Mathf.Max(40f, w - 3f - NumW - MassW - Clear - 14f);

        private float TableWidth(float w) => w - Mathf.Clamp(w * 0.31f, 112f, 150f) - 6f;

        private float TableHeight(float w)
        {
            float h = 0f;
            int n = 0;
            foreach (Row r in rows)
            {
                if (!r.Shown) continue;
                h += RowHeight(r, TableWidth(w)) + RowGap;
                n++;
            }
            if (n > 0) h -= RowGap;
            return h + (Pages > 1 ? WmcSheet.RowH + 4f : 0f);
        }

        public override float Measure(float w)
        {
            if (count == 0) return 0f;
            return Mathf.Max(150f, TableHeight(w));
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            mapW = Mathf.Clamp(s.W * 0.31f, 112f, 150f);
            mapH = Mathf.Max(150f, TableHeight(s.W));
            AvLay.Place(mapRoot, 0f, 0f, mapW, mapH);
            float tx = mapW + 6f, tw = s.W - tx, y = 0f;
            foreach (Row r in rows)
            {
                if (!r.Shown) continue;
                float h = RowHeight(r, tw);
                AvLay.Place(r.Root, tx, y, tw, h);
                AvLay.Place(r.Rail.rectTransform, 0f, 0f, 3f, h);
                AvLay.Place(r.Num.rectTransform, 5f, 0f, NumW, h);
                AvLay.Place(r.Store.rectTransform, 5f + NumW + 2f, 0f, StoreW(tw), h);
                AvLay.Place(r.Mass.rectTransform, tw - Clear - MassW - 6f, 0f, MassW, h);
                AvLay.Place(r.Clear.Rect, tw - Clear - 4f, (h - 22f) * 0.5f, Clear, 22f);
                y += h + RowGap;
            }
            if (Pages > 1)
            {
                y += 2f;
                AvLay.Place(prev.Rect, tx, y, 60f, WmcSheet.RowH);
                AvLay.Place(next.Rect, tx + tw - 60f, y, 60f, WmcSheet.RowH);
                AvLay.Place(range.rectTransform, tx + 64f, y, tw - 128f, WmcSheet.RowH);
            }
            LayoutMap();
        }

        private void LayoutMap()
        {
            if (mapW <= 0f) return;
            float k = Mathf.Min(mapW / 230f, mapH / 240f), cx = mapW * 0.5f, cy = mapH * 0.5f + 10f * k;
            AvQuadBuffer b = outline.Buffer;
            b.Clear();
            var xs = new float[OutlineX.Length];
            var ys = new float[OutlineX.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = cx + OutlineX[i] * k;
                ys[i] = mapH - (cy + OutlineY[i] * k);
            }
            Color line = AvStyleHost.FuiColor("frame", AvTheme.Frame);
            AvStrokes.Polyline(b, xs, ys, xs.Length, true, 1.4f, new Rgba(line.r, line.g, line.b, line.a));
            outline.Commit();
            int n = StationMapMath.Spots(Subset(), 92f * k, 100f * k, BoxSize, spots);
            for (int i = 0; i < boxes.Length; i++)
            {
                Box box = boxes[i];
                bool on = i < n;
                box.Station = on ? spots[i].Station : -1;
                if (box.Root.gameObject.activeSelf != on) box.Root.gameObject.SetActive(on);
                if (!on) continue;
                AvLay.Place(box.Root, cx + spots[i].X - BoxSize * 0.5f, cy - 10f * k + spots[i].Y - BoxSize * 0.5f, BoxSize, BoxSize);
                int st = spots[i].Station;
                AvText.Set(box.Label, (st + 1).ToString());
                bool sel = st == selected;
                Color c = states[st] == "ready" ? WmcState.Color("ready") : states[st] == "caution" ? WmcState.Color("caution") : AvInk.Dim;
                box.Frame.Fill = true;
                box.Frame.Paint(sel ? c.WithAlpha(0.35f) : AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert), sel ? WmcState.Color("caution") : c);
                box.Frame.Stroke = sel ? 1.6f : 0.9f;
                box.Label.color = c;
                AvHit hit = box.Frame.GetComponent<AvHit>();
                if (hit != null) hit.Interactable = enabled[st];
            }
        }

        private int[] subset = new int[0];

        private int[] Subset()
        {
            if (subset.Length != count) subset = new int[count];
            for (int i = 0; i < count; i++) subset[i] = pylons[i];
            return subset;
        }

        private void Paint(Row r)
        {
            AvState st = WmcState.Of(r.StateClass);
            WmcSheet.PaintRow(r.Frame, r.Rail, st, !r.Enabled ? "disabled" : r.Hover ? "hover" : null);
            int slot = Array.IndexOf(rows, r);
            bool sel = page * RowsPerPage + slot == selected;
            r.Frame.Bracket = sel ? 6f : 0f;
            r.Frame.BracketColor = AvStyleHost.FuiColor("select", AvTheme.Accent);
            r.Frame.SetVerticesDirty();
            if (sel) r.Rail.color = AvStyleHost.FuiColor("select", AvTheme.Accent);
            r.Num.color = sel ? AvStyleHost.FuiColor("select", AvTheme.Accent) : AvInk.Dim;
            r.Store.color = !r.Enabled ? AvTheme.Disabled : AvInk.Ink;
            r.Mass.color = AvInk.Dim;
            r.Clear.Restyle();
        }

        public override void Restyle()
        {
            if (rows == null) return;
            foreach (Row r in rows) Paint(r);
            range.color = AvInk.Dim;
            prev.Restyle();
            next.Restyle();
            if (mapW > 0f) LayoutMap();
        }
    }

    /// <summary>LOADOUT's store picker, kept open under the stations: a head naming the station and what it holds, then every store the
    /// station can carry as a two-column grid of cells (name, verdict word, mass). A blocked store reads its verdict and cannot be picked.
    /// A pager in the head turns the pages of a long list.</summary>
    internal sealed class WmcStorePicker : AvPart
    {
        public const int PerPage = 12;
        private const float HeadH = 24f, CellH = 34f, CellGap = 2f;

        private sealed class Cell
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Name, Verdict, Mass;
            public string NameFull, VerdictFull, MassFull, Level = "";
            public bool Selected, Pickable = true, Hover, Shown;
        }

        private readonly Cell[] cells = new Cell[PerPage];
        private readonly TMP_Text head, note, noteBody;
        private readonly AvControl prev, next;
        private string headFull = "", noteFull = "", bodyFull = "";
        private int shown, page, total;
        private float width;

        public WmcStorePicker(RectTransform parent, WmcControls ids, Action<int> pick, Action<int> turn)
        {
            Rect = AvLay.Child(parent, "StorePicker");
            head = WmcSheet.Text(Rect, "Head", AvTextRole.Label);
            note = WmcSheet.Text(Rect, "Note", AvTextRole.ProseSmall, TextAlignmentOptions.MidlineRight);
            noteBody = AvText.Make(Rect, "Body", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            noteBody.gameObject.SetActive(false);
            prev = WmcSheet.Chevron(Rect, () => turn(-1), true, "Previous stores");
            next = WmcSheet.Chevron(Rect, () => turn(1), false, "Next stores");
            ids.Add("lo.stores.prev", prev);
            ids.Add("lo.stores.next", next);
            for (int i = 0; i < PerPage; i++)
            {
                int slot = i;
                var c = new Cell { Root = AvLay.Child(Rect, "Cell" + i) };
                c.Frame = AvFrame.Add(c.Root, "Frame", default(AvChamfer));
                AvLay.Fill(c.Frame.rectTransform);
                c.Rail = AvLay.Solid(c.Root, "Rail", Color.clear);
                c.Name = WmcSheet.Text(c.Root, "Name", AvTextRole.ProseSmall);
                c.Verdict = WmcSheet.Text(c.Root, "Verdict", AvTextRole.Micro);
                c.Mass = WmcSheet.Text(c.Root, "Mass", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                AvHit hit = AvHit.On(c.Frame);
                hit.Hover = h => { c.Hover = h; Paint(c); };
                hit.Click = e => pick(page * PerPage + slot);
                c.Root.gameObject.SetActive(false);
                cells[i] = c;
                ids.Add("lo.store" + i, c.Root);
            }
            Restyle();
        }

        public int Shown => shown;

        public int Page => page;

        /// <summary><paramref name="title"/> names the station, <paramref name="holding"/> what it holds now.</summary>
        public void SetHead(string title, string holding)
        {
            headFull = title ?? "";
            noteFull = holding ?? "";
            Fit();
        }

        /// <summary>One line instead of the grid (a blocked station, nothing to pick); null shows the grid.</summary>
        public void SetBody(string text)
        {
            bodyFull = text ?? "";
            noteBody.gameObject.SetActive(text != null);
            AvText.Set(noteBody, bodyFull);
            Changed();
        }

        /// <summary>This page's cells; <paramref name="totalStores"/> is the whole list (for the pager).</summary>
        public void SetPaging(int currentPage, int totalStores)
        {
            page = currentPage;
            total = totalStores;
            bool paged = totalStores > PerPage;
            prev.gameObject.SetActive(paged);
            next.gameObject.SetActive(paged);
            prev.Interactable = page > 0;
            next.Interactable = (page + 1) * PerPage < totalStores;
            Changed();
        }

        public void SetCount(int n)
        {
            shown = Mathf.Clamp(n, 0, PerPage);
            for (int i = 0; i < PerPage; i++)
            {
                bool on = i < shown;
                cells[i].Shown = on;
                if (cells[i].Root.gameObject.activeSelf != on) cells[i].Root.gameObject.SetActive(on);
            }
            Changed();
        }

        /// <summary><paramref name="level"/> colours the verdict ("ok", "warn", "bad", "" for dim).</summary>
        public void Bind(int slot, string name, string verdict, string mass, string level, bool selected, bool pickable, string tip)
        {
            Cell c = cells[slot];
            c.NameFull = name;
            c.VerdictFull = verdict;
            c.MassFull = mass;
            c.Level = level ?? "";
            c.Selected = selected;
            c.Pickable = pickable;
            AvHit hit = c.Frame.GetComponent<AvHit>();
            if (hit != null) hit.Interactable = pickable;
            AvHelpTip.Attach(c.Frame.gameObject, tip);
            FitCell(c);
            Paint(c);
        }

        private bool BodyOnly => noteBody.gameObject.activeSelf;

        private int Rows => (shown + 1) / 2;

        public override float Measure(float w)
        {
            float h = HeadH + 4f;
            if (BodyOnly) return h + Mathf.Max(WmcSheet.RowH, AvText.Height(noteBody, w - 16f) + 8f);
            return h + (Rows == 0 ? 0f : Rows * (CellH + CellGap) - CellGap);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            float arrows = total > PerPage ? 2f * 36f : 0f;
            AvLay.Place(prev.Rect, s.W - arrows, 1f, 34f, HeadH - 2f);
            AvLay.Place(next.Rect, s.W - 36f, 1f, 34f, HeadH - 2f);
            Fit();
            AvLay.Place(noteBody.rectTransform, 8f, HeadH + 4f, s.W - 16f, AvText.Height(noteBody, s.W - 16f));
            float cw = (s.W - CellGap) * 0.5f;
            for (int i = 0; i < PerPage; i++)
            {
                Cell c = cells[i];
                if (!c.Shown) continue;
                AvLay.Place(c.Root, i % 2 * (cw + CellGap), HeadH + 4f + i / 2 * (CellH + CellGap), cw, CellH);
                FitCell(c);
            }
        }

        private void Fit()
        {
            float w = width > 0f ? width : 400f, arrows = total > PerPage ? 2f * 36f : 0f;
            AvText.Set(head, headFull);
            float hw = AvText.Width(head) + 4f;
            AvLay.Place(head.rectTransform, 2f, 0f, hw, HeadH);
            float nw = Mathf.Max(0f, w - arrows - hw - 14f);
            WmcSheet.SetFit(note, noteFull, nw);
            AvLay.Place(note.rectTransform, hw + 8f, 0f, Mathf.Max(0f, w - arrows - hw - 10f), HeadH);
        }

        private void FitCell(Cell c)
        {
            float w = width > 0f ? (width - CellGap) * 0.5f : 200f;
            AvLay.Place(c.Rail.rectTransform, 0f, 0f, 2f, CellH);
            AvText.Set(c.Mass, c.MassFull);
            float mw = c.MassFull != null && c.MassFull.Length > 0 ? AvText.Width(c.Mass) + 4f : 0f;
            AvLay.Place(c.Mass.rectTransform, w - 6f - mw, 1f, mw, 18f);
            WmcSheet.SetFit(c.Name, c.NameFull, w - 20f - mw);
            AvLay.Place(c.Name.rectTransform, 8f, 1f, Mathf.Max(0f, w - 20f - mw), 18f);
            WmcSheet.SetFit(c.Verdict, c.VerdictFull, w - 14f);
            AvLay.Place(c.Verdict.rectTransform, 8f, 18f, Mathf.Max(0f, w - 14f), 15f);
        }

        private void Paint(Cell c)
        {
            WmcSheet.PaintRow(c.Frame, c.Rail, c.Level == "ok" ? AvState.Ready : c.Level.Length > 0 ? AvState.Caution : AvState.Inert,
                !c.Pickable ? "disabled" : c.Selected ? "armed" : c.Hover ? "hover" : null);
            c.Frame.Bracket = c.Selected ? 6f : 0f;
            c.Frame.BracketColor = AvStyleHost.FuiColor("select", AvTheme.Accent);
            c.Frame.SetVerticesDirty();
            c.Name.color = !c.Pickable ? AvTheme.Disabled : AvInk.Ink;
            c.Verdict.color = c.Level.Length == 0 ? AvInk.Dim : WmcSheet.Level(c.Level);
            c.Mass.color = AvInk.Dim;
        }

        public override void Restyle()
        {
            if (cells == null) return;
            foreach (Cell c in cells) Paint(c);
            head.color = AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
            note.color = AvInk.Dim;
            noteBody.color = AvInk.Dim;
            prev.Restyle();
            next.Restyle();
        }
    }
}

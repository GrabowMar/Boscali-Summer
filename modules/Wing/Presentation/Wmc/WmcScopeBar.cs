using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Who the page's orders go to (spec bezel v2 §5), as a kit v2 flow line: a key word, the scope in words, then ALL and a
    /// chip per element in use. It reads and writes the one selection, so the scope never differs between tabs. (Kit gap: there is no
    /// chip-select part; this composes <see cref="AvControl"/>s the way <see cref="AvSegmented"/> does.)</summary>
    internal sealed class WmcScopeBar : AvPart
    {
        private const float KeyW = 64f, TextW = 132f, AllW = 40f, ChipGap = 3f, Inset = 10f, ButtonH = 26f;

        private readonly WmcControls ids;
        private readonly string prefix;
        private readonly AvFrame frame;
        private readonly TMP_Text key, scopeText;
        private readonly AvControl allChip;
        private readonly AvControl[] elementChips = new AvControl[ElementRoster.MaxElements];
        private readonly bool[] chipInUse = new bool[ElementRoster.MaxElements];
        private readonly string[] chipNames = new string[ElementRoster.MaxElements];
        private readonly List<uint> members = new List<uint>();
        private int scopeKey = int.MinValue, used;
        private string scopeLabelShown;
        private float width, height = AvGridTokens.Row;
        private WmcContext last;

        public WmcScopeBar(RectTransform parent, WmcControls controls, string idPrefix, string keyText)
        {
            ids = controls;
            prefix = idPrefix;
            Rect = AvLay.Child(parent, "ScopeBar");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            key = AvText.Make(Rect, "Key", AvTextRole.Label, keyText, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(key, false);
            scopeText = AvText.Make(Rect, "Scope", AvTextRole.Label, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(scopeText, false);
            allChip = AvControl.Make(Rect, new AvControl.Spec("ALL", PickAll));
            allChip.Help = "Orders go to the whole wing.";
            ids.Add(prefix + "all", allChip);
            for (int e = 0; e < elementChips.Length; e++)
            {
                int k = e;
                elementChips[e] = AvControl.Make(Rect, new AvControl.Spec(ElementRoster.Letter(e), () => PickElement(k)));
                elementChips[e].gameObject.SetActive(false);
                ids.Add(prefix + "el" + e, elementChips[e]);
            }
            Restyle();
        }

        public override float Measure(float w) => AvGridTokens.Row;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            width = slot.W;
            height = slot.H;
            AvLay.Place(key.rectTransform, Inset, 0f, KeyW, height);
            AvLay.Place(scopeText.rectTransform, Inset + KeyW, 0f, TextW, height);
            AvLay.Place(allChip.Rect, AllX, (height - ButtonH) * 0.5f, AllW, ButtonH);
            PlaceChips();
        }

        private float AllX => Inset + KeyW + TextW + 4f;

        public override void Restyle()
        {
            AvStyle row = AvStyleHost.FuiStyle("row");
            frame.Paint(AvStyleHost.Resolve(row.Background, AvTheme.SurfaceInert),
                row.Border.HasValue ? AvStyleHost.Resolve(row.Border, Color.clear) : Color.clear);
            key.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            scopeText.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            allChip.Restyle();
            foreach (AvControl c in elementChips) c.Restyle();
        }

        private void PickAll()
        {
            if (last == null) return;
            last.Selection.Clear();
            last.Rescope();
        }

        /// <summary>Orders go to element <paramref name="e"/> (its chip, or its head in the flight list).</summary>
        public void PickElement(int e)
        {
            if (last == null) return;
            members.Clear();
            for (int i = 0; i < last.Count; i++)
                if (last.Rows[i].Element == e) members.Add(last.Rows[i].Id);
            if (members.Count > 0) last.Selection.SelectElement(e, members);
            last.Rescope();
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            int inScope = Count(c);
            int k = (int)c.Scope.Kind * 100000 + c.ScopeElement * 10000 + inScope * 100 + c.Selection.Count;
            if (k != scopeKey || !ReferenceEquals(scopeLabelShown, c.ScopeLabel))
            {
                scopeKey = k;
                scopeLabelShown = c.ScopeLabel;
                AvText.Set(scopeText, WmcWords.ScopeText(c.ScopeLabel, inScope));
            }
            allChip.Latched = c.Scope.Kind == ScopeKind.Wing;

            bool changed = false;
            int n = 0;
            for (int e = 0; e < elementChips.Length; e++)
            {
                bool present = false;
                for (int i = 0; i < c.Count && !present; i++) present = c.Rows[i].Element == e;
                string name = present && c.Wing != null && !c.Client ? c.Wing.Roster.Name(e) : ElementRoster.Letter(e);
                if (present != chipInUse[e] || !string.Equals(name, chipNames[e], System.StringComparison.Ordinal)) changed = true;
                chipInUse[e] = present;
                chipNames[e] = name;
                if (present) n++;
            }
            if (changed)
            {
                used = n;
                PlaceChips();
            }
            for (int e = 0; e < elementChips.Length; e++)
                if (chipInUse[e]) elementChips[e].Latched = c.Scope.Kind == ScopeKind.Element && c.Scope.Element == e;
        }

        /// <summary>Element chips share what ALL leaves; a chip too narrow for "B · VIPER" shows its letter (the name is in its help).</summary>
        private void PlaceChips()
        {
            if (width <= 0f) return;
            float start = AllX + AllW + ChipGap, end = width - 4f;
            float w = used > 0 ? (end - start - ChipGap * (used - 1)) / used : 0f;
            int k = 0;
            for (int e = 0; e < elementChips.Length; e++)
            {
                AvControl chip = elementChips[e];
                if (chip.gameObject.activeSelf != chipInUse[e]) chip.gameObject.SetActive(chipInUse[e]);
                if (!chipInUse[e]) continue;
                AvLay.Place(chip.Rect, start + k * (w + ChipGap), (height - ButtonH) * 0.5f, w, ButtonH);
                string letter = ElementRoster.Letter(e), name = chipNames[e];
                bool named = !string.IsNullOrEmpty(name) && name != letter;
                string full = named ? letter + " · " + name : letter;
                // ~6.5 px a character at the chip's size; the button keeps a 12 px margin.
                chip.Label = full.Length * 6.5f <= w - 12f ? full : letter;
                chip.Help = "Orders go to element " + full + ".";
                k++;
            }
        }

        /// <summary>Aircraft the scope holds now.</summary>
        public static int Count(WmcContext c)
        {
            switch (c.Scope.Kind)
            {
                case ScopeKind.Members: return c.Scope.Members?.Length ?? 0;
                case ScopeKind.Element:
                    int n = 0;
                    for (int i = 0; i < c.Count; i++)
                        if (c.Rows[i].Element == c.Scope.Element) n++;
                    return n;
                default: return c.Count;
            }
        }
    }
}

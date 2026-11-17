using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Radio.Presentation
{
    /// <summary>One control of a <see cref="RadioStripPart"/>: its spec, its share of the width and whether a wider gap precedes it.</summary>
    internal readonly struct RadioStripItem
    {
        public readonly AvControl.Spec Spec;
        public readonly float Weight;
        public readonly bool NewGroup;

        public RadioStripItem(string label, Action click, AvButtonStyle style, AvIcon icon, float weight = 1f, bool newGroup = false)
        {
            Spec = new AvControl.Spec(label, click, style, icon);
            Weight = weight;
            NewGroup = newGroup;
        }
    }

    /// <summary>
    /// One line of mixed-width buttons, grouped by wider gaps: the receiver's band keys, dial keys and
    /// transport, or the deck's transport, modes and stop. Icon-only keys are narrow, worded keys wide;
    /// the line splits the width by weight, so nothing wraps to a second row.
    /// </summary>
    internal sealed class RadioStripPart : AvPart
    {
        private const float InGap = 3f, GroupGap = 9f;
        private readonly AvControl[] controls;
        private readonly float[] weights;
        private readonly bool[] groups;

        public AvControl this[int index] => controls[index];

        public RadioStripPart(RectTransform parent, params RadioStripItem[] items)
        {
            Rect = AvLay.Child(parent, "Strip");
            controls = new AvControl[items.Length];
            weights = new float[items.Length];
            groups = new bool[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                controls[i] = AvControl.Make(Rect, items[i].Spec);
                weights[i] = Mathf.Max(0.1f, items[i].Weight);
                groups[i] = items[i].NewGroup;
            }
        }

        public override float Measure(float width) => AvGridTokens.Row + 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float gaps = 0f, total = 0f;
            for (int i = 0; i < controls.Length; i++)
            {
                total += weights[i];
                if (i > 0) gaps += groups[i] ? GroupGap : InGap;
            }
            float unit = Mathf.Max(0f, s.W - gaps) / Mathf.Max(0.1f, total), x = 0f;
            for (int i = 0; i < controls.Length; i++)
            {
                if (i > 0) x += groups[i] ? GroupGap : InGap;
                float w = unit * weights[i];
                AvLay.Place(controls[i].Rect, x, 0f, w, s.H);
                x += w;
            }
        }

        public override void Restyle()
        {
            foreach (AvControl c in controls) c.Restyle();
        }
    }

    /// <summary>A one-pixel box around a rect, built from four solids (the kit frame is chamfered and themed, this one takes the station colour).</summary>
    internal sealed class RadioOutline
    {
        private readonly Image top, bottom, left, right;

        public RadioOutline(RectTransform parent, string name)
        {
            top = AvLay.Solid(parent, name + "T", Color.clear);
            bottom = AvLay.Solid(parent, name + "B", Color.clear);
            left = AvLay.Solid(parent, name + "L", Color.clear);
            right = AvLay.Solid(parent, name + "R", Color.clear);
        }

        public Color Color { set { top.color = bottom.color = left.color = right.color = value; } }

        public void Place(float x, float y, float w, float h)
        {
            AvLay.Place(top.rectTransform, x, y, w, 1f);
            AvLay.Place(bottom.rectTransform, x, y + h - 1f, w, 1f);
            AvLay.Place(left.rectTransform, x, y, 1f, h);
            AvLay.Place(right.rectTransform, x + w - 1f, y, 1f, h);
        }
    }

    /// <summary>
    /// BROADCAST DESK head card: the station's crest and dial, its name and state slab, the show and its host,
    /// what is on air now (a song with its clock, or the station's own talk) and the break that comes next.
    /// The card takes the station's colour, so each faction's station reads as its own.
    /// </summary>
    internal sealed class RadioOnAirPart : AvPart
    {
        private const float H = 132f, Crest = 86f, Pad = 10f, ClockW = 92f;
        private readonly Image back, crestBack, crestIcon, rule, barBack, barFill, slabBack, hit;
        private readonly RadioOutline outline, crestOutline;
        private readonly TMP_Text crestCode, station, slab, show, label, title, clock, next, nextIn;
        private Color tint = AvTheme.Friendly;
        private float placedW = -1f, progress;
        private bool slabLive, compact;
        private float CardHeight => compact ? 92f : H;
        public void SetCompact(bool value) { if (compact == value) return; compact = value; Changed(); }

        public RadioOnAirPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "OnAir");
            back = AvLay.Solid(Rect, "Back", Color.clear);
            hit = AvLay.Solid(Rect, "Hit", Color.clear);
            hit.raycastTarget = true;
            outline = new RadioOutline(Rect, "Edge");
            crestBack = AvLay.Solid(Rect, "CrestBack", Color.clear);
            crestOutline = new RadioOutline(Rect, "CrestEdge");
            crestIcon = AvLay.Solid(Rect, "CrestIcon", Color.white);
            crestIcon.preserveAspect = true;
            crestIcon.enabled = false;
            crestCode = AvText.Make(Rect, "CrestCode", AvTextRole.Micro, string.Empty, TextAlignmentOptions.Center);
            station = AvText.Make(Rect, "Station", AvTextRole.Title);
            AvText.Fit(station, false);
            slabBack = AvLay.Solid(Rect, "SlabBack", Color.clear);
            slab = AvText.Make(Rect, "Slab", AvTextRole.Micro, string.Empty, TextAlignmentOptions.Center);
            show = AvText.Make(Rect, "Show", AvTextRole.Micro);
            AvText.Fit(show, false);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            label = AvText.Make(Rect, "Label", AvTextRole.Micro);
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            barBack = AvLay.Solid(Rect, "BarBack", Color.clear);
            barFill = AvLay.Solid(Rect, "BarFill", Color.clear);
            clock = AvText.Make(Rect, "Clock", AvTextRole.DataSmall, string.Empty, TextAlignmentOptions.MidlineRight);
            next = AvText.Make(Rect, "Next", AvTextRole.Micro);
            AvText.Fit(next, false);
            nextIn = AvText.Make(Rect, "NextIn", AvTextRole.DataSmall, string.Empty, TextAlignmentOptions.MidlineRight);
            Restyle();
        }

        public string Help { set => AvHelpTip.Attach(hit.gameObject, value); }

        public override float Measure(float width) => CardHeight;

        public void Set(Color stationColor, Sprite crest, string code, string stationName, string stateWord, bool live,
            string showLine, string labelText, string titleText, float progress01, string clockText, string nextText, string nextInText)
        {
            if (crestIcon.sprite != crest) { crestIcon.sprite = crest; crestIcon.enabled = crest != null; }
            Put(crestCode, code); Put(station, stationName); Put(slab, stateWord); Put(show, showLine);
            Put(label, labelText); Put(title, titleText); Put(clock, clockText); Put(next, nextText); Put(nextIn, nextInText);
            progress = Mathf.Clamp01(progress01);
            if (tint != stationColor || slabLive != live) { tint = stationColor; slabLive = live; Restyle(); }
            if (placedW > 0f) Arrange(placedW);
        }

        private static void Put(TMP_Text t, string s)
        {
            if (t.text != (s ?? string.Empty)) t.text = s ?? string.Empty;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            placedW = s.W;
            AvLay.Place(back.rectTransform, 0f, 0f, s.W, CardHeight);
            AvLay.Place(hit.rectTransform, 0f, 0f, s.W, CardHeight);
            outline.Place(0f, 0f, s.W, CardHeight);
            Arrange(s.W);
        }

        private void Arrange(float w)
        {
            float crest = compact ? 64f : Crest;
            barBack.enabled = barFill.enabled = clock.enabled = next.enabled = nextIn.enabled = !compact;
            AvLay.Place(crestBack.rectTransform, Pad, Pad, crest, crest);
            crestOutline.Place(Pad, Pad, crest, crest);
            AvLay.Place(crestIcon.rectTransform, Pad + 17f, Pad + 8f, crest - 34f, crest - 34f);
            AvLay.Place(crestCode.rectTransform, Pad, Pad + crest - 22f, crest, 18f);

            float x = Pad * 2f + crest, rw = Mathf.Max(40f, w - x - Pad);
            float slabW = Mathf.Min(rw * 0.35f, AvText.Width(slab) + 14f);
            AvLay.Place(station.rectTransform, x, Pad - 2f, Mathf.Max(20f, rw - slabW - 6f), 22f);
            AvLay.Place(slabBack.rectTransform, x + rw - slabW, Pad, slabW, 17f);
            AvLay.Place(slab.rectTransform, x + rw - slabW, Pad, slabW, 17f);
            AvLay.Place(show.rectTransform, x, Pad + 21f, rw, 16f);
            AvLay.Place(rule.rectTransform, x, Pad + 41f, rw, 1f);
            AvLay.Place(label.rectTransform, x, Pad + 45f, rw, 18f);
            AvLay.Place(title.rectTransform, x, Pad + 63f, rw, 18f);
            float barW = Mathf.Max(10f, rw - ClockW - 6f);
            AvLay.Place(barBack.rectTransform, x, Pad + 86f, barW, 4f);
            AvLay.Place(barFill.rectTransform, x, Pad + 86f, barW * progress, 4f);
            AvLay.Place(clock.rectTransform, x + rw - ClockW, Pad + 80f, ClockW, 16f);

            AvLay.Place(next.rectTransform, Pad, H - 26f, Mathf.Max(20f, w - 2f * Pad - 70f), 18f);
            AvLay.Place(nextIn.rectTransform, w - Pad - 70f, H - 26f, 70f, 18f);
        }

        public override void Restyle()
        {
            Color dark = AvStyleHost.FuiFill("panel", AvTheme.Surface);
            back.color = Color.Lerp(dark, tint, 0.07f);
            outline.Color = tint.WithAlpha(0.45f);
            crestBack.color = Color.Lerp(dark, tint, 0.14f);
            crestOutline.Color = tint;
            crestCode.color = tint;
            station.color = Color.Lerp(tint, Color.white, 0.55f);
            // The slab is the only filled block on the card: the station colour while on air, grey otherwise.
            slabBack.color = slabLive ? tint : AvTheme.Dim.WithAlpha(0.6f);
            slab.color = Color.black;
            show.color = Color.Lerp(tint, AvTheme.Dim, 0.45f);
            rule.color = tint.WithAlpha(0.25f);
            label.color = Color.Lerp(tint, AvTheme.Dim, 0.6f);
            title.color = AvStyleHost.FuiInk("readout", AvTheme.TextPrimary);
            barBack.color = tint.WithAlpha(0.18f);
            barFill.color = tint;
            clock.color = Color.Lerp(tint, AvTheme.Dim, 0.4f);
            next.color = nextIn.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
        }
    }

}

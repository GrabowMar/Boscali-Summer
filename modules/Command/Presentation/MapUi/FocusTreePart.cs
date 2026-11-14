using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The FAC POLITICS page's focus tree: a nation strip, the running focus, three lanes of four tiers joined by connectors,
    /// and the selected focus's card with PICK NEXT. Every number comes from <see cref="NationView"/>; a press is only a request.
    /// Fixed height, so the page never re-lays on a refresh.
    /// </summary>
    internal sealed class FocusTreePart : AvPart
    {
        private const float StripH = 16f, HeroH = 74f, LaneHeadH = 20f, NodeH = 64f, LinkH = 12f, CardH = 150f, RemarkH = 28f, Gap = 6f, Pad = 10f, ColGap = 8f;
        private static readonly Color Pink = new Color(1f, .44f, .66f);
        private static readonly AvIcon[] Icons =
        {
            AvIcon.BuildingFactory, AvIcon.Gauge, AvIcon.Coins, AvIcon.Bolt,
            AvIcon.Plane, AvIcon.Truck, AvIcon.Tank, AvIcon.Formation,
            AvIcon.Flag, AvIcon.Coins, AvIcon.Antenna, AvIcon.Eye,
        };
        private static readonly string[] Effects = BuildEffects();

        private static string[] BuildEffects()
        {
            var e = new string[FocusTreeModel.Count];
            for (int i = 0; i < e.Length; i++) e[i] = NationWords.FocusEffect(i);
            return e;
        }

        private static Color LaneInk(int lane) => lane == 0 ? AvTheme.RailReady : lane == 1 ? AvTheme.RailInfo : Pink;

        private readonly TMP_Text strip, heroTitle, heroClock, heroEffect, heroSub, remark;
        private readonly AvFrame heroFrame, cardFrame;
        private readonly Image barTrack, barFill;
        private readonly TMP_Text[] laneHeads = new TMP_Text[3];
        private readonly Image[] laneRules = new Image[3];
        private readonly Node[] nodes = new Node[FocusTreeModel.Count];
        private readonly Image[] links = new Image[9];
        private readonly TMP_Text cardKey, cardTitle, cardTakes, cardEffect, cardLock, cardOutcome;
        private readonly AvControl pickButton, removeButton;

        private INationView nation;
        private NationView view;
        private int selected = -1;
        private float barX, barY, barW;
        private string lastStrip = "";

        public FocusTreePart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "FocusTree");
            strip = AvText.Make(Rect, "Strip", AvTextRole.DataSmall);
            AvText.Fit(strip, false);

            heroFrame = AvFrame.Add(Rect, "Hero", AvChamfer.Diagonal(6f));
            AvLay.Fill(heroFrame.rectTransform);
            heroTitle = AvText.Make(Rect, "HeroTitle", AvTextRole.Head); AvText.Fit(heroTitle, false);
            heroClock = AvText.Make(Rect, "HeroClock", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            heroEffect = AvText.Make(Rect, "HeroEffect", AvTextRole.ProseSmall); AvText.Fit(heroEffect, false);
            heroSub = AvText.Make(Rect, "HeroSub", AvTextRole.Micro); AvText.Fit(heroSub, false);
            barTrack = AvLay.Solid(Rect, "BarTrack", AvTheme.SurfaceInert);
            barFill = AvLay.Solid(Rect, "BarFill", AvTheme.RailCaution);

            string[] lanes = { "INDUSTRY", "MILITARY", "STATE" };
            for (int l = 0; l < 3; l++)
            {
                laneHeads[l] = AvText.Make(Rect, "Lane" + l, AvTextRole.Label, lanes[l], TextAlignmentOptions.Center);
                AvText.Fit(laneHeads[l], false);
                laneRules[l] = AvLay.Solid(Rect, "LaneRule" + l, LaneInk(l));
            }
            for (int id = 0; id < nodes.Length; id++) nodes[id] = new Node(Rect, id, Select);
            for (int i = 0; i < links.Length; i++) links[i] = AvLay.Solid(Rect, "Link" + i, AvTheme.Hairline);

            cardFrame = AvFrame.Add(Rect, "Card", AvChamfer.Diagonal(6f));
            AvLay.Fill(cardFrame.rectTransform);
            cardKey = AvText.Make(Rect, "CardKey", AvTextRole.Micro, "// SELECTED"); AvText.Fit(cardKey, false);
            cardTitle = AvText.Make(Rect, "CardTitle", AvTextRole.Head); AvText.Fit(cardTitle, false);
            cardTakes = AvText.Make(Rect, "CardTakes", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            cardEffect = AvText.Make(Rect, "CardEffect", AvTextRole.ProseSmall); AvText.Fit(cardEffect, false);
            pickButton = AvControl.Make(Rect, new AvControl.Spec("PICK NEXT", () => Press(false), AvButtonStyle.Primary, AvIcon.PlayerPlay));
            removeButton = AvControl.Make(Rect, new AvControl.Spec("QUEUED · REMOVE", () => Press(true), AvButtonStyle.Default, AvIcon.X));
            cardLock = AvText.Make(Rect, "CardLock", AvTextRole.Micro); AvText.Fit(cardLock, false);
            cardOutcome = AvText.Make(Rect, "CardOutcome", AvTextRole.Micro); AvText.Fit(cardOutcome, false);
            remark = AvText.Make(Rect, "Remark", AvTextRole.ProseSmall, "NO PICK? THE STAFF CHOOSES. THE ENEMY CABINET RUNS THE SAME TREE.",
                TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public override float Measure(float width) =>
            StripH + Gap + HeroH + Gap + LaneHeadH + Gap + 4 * NodeH + 3 * LinkH + Gap + CardH + Gap + RemarkH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = s.W, y = 0f;
            AvLay.Place(strip, 0f, y, w, StripH); y += StripH + Gap;

            AvLay.Place(heroFrame.rectTransform, 0f, y, w, HeroH);
            AvLay.Place(heroTitle, Pad, y + 6f, w - 2f * Pad - 80f, 20f);
            AvLay.Place(heroClock, w - Pad - 80f, y + 6f, 80f, 20f);
            AvLay.Place(heroEffect, Pad, y + 26f, w - 2f * Pad, 16f);
            barX = Pad; barY = y + 46f; barW = w - 2f * Pad;
            AvLay.Place(barTrack.rectTransform, barX, barY, barW, 6f);
            AvLay.Place(heroSub, Pad, y + 55f, w - 2f * Pad, 14f);
            y += HeroH + Gap;

            float col = (w - 2f * ColGap) / 3f;
            for (int l = 0; l < 3; l++)
            {
                float x = l * (col + ColGap);
                AvLay.Place(laneHeads[l], x, y, col, LaneHeadH - 3f);
                AvLay.Place(laneRules[l].rectTransform, x, y + LaneHeadH - 3f, col, 3f);
            }
            y += LaneHeadH + Gap;

            for (int t = 0; t < 4; t++)
            {
                float ny = y + t * (NodeH + LinkH);
                for (int l = 0; l < 3; l++)
                {
                    float x = l * (col + ColGap);
                    nodes[l * 4 + t].Place(new AvSlot(x, ny, col, NodeH));
                    if (t < 3) AvLay.Place(links[l * 3 + t].rectTransform, x + col * .5f - 1f, ny + NodeH, 2f, LinkH);
                }
            }
            y += 4 * NodeH + 3 * LinkH + Gap;

            AvLay.Place(cardFrame.rectTransform, 0f, y, w, CardH);
            AvLay.Place(cardKey, Pad, y + 6f, w - 2f * Pad, 14f);
            AvLay.Place(cardTitle, Pad, y + 22f, w - 2f * Pad - 90f, 20f);
            AvLay.Place(cardTakes, w - Pad - 90f, y + 22f, 90f, 20f);
            AvLay.Place(cardEffect, Pad, y + 44f, w - 2f * Pad, 16f);
            float bw = (w - 2f * Pad - 6f) / 2f;
            AvLay.Place(pickButton.Rect, Pad, y + 66f, bw, 40f);
            AvLay.Place(removeButton.Rect, Pad + bw + 6f, y + 66f, bw, 40f);
            AvLay.Place(cardLock, Pad, y + 112f, w - 2f * Pad, 14f);
            AvLay.Place(cardOutcome, Pad, y + 128f, w - 2f * Pad, 14f);
            y += CardH + Gap;
            AvLay.Place(remark, 0f, y, w, RemarkH);
            if (view != null) SetBar(FocusTreeModel.Percent(view));
        }

        public override void Restyle()
        {
            if (cardFrame == null) return;
            strip.color = AvInk.Dim;
            heroFrame.Paint(AvTheme.Surface, AvTheme.RailCaution);
            heroTitle.color = AvInk.Ink; heroClock.color = AvInk.Ink; heroEffect.color = AvInk.Dim; heroSub.color = AvInk.Dim;
            barTrack.color = AvTheme.SurfaceInert;
            barFill.color = AvTheme.RailCaution;
            for (int l = 0; l < 3; l++) { laneHeads[l].color = LaneInk(l); laneRules[l].color = LaneInk(l); }
            cardFrame.Paint(AvTheme.Surface, AvTheme.RailInfo);
            cardKey.color = AvInk.Key; cardTitle.color = AvInk.Ink; cardTakes.color = AvInk.Dim; cardEffect.color = AvInk.Dim;
            cardLock.color = AvInk.Dim; cardOutcome.color = AvTheme.RailDanger; remark.color = AvInk.Dim;
            pickButton.Restyle(); removeButton.Restyle();
            foreach (Node n in nodes) n.Restyle();
        }

        // ------------------------------------------------------------------ data

        /// <summary>Repaints from the nation's current view; called on the page's refresh tick.</summary>
        public void Set(INationView source)
        {
            nation = source;
            view = source.Local;
            if (view == null) return;
            if (selected < 0)
            {
                int first = view.Queued;
                for (int i = 0; first < 0 && i < nodes.Length; i++) if (FocusTreeModel.State(view, i) == FocusNodeState.Open) first = i;
                selected = Mathf.Max(0, first);
            }
            Paint();
        }

        private void SetBar(float pct) => AvLay.Place(barFill.rectTransform, barX, barY, barW * pct, 6f);

        private void Paint()
        {
            NationView v = view;
            int run = v.Running;
            float pct = FocusTreeModel.Percent(v);
            int percent = Mathf.RoundToInt(pct * 100f);

            string runWord = run >= 0 ? NationWords.Focus(run) + " " + percent + " %" : "NO FOCUS";
            string stripText = "FACTORIES " + v.Plants.Count + "/" + v.Slots + "   ·   " + runWord +
                               "   ·   STABILITY " + Mathf.RoundToInt(v.Stability) + "   ·   FUNDS " + AvNum.Thousands(Mathf.Round(v.Funds));
            if (stripText != lastStrip) { lastStrip = stripText; strip.text = stripText; }

            AvText.Set(heroTitle, run >= 0 ? NationWords.Focus(run) : "NATIONAL FOCUS");
            AvText.Set(heroClock, run >= 0 ? AvNum.Clock(Mathf.Ceil(v.RunningLeft)) + " LEFT" : "");
            AvText.Set(heroEffect, run >= 0 ? Effects[run] + " FOR THE REST OF THE WAR" : "STAFF PICKS THE NEXT FOCUS");
            string sub = run >= 0 && v.PickLock > 0f && !string.IsNullOrEmpty(v.PickHolder) ? "PICKED BY " + v.PickHolder.ToUpperInvariant() : "";
            if (v.Queued >= 0) sub += (sub.Length > 0 ? " · " : "") + "THEN: " + NationWords.Focus(v.Queued);
            if (sub.Length == 0) sub = FocusTreeModel.DoneCount(v) + "/12 DONE · ONE AT A TIME";
            AvText.Set(heroSub, sub);
            SetBar(pct);

            for (int id = 0; id < nodes.Length; id++)
            {
                FocusNodeState st = FocusTreeModel.State(v, id);
                string line = st == FocusNodeState.Done ? "DONE"
                    : st == FocusNodeState.Running ? "RUNNING " + percent + " %"
                    : st == FocusNodeState.Queued ? "QUEUED NEXT"
                    : st == FocusNodeState.Open ? "OPEN · PRESS TO PICK"
                    : FocusTreeModel.LockReason(v, id);
                nodes[id].Paint(st, line, id == selected);
                if (id % 4 < 3)
                    links[(id / 4) * 3 + id % 4].color = FocusTreeModel.IsDone(v, id) ? AvTheme.RailReady.WithAlpha(.8f) : AvTheme.Hairline;
            }
            PaintCard();
        }

        private void PaintCard()
        {
            NationView v = view;
            int id = selected;
            AvText.Set(cardTitle, NationWords.Focus(id));
            AvText.Set(cardEffect, Effects[id]);
            AvText.Set(cardKey, "// SELECTED  ·  " + NationWords.BranchName(id) + "  ·  TIER " + (FocusTreeModel.Tier(id) + 1));
            float takes = v.RunningTotal > 0f ? v.RunningTotal : FocusTreeModel.DefaultTakes;
            AvText.Set(cardTakes, "TAKES " + AvNum.Clock(takes));
            pickButton.Interactable = FocusTreeModel.CanPick(v, id);
            pickButton.Label = v.Running < 0 ? "START FOCUS" : "PICK NEXT";
            removeButton.Interactable = v.Queued == id;
            AvText.Set(cardLock, v.PickLock > 0f && !string.IsNullOrEmpty(v.PickHolder)
                ? v.PickHolder.ToUpperInvariant() + " HOLDS THE PICK · " + AvNum.Clock(Mathf.Ceil(v.PickLock)) + " · THEN ANY PILOT" : "");
            AvText.Set(cardOutcome, nation != null && nation.LastOutcome != NationOutcome.Ok && Time.unscaledTime - nation.LastOutcomeAt < 4f
                ? NationWords.Outcome(nation.LastOutcome) : "");
        }

        private void Select(int id)
        {
            selected = id;
            if (view != null) Paint();
        }

        private void Press(bool remove)
        {
            if (nation == null || selected < 0) return;
            nation.Request(remove ? NationVerb.Unqueue : NationVerb.Focus, remove ? 0 : selected);
        }

        // ------------------------------------------------------------------ node

        private sealed class Node
        {
            private readonly RectTransform rect;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text glyph, label, effect, state;
            private readonly int id;
            private FocusNodeState st = FocusNodeState.Locked;
            private bool picked, hover;

            public Node(RectTransform parent, int focus, Action<int> pick)
            {
                id = focus;
                rect = AvLay.Child(parent, "Node " + focus);
                frame = AvFrame.Add(rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(rect, "Rail", Color.clear);
                glyph = AvIcons.Make(rect, Icons[focus], AvGridTokens.IconInline, Color.white);
                label = AvText.Make(rect, "Name", AvTextRole.Label, NationWords.Focus(focus), TextAlignmentOptions.TopLeft, true);
                label.characterSpacing = 1f;
                AvText.Fit(label, true);
                effect = AvText.Make(rect, "Effect", AvTextRole.Micro, Effects[focus], TextAlignmentOptions.TopLeft);
                AvText.Fit(effect, false);
                state = AvText.Make(rect, "State", AvTextRole.Micro, "", TextAlignmentOptions.BottomLeft);
                AvText.Fit(state, false);
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => pick(id);
                AvHelpTip.Attach(frame.gameObject, NationWords.Focus(focus) + " — " + Effects[focus]);
                Restyle();
            }

            public void Place(AvSlot s)
            {
                AvLay.Place(rect, s);
                AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
                AvLay.Place(glyph.rectTransform, s.W - 19f, 5f, 14f, 14f);
                AvLay.Place(label.rectTransform, 8f, 4f, s.W - 30f, 28f);
                AvLay.Place(effect.rectTransform, 8f, 33f, s.W - 11f, 13f);
                AvLay.Place(state.rectTransform, 8f, s.H - 16f, s.W - 11f, 13f);
            }

            public void Paint(FocusNodeState next, string line, bool isSelected)
            {
                AvText.Set(state, line);
                if (next == st && isSelected == picked) return;
                st = next; picked = isSelected;
                Restyle();
            }

            public void Restyle()
            {
                Color accent = AvTheme.RailInert, fill = AvTheme.SurfaceInert, ink = AvInk.Ink, dim = AvInk.Dim;
                switch (st)
                {
                    case FocusNodeState.Done: accent = AvTheme.RailReady; fill = accent.WithAlpha(.16f); break;
                    case FocusNodeState.Running: accent = AvTheme.RailCaution; fill = accent.WithAlpha(.2f); break;
                    case FocusNodeState.Queued: accent = AvTheme.RailInfo; fill = accent.WithAlpha(.12f); break;
                    case FocusNodeState.Open: accent = AvTheme.RailInfo; fill = AvTheme.Surface; break;
                    default: ink = dim = AvTheme.Disabled; break;
                }
                if (hover && st != FocusNodeState.Locked) fill = Color.Lerp(fill, Color.white, .08f);
                frame.Paint(fill, picked ? AvInk.Ink : st == FocusNodeState.Locked ? AvTheme.Hairline : accent.WithAlpha(.8f));
                rail.color = accent;
                glyph.color = st == FocusNodeState.Locked ? AvTheme.Disabled : accent;
                label.color = ink; effect.color = dim; state.color = st == FocusNodeState.Locked ? AvTheme.Disabled : accent;
            }
        }
    }
}

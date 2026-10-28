using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The live operation, the expanded lane of the board: MAIN EFFORT slab, route strip, the tug-of-war lane, groups and their separate honest counts,
    /// the tied sorties as tickets, and the priced reinforcement wave. At most three tickets keep ACCEPT and the wave strip above the 596 px bay's fold.
    /// </summary>
    internal sealed class StrHeroLane : StrLaidPart
    {
        private const float Pad = 10f, LockW = 138f, PressureW = 150f, PercentW = 62f;

        private readonly AvFrame frame, headerHit;
        private readonly StrSlab slab;
        private readonly TMP_Text title, picked, routeCount, pressure, trendText, vehicles, movement, eta;
        private readonly TMP_Text sortieIcon, sortieTitle, sortieCaption, none, more, lockIcon, lockWord;
        private readonly TMP_Text[] stepLabels = new TMP_Text[3], groupTag = new TMP_Text[3], groupDist = new TMP_Text[3];
        private readonly StrFlap lockFlap;
        private readonly StrLaneBar lane;
        private readonly RectTransform routeRect, pipsRect;
        private readonly AvVector route, pips;
        private readonly StrSortieTicket[] tickets = new StrSortieTicket[StrViewBuilder.HeroTickets];
        private readonly StrWaveStrip wave;

        internal StrHeroLane(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Hero", controls, hub)
        {
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            headerHit = AvFrame.Add(Rect, "HeaderHit", default(AvChamfer));
            headerHit.Fill = false;
            headerHit.Stroke = 0f;
            AvHit hit = AvHit.On(headerHit);
            hit.Click = e => { if (Vm != null && Vm.Effort.Live && LocateGate(Vm.Effort.Id).Allowed) Locate(Vm.Effort.Id); };
            AvHelpTip.Attach(headerHit.gameObject, "Fly the map to this operation.");
            slab = new StrSlab(Rect, "Slab");
            title = Text(Rect, "Title", AvTextRole.Title, TextAlignmentOptions.MidlineLeft);
            picked = Text(Rect, "PickedBy", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            routeRect = AvLay.Child(Rect, "Route");
            route = AvVector.Create(routeRect, "RouteInk", 40);
            routeCount = Text(routeRect, "Count", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < stepLabels.Length; i++) stepLabels[i] = Text(routeRect, "Step " + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            lockIcon = AvIcons.Make(Rect, AvIcon.Lock, AvGridTokens.IconInline, Color.white);
            lockWord = Text(Rect, "LockWord", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            lockWord.text = "LOCK";
            lockFlap = new StrFlap(Rect, "Lock");
            lane = new StrLaneBar(Rect, "Lane");
            pressure = Text(Rect, "val.pressure", AvTextRole.Display, TextAlignmentOptions.MidlineRight);
            trendText = Text(Rect, "Trend", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            vehicles = Text(Rect, "Vehicles", AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
            pipsRect = AvLay.Child(Rect, "Groups");
            pips = AvVector.Create(pipsRect, "GroupPips", 24);
            for (int i = 0; i < groupTag.Length; i++)
            {
                groupTag[i] = Text(pipsRect, "GroupTag " + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                groupDist[i] = Text(pipsRect, "GroupDist " + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
            }
            movement = Text(Rect, "Movement", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
            eta = Text(Rect, "Eta", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
            sortieIcon = AvIcons.Make(Rect, AvIcon.Target, AvGridTokens.IconInline, Color.white);
            sortieTitle = Text(Rect, "SortiesTitle", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            sortieTitle.text = "SORTIES";
            sortieCaption = Text(Rect, "SortiesCaption", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            none = Text(Rect, "NoSorties", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            more = Text(Rect, "More", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < tickets.Length; i++) tickets[i] = new StrSortieTicket(Rect, controls, hub, i) { Parent = this };
            wave = new StrWaveStrip(Rect, controls, hub) { Parent = this };
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            bool on = StrPagePlan.For(vm.Mode).Hero && vm.Effort.Live;
            SetShown(on);
            for (int i = 0; i < tickets.Length; i++) tickets[i].Bind(vm);
            wave.Bind(vm);
            if (!on) return;
            StrEffortView e = vm.Effort;
            slab.Set(e.KindText, AvInk.Effort);
            AvText.Set(title, TheaterWords.Kind(e.Kind) + " " + e.Name);
            AvText.Set(picked, e.PickedText);
            AvText.Set(routeCount, e.RouteStep + "/" + e.RouteSteps);
            for (int i = 0; i < stepLabels.Length; i++)
            {
                stepLabels[i].gameObject.SetActive(i < e.RouteSteps);
                AvText.Set(stepLabels[i], i < e.RouteSteps ? StrWords.Route(e.Kind, i + 1) : "");
            }
            bool locked = e.LockSeconds > 0;
            lockIcon.gameObject.SetActive(locked);
            lockWord.gameObject.SetActive(locked);
            lockFlap.SetActive(locked);
            lockFlap.Set(locked ? StrPanelText.Flap(e.LockSeconds) : "");
            lane.Set(e.Pressure, e.Trend);
            AvText.Set(pressure, e.PressureText);
            AvText.Set(trendText, e.TrendText);
            AvText.Set(vehicles, e.VehiclesText);
            for (int i = 0; i < groupTag.Length; i++)
            {
                bool g = i < e.MarkCount;
                groupTag[i].gameObject.SetActive(g);
                groupDist[i].gameObject.SetActive(g);
                if (g) { AvText.Set(groupTag[i], e.Marks[i].Tag); AvText.Set(groupDist[i], e.Marks[i].DistanceText); }
            }
            AvText.Set(movement, e.MarkCount > 0 ? e.MovementText : e.GroupsText + StrWords.Dot + e.MovementText);
            AvText.Set(eta, e.EtaText);
            AvText.Set(sortieCaption, StrPanelText.SortiesCaption(vm));
            bool noSorties = vm.SortieCount == 0;
            none.gameObject.SetActive(noSorties);
            AvText.Set(none, noSorties ? (vm.SortiesAvailable ? "NO SORTIE IS TIED TO THIS OPERATION YET. SCOUT OR WAIT FOR CONTACT." : "NO SORTIE LIST FROM THE HOST YET.") : "");
            string moreText = StrPanelText.MoreOnTheMap(vm);
            more.gameObject.SetActive(moreText.Length > 0);
            AvText.Set(more, moreText);
            Controls.Register(StrControlIds.HeroLocate, () => LocateGate(Vm.Effort.Id), () => Locate(Vm.Effort.Id));
            Paint();
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            StrViewModel vm = Vm;
            if (vm == null || !vm.Effort.Live) return 0f;
            StrEffortView e = vm.Effort;
            float inner = width - 2f * Pad, y = 8f;

            // header: slab, title, who set it
            float slabW = slab.WidthFor(), pickedW = Mathf.Min(150f, Mathf.Ceil(AvText.Width(picked)) + 4f);
            if (place)
            {
                slab.Place(Pad, y + 2f, slabW, 18f);
                AvLay.Place(title, Pad + slabW + 8f, y, Mathf.Max(40f, inner - slabW - 8f - pickedW - 6f), 22f);
                AvLay.Place(picked, width - Pad - pickedW, y, pickedW, 22f);
                AvLay.Place(headerHit.rectTransform, 4f, y - 2f, width - 8f, 26f);
            }
            y += 22f + 5f;

            // route strip and lock
            bool locked = e.LockSeconds > 0;
            float zoneW = inner - (locked ? LockW + 6f : 0f);
            if (place)
            {
                AvLay.Place(routeRect, Pad, y, zoneW, 20f);
                DrawRoute(zoneW, e);
                if (locked)
                {
                    float lx = width - Pad - LockW;
                    AvLay.Place(lockIcon.rectTransform, lx, y + 3f, 14f, 14f);
                    AvLay.Place(lockWord.rectTransform, lx + 18f, y, 34f, 20f);
                    lockFlap.Place(width - Pad - StrFlap.Width, y - 1f);
                }
            }
            y += 20f + 6f;

            // lane block: the bar, then the share and its trend on one line
            float laneW = inner - PressureW - 8f;
            if (place)
            {
                lane.Place(Pad, y + StrLaneBar.PadTop, laneW, 12f);
                AvLay.Place(pressure.rectTransform, width - Pad - PressureW, y, PercentW, 28f);
                AvLay.Place(trendText.rectTransform, width - Pad - PressureW + PercentW + 6f, y + 8f, PressureW - PercentW - 6f, 16f);
            }
            y += 30f;

            // counters: vehicles, the nearest groups with their strength, then the three honest movement numbers
            if (place)
            {
                AvLay.Place(vehicles.rectTransform, Pad, y, 62f, 18f);
                float gx = Pad + 66f, gw = (inner - 66f) / 3f;
                AvLay.Place(pipsRect, 0f, y, width, 18f);
                DrawGroups(gx, gw, e);
            }
            y += 18f + 1f;
            if (place)
            {
                float etaW = Mathf.Ceil(AvText.Width(eta)) + 4f;
                AvLay.Place(movement.rectTransform, Pad, y, inner - etaW - 6f, 16f);
                AvLay.Place(eta.rectTransform, width - Pad - etaW, y, etaW, 16f);
            }
            y += 16f + 6f;

            // tied sorties
            if (place)
            {
                AvLay.Place(sortieIcon.rectTransform, Pad, y + 2f, 14f, 14f);
                AvLay.Place(sortieTitle.rectTransform, Pad + 18f, y, 70f, 18f);
                AvLay.Place(sortieCaption.rectTransform, Pad + 92f, y, inner - 92f, 18f);
            }
            y += 18f + 3f;
            int drawn = StrViewBuilder.TicketsDrawn(vm);
            for (int i = 0; i < tickets.Length; i++)
            {
                if (i >= drawn) continue;
                float th = tickets[i].Measure(inner);
                if (place) tickets[i].Place(new AvSlot(Pad, y, inner, th));
                y += th + 3f;
            }
            if (drawn == 0)
            {
                float nh = AvText.Height(none, inner);
                if (place) AvLay.Place(none.rectTransform, Pad, y, inner, nh);
                y += nh + 6f;
            }
            if (more.gameObject.activeSelf)
            {
                if (place) AvLay.Place(more.rectTransform, Pad, y, inner, 14f);
                y += 14f + 4f;
            }

            // the wave
            if (wave.Shown)
            {
                float wh = wave.Measure(inner);
                if (place) wave.Place(new AvSlot(Pad, y, inner, wh));
                y += wh;
            }
            return y + 8f;
        }

        // The diamonds and connectors of the route strip: passed steps filled in the effort amber, the current one bright, ahead outlined.
        private void DrawRoute(float zoneW, StrEffortView e)
        {
            AvQuadBuffer b = route.Buffer;
            b.Clear();
            AvLay.Place(routeCount.rectTransform, 0f, 0f, 30f, 20f);
            float x0 = 34f, stepW = Mathf.Max(30f, (zoneW - x0) / Mathf.Max(1, e.RouteSteps));
            Rgba dim = AvInk.Dim.ToRgba(), amber = AvInk.Effort.ToRgba(), bright = (e.Alarm ? AvInk.State(AvState.Danger) : AvInk.Ink).ToRgba();
            for (int i = 0; i < e.RouteSteps && i < stepLabels.Length; i++)
            {
                float cx = x0 + i * stepW + 5f, cy = 10f;
                bool passed = i + 1 < e.RouteStep, current = i + 1 == e.RouteStep;
                Rgba c = current ? bright : passed ? amber : dim;
                if (passed || current) b.Add(cx, cy - 5f, cx + 5f, cy, cx, cy + 5f, cx - 5f, cy, c, c, c, c);
                else
                {
                    AvStrokes.Line(b, cx, cy - 5f, cx + 5f, cy, 1.2f, c); AvStrokes.Line(b, cx + 5f, cy, cx, cy + 5f, 1.2f, c);
                    AvStrokes.Line(b, cx, cy + 5f, cx - 5f, cy, 1.2f, c); AvStrokes.Line(b, cx - 5f, cy, cx, cy - 5f, 1.2f, c);
                }
                AvLay.Place(stepLabels[i].rectTransform, cx + 9f, 0f, stepW - 14f, 20f);
                stepLabels[i].color = current ? AvInk.Ink : AvInk.Dim;
                if (i + 1 < e.RouteSteps)
                {
                    float lx = cx + 10f + Mathf.Min(stepW - 30f, Mathf.Ceil(AvText.Width(stepLabels[i])) + 4f);
                    AvStrokes.Line(b, lx, cy, cx + stepW - 8f, cy, 1.2f, passed ? amber : dim.WithAlpha(0.5f));
                }
            }
            route.Commit();
        }

        private void DrawGroups(float gx, float gw, StrEffortView e)
        {
            AvQuadBuffer b = pips.Buffer;
            b.Clear();
            for (int i = 0; i < groupTag.Length && i < e.MarkCount; i++)
            {
                StrGroupRow g = e.Marks[i];
                float x = gx + i * gw;
                AvLay.Place(groupTag[i].rectTransform, x, 0f, 20f, 18f);
                Rgba c = StageInk(g.Stage).ToRgba();
                int alive = Mathf.Clamp(g.Alive, 0, 6);
                for (int k = 0; k < alive; k++) b.Add(x + 22f + k * 6f, 5f, x + 26f + k * 6f, 5f, x + 26f + k * 6f, 13f, x + 22f + k * 6f, 13f, c, c, c, c);
                float dx = x + 22f + 6f * 6f + 2f;
                AvLay.Place(groupDist[i].rectTransform, dx, 0f, Mathf.Max(30f, gw - (dx - x) - 2f), 18f);
            }
            pips.Commit();
        }

        private static Color StageInk(byte stage)
        {
            switch ((GroundStage)stage)
            {
                case GroundStage.Assault: return AvInk.Effort;
                case GroundStage.Contact: return AvInk.State(AvState.Danger);
                case GroundStage.Withdraw: return AvInk.State(AvState.Caution);
                case GroundStage.Line: return AvInk.Friendly;
                default: return AvInk.Dim;
            }
        }

        private void Paint()
        {
            frame.Paint(AvInk.Effort.WithAlpha(0.06f), AvInk.Effort.WithAlpha(0.8f));
            frame.Bracket = 8f;
            frame.BracketColor = AvInk.Effort.WithAlpha(0.8f);
            title.color = AvInk.Ink;
            picked.color = AvInk.Dim;
            routeCount.color = AvInk.Effort;
            lockIcon.color = AvInk.Effort;
            lockWord.color = AvInk.Dim;
            lockFlap.Restyle(StrInkRole.Effort);
            pressure.color = AvInk.Ink;
            trendText.color = Vm != null && Vm.Effort.Trend > 0.05f ? AvInk.State(AvState.Caution) : AvInk.Dim;
            vehicles.color = AvInk.Ink;
            for (int i = 0; i < groupTag.Length; i++) { groupTag[i].color = AvInk.Dim; groupDist[i].color = AvInk.Ink; }
            movement.color = AvInk.Ink;
            eta.color = AvInk.Dim;
            sortieIcon.color = AvInk.Key;
            sortieTitle.color = AvInk.Key;
            sortieCaption.color = AvInk.Dim;
            none.color = AvInk.Dim;
            more.color = AvInk.Dim;
        }

        public override void Restyle()
        {
            Paint();
            lane.Restyle();
            for (int i = 0; i < tickets.Length; i++) tickets[i].Restyle();
            wave.Restyle();
        }
    }

    /// <summary>One sortie tied to the operation: family chip, title and who flies it, the data line, ACCEPT, the wing button, LOCATE ON MAP, and the one line under it when the main verb is refused or the wing is out.</summary>
    internal sealed class StrSortieTicket : StrLaidPart
    {
        private const float Pad = 5f, AcceptW = 66f, WingW = 78f, ButtonGap = 4f;

        private readonly int index;
        private readonly AvFrame frame;
        private readonly StrSlab chip, state;
        private readonly TMP_Text title, by, data, note;
        private readonly StrButton accept, wing, locate;
        private StrSortieRow row;
        private StrInkRole ink = StrInkRole.Offered;
        private bool noteIsReason;

        internal StrSortieTicket(RectTransform parent, StrControls controls, IStrHub hub, int ordinal) : base(parent, "Ticket " + (ordinal + 1), controls, hub)
        {
            index = ordinal;
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            chip = new StrSlab(Rect, "Chip");
            state = new StrSlab(Rect, "State");
            title = Text(Rect, "Title", AvTextRole.Head, TextAlignmentOptions.MidlineLeft);
            by = Text(Rect, "By", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            data = Text(Rect, "Data", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
            note = Text(Rect, "Note", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            accept = new StrButton(Rect, "ACCEPT", AvButtonStyle.Primary, () => Hub.Run(StrActionKind.Accept, row.Id));
            wing = new StrButton(Rect, "SEND WING", AvButtonStyle.Default, () => Hub.Run(StrPanelText.WingTasked(row) ? StrActionKind.Recall : StrActionKind.SendWing, row.Id));
            locate = new StrButton(Rect, "LOCATE ON MAP", AvButtonStyle.Quiet, () => Locate(row.Id));
            Restyle();
        }

        private static StrInkRole FamilyInk(string chipText)
        {
            switch (chipText)
            {
                case "STRIKE": return StrInkRole.Strike;
                case "COVER":
                case "CONTROL": return StrInkRole.Effort;
                case "INSERT":
                case "RECOVER":
                case "SUPPLY": return StrInkRole.Own;
                default: return StrInkRole.Offered;
            }
        }

        protected override void Apply(StrViewModel vm)
        {
            bool on = StrPagePlan.For(vm.Mode).Hero && vm.Effort.Live && index < StrViewBuilder.TicketsDrawn(vm);
            SetShown(on);
            if (!on) { row = null; return; }
            row = vm.Sorties[index];
            StrSortieRow r = row;
            ink = FamilyInk(r.ChipText);
            chip.Set(r.ChipText, StrInk.Of(ink));
            AvText.Set(title, r.Title);
            AvText.Set(by, r.ByText);
            string line = r.DistanceText + StrWords.Dot + r.RewardText + StrWords.Dot + r.TimeText;
            if (!float.IsNaN(r.Progress)) line += StrWords.Dot + TheaterReadout.Percent(r.Progress);
            AvText.Set(data, line);
            string reason = StrPanelText.TicketReason(r);
            noteIsReason = reason.Length > 0;
            AvText.Set(note, noteIsReason ? reason : r.WingText);
            note.gameObject.SetActive(note.text.Length > 0);
            accept.SetActive(r.Offered);
            state.SetActive(!r.Offered);
            if (r.Offered)
                accept.Bind(Controls, StrControlIds.Sortie(index + 1, "accept"), "ACCEPT", () => Vm.Sorties[index].Accept, "Take this sortie. Finishing it counts toward the reinforcement wave.");
            else state.SetOutline(r.StateText.Length > 0 ? r.StateText : "DONE", r.Active ? AvInk.Effort : AvInk.Dim);
            wing.Bind(Controls, StrControlIds.Sortie(index + 1, "wing"), r.WingLabel, () => Vm.Sorties[index].Wing,
                "Task your recruited wing to this sortie. Their kills count toward it.");
            locate.Bind(Controls, StrControlIds.Sortie(index + 1, "locate"), "LOCATE ON MAP", () => LocateGate(Vm.Sorties[index].Id), "Fly the map to this sortie.");
            Paint();
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            float bx = width - Pad - AcceptW - ButtonGap - WingW, leftW = bx - Pad - 10f;
            bool hasNote = note.gameObject.activeSelf && note.text.Length > 0;
            float noteH = hasNote ? AvText.Height(note, width - 2f * Pad - 8f) : 0f;
            float h = Pad + 20f + 3f + 18f + Pad - 1f;
            if (hasNote) h += 1f + noteH;
            if (!place) return h;
            float cw = chip.WidthFor(), byW = by.text.Length > 0 ? Mathf.Ceil(AvText.Width(by)) + 4f : 0f;
            float tx = Pad + 5f + cw + 6f;
            chip.Place(Pad + 5f, Pad + 1f, cw, 18f);
            AvLay.Place(title.rectTransform, tx, Pad, Mathf.Max(30f, leftW - (tx - Pad) - byW - 4f), 20f);
            AvLay.Place(by.rectTransform, bx - 10f - byW, Pad, byW, 20f);
            AvLay.Place(data.rectTransform, Pad + 5f, Pad + 23f, leftW, 18f);
            accept.Place(bx, Pad, AcceptW, 20f);
            state.Place(bx, Pad, AcceptW, 20f);
            wing.Place(bx + AcceptW + ButtonGap, Pad, WingW, 20f);
            locate.Place(bx, Pad + 23f, AcceptW + ButtonGap + WingW, 18f);
            if (hasNote) AvLay.Place(note.rectTransform, Pad + 5f, Pad + 43f, width - 2f * Pad - 8f, noteH);
            return h;
        }

        private void Paint()
        {
            Color c = StrInk.Of(ink);
            frame.Paint(AvInk.Inert, c.WithAlpha(0.5f));
            title.color = AvInk.Ink;
            by.color = AvInk.Effort;
            data.color = AvInk.Dim;
            note.color = noteIsReason ? AvInk.State(AvState.Caution) : AvInk.Dim;
            if (row != null) chip.Set(row.ChipText, c);
        }

        public override void Restyle()
        {
            Paint();
            accept.Restyle();
            wing.Restyle();
            locate.Restyle();
        }
    }

    /// <summary>
    /// The reinforcement wave with its price up front (R4): earned pips, state and whether the pool covers it on the first line; group, cost and the pool before
    /// and after (or the held reason) on the second. A skipped wave is a persistent state on this strip, not a toast. The last purchase rides on its hover help.
    /// </summary>
    internal sealed class StrWaveStrip : StrLaidPart
    {
        private const float Pad = 8f, PipW = 12f, PipH = 6f;

        private readonly AvFrame frame;
        private readonly TMP_Text flag, title, stateText, line, check;
        private readonly AvFrame[] pips = new AvFrame[6];

        internal StrWaveStrip(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Wave", controls, hub)
        {
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            flag = AvIcons.Make(Rect, AvIcon.Flag, AvGridTokens.IconInline, Color.white);
            title = Text(Rect, "Title", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            title.text = "REINFORCEMENT WAVE";
            stateText = Text(Rect, "State", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            line = Text(Rect, "Line", AvTextRole.DataSmall, TextAlignmentOptions.TopLeft, true);
            check = Text(Rect, "Check", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = AvFrame.Add(Rect, "Pip " + i, default(AvChamfer));
                pips[i].Stroke = 1f;
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            bool on = StrPagePlan.For(vm.Mode).Hero && vm.Effort.Live;
            SetShown(on);
            if (!on) return;
            StrWaveView w = vm.Wave;
            AvText.Set(stateText, w.StateText);
            AvText.Set(line, StrPanelText.WaveLine(vm));
            AvText.Set(check, StrPanelText.WaveCheck(vm));
            for (int i = 0; i < pips.Length; i++) pips[i].gameObject.SetActive(w.Present && i < Mathf.Clamp(w.PerWave, 1, pips.Length));
            AvHelpTip.Attach(frame.gameObject, "Reinforcement wave: bought from the faction pool after finished sorties." + (w.LastText.Length > 0 ? " " + w.LastText + "." : ""));
            Paint();
            Changed();
        }

        protected override float Lay(float width, bool place)
        {
            float inner = width - 2f * Pad;
            bool present = Vm != null && Vm.Wave.Present;
            float lineH = present && line.text.Length > 0 ? Mathf.Max(15f, AvText.Height(line, inner)) : 0f;
            float h = Pad - 2f + 18f + (lineH > 0f ? 2f + lineH : 0f) + Pad - 2f;
            if (!place) return h;
            float y = Pad - 2f;
            AvLay.Place(flag.rectTransform, Pad, y + 2f, 14f, 14f);
            AvLay.Place(title.rectTransform, Pad + 18f, y, 150f, 18f);
            float px = Pad + 18f + 150f;
            for (int i = 0; i < pips.Length; i++) AvLay.Place(pips[i].rectTransform, px + i * (PipW + 3f), y + 6f, PipW, PipH);
            float checkW = check.text.Length > 0 ? Mathf.Ceil(AvText.Width(check)) + 6f : 0f;
            float stateW = Mathf.Ceil(AvText.Width(stateText)) + 6f;
            AvLay.Place(check.rectTransform, width - Pad - checkW, y, checkW, 18f);
            AvLay.Place(stateText.rectTransform, width - Pad - checkW - stateW, y, stateW, 18f);
            if (lineH > 0f) AvLay.Place(line.rectTransform, Pad, y + 20f, inner, lineH);
            return h;
        }

        private void Paint()
        {
            StrWaveView w = Vm != null ? Vm.Wave : null;
            bool held = w != null && w.Held;
            Color ink = held ? AvInk.State(AvState.Caution) : AvInk.Effort;
            frame.Paint(ink.WithAlpha(0.06f), ink.WithAlpha(0.5f));
            flag.color = ink;
            title.color = AvInk.Ink;
            stateText.color = held ? AvInk.State(AvState.Caution) : AvInk.Dim;
            line.color = AvInk.Ink;
            check.color = w != null && w.Affordable == StrTri.No ? AvInk.State(AvState.Caution) : w != null && w.Affordable == StrTri.Yes ? AvInk.State(AvState.Ready) : AvInk.Dim;
            int credits = w != null ? w.Credits : 0;
            for (int i = 0; i < pips.Length; i++)
                pips[i].Paint(i < credits ? ink : Color.clear, ink.WithAlpha(0.9f));
        }

        public override void Restyle() => Paint();
    }
}

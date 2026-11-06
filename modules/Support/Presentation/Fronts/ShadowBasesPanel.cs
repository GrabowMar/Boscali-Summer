using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SHADOW room's TEAMS &amp; BASES tab (spec 7.2), top to bottom: the front's funds; the camps (BUILD CAMP arms the map pick on ground you hold, a building camp
    /// takes CHIP IN, a lost one REBUILD at the same point) and the FOB (PLACE arms the pick on a held building); the teams, a personnel jacket each with its state, home,
    /// mission, exposure, odds and live verbs, each queued TRAIN as a jacket in training with CHIP IN, TRAIN TEAM with the reason it is greyed; the standing orders the
    /// directors follow when nobody drives. Every press is a request to <see cref="IFrontActions"/>; the host judges.
    /// </summary>
    internal sealed partial class ShadowRoom
    {
        private const float SecX = 12f, CardX = 20f, CampY = 50f, CampH = 204f, CampCardH = 46f, CampStep = 50f, TeamY = 260f, TeamH = 474f, OrderY = 740f, OrderH = 104f;
        private static readonly FrontDirective[] Orders = { FrontDirective.Recon, FrontDirective.Sabotage, FrontDirective.Hold };

        private FrontBox bases;
        private FrontVector basesG;
        private TMP_Text funds, alloc, campHead, campHint, teamHead, teamNote, teamHint, orderLock, orderLine;
        private AvControl buildCamp, train;
        private readonly AvControl[] orders = new AvControl[3];
        private readonly CampCard[] campCards = new CampCard[MaxCamps + 1]; // the camps by slot, then the FOB
        private string campWhy = "", fobWhy = "";

        // The two local map picks (one at a time) and what a miss said.
        private enum PickMode : byte { None, Camp, Fob }
        private PickMode pick;
        private string pickMiss = "";
        private readonly List<FrontPlacement.Base> ground = new List<FrontPlacement.Base>(FrontRoomView.MaxAirbases);

        private void BuildBases(RectTransform column, float cw, float ch)
        {
            bases = new FrontBox(column, skin, "Bases", 0f, 0f, cw, ch, "TEAMS & BASES", AvIcon.UsersGroup, FrontLook.Folder).Tagged("FILE 07-A");
            RectTransform b = bases.Rect;
            float secW = cw - 2f * SecX, cardW = cw - 2f * CardX;
            basesG = FrontVector.Add(b, "Sections");
            AvLay.Place(basesG.rectTransform, 0f, 0f, cw, ch);
            skin.Add(() =>
            {
                basesG.Clear();
                Slab(basesG, SecX, CampY, secW, CampH);
                Slab(basesG, SecX, TeamY, secW, TeamH);
                Slab(basesG, SecX, OrderY, secW, OrderH);
                basesG.Flush();
            });
            funds = FrontKit.Mono(b, "Funds", SecX + 2f, 26f, secW * 0.64f, 20f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            alloc = FrontKit.Mono(b, "Alloc", SecX + secW * 0.64f, 26f, secW * 0.36f - 2f, 20f, 10.5f, TextAlignmentOptions.MidlineRight);

            campHead = Head(b, "CampHead", CampY, secW);
            TMP_Text campNote = FrontKit.Mono(b, "CampNote", SecX + 150f, CampY + 5f, 200f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            campNote.text = "HOLD " + FrontRules.TeamsPerBase + " TEAMS EACH";
            buildCamp = Button(b, "BUILD CAMP", SecX + secW - 8f - 150f, CampY + 4f, 150f, 24f, AvButtonStyle.Primary, () => Arm(PickMode.Camp),
                "Arms a pick on the map: click ground you hold (the nearest airbase is yours; the rings mark safe ground). Press again or Esc to cancel. Front funds pay, then it builds (2:00).");
            for (int i = 0; i < campCards.Length; i++) campCards[i] = new CampCard(this, b, CardX, CampY + 30f + i * CampStep, cardW, i);
            campHint = FrontKit.Mono(b, "CampHint", CardX + 2f, CampY + CampH - 22f, cardW - 4f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);

            teamHead = Head(b, "TeamHead", TeamY, secW);
            teamNote = FrontKit.Mono(b, "TeamNote", SecX + 150f, TeamY + 5f, 230f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            train = Button(b, "TRAIN TEAM", SecX + secW - 8f - 150f, TeamY + 4f, 150f, 24f, AvButtonStyle.Primary, () => Do(() => actions.Queue(Front.Sof, ProgrammeId.TrainTeam, 0)),
                "Queues a team: front funds pay, then it trains at a camp with room (or the FOB). Each standing camp holds " + FrontRules.TeamsPerBase + " teams, " + FrontRules.TeamCap + " at most.");
            for (int i = 0; i < cards.Length; i++) cards[i] = new Card(this, b, CardX, cardW);
            teamHint = FrontKit.Mono(b, "TeamHint", CardX + 2f, TeamY + TeamH - 22f, cardW - 4f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);

            TMP_Text orderHead = Head(b, "OrderHead", OrderY, secW);
            orderHead.text = "STANDING ORDERS";
            orderLock = FrontKit.Mono(b, "OrderLock", SecX + secW * 0.45f, OrderY + 5f, secW * 0.55f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            float ow = (cardW - 12f) / 3f;
            string locks = ((int)FrontRules.LockSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < orders.Length; i++)
            {
                FrontDirective d = Orders[i];
                orders[i] = Button(b, FrontRules.Name(d), CardX + i * (ow + 6f), OrderY + 30f, ow, 24f, AvButtonStyle.Default, () => Do(() => actions.SetDirective(Front.Sof, d)),
                    FrontRules.Name(d) + ": " + FrontRail.Describe(Front.Sof, d) + " The directors follow it when nobody is driving. Free for any pilot on your side; a " + locks + " s lock follows.");
            }
            orderLine = FrontKit.Mono(b, "OrderLine", CardX + 2f, OrderY + 60f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            TMP_Text orderNote = FrontKit.Mono(b, "OrderNote", CardX + 2f, OrderY + 80f, cardW - 4f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
            orderNote.text = "WHEN NOBODY IS DRIVING · ANY PILOT ON YOUR SIDE MAY CHANGE IT";
            skin.Add(() =>
            {
                alloc.color = RoomInk.Khaki; campNote.color = Paper.BoardInk; teamNote.color = Paper.BoardInk; orderNote.color = Paper.BoardInk; orderHead.color = RoomInk.Gold;
            });
        }

        /// <summary>A section of the column: olive glass, a gold hairline and a 3 px gold rail (the field tablet; paper is only for the documents on it).</summary>
        private static void Slab(FrontVector g, float x, float y, float w, float h)
        {
            g.Rect(x, y, w, h, RoomInk.OliveGlass.WithAlpha(0.88f));
            g.Frame(x, y, w, h, 1f, RoomInk.Gold.WithAlpha(0.3f));
            g.Rect(x, y, 3f, h, RoomInk.Gold.WithAlpha(0.9f));
        }

        private static TMP_Text Head(RectTransform p, string name, float y, float secW) =>
            FrontKit.Mono(p, name, SecX + 10f, y + 5f, 140f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);

        private void PaintBases(FrontRoomView v)
        {
            ShadowRoomView s = v.Shadow;
            FrontRow r = v.Row;
            bool wait = FrontViews.IsWaiting(r);
            FrontKit.Set(funds, wait ? "FRONT FUNDS — · WAITING FOR THE HOST" : "FRONT FUNDS " + r.Budget + " · +" + v.FundsRate + " / MIN · CAP " + (int)FrontRules.BudgetCap,
                wait ? RoomInk.Khaki : RoomInk.FieldInk);
            FrontKit.Set(alloc, "YOUR ALLOCATION " + v.Allocation + " AP", RoomInk.Khaki);
            bases.SetMeta("CAMPS " + s.CampsUp + "/" + FrontRules.CampCap + " · TEAMS " + s.TeamsUp + "/" + FrontRules.TeamCap + (s.Fob.Up ? " · FOB UP" : ""), RoomInk.Gold.WithAlpha(0.8f));

            // Camps: BUILD CAMP needs a free camp (the offer's refusal) and ground we hold to click. The FOB card follows them.
            FrontOfferView camp = FrontPanels.OfferOf(v, ProgrammeId.Camp), fob = FrontPanels.OfferOf(v, ProgrammeId.Fob);
            bool held = false;
            foreach (MapAirbaseView a in v.Airbases) held |= a.Side == AirbaseSide.Own;
            campWhy = !string.IsNullOrEmpty(camp.Refusal) ? camp.Refusal : held ? "" : FrontWords.Refusal(FrontOutcome.NoSite, Front.Sof);
            fobWhy = !string.IsNullOrEmpty(fob.Refusal) ? fob.Refusal : s.Held.Count == 0 ? FrontWords.Refusal(FrontOutcome.NoHeldBuilding, Front.Sof) : "";
            FrontKit.Set(campHead, "CAMPS " + s.CampsUp + " / " + FrontRules.CampCap, RoomInk.Gold);
            buildCamp.Latched = pick == PickMode.Camp;
            OpsKit.Enable(buildCamp, pick == PickMode.Camp || campWhy.Length == 0);
            for (int i = 0; i < campCards.Length; i++) campCards[i].Paint(v);
            string hint = pick != PickMode.None ? PickWords(false)
                : campWhy.Length > 0 ? "BUILD CAMP · " + campWhy
                : "A CAMP: " + Mathf.RoundToInt(camp.Cost) + " FUNDS · BUILDS " + FrontKit.Clock(camp.BuildSeconds) + " · ON GROUND YOU HOLD · HOLDS " + FrontRules.TeamsPerBase + " TEAMS";
            FrontKit.Set(campHint, FrontKit.Fit(campHint, hint, 10.5f), pick != PickMode.None && pickMiss.Length > 0 ? Paper.Grease : pick != PickMode.None || campWhy.Length > 0 ? RoomInk.Gold : RoomInk.Khaki);

            // Teams: every team on the roster, then the queued ones in training; TRAIN TEAM greys with the ladder's reason.
            FrontOfferView tr = FrontPanels.OfferOf(v, ProgrammeId.TrainTeam);
            int room = Math.Max(0, FrontRules.TeamsPerBase * (s.CampsUp + (s.Fob.Up ? 1 : 0)));
            FrontKit.Set(teamHead, "TEAMS " + s.TeamsUp + " / " + FrontRules.TeamCap, RoomInk.Gold);
            FrontKit.Set(teamNote, "TRAIN " + Mathf.RoundToInt(tr.Cost) + " · " + FrontKit.Clock(tr.BuildSeconds) + " · ROOM FOR " + Math.Min(room, FrontRules.TeamCap), Paper.BoardInk);
            OpsKit.Enable(train, string.IsNullOrEmpty(tr.Refusal));
            float y = TeamY + 30f;
            for (int i = 0; i < cards.Length; i++) y = i < s.Teams.Count ? cards[i].Paint(v, s.Teams[i], y) : cards[i].Hide(y);
            string why = !string.IsNullOrEmpty(tr.Refusal) ? "TRAIN TEAM · " + tr.Refusal
                : s.Teams.Count == 0 ? "NO TEAM YET · TRAIN ONE: IT RAISES AT A CAMP WITH ROOM" : "PICK A TEAM, THEN SEND IT FROM TARGETS (OR CLICK MAP GROUND FOR RECON)";
            FrontKit.Set(teamHint, FrontKit.Fit(teamHint, why, 10.5f), string.IsNullOrEmpty(tr.Refusal) ? RoomInk.Khaki : RoomInk.Gold);

            // Standing orders: a lock held by someone else disables them here (the host still judges).
            float left = r.DirectiveLockUntil - v.Now;
            bool locked = !wait && left > 0f, blocked = locked && !FrontRail.SameName(r.DirectiveBy, v.Callsign);
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].Latched = !wait && r.Directive == Orders[i];
                OpsKit.Enable(orders[i], !wait && !blocked);
            }
            FrontKit.Set(orderLock, wait ? "WAITING FOR HOST" : locked ? FrontRail.LockWord(!blocked, r.DirectiveBy, left) : FrontRules.Name(r.Directive) + " ACTIVE", locked ? RoomInk.Gold : RoomInk.Khaki);
            FrontKit.Set(orderLine, FrontKit.Fit(orderLine, wait ? "WAITING FOR THE HOST" : "> " + FrontRail.Describe(Front.Sof, r.Directive).ToUpperInvariant(), 10.5f), RoomInk.FieldInk);
        }

        // ---- Map picks -----------------------------------------------------------------------------------------------

        /// <summary>BUILD CAMP or PLACE FOB: arms that pick (the same press again disarms it; one pick at a time).</summary>
        private void Arm(PickMode mode)
        {
            actions.Touch();
            pick = pick == mode ? PickMode.None : mode;
            pickMiss = "";
            Repaint();
        }

        /// <summary>The window closed, switched rooms or left TEAMS &amp; BASES: the armed pick ends (nothing was sent).</summary>
        public void EndPicks() { pick = PickMode.None; pickMiss = ""; }

        public bool CancelMode()
        {
            if (pick == PickMode.None) return false;
            actions.Touch();
            EndPicks();
            return true;
        }

        /// <summary>What the armed pick wants (or why the last click missed), short for the map's foot or long for the camp section's hint.</summary>
        private string PickWords(bool foot)
        {
            if (pickMiss.Length > 0) return pickMiss + (pick == PickMode.Camp ? " · INSIDE A RING IS SAFE" : " · CLICK INSIDE A RING") + " · ESC CANCELS";
            return pick == PickMode.Camp ? "BUILD CAMP · CLICK GROUND YOU HOLD" + (foot ? "" : " (THE NEAREST AIRBASE IS YOURS; RINGED ON THE MAP)") + " · ESC CANCELS"
                : "PLACE FOB · CLICK A HELD BUILDING" + (foot ? "" : " (RINGED ON THE MAP, 1 KM)") + " · ESC CANCELS";
        }

        /// <summary>The theatre's airbases as the host's own-ground rule reads them (<see cref="FrontPlacement.OwnGround"/>), in world metres.</summary>
        private List<FrontPlacement.Base> Ground(FrontRoomView v)
        {
            ground.Clear();
            MapFrame f = FrontController.Frame();
            foreach (MapAirbaseView a in v.Airbases) ground.Add(new FrontPlacement.Base(ground.Count, f.X(a.U), f.Z(a.V), a.Side == AirbaseSide.Own, false, true));
            return ground;
        }

        /// <summary>
        /// Where a camp is surely on our ground: round each airbase we hold, half the way to the nearest airbase that is not ours (25 km with none); metres. The click
        /// itself is judged by the host's rule over the whole map, so a click outside a ring may still be ground we hold.
        /// </summary>
        private static float SafeRadius(List<FrontPlacement.Base> bases, in FrontPlacement.Base own)
        {
            float best = 50000f;
            foreach (FrontPlacement.Base b in bases)
            {
                if (b.Ours) continue;
                float dx = b.X - own.X, dz = b.Z - own.Z;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best * 0.5f;
        }

        /// <summary>
        /// The armed pick's click: on ground we hold it queues a CAMP at the point, within 1 km of a held building a FOB on that building, and disarms; anywhere else
        /// it says why and stays armed. The host judges the place again.
        /// </summary>
        private void PickAt(Vector2 uv)
        {
            actions.Touch();
            MapFrame f = FrontController.Frame();
            if (pick == PickMode.Camp)
            {
                if (!FrontPlacement.OwnGround(Ground(last), f.X(uv.x), f.Z(uv.y))) { pickMiss = FrontWords.Refusal(FrontOutcome.NotOwnGround, Front.Sof); return; }
                EndPicks();
                actions.Queue(Front.Sof, ProgrammeId.Camp, 0, true, uv.x, uv.y);
                return;
            }
            int best = -1;
            float bestD = FrontRules.FobPickMetres;
            for (int i = 0; i < lastView.Held.Count; i++)
            {
                SofHeldView h = lastView.Held[i];
                float dx = (h.X - uv.x) * f.W, dz = (h.Y - uv.y) * f.H, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= bestD) { bestD = d; best = i; }
            }
            if (best < 0) { pickMiss = "PICK A SEIZED BUILDING"; return; }
            EndPicks();
            actions.Queue(Front.Sof, ProgrammeId.Fob, 0, true, lastView.Held[best].X, lastView.Held[best].Y); // snapped to the building (ruling D-R3: the host takes the held one within 1 km)
        }

        /// <summary>The armed pick's rings on the map: the safe ground round each airbase we hold (CAMP), or 1 km round each held building (FOB).</summary>
        private void PaintPickRings(FrontVector g, FrontRoomView v)
        {
            if (pick == PickMode.None) return;
            FrontMapView mv = plate.View;
            MapFrame f = FrontController.Frame();
            Color k = Own;
            if (pick == PickMode.Camp)
            {
                List<FrontPlacement.Base> bases = Ground(v);
                foreach (FrontPlacement.Base b in bases)
                {
                    if (!b.Ours) continue;
                    Vector2 p = P(f.U(b.X), f.V(b.Z));
                    float r = Mathf.Max(14f, SafeRadius(bases, b) / f.W * mv.Scale);
                    g.Disc(p, r, k.WithAlpha(0.06f), 64);
                    g.Ring(p, r, 1.8f, k.WithAlpha(0.95f), 72);
                    g.Ring(p, r + 4f, 1f, k.WithAlpha(0.4f), 72, 1f, 1f);
                }
                return;
            }
            float rf = Mathf.Max(12f, FrontRules.FobPickMetres / f.W * mv.Scale);
            foreach (SofHeldView h in v.Shadow.Held)
            {
                Vector2 p = P(h.X, h.Y);
                g.Disc(p, rf, k.WithAlpha(0.12f), 32);
                g.Ring(p, rf, 1.8f, k.WithAlpha(0.95f), 36);
                g.Ring(p, rf + 4f, 1f, k.WithAlpha(0.4f), 36, 1f, 1f);
            }
        }

        // ---- Camp and FOB cards ----------------------------------------------------------------------------------------

        /// <summary>
        /// One camp slot (CAMP-1 is host slot 0) or the FOB, as a paper index card with a pin in its state's colour. A camp: UP / DAMAGED with the teams it holds,
        /// FUNDING / BUILDING with its bar and CHIP IN, LOST with REBUILD at the same point, or NOT BUILT. The FOB (ruling D-R3): UP with the time left, FUNDING /
        /// BUILDING with CHIP IN, NOT BUILT with PLACE (the held-building pick), or NEEDS A SEIZED BUILDING.
        /// </summary>
        private sealed class CampCard
        {
            private readonly ShadowRoom room;
            private readonly int index;
            private readonly float w;
            private readonly FrontVector gfx;
            private readonly TMP_Text name, state, line;
            private readonly AvControl act;
            private int verb; // what the button does now: 0 nothing, 1 CHIP IN, 2 REBUILD, 3 PLACE FOB
            private ushort row;
            private float u, v;

            private bool Fob => index >= MaxCamps;

            public CampCard(ShadowRoom room, RectTransform p, float x, float y, float w, int index)
            {
                this.room = room; this.index = index; this.w = w;
                RectTransform root = AvLay.Child(p, index >= MaxCamps ? "FobCard" : "CampCard" + index);
                AvLay.Place(root, x, y, w, CampCardH);
                gfx = FrontVector.Add(root, "Card");
                AvLay.Place(gfx.rectTransform, 0f, 0f, w, CampCardH);
                name = FrontKit.Mono(root, "Name", 30f, 3f, w - 30f - 216f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", w - 8f - 210f, 3f, 210f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                line = FrontKit.Mono(root, "Line", 30f, 22f, w - 30f - 196f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
                act = room.Button(root, "CHIP IN", w - 8f - 180f, 22f, 180f, 20f, AvButtonStyle.Default, Press, "");
            }

            private void Press()
            {
                FrontRoomView fv = room.last;
                if (fv == null) return;
                room.actions.Touch();
                if (verb == 1) FrontPanels.Donate(room.actions, Front.Sof, fv, row);
                else if (verb == 2) room.actions.Queue(Front.Sof, ProgrammeId.Camp, 0, true, u, v); // REBUILD: the same programme at the same point
                else if (verb == 3) room.Arm(PickMode.Fob);
            }

            public void Paint(FrontRoomView fv)
            {
                ShadowRoomView s = fv.Shadow;
                string nm, st, a;
                AvState tone;
                float fill = -1f;
                bool dashed = false, cross = false;
                verb = 0;
                if (Fob)
                {
                    SofFobView f = s.Fob;
                    nm = "FOB · " + Mathf.RoundToInt(FrontPanels.OfferOf(fv, ProgrammeId.Fob).Cost) + " · REARM + REFUEL";
                    st = f.State; a = f.Note; tone = f.Tone;
                    dashed = !f.Up;
                    if (f.Queued) { fill = f.Progress; row = f.Row; verb = Chip(fv, f.Row) > 0 ? 1 : 0; }
                    else if (!f.Up) verb = 3; // PLACE FOB, greyed with the reason while no building is held
                }
                else
                {
                    nm = FrontViews.CampName(index);
                    SofCampView c = default;
                    bool has = false;
                    foreach (SofCampView x in s.Camps) if (x.Slot == index) { c = x; has = true; break; }
                    if (!has) { st = "NOT BUILT"; a = "BUILD CAMP, THEN CLICK GROUND YOU HOLD"; tone = AvState.Inert; dashed = true; }
                    else
                    {
                        nm += " · " + c.Place;
                        u = c.X; v = c.Y;
                        switch (c.State)
                        {
                            case InfraState.Building:
                                bool building = FrontPanels.QueueRow(fv, c.Row, out FrontQueueView q) && q.Building;
                                int total = Mathf.RoundToInt(q.Cost), funded = building ? total : Mathf.Min(total, Mathf.RoundToInt(q.Cost * q.Progress));
                                st = building ? "BUILDING " + FrontKit.Clock(c.BuildLeft) : "FUNDING " + Mathf.RoundToInt(Mathf.Clamp01(c.Progress) * 100f) + " %";
                                a = building ? "UP IN " + FrontKit.Clock(c.BuildLeft) + " · THEN IT HOLDS " + FrontRules.TeamsPerBase + " TEAMS" : funded + " / " + total + " FUNDS · CHIP IN SPEEDS IT UP";
                                tone = building ? AvState.Caution : AvState.Info;
                                fill = c.Progress; row = c.Row; dashed = true;
                                verb = Chip(fv, c.Row) > 0 ? 1 : 0;
                                break;
                            case InfraState.Lost:
                                st = "LOST"; a = "REBUILD IT AT THE SAME SPOT · FULL PRICE"; tone = AvState.Danger; cross = true;
                                verb = 2;
                                break;
                            default:
                                bool damaged = c.State == InfraState.Damaged;
                                st = damaged ? "DAMAGED · TRAINS 1.5x SLOWER" : "UP";
                                a = c.Holds; tone = damaged ? AvState.Caution : AvState.Ready;
                                break;
                        }
                    }
                }
                Color k = Paper.Of(tone);
                FrontKit.Set(name, FrontKit.Fit(name, nm, 11.5f), Paper.Pen);
                FrontKit.Set(state, FrontKit.Fit(state, st, 10.5f), k);
                FrontKit.Set(line, FrontKit.Fit(line, a, 10.5f), tone == AvState.Danger ? Paper.Red : Paper.PenDim);
                bool show = verb != 0;
                if (act.Rect.gameObject.activeSelf != show) act.Rect.gameObject.SetActive(show);
                if (verb == 1)
                {
                    int chip = Chip(fv, row);
                    OpsKit.Label(act, "CHIP IN · " + FrontRail.Pays(chip));
                    OpsKit.Enable(act, fv.Allocation >= chip);
                    act.Latched = false;
                    act.Help = "Optional: speed it up with your allocation. Front funds pay for it anyway.";
                }
                else if (verb == 2)
                {
                    OpsKit.Label(act, "REBUILD");
                    OpsKit.Enable(act, room.campWhy.Length == 0);
                    act.Latched = false;
                    act.Help = room.campWhy.Length == 0 ? "Queues a new CAMP at the same point, full price. The host checks the ground is still yours." : room.campWhy;
                }
                else if (verb == 3)
                {
                    OpsKit.Label(act, "PLACE FOB");
                    OpsKit.Enable(act, room.pick == PickMode.Fob || room.fobWhy.Length == 0);
                    act.Latched = room.pick == PickMode.Fob;
                    act.Help = room.fobWhy.Length == 0 ? "Arms a pick on the map: click a building your teams hold (ringed, 1 km). Press again or Esc to cancel. Front funds pay, then it builds (3:00)." : room.fobWhy;
                }

                // The card: shadow, cream stock, a pin in the state's colour (dashed while only coming, crossed once lost), a dark plate under the button.
                FrontVector g = gfx;
                g.Clear();
                g.Rect(1.5f, 2f, w, CampCardH, Paper.Shadow);
                g.Rect(0f, 0f, w, CampCardH, dashed ? Paper.Card.WithAlpha(0.85f) : Paper.Card);
                if (dashed) Hand.DashRect(g, 0.5f, 0.5f, w - 1f, CampCardH - 1f, 1f, Paper.CardEdge);
                else g.Frame(0f, 0f, w, CampCardH, 1f, Paper.CardEdge);
                g.Rect(0f, 0f, 4f, CampCardH, Paper.TabFill(tone));
                Vector2 pin = new Vector2(17f, 12f);
                if (dashed) g.Ring(pin, 5f, 1.4f, k, 16, 1f, 1f); else Hand.Pin(g, pin, 5f, Paper.TabFill(tone));
                if (cross) FrontKit.Cross(g, pin, 6f, Paper.Red);
                if (fill >= 0f) Hand.Gauge(g, 30f, 40.5f, w - 30f - 196f, 4f, fill, tone == AvState.Caution ? Paper.GaugeWarn : Paper.GaugeAmmo);
                if (show) g.Rect(w - 8f - 181f, 21f, 182f, 22f, Paper.Hole);
                g.Flush();
            }

            private static int Chip(FrontRoomView fv, ushort row) => FrontPanels.QueueRow(fv, row, out FrontQueueView q) ? FrontPanels.ChipAp(q) : 0;
        }
    }
}

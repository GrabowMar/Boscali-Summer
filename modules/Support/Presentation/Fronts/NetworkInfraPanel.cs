using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The NETWORK room's INFRASTRUCTURE tab (spec 6.2), top to bottom: the front's funds; the data centers (BUILD DATA CENTER arms the map pick, a building one
    /// takes CHIP IN, a lost one REBUILD at the same point); the EW trucks (what their reach covers, the trace of the sessions they source, RELOCATE: pack up for
    /// 45 s and redeploy at a picked point; DEPLOY TRUCK arms the pick); the standing orders the directors follow when nobody drives; a key to the map.
    /// Every press is a request to <see cref="IFrontActions"/>; the host judges.
    /// </summary>
    internal sealed partial class NetworkRoom
    {
        private const float SecX = 12f, CardX = 20f, CardH = 76f, CardStep = 80f, DcY = 50f, SecH = 224f, TrY = 282f, OrderY = 514f, OrderH = 112f, KeyY = 634f, KeyH = 108f;
        private static readonly FrontDirective[] Orders = { FrontDirective.Defend, FrontDirective.Balanced, FrontDirective.Attack };

        private FrontBox infra;
        private FrontVector infraG;
        private TMP_Text funds, alloc, dcHead, dcNote, dcHint, trHead, trNote, trHint, orderHead, orderLock, orderLine;
        private AvControl buildDc, deploy;
        private readonly AvControl[] orders = new AvControl[3];
        private readonly InfraCard[] centerCards = new InfraCard[FrontRules.DataCenterCap], truckCards = new InfraCard[FrontRules.TruckCap];
        private string dcWhy = "", trWhy = "";

        private void BuildInfra(RectTransform column, float cw, float ch)
        {
            infra = new FrontBox(column, skin, "Infrastructure", 0f, 0f, cw, ch, "INFRASTRUCTURE · NETWORK", AvIcon.Database, FrontLook.Terminal).Tagged("tty4");
            RectTransform b = infra.Rect;
            float secW = cw - 2f * SecX, cardW = cw - 2f * CardX;
            infraG = FrontVector.Add(b, "Sections");
            AvLay.Place(infraG.rectTransform, 0f, 0f, cw, ch);
            skin.Add(() =>
            {
                infraG.Clear();
                Slab(infraG, SecX, DcY, secW, SecH, Phos);
                Slab(infraG, SecX, TrY, secW, SecH, Phos);
                Slab(infraG, SecX, OrderY, secW, OrderH, Amber);
                Slab(infraG, SecX, KeyY, secW, KeyH, PhosDim);
                Key(infraG, CardX + 10f, KeyY + 38f, cardW * 0.5f);
                infraG.Flush();
            });
            funds = FrontKit.Mono(b, "Funds", SecX + 2f, 26f, secW * 0.64f, 20f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            alloc = FrontKit.Mono(b, "Alloc", SecX + secW * 0.64f, 26f, secW * 0.36f - 2f, 20f, 10.5f, TextAlignmentOptions.MidlineRight);

            dcHead = SecHead(b, "DcHead", DcY, secW);
            dcNote = FrontKit.Mono(b, "DcNote", SecX + 190f, DcY + 5f, 170f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            dcNote.text = "EACH RUNS ONE TRUCK";
            buildDc = Small(b, "BUILD DATA CENTER", SecX + secW - 8f - 190f, DcY + 4f, 190f, () => Arm(PickMode.Center),
                "Arms a pick on the map: click within 8 km of an airbase you hold (they are ringed). Press again or Esc to cancel. Front funds pay, then it builds (2:00).");
            buildDc.SetStyle(AvButtonStyle.Primary);
            for (int i = 0; i < centerCards.Length; i++) centerCards[i] = new InfraCard(this, b, CardX, DcY + 32f + i * CardStep, cardW, false, i);
            dcHint = Wrapped(b, "DcHint", CardX + 2f, DcY + SecH - 32f, cardW - 4f, 30f, 10.5f);

            trHead = SecHead(b, "TrHead", TrY, secW);
            trNote = FrontKit.Mono(b, "TrNote", SecX + 190f, TrY + 5f, 200f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            trNote.text = "REACH = WHERE HACKS START";
            deploy = Small(b, "DEPLOY TRUCK", SecX + secW - 8f - 190f, TrY + 4f, 190f, () => Arm(PickMode.Truck),
                "Arms a pick on the map: click within 20 km of a data center (standing or queued; they are ringed). Press again or Esc to cancel. Front funds pay, then it builds (1:30).");
            deploy.SetStyle(AvButtonStyle.Primary);
            for (int i = 0; i < truckCards.Length; i++) truckCards[i] = new InfraCard(this, b, CardX, TrY + 32f + i * CardStep, cardW, true, i);
            trHint = Wrapped(b, "TrHint", CardX + 2f, TrY + SecH - 32f, cardW - 4f, 30f, 10.5f);

            orderHead = SecHead(b, "OrderHead", OrderY, secW);
            orderHead.text = "STANDING ORDERS";
            orderLock = FrontKit.Mono(b, "OrderLock", SecX + secW * 0.45f, OrderY + 5f, secW * 0.55f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            float ow = (cardW - 12f) / 3f;
            string locks = ((int)FrontRules.LockSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < orders.Length; i++)
            {
                FrontDirective d = Orders[i];
                orders[i] = Small(b, FrontRules.Name(d), CardX + i * (ow + 6f), OrderY + 30f, ow, () => { actions.Touch(); actions.SetDirective(Front.Cyber, d); },
                    FrontRules.Name(d) + ": " + FrontRail.Describe(Front.Cyber, d) + " The directors follow it when nobody is driving. Free for any pilot on your side; a " + locks + " s lock follows.");
            }
            orderLine = FrontKit.Mono(b, "OrderLine", CardX + 2f, OrderY + 60f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            TMP_Text orderNote = FrontKit.Mono(b, "OrderNote", CardX + 2f, OrderY + 80f, cardW - 4f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
            orderNote.text = "WHEN NOBODY IS DRIVING · ANY PILOT ON YOUR SIDE MAY CHANGE IT";

            TMP_Text keyHead = SecHead(b, "KeyHead", KeyY, secW);
            keyHead.text = "ON THE MAP";
            string[] words = { "YOURS (DATA CENTER, TRUCK)", "QUEUED BUILD · ITS TIMER", "TRUCK REACH", "BREACHED · AN ENEMY INSIDE", "IN REACH · HOP IT", "HELD", "HOPPING", "OUT OF REACH" };
            var keyText = new TMP_Text[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                keyText[i] = FrontKit.Mono(b, "Key" + i, CardX + 34f + (i / 4) * cardW * 0.5f, KeyY + 30f + (i % 4) * 18f, cardW * 0.5f - 40f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
                keyText[i].text = words[i];
            }
            skin.Add(() =>
            {
                alloc.color = PhosDim; dcNote.color = PhosDim; trNote.color = PhosDim; orderNote.color = PhosDim; orderHead.color = Amber; keyHead.color = PhosDim;
                foreach (TMP_Text t in keyText) t.color = PhosDim;
            });
        }

        /// <summary>A section of the column: dark glass, a phosphor hairline, a 3 px rail in the section's hue.</summary>
        private static void Slab(FrontVector g, float x, float y, float w, float h, Color rail)
        {
            g.Rect(x, y, w, h, Glass.WithAlpha(0.88f));
            g.Frame(x, y, w, h, 1f, Phos.WithAlpha(0.28f));
            g.Rect(x, y, 3f, h, rail.WithAlpha(0.9f));
        }

        /// <summary>The map key's swatches, two columns of four: ours, a ghost, a truck's reach, a breach; then the enemy node states.</summary>
        private static void Key(FrontVector g, float x, float y, float colW)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 p = new Vector2(x + 8f + (i / 4) * colW, y + (i % 4) * 18f);
                switch (i)
                {
                    case 0: g.Disc(p, 7f, Glass, 6); Hex(g, p, 7f, Phos, 1.4f); g.Disc(p, 2f, Phos, 8); break;
                    case 1: DashedHex(g, p, 7f, Phos.WithAlpha(0.85f)); break;
                    case 2: g.Ring(p, 7f, 1.2f, Phos.WithAlpha(0.7f), 20, 1f, 1f); break;
                    case 3: Hex(g, p, 6f, Phos, 1.2f); Flag(g, p + new Vector2(3f, 7f), 12f, TraceRed); break;
                    default:
                        Color k = i == 4 ? Amber : i == 5 ? Phos : i == 6 ? HopInk : OutInk;
                        g.Disc(p, 7f, Glass, 6); Hex(g, p, 7f, k.WithAlpha(0.6f), 1f); g.Disc(p, 2.4f, k, 8);
                        break;
                }
            }
        }

        /// <summary>A section heading on its slab.</summary>
        private static TMP_Text SecHead(RectTransform p, string name, float y, float secW) =>
            FrontKit.Mono(p, name, SecX + 10f, y + 5f, 178f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);

        private AvControl Small(RectTransform p, string label, float x, float y, float w, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default));
            c.Accent = Phos;
            AvLay.Place(c.Rect, x, y, w, 24f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        /// <summary>A card clicked: one of ours is picked (its node lit on the map) and the map centres on it. Local: nothing is sent.</summary>
        private void CardClick(int ownId, float u, float v)
        {
            actions.Touch();
            if (ownId != 0) selOwn = ownId;
            if (u >= 0f) plate.View.CenterOn(new Vector2(u, v));
            Repaint();
        }

        private void PaintInfra(FrontRoomView v)
        {
            NetworkRoomView n = v.Network;
            FrontRow r = v.Row;
            bool wait = FrontViews.IsWaiting(r);
            FrontKit.Set(funds, wait ? "FRONT FUNDS — · WAITING FOR THE HOST" : "FRONT FUNDS " + r.Budget + " · +" + v.FundsRate + " / MIN · CAP " + (int)FrontRules.BudgetCap,
                wait ? PhosDim : Bright);
            FrontKit.Set(alloc, "YOUR ALLOCATION " + v.Allocation + " AP", PhosDim);
            infra.SetMeta("DATA CENTERS " + n.CentersUp + "/" + FrontRules.DataCenterCap + " · TRUCKS " + n.TrucksUp + "/" + FrontRules.TruckCap, PhosDim);

            // Data centers: BUILD DATA CENTER needs a free slot (the offer's refusal) and a held land airbase to click.
            bool held = false;
            foreach (MapAirbaseView a in v.Airbases) held |= a.Side == AirbaseSide.Own;
            dcWhy = !string.IsNullOrEmpty(n.BuildCenter.Refusal) ? n.BuildCenter.Refusal : held ? "" : FrontWords.Refusal(FrontOutcome.NoSite, Front.Cyber);
            FrontKit.Set(dcHead, "DATA CENTERS " + n.CentersUp + " / " + FrontRules.DataCenterCap, Phos);
            buildDc.Latched = pick == PickMode.Center;
            OpsKit.Enable(buildDc, pick == PickMode.Center || dcWhy.Length == 0);
            string effect = "CUTS YOUR TRACE ×" + CyberRules.DataCenterTraceFactor.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
            Hint(dcHint, PickMode.Center, dcWhy, "BUILD DATA CENTER",
                "A DATA CENTER: " + Mathf.RoundToInt(n.BuildCenter.Cost) + " FUNDS · BUILDS " + FrontKit.Clock(n.BuildCenter.BuildSeconds) + " · AT A HELD AIRBASE · RUNS ONE EW TRUCK · " + effect);
            for (int i = 0; i < centerCards.Length; i++) centerCards[i].Paint(v);

            // Trucks: DEPLOY TRUCK needs a free data center (the offer's refusal) and a ring to click in (a standing data center, or a queued one with a point).
            trWhy = !string.IsNullOrEmpty(n.DeployTruck.Refusal) ? n.DeployTruck.Refusal : Rings(v, PickMode.Truck).Count > 0 ? "" : "WAITS FOR A DATA CENTER WITH A SPOT";
            FrontKit.Set(trHead, "EW TRUCKS " + n.TrucksUp + " / " + FrontRules.TruckCap, Phos);
            deploy.Latched = pick == PickMode.Truck;
            OpsKit.Enable(deploy, pick == PickMode.Truck || trWhy.Length == 0);
            Hint(trHint, pick == PickMode.Relocate ? PickMode.Relocate : PickMode.Truck, trWhy, "DEPLOY TRUCK",
                "AN EW TRUCK: " + Mathf.RoundToInt(n.DeployTruck.Cost) + " FUNDS · BUILDS " + FrontKit.Clock(n.DeployTruck.BuildSeconds) + " · WITHIN 20 KM OF A DATA CENTER · ITS REACH IS WHERE HACKS START");
            for (int i = 0; i < truckCards.Length; i++) truckCards[i].Paint(v);

            // Standing orders: a lock held by someone else disables them here (the host still judges).
            float left = r.DirectiveLockUntil - v.Now;
            bool locked = !wait && left > 0f, blocked = locked && !FrontRail.SameName(r.DirectiveBy, v.Callsign);
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].Latched = !wait && r.Directive == Orders[i];
                OpsKit.Enable(orders[i], !wait && !blocked);
            }
            FrontKit.Set(orderLock, wait ? "WAITING FOR HOST" : locked ? FrontRail.LockWord(!blocked, r.DirectiveBy, left) : FrontRules.Name(r.Directive) + " ACTIVE", locked ? Amber : PhosDim);
            FrontKit.Set(orderLine, FrontKit.Fit(orderLine, wait ? "WAITING FOR THE HOST" : "> " + FrontRail.Describe(Front.Cyber, r.Directive).ToUpperInvariant(), 10.5f), Bright);
        }

        /// <summary>A section's foot: the armed pick's words (the miss in trace red), else why its button is greyed, else what the build costs and does.</summary>
        private void Hint(TMP_Text t, PickMode mode, string why, string verb, string offer)
        {
            bool armed = pick == mode;
            string s = armed ? PickWords(false) : why.Length > 0 ? verb + " · " + why : offer;
            FrontKit.Set(t, s, armed && pickMiss.Length > 0 ? TraceRed : armed || why.Length > 0 ? Amber : PhosDim);
        }

        // ---- Cards -------------------------------------------------------------------------------------------------

        /// <summary>
        /// One data center or truck slot. A center: UP / DAMAGED with the truck it runs and what it does for the trace, FUNDING / BUILDING with its bar and CHIP IN,
        /// LOST with REBUILD at the same point, or NOT BUILT. A truck: UP / DAMAGED / LOCKED with the enemy nodes in its reach, the trace of its sessions and RELOCATE,
        /// PACKING with the room's countdown, FUNDING / BUILDING with CHIP IN, DOWN, or NOT DEPLOYED. A click lights it on the map and centres there.
        /// </summary>
        private sealed class InfraCard
        {
            private readonly NetworkRoom room;
            private readonly bool truck;
            private readonly int slot;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly FrontVector glyph;
            private readonly TMP_Text name, state, lineA, lineB;
            private readonly FrontBar bar;
            private readonly AvControl act;
            private bool has, lit;
            private InfraCenterView center;
            private InfraTruckView unit;

            public InfraCard(NetworkRoom room, RectTransform p, float x, float y, float w, bool truck, int slot)
            {
                this.room = room; this.truck = truck; this.slot = slot;
                RectTransform root = AvLay.Child(p, (truck ? "TruckCard" : "CenterCard") + slot);
                AvLay.Place(root, x, y, w, CardH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, CardH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, CardH, Color.clear);
                glyph = FrontVector.Add(root, "Glyph");
                AvLay.Place(glyph.rectTransform, 0f, 0f, w, CardH);
                name = FrontKit.Mono(root, "Name", 32f, 2f, w - 32f - 262f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", w - 8f - 254f, 2f, 254f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                lineA = FrontKit.Mono(root, "LineA", 10f, 23f, w - 20f - 196f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
                lineB = FrontKit.Mono(root, "LineB", 10f, 40f, w - 20f - 196f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
                bar = new FrontBar(root, room.skin, 10f, 61f, w - 20f - 196f, 6f);
                act = room.Small(root, truck ? "RELOCATE" : "REBUILD", w - 8f - 186f, 28f, 186f, Press, "");
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Click(); };
                room.skin.Add(Restyle);
            }

            private void Restyle() =>
                frame.Paint(lit ? Color.Lerp(RoomInk.CrtBar, Phos, 0.14f).WithAlpha(0.96f) : RoomInk.CrtBar, lit ? Phos : Phos.WithAlpha(0.3f));

            private float U => !has ? -1f : truck ? unit.U : center.U;
            private float V => !has ? -1f : truck ? unit.V : center.V;
            private bool Standing => has && (truck ? unit.State != InfraState.Building : center.State != InfraState.Building);
            private int OwnId => NetworkRoomView.OwnId(truck ? AnchorKind.EwTruck : AnchorKind.DataCenter, slot);

            private void Click()
            {
                if (has) room.CardClick(Standing ? OwnId : 0, U, V);
            }

            private void Press()
            {
                FrontRoomView v = room.last;
                if (!has || v == null) return;
                room.actions.Touch();
                InfraState st = truck ? unit.State : center.State;
                if (st == InfraState.Building) FrontPanels.Donate(room.actions, Front.Cyber, v, truck ? unit.Row : center.Row);
                else if (truck) room.Arm(PickMode.Relocate, slot);
                else if (st == InfraState.Lost && center.U >= 0f) room.actions.Queue(Front.Cyber, ProgrammeId.DataCenter, 0, true, center.U, center.V); // REBUILD: the same programme at the same point
            }

            public void Paint(FrontRoomView v)
            {
                NetworkRoomView n = v.Network;
                has = false;
                if (truck) { foreach (InfraTruckView t in n.Trucks) if (t.Ordinal == slot) { unit = t; has = true; break; } }
                else foreach (InfraCenterView c in n.Centers) if (c.Slot == slot) { center = c; has = true; break; }
                bool sel = Standing && room.selOwn == OwnId;
                if (lit != sel) { lit = sel; Restyle(); }
                string nm = (truck ? "EW-" : "DC-") + (slot + 1), st, a = "", b = "";
                Color k = PhosDim, sk = PhosDim, bk = PhosDim;
                bool showBar = false, showAct = false, dashed = false, cross = false, flag = false;
                float fill = 0f;
                InfraState s = !has ? InfraState.Lost : truck ? unit.State : center.State;
                if (!has)
                {
                    k = OutInk;
                    st = truck ? "NOT DEPLOYED" : "NOT BUILT";
                    a = truck ? "DEPLOY TRUCK, THEN CLICK WITHIN 20 KM OF A DATA CENTER" : "BUILD DATA CENTER, THEN CLICK A HELD AIRBASE";
                    dashed = true;
                }
                else if (s == InfraState.Building)
                {
                    ushort row = truck ? unit.Row : center.Row;
                    bool building = FrontPanels.QueueRow(v, row, out FrontQueueView q) && q.Building;
                    int total = Mathf.RoundToInt(q.Cost), funded = building ? total : Mathf.Min(total, Mathf.RoundToInt(q.Cost * q.Progress)), chip = FrontPanels.ChipAp(q);
                    int left = truck ? unit.BuildLeft : center.BuildLeft;
                    nm += " · " + (truck ? unit.Grid : center.Place);
                    k = Amber; sk = building ? Amber : HopInk;
                    st = building ? "BUILDING " + FrontKit.Clock(left) : "FUNDING " + Mathf.RoundToInt(Mathf.Clamp01(truck ? unit.Progress : center.Progress) * 100f) + " %";
                    a = building ? (truck ? "ROLLS OUT IN " : "UP IN ") + FrontKit.Clock(left) + (truck ? "" : " · THEN IT RUNS A TRUCK") : funded + " / " + total + " FUNDS";
                    b = building ? "" : "FRONT FUNDS PAY IT · CHIP IN SPEEDS IT UP";
                    showBar = true; fill = truck ? unit.Progress : center.Progress; bk = building ? Amber : HopInk;
                    dashed = true;
                    showAct = chip > 0;
                    if (showAct)
                    {
                        OpsKit.Label(act, "CHIP IN · " + FrontRail.Pays(chip));
                        OpsKit.Enable(act, v.Allocation >= chip);
                        act.Latched = false;
                        act.Help = "Optional: speed it up with your allocation. Front funds pay for it anyway.";
                    }
                }
                else if (s == InfraState.Lost)
                {
                    nm += " · " + (truck ? unit.Grid : center.Place);
                    k = TraceRed; sk = TraceRed; cross = true;
                    st = truck ? "DOWN" : "LOST";
                    a = truck ? "LOST · DEPLOY TRUCK BUILDS ANOTHER" : "REBUILD IT AT THE SAME SPOT · FULL PRICE";
                    if (!truck)
                    {
                        showAct = true;
                        OpsKit.Label(act, "REBUILD");
                        OpsKit.Enable(act, room.dcWhy.Length == 0);
                        act.Latched = false;
                        act.Help = room.dcWhy.Length == 0 ? "Queues a new DATA CENTER at the same point, full price. The host checks the airbase is still yours." : room.dcWhy;
                    }
                }
                else if (truck && s == InfraState.Packing)
                {
                    nm += " · " + unit.Grid;
                    int left = room.PackLeft(slot, v.Now);
                    k = Amber; sk = Amber; dashed = true;
                    st = "PACKING · REDEPLOYS IN ~" + FrontKit.Clock(Mathf.Max(0, left));
                    a = "OFFLINE · NO REACH UNTIL IT REDEPLOYS";
                    MapFrame f = FrontController.Frame();
                    b = room.PackDest(slot, out Vector2 to) ? "→ GRID " + OpsKit.Grid(f.X(to.x), f.Z(to.y)) : "REDEPLOYS AT THE PICKED SPOT";
                }
                else if (truck)
                {
                    nm += " · " + unit.Grid;
                    bool damaged = s == InfraState.Damaged;
                    k = unit.LockLeft > 0 || damaged ? Amber : Phos;
                    st = unit.LockLeft > 0 ? "TRACED · LOCKED " + FrontKit.Clock(unit.LockLeft) : damaged ? "DAMAGED" : "UP";
                    sk = unit.LockLeft > 0 ? TraceRed : damaged ? Amber : Phos;
                    a = unit.InReachWord; b = unit.TraceWord;
                    showBar = true; fill = unit.Trace; bk = unit.Trace >= 0.6f ? TraceRed : unit.Trace >= 0.25f ? Amber : Phos;
                    showAct = true;
                    bool armed = room.pick == PickMode.Relocate && room.moveTruck == slot, ring = room.Rings(v, PickMode.Relocate).Count > 0;
                    OpsKit.Label(act, "RELOCATE");
                    act.Latched = armed;
                    OpsKit.Enable(act, armed || ring);
                    act.Help = ring ? "Pack it up and pick a new spot on the map within 20 km of a live data center: offline for 45 s, then it redeploys there. No money. Press again or Esc to cancel."
                        : "Needs a live data center: a truck redeploys within 20 km of one.";
                }
                else
                {
                    nm += " · " + center.Place;
                    bool damaged = s == InfraState.Damaged;
                    k = damaged ? Amber : Phos; sk = center.Breached ? TraceRed : k;
                    st = (damaged ? "DAMAGED" : "UP") + (center.Breached ? " · BREACHED" : ""); // the map flags it; the card says it
                    a = center.Runs + " · " + center.Effect;
                    flag = center.Breached;
                    b = center.Breached ? "BREACHED · AN ENEMY HOLDS IT · PURGE IT ON NODES" : "";
                }
                rail.color = k;
                FrontKit.Set(name, FrontKit.Fit(name, nm, 11.5f), has ? Bright : PhosDim);
                FrontKit.Set(state, FrontKit.Fit(state, st, 10.5f), sk);
                FrontKit.Set(lineA, FrontKit.Fit(lineA, a, 10.5f), has && s != InfraState.Lost ? Bright : PhosDim);
                FrontKit.Set(lineB, FrontKit.Fit(lineB, b, 10.5f), flag ? TraceRed : PhosDim);
                bar.Show(showBar);
                if (showBar) bar.Set(fill, bk);
                if (act.Rect.gameObject.activeSelf != showAct) act.Rect.gameObject.SetActive(showAct);
                // The card's own node: the hex and glyph the map draws, dashed while it is only coming (or packing), crossed once lost.
                glyph.Clear();
                Vector2 c0 = new Vector2(17f, 11f);
                glyph.Disc(c0, 9f, Glass, 6);
                if (dashed) DashedHex(glyph, c0, 9f, k); else Hex(glyph, c0, 9f, k, 1.4f);
                if (truck) glyph.Disc(c0, 2.6f, k, 8); else Glyph(glyph, NodeKind.DataCenter, c0, 5f, k, 1.1f);
                if (cross) FrontKit.Cross(glyph, c0, 6f, TraceRed);
                glyph.Flush();
            }
        }
    }
}

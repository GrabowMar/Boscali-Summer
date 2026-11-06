using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Modules.Support.Presentation.Fronts.FrontPanels;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The ORBIT room's SATELLITES tab (spec 5.2), top to bottom: the front's funds; the launch pads (BUILD PAD arms the map pick, a building pad takes CHIP IN, a
    /// lost one REBUILD at the same point); the fleet by kind (state, what it covers, the perk it enables, fuel, RELOCATE with the cost rings, FEED › to its
    /// DOWNLINK, or LAUNCH with the reason it cannot); the hostile birds the OPS mirror carries and ASAT; the standing orders the directors follow when nobody
    /// drives (the posture, moved here from the FRONT rail). Every press is a request to <see cref="IFrontActions"/>; the host judges.
    /// </summary>
    internal sealed partial class OrbitRoom
    {
        private const float SecX = 12f, CardX = 20f, PadY = 50f, PadH = 152f, PadCardH = 46f, FleetY = 208f, FleetH = 376f, FleetCardH = 110f, FleetStep = 116f,
            HostY = 590f, HostH = 110f, OrderY = 706f, OrderH = 104f;
        private static readonly FrontDirective[] Orders = { FrontDirective.Recon, FrontDirective.Strike, FrontDirective.Defend };

        private FrontBox sats;
        private FrontVector colG;
        private TMP_Text funds, alloc, padHead, padHint, fleetHead, fleetNote, hostHead, asatWhy, orderHead, orderLock, orderLine;
        private readonly TMP_Text[] hostLines = new TMP_Text[FrontRules.BirdCap];
        private AvControl buildPad, asat;
        private readonly AvControl[] orders = new AvControl[3];
        private readonly PadCard[] padCards = new PadCard[FrontRules.PadCap];
        private readonly FleetCard[] fleetCards = new FleetCard[FrontRules.BirdCap];
        private string padWhy = "";

        private void BuildSatellites(RectTransform column, float cw, float ch)
        {
            sats = new FrontBox(column, skin, "Satellites", 0f, 0f, cw, ch, "SATELLITES · FLEET", AvIcon.Satellite, FrontLook.Console).Tagged("FDO");
            RectTransform b = sats.Rect;
            float secW = cw - 2f * SecX, cardW = cw - 2f * CardX;
            colG = FrontVector.Add(b, "Sections");
            AvLay.Place(colG.rectTransform, 0f, 0f, cw, ch);
            skin.Add(() =>
            {
                colG.Clear();
                Slab(colG, SecX, PadY, secW, PadH, Own);
                Slab(colG, SecX, FleetY, secW, FleetH, Own);
                Slab(colG, SecX, HostY, secW, HostH, AvInk.Hostile);
                Slab(colG, SecX, OrderY, secW, OrderH, RoomInk.Telemetry);
                colG.Flush();
            });
            funds = FrontKit.Mono(b, "Funds", SecX + 2f, 26f, secW * 0.64f, 20f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            alloc = FrontKit.Mono(b, "Alloc", SecX + secW * 0.64f, 26f, secW * 0.36f - 2f, 20f, 10.5f, TextAlignmentOptions.MidlineRight);

            padHead = Head(b, "PadHead", PadY, secW);
            buildPad = Small(b, "BUILD PAD", SecX + secW - 8f - 140f, PadY + 4f, 140f, ArmPadPick,
                "Arms a pick on the map: click within 8 km of an airbase you hold (they glow). Press again or Esc to cancel. Front funds pay, then it builds (2:00).");
            buildPad.SetStyle(AvButtonStyle.Primary);
            for (int i = 0; i < padCards.Length; i++) padCards[i] = new PadCard(this, b, CardX, PadY + 32f + i * (PadCardH + 4f), cardW, i);
            padHint = FrontKit.Mono(b, "PadHint", CardX + 2f, PadY + PadH - 20f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);

            fleetHead = Head(b, "FleetHead", FleetY, secW);
            fleetNote = FrontKit.Mono(b, "FleetNote", SecX + secW * 0.5f, FleetY + 5f, secW * 0.5f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            for (int k = 0; k < fleetCards.Length; k++) fleetCards[k] = new FleetCard(this, b, CardX, FleetY + 30f + k * FleetStep, cardW, k);

            hostHead = Head(b, "HostHead", HostY, secW);
            asat = Small(b, "ASAT", SecX + secW - 8f - 140f, HostY + 4f, 140f, () => { actions.Touch(); actions.Queue(Front.Space, ProgrammeId.Asat, 0); },
                "Queues an ASAT without a target: when it fires it hits the enemy satellite over most of your ground. Front funds pay, then it builds (3:00).");
            for (int i = 0; i < hostLines.Length; i++) hostLines[i] = FrontKit.Mono(b, "Hostile" + i, CardX + 2f, HostY + 32f + i * 18f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            asatWhy = FrontKit.Mono(b, "AsatWhy", CardX + 2f, HostY + HostH - 22f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);

            orderHead = Head(b, "OrderHead", OrderY, secW);
            orderHead.text = "STANDING ORDERS";
            orderLock = FrontKit.Mono(b, "OrderLock", SecX + secW * 0.45f, OrderY + 5f, secW * 0.55f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            float ow = (cardW - 12f) / 3f;
            string locks = ((int)FrontRules.LockSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < orders.Length; i++)
            {
                FrontDirective d = Orders[i];
                orders[i] = Small(b, FrontRules.Name(d), CardX + i * (ow + 6f), OrderY + 30f, ow, () => { actions.Touch(); actions.SetDirective(Front.Space, d); },
                    FrontRules.Name(d) + ": " + FrontRail.Describe(Front.Space, d) + " The directors follow it when nobody is driving. Free for any pilot on your side; a " + locks + " s lock follows.");
            }
            orderLine = FrontKit.Mono(b, "OrderLine", CardX + 2f, OrderY + 60f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            TMP_Text orderNote = FrontKit.Mono(b, "OrderNote", CardX + 2f, OrderY + 78f, cardW - 4f, 16f, 10f, TextAlignmentOptions.MidlineLeft);
            orderNote.text = "WHEN NOBODY IS DRIVING · ANY PILOT ON YOUR SIDE MAY CHANGE IT";
            skin.Add(() => { alloc.color = Mc.Steel; fleetNote.color = Mc.Steel; orderNote.color = Mc.Steel; orderHead.color = RoomInk.Telemetry; });
        }

        /// <summary>A section heading on its slab.</summary>
        private static TMP_Text Head(RectTransform p, string name, float y, float secW) =>
            FrontKit.Mono(p, name, SecX + 10f, y + 5f, secW * 0.5f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);

        private void Donate(ushort row) => FrontPanels.Donate(actions, Front.Space, last, row);

        /// <summary>A fleet card's buttons, judged on the press: an up bird RELOCATE / FEED › (CONFIRM BURN / CANCEL while it relocates), a bird not up LAUNCH, a queued one CHIP IN.</summary>
        private void CardPress(int kind, int button)
        {
            actions.Touch();
            if (last == null || kind >= last.Fleet.Count) return;
            FleetCardView c = last.Fleet[kind];
            if (button == 2) { if (LaunchRow(last, kind, out FrontQueueView q)) Donate(q.Row); return; }
            if (!c.Up) { if (button == 0) actions.Queue(Front.Space, ProgrammeId.LaunchSatellite, (byte)kind); return; }
            bool burning = relocating && selKind == kind;
            if (button == 0) { if (burning) Confirm(); else StartRelocate(kind); }
            else if (burning) Cancel();
            else ShowFeed(kind);
        }

        private void PaintSatellites(FrontRoomView v)
        {
            FrontRow r = v.Row;
            bool wait = FrontViews.IsWaiting(r);
            FrontKit.Set(funds, wait ? "FRONT FUNDS — · WAITING FOR THE HOST" : "FRONT FUNDS " + r.Budget + " · +" + v.FundsRate + " / MIN · CAP " + (int)FrontRules.BudgetCap,
                wait ? Mc.Steel : RoomInk.Engrave);
            FrontKit.Set(alloc, "YOUR ALLOCATION " + v.Allocation + " AP", Mc.Steel);
            sats.SetMeta("PADS " + v.PadsUp + "/" + FrontRules.PadCap + " · BIRDS " + v.BirdsUp + "/" + FrontRules.BirdCap, AvInk.Dim);

            // Launch pads. BUILD PAD needs a pad slot (the offer's refusal) and a held airbase to click.
            FrontOfferView pad = OfferOf(v, ProgrammeId.UplinkSite);
            bool held = false;
            foreach (MapAirbaseView a in v.Airbases) held |= a.Side == AirbaseSide.Own;
            padWhy = !string.IsNullOrEmpty(pad.Refusal) ? pad.Refusal : held ? "" : FrontWords.Refusal(FrontOutcome.NoSite, Front.Space);
            FrontKit.Set(padHead, "LAUNCH PADS " + v.PadsUp + " / " + FrontRules.PadCap, Own);
            for (int i = 0; i < padCards.Length; i++) padCards[i].Paint(v);
            buildPad.Latched = padPick;
            OpsKit.Enable(buildPad, padPick || padWhy.Length == 0);
            string hint = padPick ? (padMiss ? FrontWords.Refusal(FrontOutcome.NotHeld, Front.Space) + " · CLICK INSIDE A RING ON THE MAP · ESC CANCELS" : "CLICK WITHIN 8 KM OF A HELD AIRBASE (RINGED ON THE MAP) · ESC CANCELS")
                : padWhy.Length > 0 ? "BUILD PAD · " + padWhy : "A PAD: " + Mathf.RoundToInt(pad.Cost) + " FUNDS · BUILDS " + FrontKit.Clock(pad.BuildSeconds) + " · HOLDS 2 SATELLITES";
            FrontKit.Set(padHint, FrontKit.Fit(padHint, hint, 10.5f), padPick && padMiss ? FrontKit.Tone(AvState.Danger) : padPick || padWhy.Length > 0 ? FrontKit.Tone(AvState.Caution) : Mc.Steel);

            // The fleet, one card per kind.
            FrontKit.Set(fleetHead, "FLEET " + v.BirdsUp + " / " + FrontRules.BirdCap, Own);
            FrontKit.Set(fleetNote, "ROOM FOR " + v.BirdCap + " · 2 PER LIVE PAD", Mc.Steel);
            for (int k = 0; k < fleetCards.Length; k++) fleetCards[k].Paint(v, k < v.Fleet.Count ? v.Fleet[k] : new FleetCardView { Kind = k, Callsign = FrontViews.BirdCallsign(k), State = "", Perk = "", Covers = "" });

            // Hostile birds (only what the OPS mirror carries: up and known) and ASAT.
            FrontKit.Set(hostHead, "HOSTILE " + v.Hostile.Count + " / " + FrontRules.BirdCap + " UP", v.Hostile.Count > 0 ? AvInk.Hostile : Mc.Steel);
            for (int i = 0; i < hostLines.Length; i++)
            {
                string line = "";
                Color ink = Mc.Steel;
                if (i < v.Hostile.Count)
                {
                    HostileBirdView h = v.Hostile[i];
                    line = h.Word + " SATELLITE · " + (h.OverOwn ? "OVER " + h.Nearest : "NOT OVER YOUR GROUND");
                    ink = h.OverOwn ? FrontKit.Tone(AvState.Danger) : Mc.Steel;
                }
                else if (i == 0) line = "NONE TRACKED · ENEMY SATELLITES SHOW HERE ONCE THE OPS LINK SEES THEM";
                FrontKit.Set(hostLines[i], FrontKit.Fit(hostLines[i], line, 10.5f), ink);
            }
            bool asatOk = string.IsNullOrEmpty(v.Asat.Refusal);
            OpsKit.Label(asat, "ASAT " + Mathf.RoundToInt(v.Asat.Cost));
            OpsKit.Enable(asat, asatOk);
            string why = asatOk ? "NO TARGET NEEDED · IT HITS THE ENEMY SATELLITE OVER MOST OF YOUR GROUND · BUILDS " + FrontKit.Clock(v.Asat.BuildSeconds) : "ASAT · " + v.Asat.Refusal;
            FrontKit.Set(asatWhy, FrontKit.Fit(asatWhy, why, 10.5f), asatOk ? Mc.Steel : FrontKit.Tone(AvState.Caution));

            // Standing orders: a lock held by someone else disables them here (the host still judges).
            float left = r.DirectiveLockUntil - v.Now;
            bool locked = !wait && left > 0f, blocked = locked && !FrontRail.SameName(r.DirectiveBy, v.Callsign);
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].Latched = !wait && r.Directive == Orders[i];
                OpsKit.Enable(orders[i], !wait && !blocked);
            }
            FrontKit.Set(orderLock, wait ? "WAITING FOR HOST" : locked ? FrontRail.LockWord(!blocked, r.DirectiveBy, left) : FrontRules.Name(r.Directive) + " ACTIVE",
                locked ? FrontKit.Tone(AvState.Caution) : Mc.Steel);
            FrontKit.Set(orderLine, FrontKit.Fit(orderLine, wait ? "WAITING FOR THE HOST" : "> " + FrontRail.Describe(Front.Space, r.Directive).ToUpperInvariant(), 10.5f), RoomInk.Engrave);
        }

        // ---- Parts ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// One pad slot: UP with the birds it links, FUNDING / BUILDING with its bar and CHIP IN, LOST with REBUILD at the same point, or NOT BUILT. A click on a
        /// standing or queued pad with a point centres the map on it.
        /// </summary>
        private sealed class PadCard
        {
            private readonly OrbitRoom room;
            private readonly int slot;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text glyph, name, state, line;
            private readonly FrontBar bar;
            private readonly AvControl act;
            private PadView cur;
            private bool has;

            public PadCard(OrbitRoom room, RectTransform p, float x, float y, float w, int slot)
            {
                this.room = room; this.slot = slot;
                RectTransform root = AvLay.Child(p, "PadCard" + slot);
                AvLay.Place(root, x, y, w, PadCardH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, PadCardH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, PadCardH, Color.clear);
                glyph = AvIcons.Make(root, AvIcon.Antenna, 13f, AvInk.Dim);
                AvLay.Place(glyph.rectTransform, 9f, 3f, 13f, 13f);
                name = FrontKit.Mono(root, "Name", 26f, 1f, w - 26f - 210f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", w - 8f - 200f, 1f, 200f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                line = FrontKit.Mono(root, "Line", 10f, 20f, w - 20f - 240f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
                bar = new FrontBar(root, room.skin, 10f, 37f, w - 20f - 240f, 6f);
                act = room.Small(root, "REBUILD", w - 8f - 230f, 19f, 230f, Press, "");
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left && has && cur.U >= 0f) room.ShowSite(new Vector2(cur.U, cur.V)); };
                room.skin.Add(() => frame.Paint(RoomInk.ConsoleBody, RoomInk.Bezel.WithAlpha(0.8f)));
            }

            private void Press()
            {
                if (!has || room.last == null) return;
                room.actions.Touch();
                if (cur.Lost) room.actions.Queue(Front.Space, ProgrammeId.UplinkSite, 0, true, cur.U, cur.V); // REBUILD: the same programme at the same point
                else if (cur.Building) room.Donate(cur.Row);
            }

            public void Paint(FrontRoomView v)
            {
                has = false;
                foreach (PadView pv in v.Pads) if (pv.Slot == slot) { cur = pv; has = true; break; }
                string nm = FrontViews.SiteName(slot), st, ln;
                Color k, sk;
                bool showBar = false, showAct = false;
                if (!has)
                {
                    k = AvInk.Muted; sk = Mc.Steel;
                    st = "NOT BUILT"; ln = "BUILD PAD, THEN CLICK A HELD AIRBASE";
                }
                else if (cur.Up)
                {
                    nm = cur.Name + " · " + cur.Place;
                    k = Own; sk = FrontKit.Tone(AvState.Ready);
                    st = "UP"; ln = cur.Links;
                }
                else if (cur.Lost)
                {
                    nm = cur.Name + " · " + cur.Place;
                    k = AvInk.State(AvState.Danger); sk = FrontKit.Tone(AvState.Danger);
                    st = "LOST"; ln = "REBUILD IT AT THE SAME SPOT";
                    showAct = true;
                    OpsKit.Label(act, "REBUILD");
                    OpsKit.Enable(act, room.padWhy.Length == 0);
                    act.Help = room.padWhy.Length == 0 ? "Queues a new LAUNCH PAD at the same point, full price. The host checks the airbase is still yours." : room.padWhy;
                }
                else
                {
                    nm = cur.Name + " · " + cur.Place;
                    bool building = QueueRow(v, cur.Row, out FrontQueueView q) && q.Building;
                    int total = Mathf.RoundToInt(q.Cost), funded = building ? total : Mathf.Min(total, Mathf.RoundToInt(q.Cost * q.Progress)), chip = ChipAp(q);
                    k = AvInk.State(AvState.Caution); sk = FrontKit.Tone(building ? AvState.Caution : AvState.Info);
                    st = building ? "BUILDING " + FrontKit.Clock(cur.BuildLeft) : "FUNDING " + Mathf.RoundToInt(Mathf.Clamp01(cur.Progress) * 100f) + " %";
                    ln = building ? "UP IN " + FrontKit.Clock(cur.BuildLeft) : funded + " / " + total + " FUNDS";
                    showBar = true;
                    bar.Set(cur.Progress, building ? AvInk.State(AvState.Caution) : AvInk.State(AvState.Info));
                    showAct = chip > 0;
                    if (showAct)
                    {
                        OpsKit.Label(act, "CHIP IN · " + FrontRail.Pays(chip));
                        OpsKit.Enable(act, v.Allocation >= chip);
                        act.Help = "Optional: speed it up with your allocation. Front funds pay for it anyway.";
                    }
                }
                rail.color = k; glyph.color = k;
                FrontKit.Set(name, FrontKit.Fit(name, nm, 11.5f), has ? RoomInk.Engrave : Mc.Steel);
                FrontKit.Set(state, st, sk);
                FrontKit.Set(line, FrontKit.Fit(line, ln, 10.5f), Mc.Steel);
                bar.Show(showBar);
                if (act.Rect.gameObject.activeSelf != showAct) act.Rect.gameObject.SetActive(showAct);
            }
        }

        /// <summary>
        /// One bird kind: callsign and state, what its footprint covers, the perk it enables, the tank, and its buttons. Up: RELOCATE and FEED › (CONFIRM BURN and
        /// CANCEL while it relocates, with the proposed burn on the tank). Not up: LAUNCH (or RELAUNCH) with the reason it cannot, or CHIP IN while its launch is queued.
        /// A click on the card selects an up bird (its cost rings on the map).
        /// </summary>
        private sealed class FleetCard
        {
            private const float BtnY = 80f;
            private readonly OrbitRoom room;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text glyph, name, state, cover, perk, fuelKey, fuelVal, hint;
            private readonly FrontVector gauge;
            private readonly AvControl a, b, chip;
            private readonly float w;
            private readonly Rect tank;
            private bool lit;

            public FleetCard(OrbitRoom room, RectTransform p, float x, float y, float width, int kind)
            {
                this.room = room; w = width;
                RectTransform root = AvLay.Child(p, "FleetCard" + kind);
                AvLay.Place(root, x, y, w, FleetCardH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, FleetCardH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, FleetCardH, Color.clear);
                gauge = FrontVector.Add(root, "Gauge");
                AvLay.Place(gauge.rectTransform, 0f, 0f, w, FleetCardH);
                glyph = AvIcons.Make(root, AvIcon.Satellite, 14f, AvInk.Dim);
                AvLay.Place(glyph.rectTransform, 9f, 3f, 14f, 14f);
                name = FrontKit.Mono(root, "Name", 28f, 1f, w - 28f - 220f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", w - 8f - 210f, 1f, 210f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                cover = FrontKit.Mono(root, "Cover", 10f, 21f, w - 20f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
                perk = FrontKit.Mono(root, "Perk", 10f, 38f, w - 20f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
                fuelKey = FrontKit.Mono(root, "FuelKey", 10f, 56f, 38f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
                fuelKey.text = "FUEL";
                fuelVal = FrontKit.Mono(root, "FuelVal", w - 90f, 56f, 80f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                tank = new Rect(50f, 60f, w - 150f, 9f);
                a = room.Small(root, "RELOCATE", 8f, BtnY, 132f, () => room.CardPress(kind, 0), "");
                b = room.Small(root, "FEED ›", 146f, BtnY, 110f, () => room.CardPress(kind, 1), "");
                chip = room.Small(root, "CHIP IN", 8f, BtnY, 248f, () => room.CardPress(kind, 2), "Optional: speed the launch up with your allocation. Front funds pay for it anyway.");
                hint = FrontKit.Mono(root, "Hint", 264f, BtnY, w - 272f, 24f, 10.5f, TextAlignmentOptions.MidlineLeft);
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) room.Select(kind); };
                room.skin.Add(Restyle);
                room.skin.Add(() => fuelKey.color = Mc.Steel);
            }

            private void Restyle() =>
                frame.Paint(lit ? Color.Lerp(RoomInk.ConsoleBody, AvInk.Select, 0.14f).WithAlpha(0.95f) : RoomInk.ConsoleBody, lit ? AvInk.Select : RoomInk.Bezel.WithAlpha(0.8f));

            private static void Show(Component c, bool on) { if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

            public void Paint(FrontRoomView v, in FleetCardView c)
            {
                bool sel = c.Up && room.selKind == c.Kind, burning = sel && room.relocating;
                if (lit != sel) { lit = sel; Restyle(); }
                Color k = c.Up ? Own : c.Tone == AvState.Danger ? AvInk.State(AvState.Danger) : c.Tone == AvState.Inert ? AvInk.Muted : AvInk.State(AvState.Caution);
                rail.color = k; glyph.color = k;
                FrontKit.Set(name, "<b>" + c.Callsign + "</b> · " + C2Orbit.BirdName((BirdKind)c.Kind), RoomInk.Engrave);
                FrontKit.Set(state, c.State, FrontKit.Tone(c.Tone));
                FrontKit.Set(perk, FrontKit.Fit(perk, c.Perk, 10.5f), c.Perk.EndsWith(" READY", System.StringComparison.Ordinal) ? FrontKit.Tone(AvState.Ready) : Mc.Steel);
                string cov, tip = "";
                Color covInk = Mc.Steel, tipInk = Mc.Steel;
                bool twoButtons = false, chipOn = false;
                gauge.Clear();
                if (c.Up && room.TryBird(c.Kind, out OrbitTrackView bird))
                {
                    float fuel = bird.Fuel, after = fuel;
                    bool afford = true;
                    twoButtons = true;
                    if (!burning)
                    {
                        cov = c.Covers;
                        tip = bird.Relocating ? "BURN UNDER WAY · WAIT FOR IT TO PARK" : c.CanRelocate ? "FUEL SCALES WITH DISTANCE · SEE THE RINGS" : "NOT ENOUGH FUEL TO BURN";
                        OpsKit.Label(a, "RELOCATE"); OpsKit.Enable(a, c.CanRelocate);
                        OpsKit.Label(b, "FEED ›"); OpsKit.Enable(b, true);
                        a.Help = "Pick a new parked point for this satellite on the map. Nothing is spent until you CONFIRM BURN.";
                        b.Help = "Show this satellite's picture on the DOWNLINK tab.";
                    }
                    else
                    {
                        OpsKit.Label(a, "CONFIRM BURN"); OpsKit.Label(b, "CANCEL"); OpsKit.Enable(b, true);
                        a.Help = "Burn to the chosen point: the fuel is spent now and the bird slews there.";
                        b.Help = "Drop the proposal. Nothing is spent (Esc does the same).";
                        if (!room.hasPending)
                        {
                            cov = "CLICK THE MAP FOR THE NEW PARKED POINT";
                            tip = "THE MAP DRAGS AND ZOOMS FREELY · ESC CANCELS";
                            OpsKit.Enable(a, false);
                        }
                        else
                        {
                            float cost = room.PendingCost(bird), dist = room.PendingDistance(bird);
                            afford = bird.Fuel >= cost;
                            after = Mathf.Max(0f, fuel - cost);
                            cov = "→ SLOT " + FrontViews.SlotLon(v.Faction, true, room.pending.x, v.TheatreKm * 1000f) + " · TRANSFER " + FrontKit.Clock(dist / GeoBird.Slew);
                            tip = cost <= 0f ? "ALREADY PARKED THERE" : afford ? "BURN COST -" + Mathf.RoundToInt(cost) + " % FUEL · FOOTPRINT MOVES" : "NOT ENOUGH FUEL · NEED " + Mathf.CeilToInt(cost) + " %";
                            tipInk = afford && cost > 0f ? FrontKit.Tone(AvState.Caution) : FrontKit.Tone(AvState.Danger);
                            OpsKit.Enable(a, room.CanBurn(bird));
                        }
                        covInk = RoomInk.Engrave;
                    }
                    FrontKit.Set(fuelVal, burning && room.hasPending && afford ? Mathf.RoundToInt(fuel) + " → " + Mathf.RoundToInt(after) + " %" : Mathf.RoundToInt(fuel) + " %", RoomInk.Engrave);
                    // The tank, with a proposed burn laid over it (ten segments' worth of ticks, the half heavier).
                    Rect f = tank;
                    gauge.Rect(f.x, f.y, f.width, f.height, Mc.PlateInk.WithAlpha(0.95f));
                    if (!afford) gauge.Rect(f.x + 1f, f.y + 1f, (f.width - 2f) * Mathf.Clamp01(fuel / 100f), f.height - 2f, AvInk.State(AvState.Danger));
                    else
                    {
                        gauge.Rect(f.x + 1f, f.y + 1f, (f.width - 2f) * Mathf.Clamp01(after / 100f), f.height - 2f, Own);
                        if (after < fuel) gauge.Rect(f.x + 1f + (f.width - 2f) * Mathf.Clamp01(after / 100f), f.y + 1f, (f.width - 2f) * Mathf.Clamp01((fuel - after) / 100f), f.height - 2f, AvInk.State(AvState.Caution));
                    }
                    for (int i = 1; i < 10; i++) gauge.Rect(f.x + f.width * i / 10f, f.y, 1f, i % 5 == 0 ? f.height : f.height * 0.4f, Mc.PlateInk.WithAlpha(0.9f));
                    gauge.Frame(f.x, f.y, f.width, f.height, 1f, RoomInk.Bezel);
                }
                else if (LaunchRow(v, c.Kind, out FrontQueueView q))
                {
                    int total = Mathf.RoundToInt(q.Cost), funded = q.Building ? total : Mathf.Min(total, Mathf.RoundToInt(q.Cost * q.Progress)), ap = ChipAp(q);
                    cov = q.Building ? "LAUNCHING · ON STATION IN " + FrontKit.Clock(q.BuildLeft) : "LAUNCH QUEUED · " + funded + " / " + total + " FUNDS";
                    chipOn = ap > 0;
                    if (chipOn) { OpsKit.Label(chip, "CHIP IN · " + FrontRail.Pays(ap)); OpsKit.Enable(chip, v.Allocation >= ap); }
                    else tip = q.Building ? "" : "FULLY FUNDED · IT BUILDS WHEN ITS TURN COMES";
                }
                else
                {
                    bool lost = c.Tone == AvState.Danger;
                    cov = (lost ? "RELAUNCH" : "LAUNCH") + " · " + Mathf.RoundToInt(c.Launch.Cost) + " FUNDS · BUILDS " + FrontKit.Clock(c.Launch.BuildSeconds) + " · FROM A LIVE PAD";
                    bool ok = string.IsNullOrEmpty(c.Launch.Refusal);
                    OpsKit.Label(a, lost ? "RELAUNCH" : "LAUNCH");
                    OpsKit.Enable(a, ok);
                    a.Help = ok ? "Queues this satellite's launch. Front funds pay, then it builds and goes up from a live pad." : c.Launch.Refusal;
                    tip = c.Launch.Refusal ?? "";
                    tipInk = FrontKit.Tone(AvState.Caution);
                }
                gauge.Flush();
                FrontKit.Set(cover, FrontKit.Fit(cover, cov, 10.5f), covInk);
                Show(fuelKey, c.Up); Show(fuelVal, c.Up);
                bool aOn = !chipOn && (twoButtons || !c.Up && !LaunchRow(v, c.Kind, out _));
                Show(a.Rect, aOn); Show(b.Rect, twoButtons); Show(chip.Rect, chipOn);
                float hx = chipOn || twoButtons ? 264f : aOn ? 148f : 10f;
                AvLay.Place(hint.rectTransform, hx, BtnY, w - hx - 8f, 24f);
                FrontKit.Set(hint, FrontKit.Fit(hint, tip, 10.5f), tipInk);
            }
        }
    }
}

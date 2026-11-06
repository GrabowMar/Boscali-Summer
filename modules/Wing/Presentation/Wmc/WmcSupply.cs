using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>SUPPLY (spec 2026-10-04 §SUPPLY × LOADOUT, "One Sheet"): one sheet that does not scroll on a tall dock. A DISPATCH card
    /// first (state tag, what REQUISITION would send, funds after the call, REQUISITION and the blocker's reason in words), INBOUND and
    /// ADOPT strips only when they have something to say, then
    /// ① AIRFRAME (a grid of every listed airframe) · ② PILOT &amp; CREW · ③ FIT &amp; FUEL (labelled rows) · ④ LAUNCH BASE (2026-11-05 remake). REQUISITION sends the
    /// card as a Call order (airframe, field, pilot, fit, fuel); the executor quotes again on the same rules and answers in the same
    /// words, and a bare call (the hotkey, TACTICAL CALL) flies the same draft. The airframe picked here is LOADOUT's too. A client looks;
    /// the host requisitions: everything stays visible, disabled with the one reason.</summary>
    internal sealed partial class WmcSupply : IWmcPage
    {
        /// <summary>A client sees every control disabled with this one reason.</summary>
        private static readonly string ClientWhy = ShopRules.WingReason(WingBlock.Client, default);

        private AvFlow flow;
        private bool relayout;

        // This refresh's snapshot: Shown takes a forced one, Refresh reuses it.
        private WmcContext last;
        private bool snapFresh;
        private Aircraft caller;
        private FactionHQ hq;
        private bool client;
        private ShopWing wing;
        private ShopAirframe air;
        private ShopQuote quote;
        private AircraftDefinition selected;
        private Airbase field;

        private string hint;
        private int hintKey = int.MinValue;

        public string Hint => hint;

        public string Alert => null;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            BuildPin(flow);
            BuildInbound();
            BuildAdopt();
            BuildAirframe(flow);
            BuildPilot(flow);
            BuildFit(flow);
            BuildBase(flow);
        }

        /// <summary>A step header's caption: the state word, with the glyph a caution or danger carries.</summary>
        private static string StepCaption(string text, string cls) => AvStates.Glyph(WmcState.Of(cls)) + text;

        /// <summary>The wing, the selected airframe's quote and its launch field, once a panel refresh: Shown takes a forced one and
        /// Refresh reuses it, so a pick, a toggle or an order made since the last refresh is always seen (review R4b: a per-frame
        /// reuse hid same-frame changes). The lists refill at most once a second, or at once when the page is shown; nothing picked yet
        /// takes the quick call's own airframe.</summary>
        private void Snapshot(WmcContext c, bool force)
        {
            last = c;
            snapFresh = force;
            client = c.Client;
            WingService w = c.Wing;
            // The player requisitions; without a player aircraft (automation) the anchor stands in, as the order does.
            caller = WingRequisition.CallerOf(w, client);
            hq = WingRequisition.HqOf(caller);
            WingRequisition.RefreshLists(caller, force);
            WingRequisition.SelectDefault(caller);
            wing = WingRequisition.Wing(caller);
            if (client) wing.Host = false;
            selected = WingRequisition.Selected;
            air = selected != null ? WingRequisition.For(selected, hq) : default;
            quote = selected != null ? ShopRules.Quote(wing, air) : default;
            field = selected != null && !client ? WingRequisition.PickField(caller, selected) : null;
        }

        public void Refresh(WmcContext c)
        {
            // One snapshot a refresh (Shown takes a forced one first when the page comes on screen).
            if (!ReferenceEquals(last, c) || !snapFresh) Snapshot(c, false);
            snapFresh = false;
            RefreshInbound();
            RefreshAdopt(c);
            RefreshAirframe();
            RefreshPilot();
            RefreshFit();
            RefreshBase();
            RefreshPin();
            if (relayout)
            {
                relayout = false;
                flow.RequestRelayout();
            }
            int k = (client ? 1 : 0) + inboundCount * 2;
            if (k == hintKey) return;
            hintKey = k;
            hint = SupplyWords.Hint(client, inboundCount);
        }

        /// <summary>SUPPLY came on screen: the lists refill now (the snapshot is forced).</summary>
        public void Shown(WmcContext c)
        {
            Snapshot(c, true);
            pinSet = false;
        }

        // ---------------------------------------------------------------- the DISPATCH card

        private WmcDispatchCard dispatch;
        private AvControl requisition;
        private int pinKey = int.MinValue;
        private bool pinSet;
        private AircraftDefinition pinSelected;
        private WingPilot pinPilot;
        private Airbase pinField;
        private string pinFit;
        private const float RequisitionQuiet = 0.6f;
        private float requisitionQuiet;

        private void BuildPin(AvFlow f)
        {
            dispatch = f.Add(new WmcDispatchCard(f.Content, SupplyWords.Requisition(0f), () => { WmcMotion.Punch(requisition); Requisition(); }));
            requisition = dispatch.Button;
        }

        /// <summary>The card says what REQUISITION would send and, under it, why it cannot or what being over the faction's AI
        /// limit does — always in words, always in view (the footer does not repeat it).</summary>
        private void RefreshPin()
        {
            WingPilot pilot = client ? null : WingPilotRoster.Upcoming;
            string fit = WingRequisition.FitOf(selected);
            int key;
            unchecked
            {
                key = (int)quote.Wing * 3 + (int)quote.Tile * 29 + (int)(quote.Price * 10f) * 7 + (quote.OverLimit ? 1013 : 0)
                    + WingRequisition.FuelPercent * 131 + inboundCount * 17 + wing.FactionAi * 37 + (int)(wing.FactionAiLimit * 10f) * 41
                    + wing.Members * 43 + wing.Pending * 47 + (int)wing.Mode * 53 + (client ? 59 : 0) + wing.PlayerRank * 61
                    + (wing.Sandbox ? 67 : 0) + (int)ShopRules.WingBlocker(wing) * 71 + Mathf.RoundToInt(wing.Funds * 10f) * 73 + air.FactionStock * 79;
            }
            if (pinSet && key == pinKey && ReferenceEquals(selected, pinSelected) && ReferenceEquals(pilot, pinPilot)
                && ReferenceEquals(field, pinField) && ReferenceEquals(fit, pinFit)) return;
            pinSet = true;
            pinKey = key;
            pinSelected = selected;
            pinPilot = pilot;
            pinField = field;
            pinFit = fit;

            string cls = "info", word = client ? "CLIENT" : ShopRules.State(quote, selected != null, inboundCount, out cls);
            string blocker;
            if (selected == null)
            {
                WingBlock b = client ? WingBlock.Client : ShopRules.WingBlocker(wing);
                blocker = b != WingBlock.None ? "BLOCKED · " + ShopRules.WingReason(b, wing) : null;
                dispatch.Set(word, WmcState.Of(cls), "NO AIRFRAME", null, "Tap an airframe in step 1",
                    blocker ?? "Pick a pilot, an airframe, its fit and a base", blocker != null ? "warn" : "");
                dispatch.SetMeta(client ? "THE HOST REQUISITIONS" : "");
            }
            else
            {
                string line = ShopRules.DispatchLine(pilot != null ? WmcText.Cut(pilot.Callsign, PilotPick.CallsignChars) : null, SupplyWords.Fit(fit),
                    WingRequisition.FuelPercent, field != null ? BaseName.Short(WingRequisition.NameOf(field)) : null);
                blocker = ShopRules.Blocker(quote, wing, air);
                // A known take-off limit at the chosen fuel, not a block: the requisition still goes (the user's ruling 2026-10-19).
                string runway = field != null
                    ? SupplyWords.ShortRunway(selected.unitName, SupplyWords.Code(selected.code, selected.unitName), WingRequisition.RunwayOf(field),
                        WingRequisition.FuelPercent)
                    : null;
                string level = blocker != null || runway != null ? "warn" : quote.OverLimit ? "info" : "ok";
                dispatch.Set(word, WmcState.Of(cls), selected.unitName + " · " + Credits.Price(quote.Price), IconFactory.Aircraft(selected), line,
                    blocker ?? runway ?? (quote.OverLimit ? ShopRules.OverLimitNote(wing) : null), level);
                dispatch.SetMeta(client ? "THE HOST REQUISITIONS" : SupplyWords.FundsAfter(wing.Sandbox, quote.Price, wing.Funds, air.FactionStock));
            }

            bool go = !client && selected != null && quote.Allowed;
            requisition.Label = selected != null ? SupplyWords.Requisition(quote.Price) : "REQUISITION";
            requisition.Interactable = go;
            requisition.Help = client ? ClientWhy : selected == null ? "Pick an airframe first."
                : blocker ?? "Send the requisition: it is checked again as it goes, and answered in the same words.";
            relayout = true;
        }

        /// <summary>What the card shows is what the order carries (review focus 1): its airframe, field, pilot, fit and fuel. A
        /// double click sends one requisition (the second press inside <see cref="RequisitionQuiet"/> is ignored); a deliberate
        /// second press sends the next pilot the card shows.</summary>
        private void Requisition()
        {
            if (last == null || Time.unscaledTime < requisitionQuiet) return;
            WmcUi.Order(last, () =>
            {
                AircraftDefinition d = WingRequisition.Selected;
                if (d == null) return;
                if (!ReferenceEquals(pinSelected, d)) WmcPanel.Instance?.Refresh();
                OrderResult sent = WingOrders.Run(new WingOrder
                {
                    Kind = OrderKind.Call, Number = 1,
                    Call = new CallSpec
                    {
                        Airframe = d.jsonKey, Field = pinField != null ? WingRequisition.KeyOf(pinField) : null,
                        Pilot = pinPilot != null ? pinPilot.Callsign : null, Fit = pinFit, Fuel = WingRequisition.FuelPercent / 100f,
                    },
                });
                if (sent.Accepted) requisitionQuiet = Time.unscaledTime + RequisitionQuiet;
                WmcPanel.Instance?.Refresh();
            });
        }
    }
}

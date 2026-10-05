using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>SUPPLY (spec 2026-10-04 §SUPPLY × LOADOUT, "One Sheet"): one sheet that does not scroll on a tall dock. A DISPATCH card
    /// first (state tag, what REQUISITION would send, next-call price, funds after, crew, REQUISITION and the blocker's reason in
    /// words — it also rides the footer's alert line), INBOUND and ADOPT strips only when they have something to say, then
    /// ① PILOT &amp; CREW · ② AIRFRAME (compact rows, with the HANGAR strip) · ③ FIT &amp; FUEL · ④ LAUNCH BASE. REQUISITION sends the
    /// card as a Call order (airframe, field, pilot, fit, fuel); the executor quotes again on the same rules and answers in the same
    /// words. A client looks; the host requisitions: everything stays visible, disabled with the one reason.</summary>
    internal sealed partial class WmcSupply : IWmcPage
    {
        /// <summary>A client sees every control disabled with this one reason.</summary>
        private static readonly string ClientWhy = ShopRules.WingReason(WingBlock.Client, default);

        private readonly WmcControls ids;
        private AvFlow flow;
        private AvTicker ticker;
        private int pageIndex;
        private bool relayout;

        // This refresh's snapshot: Metrics takes it first, Refresh reuses it.
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

        private string alert, hint;
        private int hintKey = int.MinValue;

        public WmcSupply(WmcControls controls) => ids = controls;

        public string Hint => hint;

        public string Alert => alert;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            ticker = pageTicker;
            pageIndex = index;
            BuildPin(flow);
            BuildInbound();
            BuildAdopt();
            BuildPilot(flow);
            BuildAirframe(flow);
            BuildFit(flow);
            BuildBase(flow);
        }

        /// <summary>A step header's caption: the state word, with the glyph a caution or danger carries.</summary>
        private static string StepCaption(string text, string cls) => AvStates.Glyph(WmcState.Of(cls)) + text;

        /// <summary>The wing, the selected airframe's quote and its launch field, once a panel refresh: Metrics takes it and
        /// Refresh reuses it, so a pick, a toggle or an order made since the last refresh is always seen (review R4b: a per-frame
        /// reuse hid same-frame changes). The lists refill at most once a second, or at once when the page is shown.</summary>
        private void Snapshot(WmcContext c, bool force)
        {
            last = c;
            snapFresh = force;
            client = c.Client;
            WingService w = c.Wing;
            // The player requisitions; without a player aircraft (automation) the anchor stands in, as the order does.
            caller = w == null || client ? null : w.Player != null ? w.Player : w.Leader;
            if (caller == null)
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null && hud.aircraft != null) caller = hud.aircraft;
            }
            hq = caller != null ? caller.NetworkHQ : null;
            WingRequisition.RefreshLists(caller, force);
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
            RefreshPilot();
            RefreshAirframe();
            RefreshHangar();
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

        /// <summary>SUPPLY came on screen: the lists refill now (the snapshot is forced), and a fit deleted on LOADOUT goes back to
        /// AUTO.</summary>
        public void Shown(WmcContext c)
        {
            Snapshot(c, true);
            OnShown();
            pinSet = false;
        }

        /// <summary>SUPPLY came into view: the lists refill now, and a fit deleted on LOADOUT meanwhile goes back to AUTO.</summary>
        private void OnShown()
        {
            WingRequisition.PurgeDeadFits();
            fitSet = false;
        }

        // ---------------------------------------------------------------- the DISPATCH card

        private WmcDispatchCard dispatch;
        private string stateWord;
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
            ids.Add("sup.requisition", requisition);
        }

        /// <summary>The card says what REQUISITION would send and, under it, why it cannot or what being over the faction's AI
        /// limit does — always in words, always in view.</summary>
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
                    + (wing.Sandbox ? 67 : 0) + (int)ShopRules.WingBlocker(wing) * 71 + (int)wing.Funds * 73 + (air.FactionStock + air.Held) * 79;
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
            stateWord = word;
            string blocker;
            if (selected == null)
            {
                WingBlock b = client ? WingBlock.Client : ShopRules.WingBlocker(wing);
                blocker = b != WingBlock.None ? "BLOCKED · " + ShopRules.WingReason(b, wing) : null;
                dispatch.Set(word, WmcState.Of(cls), "NO AIRFRAME", null, "Pick one in step 2",
                    blocker ?? "Pick a pilot, an airframe, its fit and a base", blocker != null ? "warn" : "");
                dispatch.SetMeta(client ? "THE HOST REQUISITIONS" : SupplyWords.Crew(wing.Members, wing.Pending, wing.MaxMembers));
            }
            else
            {
                string fitWord = SupplyWords.Fit(fit, fit != null && fit != CallSpec.YourLoadout ? WingLoadoutTemplates.NameOf(fit) : null);
                string line = ShopRules.DispatchLine(pilot != null ? WmcText.Cut(pilot.Callsign, PilotPick.CallsignChars) : null, fitWord,
                    WingRequisition.FuelPercent, field != null ? BaseName.Short(WingRequisition.NameOf(field)) : null);
                blocker = ShopRules.Blocker(quote, wing, air);
                string level = blocker != null ? "warn" : quote.OverLimit ? "info" : "ok";
                dispatch.Set(word, WmcState.Of(cls), selected.unitName + " · " + Credits.Price(quote.Price), IconFactory.Aircraft(selected), line,
                    blocker ?? (quote.OverLimit ? ShopRules.OverLimitNote(wing) : null), level);
                dispatch.SetMeta(client ? "THE HOST REQUISITIONS"
                    : SupplyWords.NextCall(wing.Sandbox, quote.Price, wing.Funds, wing.Members, wing.Pending, wing.MaxMembers, air.FactionStock + air.Held));
            }
            alert = selected != null && !client ? blocker : null;

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

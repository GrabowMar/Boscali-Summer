using NOAvionics;
using System.Collections.Generic;
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
    // SUPPLY's crew side: INBOUND (launches on their way), ADOPT (friendly AI selected on the map) and step 1 PILOT & CREW.
    internal sealed partial class WmcSupply
    {
        private readonly InboundRow[] inbound = new InboundRow[8];
        private WmcHidable inboundBlock;
        private AvSection inboundSection;
        private AvList inboundList;
        private int inboundCount, inboundChipShown = -1, inboundKey = int.MinValue;

        private void BuildInbound()
        {
            inboundBlock = flow.Add(new WmcHidable(flow, ticker, pageIndex, "Inbound"));
            AvFlow f = inboundBlock.Flow;
            inboundSection = f.Section(AvIcon.ArrowUp, SupplyWords.InboundTitle, "");
            inboundList = f.Add(new AvList(f.Content, ticker, BezelLayout.InboundMax, BindInbound));
        }

        /// <summary>Launches on their way (queued, spawning, taxiing, departing, joining with an ETA), the last row "+n MORE"
        /// beyond four; a row's text is rebuilt only when its launch, phase or ETA second changes.</summary>
        private void RefreshInbound()
        {
            inboundCount = client || SpawnService.Instance == null ? 0 : SpawnService.Instance.Inbound(inbound);
            inboundBlock.SetShown(inboundCount > 0);
            if (inboundCount != inboundChipShown)
            {
                inboundChipShown = inboundCount;
                inboundSection.SetCaption(SupplyWords.InboundChip(inboundCount));
                relayout = true;
            }
            int rows = BezelLayout.InboundRows(inboundCount);
            int key = rows * 7919 + inboundCount;
            unchecked
            {
                for (int i = 0; i < rows; i++)
                {
                    InboundRow row = inbound[i];
                    key = key * 31 + (int)row.Phase * 100003 + (float.IsNaN(row.Eta) ? 99999 : (int)row.Eta);
                    key = key * 31 + (row.Type != null ? row.Type.GetHashCode() : 0) + (row.Pilot != null ? row.Pilot.GetHashCode() * 3 : 0)
                        + (row.Field != null ? row.Field.GetHashCode() * 7 : 0);
                }
            }
            if (key == inboundKey) return;
            inboundKey = key;
            inboundList.SetCount(rows);
            relayout = true;
        }

        private void BindInbound(int item, AvRow row)
        {
            bool more = item == BezelLayout.InboundMax - 1 && inboundCount > BezelLayout.InboundMax;
            InboundRow r = inbound[item];
            AvState state = more || r.Phase == InboundPhase.Queued || r.Phase == InboundPhase.Spawning ? AvState.Inert
                : r.Phase == InboundPhase.Joining ? AvState.Ready : AvState.Info;
            row.Set(more ? InboundWords.More(inboundCount - (BezelLayout.InboundMax - 1)) : InboundText(r), "", "", state);
        }

        private static string InboundText(in InboundRow r)
        {
            string code = r.Type != null ? SupplyWords.Code(r.Type.code, r.Type.unitName) : WmcText.Unknown;
            string s = InboundWords.Row(code, r.Pilot != null ? WmcText.Cut(r.Pilot.Callsign, PilotPick.CallsignChars) : null, r.Phase, r.Eta);
            return r.Field != null && float.IsNaN(r.Eta) ? s + " · " + BaseName.Short(WingRequisition.NameOf(r.Field)).ToUpperInvariant() : s;
        }

        // ---------------------------------------------------------------- ADOPT

        private WmcHidable adoptBlock;
        private AvControl adoptButton;
        private WmcLines adoptNote;
        private readonly List<Aircraft> recruits = new List<Aircraft>();
        private ConfirmGate adoptGate = new ConfirmGate();
        private float adoptCost;
        private int adoptSkipped, adoptHash = int.MinValue, adoptShown = int.MinValue;
        private string adoptWhy, adoptKey;
        private bool adoptVisible;

        private void BuildAdopt()
        {
            adoptBlock = flow.Add(new WmcHidable(flow, ticker, pageIndex, "Adopt"));
            AvFlow f = adoptBlock.Flow;
            adoptButton = f.Buttons(new AvControl.Spec("ADOPT", Adopt, AvButtonStyle.Primary, AvIcon.UsersGroup)).Controls[0];
            adoptButton.Help = "Take command of the friendly AI selected on the map: press twice, the cost is shown.";
            ids.Add("sup.adopt", adoptButton);
            adoptNote = f.Add(new WmcLines(f.Content, 1));
        }

        /// <summary>Friendly AI selected on the map that can join (host only) and what they cost; the faction's others that
        /// cannot, with the first reason. Enemies, our own members and the player are not counted at all.</summary>
        private void CountAdopt(WmcContext c)
        {
            recruits.Clear();
            adoptCost = 0f;
            adoptSkipped = 0;
            adoptWhy = null;
            WingService w = c.Wing;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Aircraft player = w != null ? w.Player : null;
            if (map == null || w == null || client || player == null || map.selectedIcons == null) return;
            int room = WingService.MaxMembers - w.Members.Count - (SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0);
            foreach (MapIcon icon in map.selectedIcons)
            {
                if (!(icon is UnitMapIcon u) || !(u.unit is Aircraft a) || a == player || a.NetworkHQ != player.NetworkHQ || w.IsMember(a)) continue;
                string why;
                if (recruits.Count >= room || recruits.Count >= WingOrder.MaxUnits) why = "the wing is full";
                else if (w.CanRecruit(a, out why))
                {
                    CallQuote q = WingAdoption.Quote(a);
                    if (q.Allowed)
                    {
                        recruits.Add(a);
                        adoptCost += q.Charge;
                        continue;
                    }
                    why = q.Reason;
                }
                adoptSkipped++;
                if (adoptWhy == null) adoptWhy = why;
            }
        }

        /// <summary>The row shows only when something can join; a changed selection asks again (review focus 3).</summary>
        private void RefreshAdopt(WmcContext c)
        {
            CountAdopt(c);
            adoptVisible = AdoptRow.Visible(recruits.Count, client);
            adoptBlock.SetShown(adoptVisible);
            int hash = recruits.Count;
            unchecked
            {
                foreach (Aircraft a in recruits) hash = hash * 486187739 + (int)a.persistentID.Id;
            }
            if (hash != adoptHash)
            {
                adoptHash = hash;
                adoptGate = new ConfirmGate();
                adoptKey = adoptVisible ? AdoptRow.Key(RecruitIds()) : null;
            }
            if (!adoptVisible) return;
            bool asking = adoptKey != null && adoptGate.IsArmed(adoptKey, Time.unscaledTime);
            int key;
            unchecked
            {
                key = hash * 31 + (int)(adoptCost * 10f) * 7 + adoptSkipped * 131 + (asking ? 1 : 0) + (adoptWhy != null ? adoptWhy.GetHashCode() : 0);
            }
            if (key == adoptShown) return;
            adoptShown = key;
            adoptButton.Label = AdoptRow.Label(recruits.Count, adoptCost, asking);
            adoptButton.Latched = asking;
            adoptNote.Set(0, AdoptRow.Note(adoptSkipped, adoptWhy));
            relayout = true;
        }

        private uint[] RecruitIds()
        {
            var units = new uint[recruits.Count];
            for (int i = 0; i < units.Length; i++) units[i] = recruits[i].persistentID.Id;
            return units;
        }

        private void Adopt()
        {
            if (last == null || recruits.Count == 0 || adoptKey == null) return;
            WmcUi.Order(last, () =>
            {
                if (!adoptGate.Press(adoptKey, Time.unscaledTime))
                {
                    WingToast.Show(AdoptRow.Ask(recruits.Count, adoptCost));
                    adoptShown = int.MinValue;
                    WmcPanel.Instance?.Refresh();
                    return;
                }
                if (!WingOrders.Run(new WingOrder { Kind = OrderKind.Recruit, Units = RecruitIds() }).Accepted) return;
                // The adopted leave the game's selection (or the player's target list), as in 0.9.
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                WingService w = last.Wing;
                foreach (Aircraft a in recruits)
                {
                    if (a == null || w == null || !w.IsMember(a)) continue;
                    if (flying && hud.GetTargetList().Contains(a)) hud.DeSelectUnit(a);
                    else if (map != null) map.DeselectIcon(a);
                }
                recruits.Clear();
                WmcPanel.Instance?.Refresh();
            });
        }

        // ---------------------------------------------------------------- step 1 PILOT & CREW

        private const string PilotTip = "Choose who flies the next requisition: free pilots only, the most senior first.";
        private AvSection pilotSection;
        private WmcPilotCard pilotCard;
        private AvStepper pilotStepper;
        private string pilotCounter = "";
        private readonly List<WingPilot> free = new List<WingPilot>();
        private int pilotVersion = int.MinValue;
        private bool pilotClient;

        private void BuildPilot(AvFlow f)
        {
            pilotSection = f.Section(AvIcon.User, "1 · " + SupplyWords.PilotTitle, "");
            // The card is not a click target (0.9 critique: a dead card click); only the stepper acts.
            pilotCard = f.Add(new WmcPilotCard(f.Content));
            pilotStepper = f.Add(new AvStepper(f.Content, "NEXT PILOT", () => pilotCounter, () => StepPilot(-1), () => StepPilot(1)));
            ids.Add("sup.pilot.prev", pilotStepper.Minus);
            ids.Add("sup.pilot.next", pilotStepper.Plus);
            SetStepper(true, PilotTip);
        }

        /// <summary>The pilot the next launch seats (the roster's Upcoming); rebuilt only when the roster's version moves.</summary>
        private void RefreshPilot()
        {
            int v = WingPilotRoster.Version;
            if (v == pilotVersion && client == pilotClient) return;
            pilotVersion = v;
            pilotClient = client;
            relayout = true;
            if (client)
            {
                pilotCard.Set(WmcText.Unknown, "THE HOST KEEPS THE ROSTER", "", AvState.Inert);
                pilotCounter = WmcText.Unknown;
                pilotStepper.Refresh();
                pilotCard.Portrait.Set(null);
                StepCaption(pilotSection, WmcText.Unknown, "inert");
                SetStepper(false, ClientWhy);
                return;
            }
            WingPilotRoster.FreePilots(free);
            WingPilot up = WingPilotRoster.Upcoming;
            int i = PilotPick.Index(free.IndexOf(up), free.Count);
            pilotCard.Set(PilotPick.NameLine(up?.Callsign, up?.Name),
                PilotPick.RankLine(up != null ? WingPilotRoster.RankName(up.Rank) : null, up != null ? up.Xp : 0), PilotPick.Status(up != null),
                up != null ? AvState.Info : AvState.Inert);
            pilotCard.Portrait.Set(up);
            pilotCounter = PilotPick.Counter(i, free.Count);
            pilotStepper.Refresh();
            StepCaption(pilotSection, PilotPick.State(free.Count), free.Count > 0 ? "live" : "info");
            SetStepper(free.Count > 1, free.Count == 1 ? "Only one pilot is free." : "Nobody is free: a new pilot is drafted at launch.");
        }

        private void SetStepper(bool on, string why)
        {
            foreach (AvControl b in new[] { pilotStepper.Minus, pilotStepper.Plus })
            {
                b.Interactable = on;
                b.Help = on ? PilotTip : why;
            }
        }

        private void StepPilot(int dir)
        {
            if (client) return;
            WingPilotRoster.FreePilots(free);
            if (free.Count <= 1) return;
            int i = PilotPick.Index(free.IndexOf(WingPilotRoster.Upcoming), free.Count);
            WingPilotRoster.Select(free[PilotPick.Step(i, free.Count, dir)]);
            WmcPanel.Instance?.Refresh();
        }
    }
}

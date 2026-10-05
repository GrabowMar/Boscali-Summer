using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
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
        private WmcStripBlock inboundBlock;
        private int inboundCount, inboundKey = int.MinValue;

        private void BuildInbound()
        {
            inboundBlock = flow.Add(new WmcStripBlock(flow.Content, "Inbound", BezelLayout.InboundMax, null, null));
            inboundBlock.SetShown(false);
        }

        /// <summary>Launches on their way (queued, spawning, taxiing, departing, joining with an ETA), the last strip "+n MORE"
        /// beyond four; a strip's text is rebuilt only when its launch, phase or ETA second changes.</summary>
        private void RefreshInbound()
        {
            inboundCount = client || SpawnService.Instance == null ? 0 : SpawnService.Instance.Inbound(inbound);
            int rows = BezelLayout.InboundRows(inboundCount);
            if (inboundBlock.Shown != rows > 0)
            {
                inboundBlock.SetShown(rows > 0);
                inboundKey = int.MinValue;
                relayout = true;
            }
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
            for (int i = 0; i < inboundBlock.Capacity; i++)
            {
                if (i >= rows) inboundBlock.Hide(i);
                else BindInbound(i);
            }
            relayout = true;
        }

        private void BindInbound(int item)
        {
            bool more = item == BezelLayout.InboundMax - 1 && inboundCount > BezelLayout.InboundMax;
            InboundRow r = inbound[item];
            if (more)
            {
                inboundBlock.Set(item, "", AvState.Inert, InboundWords.More(inboundCount - (BezelLayout.InboundMax - 1)), "", -1f, "", AvState.Inert, AvState.Inert);
                return;
            }
            AvState state = r.Phase == InboundPhase.Queued || r.Phase == InboundPhase.Spawning ? AvState.Inert
                : r.Phase == InboundPhase.Joining ? AvState.Ready : AvState.Info;
            string code = r.Type != null ? SupplyWords.Code(r.Type.code, r.Type.unitName) : WmcText.Unknown;
            string pilot = r.Pilot != null ? WmcText.Cut(r.Pilot.Callsign, PilotPick.CallsignChars) : null;
            string sub = (pilot != null ? pilot + " · " : "") + InboundWords.Phase(r.Phase)
                + (r.Field != null ? " · " + BaseName.Short(WingRequisition.NameOf(r.Field)).ToUpperInvariant() : "");
            string eta = float.IsNaN(r.Eta) ? "" : "JOIN " + LegMath.Clock(r.Eta);
            inboundBlock.Set(item, "INBOUND", AvState.Info, code, sub, SupplyWords.InboundProgress(r.Phase), eta, AvState.Info, state);
        }

        // ---------------------------------------------------------------- ADOPT

        private WmcStripBlock adoptBlock;
        private AvControl adoptButton;
        private readonly List<Aircraft> recruits = new List<Aircraft>();
        private ConfirmGate adoptGate = new ConfirmGate();
        private float adoptCost;
        private int adoptSkipped, adoptHash = int.MinValue, adoptShown = int.MinValue;
        private string adoptWhy, adoptKey;
        private bool adoptVisible;

        private void BuildAdopt()
        {
            adoptBlock = flow.Add(new WmcStripBlock(flow.Content, "Adopt", 1, null, (r, k) => Adopt(), new AvControl.Spec("ADOPT", null, AvButtonStyle.Primary)));
            adoptBlock.SetShown(false);
            adoptButton = adoptBlock.Button(0, 0);
            adoptButton.Help = "Take command of the friendly AI selected on the map: press twice, the cost is shown.";
            ids.Add("sup.adopt", adoptButton);
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
            if (adoptBlock.Shown != adoptVisible)
            {
                adoptBlock.SetShown(adoptVisible);
                adoptShown = int.MinValue;
                relayout = true;
            }
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
            adoptButton.Label = asking ? "ADOPT?" : "ADOPT";
            adoptButton.Latched = asking;
            AircraftDefinition first = recruits.Count > 0 ? recruits[0].definition : null;
            string what = first != null ? SupplyWords.Code(first.code, first.unitName) : WmcText.Unknown;
            adoptBlock.Set(0, "ADOPT", AvState.Info, recruits.Count > 1 ? what + " ×" + recruits.Count : what, AdoptRow.Note(adoptSkipped, adoptWhy), -1f,
                Credits.Price(adoptCost), AvState.Info, AvState.Info);
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
        private WmcHeadBar pilotHead;
        private WmcPilotCard pilotCard;
        private readonly List<WingPilot> free = new List<WingPilot>();
        private int pilotVersion = int.MinValue;
        private bool pilotClient;

        private void BuildPilot(AvFlow f)
        {
            pilotHead = f.Add(new WmcHeadBar(f.Content, AvIcon.User, "1 · " + SupplyWords.PilotTitle));
            // The card is not a click target (0.9 critique: a dead card click); only its arrows act.
            pilotCard = f.Add(new WmcPilotCard(f.Content, StepPilot));
            ids.Add("sup.pilot.prev", pilotCard.Prev);
            ids.Add("sup.pilot.next", pilotCard.Next);
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
                pilotCard.Portrait.Set(null);
                pilotHead.SetCaption(StepCaption(WmcText.Unknown, "inert"));
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
            pilotHead.SetCaption(StepCaption(SupplyWords.PilotCaption(i, free.Count), free.Count > 0 ? "live" : "info"));
            SetStepper(free.Count > 1, free.Count == 1 ? "Only one pilot is free." : "Nobody is free: a new pilot is drafted at launch.");
        }

        private void SetStepper(bool on, string why)
        {
            foreach (AvControl b in new[] { pilotCard.Prev, pilotCard.Next })
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

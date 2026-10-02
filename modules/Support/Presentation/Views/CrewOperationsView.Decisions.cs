using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    internal sealed partial class CrewOperationsView
    {
        // Local selection never owns an operation. Every key remains a host-validated intent.
        private readonly List<Key> keys = new List<Key>(80);
        private int focusIndex = -1, selectedDelivery, defenceSelection = -1;
        private bool CyberWorkEnabled => CanSend && (support?.Settings?.EwEnabled.Value ?? true) && (support?.Settings?.CyberEnabled.Value ?? true);
        private TMP_Text deliveryTitle, deliveryDossier, deliveryLease;

        private void ClearFocus()
        {
            if (focusIndex >= 0 && focusIndex < keys.Count) keys[focusIndex].Control.SetFocus(false);
            focusIndex = -1;
        }

        private void FocusFirst()
        {
            ClearFocus();
            // Workspace keys precede delivery and service keys, so each page starts at its first control.
            for (int i = 3; i < keys.Count; i++)
                if (keys[i].Control.gameObject.activeInHierarchy) { FocusKey(i); return; }
        }

        private void FocusKey(int index)
        {
            ClearFocus(); focusIndex = index;
            RoomControl control = keys[index].Control;
            control.SetFocus(true);
            ScrollRect scroll = control.GetComponentInParent<ScrollRect>();
            if (scroll == null || control.Rect.parent != scroll.content) return;
            float height = scroll.viewport.rect.height, limit = Mathf.Max(0, scroll.content.rect.height - height);
            if (limit <= 0) return;
            float top = -control.Rect.anchoredPosition.y, bottom = top + control.Rect.rect.height;
            float offset = Mathf.Clamp(scroll.content.anchoredPosition.y, 0, limit);
            if (top < offset) offset = top;
            else if (bottom > offset + height) offset = bottom - height;
            scroll.verticalNormalizedPosition = 1f - Mathf.Clamp(offset, 0, limit) / limit;
        }

        private void FocusNext(int direction)
        {
            if (keys.Count == 0) return;
            int start = focusIndex < 0 ? direction > 0 ? -1 : 0 : focusIndex;
            // Disabled controls remain inspectable: their focused footer explains the refusal.
            for (int step = 1; step <= keys.Count; step++)
            {
                int index = (start + direction * step + keys.Count * 2) % keys.Count;
                if (keys[index].Control.gameObject.activeInHierarchy) { FocusKey(index); return; }
            }
        }

        public bool HandleKeys()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return false;
            if (Input.GetKeyDown(KeyCode.Alpha1)) { SelectPage(0); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { SelectPage(1); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { SelectPage(2); return true; }
            if (Input.GetKeyDown(KeyCode.Tab))
            { FocusNext(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1); return true; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            { if (focusIndex >= 0) keys[focusIndex].Control.Activate(); return true; }
            int direction = Input.GetKeyDown(KeyCode.Q) ? -1 : Input.GetKeyDown(KeyCode.E) ? 1 : 0;
            if (direction == 0) return false;
            if (Domain == OpsDomain.Cyber && page == 2 && network != null)
            {
                int start = defenceSelection >= 0 ? defenceSelection : Math.Max(0, support?.LocalCyberThreatSlot ?? -1);
                for (int step = 1; step <= CyberNetwork.TargetBase; step++)
                {
                    int slot = (start + direction * step + CyberNetwork.TargetBase) % CyberNetwork.TargetBase;
                    if (network.Exists(slot) && network.Node(slot).Static) { defenceSelection = slot; dirty = true; return true; }
                }
                return false;
            }
            if (page != 0) return false;
            if (Domain == OpsDomain.SpecialOperations) { CycleObjective(direction); return true; }
            if (Domain != OpsDomain.Cyber || network == null) return false;
            for (int step = 1; step <= CyberNetwork.SlotCount; step++)
            {
                int slot = (Math.Max(0, selectedNode) + direction * step + CyberNetwork.SlotCount) % CyberNetwork.SlotCount;
                if (network.Exists(slot)) { selectedNode = slot; dirty = true; return true; }
            }
            return false;
        }

        private bool Affordable(float cost) => support == null || support.BypassRequirements || support.LocalOpsReserve + .001f >= cost;

        private void PaintMarket(OrbitalPlatform p, CyberNetwork c, SpecOpsDetachment d, double now)
        {
            if (marketText == null) return;
            int supplier = d?.FindMarket(now) ?? -1;
            bool ready = supplier >= 0 && d.Enabled && (support?.Settings?.SpecOpsEnabled.Value ?? true);
            marketText.text = ready ? FieldWords.Callsign(supplier) + " / ESPIONAGE SUPPLIER\n" + d.Team(supplier).Charges + " SHARED SHIPMENTS / " + FieldWords.Clock(SpecOpsDetachment.PostPressureWindow(d.Team(supplier), now)) + " MAX WINDOW"
                : "SUPPLIER CLOSED\nNeeds a hostile espionage post / quality 2 / one charge.";
            float computing = c == null ? 0 : BlackMarketCatalog.TopUp(c.Computing, c.ComputingCapacity(), BlackMarketCatalog.Computing);
            float intel = c == null ? 0 : BlackMarketCatalog.TopUp(c.Intel, c.IntelCapacity(), BlackMarketCatalog.Intel);
            float signalPrice = support?.BlackMarketPrice(0) ?? BlackMarketCatalog.Price(0);
            string signalDenial = !ready ? "NO SUPPLIER" : !Affordable(signalPrice) ? "LOW OPS RESERVE"
                : !(support?.Settings?.EwEnabled.Value ?? true) || !(support?.Settings?.CyberEnabled.Value ?? true) ? "CYBER DISABLED"
                : c == null || !c.CommandOnline ? "NEEDS ONLINE CYBER COMMAND" : computing + intel <= .001f ? "RESOURCE BUSES FULL" : null;
            string signal = "+" + computing.ToString("0.#") + " COMP / +" + intel.ToString("0.#") + " INTEL";
            marketSignal.Set("SIGNALS KIT / " + signalPrice + " OPS\n" + (signalDenial ?? signal + " / 1 CHARGE"), CanSend && signalDenial == null, false,
                (signalDenial ?? signal) + " / Capacity limits each resource independently. Accepted orders spend one supplier charge.");
            float cargoPrice = support?.BlackMarketPrice(1) ?? BlackMarketCatalog.Price(1);
            PlatformStats stats = p != null && p.Exists ? p.Stats(now) : default;
            string cargoDenial = !ready ? "NO SUPPLIER" : !Affordable(cargoPrice) ? "LOW OPS RESERVE" : p == null || !p.Exists ? "NO STATION"
                : p.Pending != ModuleKind.None ? "STATION DELIVERY BUSY"
                : p.Rods >= stats.RodCapacity && p.Fuel >= stats.FuelCapacity - .001f ? "FUEL / MAGAZINE FULL" : null;
            marketCargo.Set("ORBITAL CARGO / " + cargoPrice + " OPS\n" + (cargoDenial ?? "8 S DELIVERY / 1 CHARGE"), CanSend && cargoDenial == null, false,
                (cargoDenial ?? "Resupply the actual fitted fuel tanks and rod magazine.") + " / Host rechecks the shared stock before spending.");
        }

        private int DefenseTarget(CyberVerb verb)
        {
            if (network == null) return -1;
            if (CyberNetwork.TargetsIncident(verb))
            {
                for (int i = 0; i < CyberNetwork.IncidentSlots; i++)
                    if (network.IncidentActive(i) && network.Check(verb, network.Incident(i).Id, clock) == CyberDenial.None) return network.Incident(i).Id;
                return -1;
            }
            int threat = support?.LocalCyberThreatSlot ?? -1;
            if (defenceSelection >= 0 && network.Exists(defenceSelection) && network.Node(defenceSelection).Static) return defenceSelection;
            if (verb != CyberVerb.Patch && threat >= 0 && network.Exists(threat) && network.Node(threat).Static) return threat;
            if (verb == CyberVerb.Patch)
                for (int i = 0; i < CyberNetwork.TargetBase; i++)
                    if (network.Exists(i) && network.Node(i).Static && network.Node(i).Compromised) return i;
            if (selectedNode >= 0 && network.Exists(selectedNode) && network.Node(selectedNode).Static) return selectedNode;
            int first = -1;
            for (int i = 0; i < CyberNetwork.TargetBase; i++)
                if (network.Exists(i) && network.Node(i).Static)
                { if (first < 0) first = i; if (network.Check(verb, i, clock) == CyberDenial.None) return i; }
            return first;
        }

        private void PaintDefence(CyberNetwork c, double now)
        {
            if (Domain != OpsDomain.Cyber) return;
            int threat = support?.LocalCyberThreatSlot ?? -1;
            bool incoming = c != null && threat >= 0 && c.Exists(threat);
            defenceWatch.Primary = incoming;
            defenceWatch.Set(incoming ? "INCOMING / " + CyberWords.Callsign(c, threat) + " / OPEN DEFENCE" : "HOME NET / OPEN DEFENCE", true, incoming,
                incoming ? "An active enemy intrusion reaches this home node. Honeypot interrupts its route; Cyber Command stays online." : "Defend owned home nodes or respond to a live incident. Q / E cycles map targets.");
            for (int i = 0; i < upgrades.Length; i++)
            {
                var upgrade = (CyberUpgrade)i;
                float cost = support?.CyberUpgradeCost(upgrade) ?? c?.UpgradeCost(upgrade) ?? 0;
                int level = c?.UpgradeLevel(upgrade) ?? 0;
                bool available = c != null && c.CanUpgrade(upgrade);
                string reason = !(support?.Settings?.EwEnabled.Value ?? true) ? "EW DISABLED" : c == null ? "NO CYBER COMMAND" : !available ? "MAXIMUM LEVEL" : !Affordable(cost) ? "LOW OPS RESERVE" : CyberLocations.UpgradeEffect(upgrade);
                upgrades[i].Set(CyberLocations.UpgradeName(upgrade) + " / " + level + "/" + CyberLocations.UpgradeLevels + (available ? " / " + cost + " OPS" : "") + "\n" + reason,
                    CanSend && (support?.Settings?.EwEnabled.Value ?? true) && available && Affordable(cost), false, reason + " / Applied to the entire faction network.");
            }
            for (int i = 0; i < defenses.Length; i++)
            {
                var verb = (CyberVerb)i;
                defenseTargets[i] = DefenseTarget(verb);
                var denial = c?.Check(verb, defenseTargets[i], now) ?? CyberDenial.NoCommand;
                string target = c == null || defenseTargets[i] < 0 ? "NO ELIGIBLE TARGET" : CyberNetwork.TargetsIncident(verb) ? "INCIDENT " + defenseTargets[i] : CyberWords.Callsign(c, defenseTargets[i]);
                bool rejoin = verb == CyberVerb.Isolate && c != null && c.Node(defenseTargets[i]).Isolated;
                float cooling = verb == CyberVerb.Isolate && !rejoin ? support?.TeamCooldownRemaining(TeamGate.Isolate) ?? 0 : 0;
                bool enabled = CanSend && (support?.Settings?.EwEnabled.Value ?? true) && denial == CyberDenial.None && cooling <= .5f;
                string reason = !(support?.Settings?.EwEnabled.Value ?? true) ? "EW DISABLED" : cooling > .5f ? "TEAM RE-TASKING / " + FieldWords.Clock(cooling) : CyberWords.Denial(denial);
                defenses[i].Set((rejoin ? "REJOIN" : verb.ToString().ToUpperInvariant()) + " / " + (rejoin ? 0 : CyberNetwork.VerbCost(verb)) + " COMP / " + target + "\n" + reason, enabled, false,
                    CyberWords.VerbHelp(verb) + " / " + target + " / " + reason + " / Q E selects another home node.");
            }
        }

        private static string DeliveryRequirement(SupportActionDefinition action) => action.Hack.HasValue ? "Q" + CyberCatalog.RequiredQuality(action.Hack.Value)
            : action.Cap.HasValue ? "Q6" : action.Field.HasValue ? "Q" + FieldCatalog.RequiredQuality(action.Field.Value) + " / 1 CHARGE" : "NATIVE HARDWARE";

        private void PaintCapabilityRegisters()
        {
            if (Domain == OpsDomain.Space)
            {
                OrbitalPlatform p = currentPlatform;
                deliveryRegisters[0].text = "RECON / SENSOR BUS\nCORE SURVEY " + (p != null && p.Exists ? "FITTED" : "ABSENT") + "\nIMAGER " + HardwareWord(p, ModuleKind.Imager) + "\nSIGINT " + HardwareWord(p, ModuleKind.Sigint);
                deliveryRegisters[1].text = "KINETIC / MAGAZINE\nRODS " + (p?.Rods ?? 0) + " / " + HardwareWord(p, ModuleKind.Rods) + "\nGYRO " + HardwareWord(p, ModuleKind.Gyro) + "\nSCATTER " + (p != null && p.Exists ? Mathf.RoundToInt(p.PreparedRodScatter(clock)) + " M" : "UNAVAILABLE");
                deliveryRegisters[2].text = "SCREEN / EMP\nEMITTER " + HardwareWord(p, ModuleKind.Emp) + "\nPACKAGE " + (p != null && p.PackageAppliesTo(PlatformAbility.EmpBurst, clock) ? Mathf.RoundToInt(p.Boost * 100) + "%" : "NOT BANKED") + "\nRECON " + (p != null && p.SolutionRemaining(clock) > 0 ? FieldWords.Clock(p.SolutionRemaining(clock)) : "NO SOLUTION");
            }
            else if (Domain == OpsDomain.Cyber)
            {
                CyberNetwork c = network;
                CyberNode access = c != null ? c.Node(c.AccessSlot) : default;
                string sector = c != null && c.ControlsSector(access.X, access.Z, clock)
                    ? OpsSectors.Code(access.X, access.Z) : "LOCKED";
                deliveryRegisters[0].text = "INTRUSION\nQ0 / EMITTER PING + SCAN\nQ2 / HIJACK\nQ4 / OVERLOAD";
                deliveryRegisters[1].text = "ELECTRONIC WARFARE\nQ2 / TRACK + BLACKOUT\nQ4 / GHOST + SPOOF\nQ6 / NETWORK PAYLOAD";
                deliveryRegisters[2].text = "TEAM LEASE\nSECTOR " + sector + "\nQUALITY " + (c?.AccessQuality ?? 0) + "/6 / " + FieldWords.Clock(c?.AccessRemaining(clock) ?? 0) + "\nONE ACCEPTED EFFECT";
                if (deliveryLease != null) deliveryLease.text = c == null ? "NO CYBER COMMAND" : c.BreachActive ? "LIVE INTRUSION\n" + CyberWords.Callsign(c, c.BreachTarget) + "\n" + c.WorkProgress + "/3 GATES / TRACE " + Mathf.RoundToInt(c.BreachTrace * 100) + "%"
                    : c.AccessRemaining(clock) > 0 ? "ACCESS " + CyberWords.Callsign(c, c.AccessSlot) + " / " + sector + "\nQUALITY " + c.AccessQuality + "/6 / " + FieldWords.Clock(c.AccessRemaining(clock)) + "\nINTEL " + Mathf.FloorToInt(c.Intel) : "NO SHARED ACCESS\nPROFILE / SECURE THREE GATES\nRELEASE BEFORE DESIGNATION";
            }
            else
            {
                int charges = 0, posts = 0;
                if (detachment != null) for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                { FieldTeam team = detachment.Team(i); if (team.State == TeamState.Holding && team.Charges > 0 && team.PhaseEnd > clock &&
                    OpsSectors.TryLocate(team.X, team.Z, out _)) { charges += team.Charges; posts++; } }
                deliveryRegisters[0].text = "RECON / DIRECT ACTION\nSPOT Q0 / SKYWATCH Q1\nSUPPRESS Q0 / HUNT Q2";
                deliveryRegisters[1].text = "ESPIONAGE / SUPPORT\nEAVESDROP Q0\nHOSTILE Q2 / SUPPLIER\nSAFEHOUSE / FORTIFY SECTOR";
                deliveryRegisters[2].text = "FORWARD STOCK\n" + posts + " HELD POSTS\n" + charges + " SHARED CHARGES\nMATCHING CONTROLLED SECTOR";
            }
        }

        private string HardwareWord(OrbitalPlatform p, ModuleKind kind) => p == null || !p.Fitted(kind) ? "ABSENT" : p.FittedOnline(kind, clock) ? "ONLINE" : "OFFLINE";

        private void PaintDeliveryDossier()
        {
            PaintCapabilityRegisters();
            if (deliveries.Count == 0) { deliveryDossier.text = "Awaiting the faction capability catalogue."; return; }
            selectedDelivery = Mathf.Clamp(selectedDelivery, 0, deliveries.Count - 1);
            SupportActionDefinition action = deliveries[selectedDelivery];
            AbilityFacts facts = AbilityStatus.For(support, action, support.BypassRequirements);
            deliveryTitle.text = action.Name;
            string prerequisite = action.Hack.HasValue ? CyberCatalog.Branch(action.Hack.Value) + " / QUALITY " + CyberCatalog.RequiredQuality(action.Hack.Value)
                : action.Cap.HasValue ? "NETWORK PAYLOAD / QUALITY 6 / CHOSEN SPECIALIZATION"
                : action.Field.HasValue ? FieldWords.Post(FieldCatalog.PostFor(action.Field.Value)) + " / QUALITY " + FieldCatalog.RequiredQuality(action.Field.Value) + " / 1 CHARGE"
                : action.Id == SupportActionId.Fortify ? "OWNED GROUND OR SAFEHOUSE SECTOR / FORTIFY PERK REQUIRED" : "FACTION EFFECT / " + facts.CostText;
            deliveryDossier.text = prerequisite + "\n" + action.Description;
            string destination = action.IsCyber || action.IsField ? "a grid in the matching controlled sector"
                : action.Id == SupportActionId.Fortify ? "owned ground or a held safehouse sector" : "a grid inside the package's reach";
            deliveryStatus.text = facts.Readiness + "\n\n01  Activate to designate on the game map.\n02  Confirm " + destination + ".\n03  An accepted effect spends shared access or stock.";
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The radio's YES/NO askers for systems with no speaker of their own (2026-11-15): COMMAND offers each new contract (YES accepts it and puts it on the HUD),
    /// OPS each perk that comes off its cooldown (YES arms it, then OPS asks to fire it at the pilot's aim), VANGUARD the drones when the pilot locks a new target
    /// (YES sends them). Each answer reads its service again before it acts: what was asked about may be gone. The first look at each system is a baseline, so
    /// joining a mission asks nothing; only airborne pilots are asked. Ticked every two seconds while the player flies.
    /// </summary>
    internal sealed class RadioAsks
    {
        private const float ContractGap = 150f, PerkGap = 60f, DroneGap = 60f, AirborneAlt = 30f;

        private readonly HashSet<int> known = new HashSet<int>(), active = new HashSet<int>(), seen = new HashSet<int>();
        private readonly Dictionary<string, bool> ready = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
        private bool contractsBaseline, perksBaseline, couldStrike = true;
        private float lastContract = float.NegativeInfinity, lastPerk = float.NegativeInfinity, lastDrones = float.NegativeInfinity;
        private int selectWhenActive;

        public void Reset()
        {
            known.Clear();
            active.Clear();
            ready.Clear();
            contractsBaseline = perksBaseline = false;
            couldStrike = true;
            lastContract = lastPerk = lastDrones = float.NegativeInfinity;
            selectWhenActive = 0;
        }

        public void Tick(IChatter radio, Aircraft me)
        {
            bool airborne = me != null && me.radarAlt > AirborneAlt;
            Contracts(radio, airborne);
            Perks(radio, airborne);
            Drones(radio, airborne);
        }

        // ------------------------------------------------------------------ contracts

        private void Contracts(IChatter radio, bool airborne)
        {
            if (!ModuleServices.TryGet<ISecondaryObjectivesView>(out var board) || !board.IsFresh) return;
            ChatterSpeaker command = radio.Station(ChatterNet.Command, "COMMAND");
            SecondaryObjectiveView offer = null;
            int activeCount = 0;
            seen.Clear();
            foreach (SecondaryObjectiveView o in board.Objectives)
            {
                if (o == null) continue;
                if (o.IsActive) { seen.Add(o.Id); activeCount++; }
                // A contract the faction held is done: Command says so.
                if (o.IsComplete && active.Contains(o.Id) && contractsBaseline)
                    radio.Raise("command.contractdone", command, "contractdone:" + o.Id, ("title", o.Title ?? "the contract"));
                if (o.IsOffered && !known.Contains(o.Id) && (offer == null || o.Id < offer.Id)) offer = o;
            }
            active.Clear();
            active.UnionWith(seen);
            if (!contractsBaseline)
            {
                foreach (SecondaryObjectiveView o in board.Objectives) if (o != null && o.IsOffered) known.Add(o.Id);
                contractsBaseline = true;
                return;
            }
            if (selectWhenActive != 0 && active.Contains(selectWhenActive))
            {
                board.SelectForHud(selectWhenActive);
                selectWhenActive = 0;
            }
            if (offer == null || !airborne || board.IsActionPending || radio.Now - lastContract < ContractGap) return;
            if (board.ActiveLimit > 0 && activeCount >= board.ActiveLimit) return;
            int id = offer.Id;
            if (!radio.Ask(command, "command.askcontract", "contract:" + id,
                    () => AcceptContract(radio, id),
                    () => radio.Raise("command.contractno", command, "contractno:" + id),
                    ("title", string.IsNullOrEmpty(offer.Title) ? "a contract" : offer.Title))) return;
            known.Add(id);
            lastContract = radio.Now;
        }

        private void AcceptContract(IChatter radio, int id)
        {
            if (!ModuleServices.TryGet<ISecondaryObjectivesView>(out var board) || board.IsActionPending) return;
            bool stillOffered = false;
            foreach (SecondaryObjectiveView o in board.Objectives) if (o != null && o.Id == id && o.IsOffered) stillOffered = true;
            if (!stillOffered) return;
            board.RequestAccept(id);
            selectWhenActive = id;
            radio.Raise("command.contractyes", radio.Station(ChatterNet.Command, "COMMAND"), "contractyes:" + id);
        }

        // ------------------------------------------------------------------ OPS perks

        private void Perks(IChatter radio, bool airborne)
        {
            if (!ModuleServices.TryGet<IOpsPerks>(out var perks)) return;
            string candidate = null;
            listed.Clear();
            for (int i = 0; i < perks.PerkCount; i++)
            {
                string label = perks.PerkLabel(i);
                if (string.IsNullOrEmpty(label)) continue;
                listed.Add(label);
                bool now = perks.PerkCooldown(i) <= 0 && !perks.PerkArmed(i);
                // Newly ready: it came off its cooldown, or it joined the list ready.
                bool was = ready.TryGetValue(label, out bool before) && before;
                if (now && !was && perksBaseline && candidate == null) candidate = label;
                ready[label] = now;
            }
            // A perk that left the list starts over: next time it shows up ready, it is news.
            if (ready.Count > listed.Count)
            {
                var gone = new List<string>();
                foreach (string label in ready.Keys) if (!listed.Contains(label)) gone.Add(label);
                foreach (string label in gone) ready.Remove(label);
            }
            perksBaseline = true;
            if (candidate == null || !airborne || radio.Now - lastPerk < PerkGap) return;
            ChatterSpeaker ops = radio.Station(ChatterNet.Command, "OPS");
            string perk = candidate;
            if (radio.Ask(ops, "command.askperk", "perk:" + perk, () => ArmPerk(radio, perk), () => radio.Raise("command.perkno", ops, "perkno:" + perk), ("perk", perk)))
                lastPerk = radio.Now;
        }

        private static int PerkIndex(IOpsPerks perks, string label)
        {
            for (int i = 0; i < perks.PerkCount; i++) if (perks.PerkLabel(i) == label) return i;
            return -1;
        }

        private void ArmPerk(IChatter radio, string perk)
        {
            if (!ModuleServices.TryGet<IOpsPerks>(out var perks)) return;
            int i = PerkIndex(perks, perk);
            if (i < 0 || perks.PerkCooldown(i) > 0) return;
            // The same press as the OPS page ARM button: the first press arms.
            if (!perks.PerkArmed(i)) perks.PerkPress(i);
            if (!perks.PerkArmed(i)) return;
            ChatterSpeaker ops = radio.Station(ChatterNet.Command, "OPS");
            radio.Raise("command.perkyes", ops, "perkyes:" + perk);
            radio.Ask(ops, "command.askfire", "perkfire:" + perk, () => FirePerk(radio, perk), () => radio.Raise("command.fireno", ops, "fireno:" + perk), ("perk", perk));
        }

        private static void FirePerk(IChatter radio, string perk)
        {
            if (!ModuleServices.TryGet<IOpsPerks>(out var perks)) return;
            int i = PerkIndex(perks, perk);
            if (i < 0 || !perks.PerkArmed(i)) return;
            // The second press fires at the pilot's aim.
            perks.PerkPress(i);
            radio.Raise("command.fireyes", radio.Station(ChatterNet.Command, "OPS"), "fireyes:" + perk);
        }

        // ------------------------------------------------------------------ VANGUARD drones

        private void Drones(IChatter radio, bool airborne)
        {
            if (!ModuleServices.TryGet<IDroneCommand>(out var drones)) return;
            bool can = (drones.Ready > 0 || drones.Linkable > 0) && drones.CanStrike && !drones.Striking;
            bool fresh = can && !couldStrike;
            couldStrike = can;
            if (!fresh || !airborne || radio.Now - lastDrones < DroneGap) return;
            ChatterSpeaker vanguard = radio.Station(ChatterNet.Command, "VANGUARD");
            if (radio.Ask(vanguard, "command.askdrones", "drones",
                    () =>
                    {
                        if (!ModuleServices.TryGet<IDroneCommand>(out var d) || !d.CanStrike || (d.Ready == 0 && d.Linkable == 0)) return;
                        d.Strike();
                        radio.Raise("command.dronesyes", vanguard, "dronesyes");
                    },
                    () => radio.Raise("command.dronesno", vanguard, "dronesno")))
                lastDrones = radio.Now;
        }
    }
}

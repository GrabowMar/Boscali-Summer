using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>What a wave would be right now: the first failing gate and the group, price and pool it would be bought at. Cost, Pool and CooldownSeconds are -1 when unknown.</summary>
    internal readonly struct WaveQuote
    {
        internal readonly WaveBlock Block;
        internal readonly int GroupIndex;        // -1 none
        internal readonly string Name;
        internal readonly int Cost;              // rounded; -1 unknown
        internal readonly int Pool;              // floor; -1 unknown
        internal readonly int CooldownSeconds;   // -1 unknown
        internal readonly bool DepotKnown;
        internal readonly float DepotX, DepotZ;  // global metres

        internal WaveQuote(WaveBlock block, int groupIndex, string name, int cost, int pool, int cooldownSeconds,
            bool depotKnown, float depotX, float depotZ)
        {
            Block = block; GroupIndex = groupIndex; Name = name ?? string.Empty; Cost = cost; Pool = pool;
            CooldownSeconds = cooldownSeconds; DepotKnown = depotKnown; DepotX = depotX; DepotZ = depotZ;
        }
    }

    /// <summary>
    /// The one door for spending the faction pool on a convoy (the reinforcement desk and the Events convoy beat both come through here). Funding is the
    /// vanilla convoy path: the pool pays the mission's own group cost, the group enters the supply queue and the game deploys it from a depot on its
    /// next pass. This service quotes, spends and reports; it spawns nothing and orders nothing.
    /// </summary>
    internal sealed class TheaterLogisticsService : MonoBehaviour, ISceneService, IConvoyPurchase
    {
        internal static TheaterLogisticsService Active { get; private set; }

        private const int MaximumCombatGroups = 32;
        private const int MaximumGroupUnits = 16;
        private const float CombatRoleThreshold = 0.25f;

        private readonly Dictionary<int, int> lastGroup = new Dictionary<int, int>(8);
        private readonly GroupRead[] reads = new GroupRead[MaximumCombatGroups];

        private TheaterOpsSettings settings;

        // What the last purchase did to the pool, taken inside Buy in one frame; the sim readback (WaveProof) compares them with constants.
        internal float LastPoolBefore = -1f, LastPoolAfter = -1f, LastCost = -1f, LastGrant, SpentTotal;
        internal int Purchases;
#if !PUBLIC_RELEASE
        internal int LastSupplyBefore = -1, LastSupplyAfter = -1, LastSupplyUnits;
#endif

        public void Configure(TheaterOpsSettings config) => settings = config;

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
        }

        public void ResetForScene()
        {
            lastGroup.Clear();
            LastPoolBefore = LastPoolAfter = LastCost = -1f;
            LastGrant = SpentTotal = 0f;
            Purchases = 0;
        }

        private bool Authoritative => settings != null && settings.Enabled.Value && GameAccess.IsServer();

        /// <summary>
        /// Host only (the native cooldown read throws on a client). The first failing gate, and the group that would be bought: the first eligible one off cooldown and
        /// affordable from the rotation point, else the first eligible one so the price can still be shown. The same numbers feed the panel before anything is spent.
        /// </summary>
        internal WaveQuote Quote(FactionHQ hq, in OperationFocus live, bool sortiesAvailable, bool switchedOff, float poolFactor)
        {
            List<Faction.ConvoyGroup> groups = hq?.faction?.GetConvoyGroups();
            int count = groups == null ? 0 : Mathf.Min(groups.Count, MaximumCombatGroups);
            for (int i = 0; i < count; i++) reads[i] = ReadGroup(hq, groups[i], i);
            float pool = hq != null ? hq.factionFunds : float.NaN;
            int start = hq != null && lastGroup.TryGetValue(hq.GetInstanceID(), out int last) ? last + 1 : Mathf.Abs(live.OperationId) % Mathf.Max(1, count);
            int index = -1;
            bool has = count > 0 && WavePolicy.Choose(reads, count, start, pool, poolFactor, out index);
            float cost = has ? reads[index].Cost : float.NaN, cooldown = has ? reads[index].Cooldown : float.NaN;

            bool depot = false;
            float depotX = 0f, depotZ = 0f;
            if (hq != null && live.IsLive && hq.TryGetNearestDepot(new GlobalPosition(live.X, 0f, live.Z).ToLocalPosition(), float.MaxValue, out VehicleDepot found) &&
                found != null && !found.disabled)
            {
                Vector3 at = found.transform.position.ToGlobalPosition().AsVector3();
                depot = float.IsFinite(at.x) && float.IsFinite(at.z);
                if (depot) { depotX = at.x; depotZ = at.z; }
            }
            WaveBlock block = WavePolicy.Evaluate(new WaveInputs(switchedOff, sortiesAvailable, hq != null && !hq.preventDonation, live.IsLive, depot, has,
                cooldown, pool, cost, poolFactor));
            return new WaveQuote(block, has ? index : -1, has ? groups[index].Name : "", float.IsFinite(cost) ? Mathf.RoundToInt(cost) : -1,
                float.IsFinite(pool) ? (int)Mathf.Min(Mathf.Floor(pool), 1000000f) : -1, float.IsFinite(cooldown) ? Mathf.CeilToInt(Mathf.Max(0f, cooldown)) : -1,
                depot, depotX, depotZ);
        }

        /// <summary>
        /// Host only. Buys the quoted group: the grant (a bounded event subsidy, normally 0) is credited first, the group queued, then the cost debited; a throw from
        /// AddConvoy takes the grant back and spends nothing. Refuses anything but a ready quote.
        /// </summary>
        internal bool Buy(FactionHQ hq, in WaveQuote quote, float grant, string source, out string why)
        {
            why = "";
            List<Faction.ConvoyGroup> groups = hq?.faction?.GetConvoyGroups();
            if (!Authoritative || quote.Block != WaveBlock.None || quote.GroupIndex < 0 || groups == null || quote.GroupIndex >= groups.Count)
            { why = quote.Block != WaveBlock.None ? WaveText.Reason(quote.Block) : "NO GROUP"; return false; }
            Faction.ConvoyGroup group = groups[quote.GroupIndex];
            float cost = group.GetCost();
            if (!float.IsFinite(cost) || cost < 0f) { why = "NO PRICE"; return false; }
            grant = Mathf.Max(0f, grant);
            float before = hq.factionFunds;
#if !PUBLIC_RELEASE
            int supplyBefore = Supply(hq, group);
#endif
            if (grant > 0f) hq.AddFunds(grant);
            try { hq.AddConvoy(group); }
            catch (Exception error)
            {
                if (grant > 0f) hq.AddFunds(-grant);
                why = "CONVOY REFUSED";
                Plugin.Logger?.LogWarning("[TheaterOps] Reinforcement " + group.Name + " refused: " + error.Message);
                return false;
            }
            hq.AddFunds(-cost);
            lastGroup[hq.GetInstanceID()] = quote.GroupIndex;
            LastPoolBefore = before; LastPoolAfter = hq.factionFunds; LastCost = cost; LastGrant = grant;
            SpentTotal += cost - grant;
            Purchases++;
#if !PUBLIC_RELEASE
            LastSupplyBefore = supplyBefore; LastSupplyAfter = Supply(hq, group);
            LastSupplyUnits = 0;
            foreach (Faction.ConvoyUnit unit in group.Constituents) LastSupplyUnits += unit.Count;
#endif
            Plugin.Logger?.LogInfo("Reinforcement " + group.Name + " funded for " + hq.faction.factionName + " at " + cost.ToString("F0") + " (" + source + ").");
            return true;
        }

        /// <summary>The event beat's door (IConvoyPurchase): the cheapest ready ground group the pool plus the subsidy can pay.</summary>
        public bool TryBuyCheapest(int factionInstanceId, float subsidy, string source, out string group, out int cost, out string why)
        {
            group = ""; cost = 0; why = "";
            FactionHQ hq = null;
            foreach (FactionHQ candidate in FactionRegistry.GetAllHQs())
                if (candidate != null && candidate.GetInstanceID() == factionInstanceId) { hq = candidate; break; }
            List<Faction.ConvoyGroup> groups = hq?.faction?.GetConvoyGroups();
            if (!Authoritative || groups == null) { why = "NO FACTION"; return false; }
            if (hq.preventDonation) { why = WaveText.Reason(WaveBlock.MissionForbids); return false; }
            if (!hq.TryGetNearestDepot(Vector3.zero, float.MaxValue, out VehicleDepot depot) || depot == null || depot.disabled)
            { why = WaveText.Reason(WaveBlock.NoDepot); return false; }
            int count = Mathf.Min(groups.Count, MaximumCombatGroups), pick = -1;
            float available = Mathf.Max(0f, hq.factionFunds) + Mathf.Max(0f, subsidy), best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                GroupRead read = ReadGroup(hq, groups[i], i);
                if (read.Eligible && read.Cooldown <= 0f && float.IsFinite(read.Cost) && read.Cost <= available && read.Cost < best) { best = read.Cost; pick = i; }
            }
            if (pick < 0) { why = WaveText.Reason(WaveBlock.NoGroup); return false; }
            float pool = hq.factionFunds;
            var quote = new WaveQuote(WaveBlock.None, pick, groups[pick].Name, Mathf.RoundToInt(best), (int)Mathf.Floor(pool), 0, false, 0f, 0f);
            if (!Buy(hq, quote, Mathf.Clamp(best - pool, 0f, Mathf.Max(0f, subsidy)), source, out why)) return false;
            group = groups[pick].Name;
            cost = Mathf.RoundToInt(best);
            LivingFrontService.Active?.Desk.NoteConvoy(hq, WaveText.EventConvoy(group, cost));
            return true;
        }

        /// <summary>A group the wave may buy: ground vehicles only (AddConvoy throws for anything else), mostly combat. Cost and cooldown are the native reads; a throw makes it ineligible.</summary>
        private static GroupRead ReadGroup(FactionHQ hq, Faction.ConvoyGroup group, int index)
        {
            try
            {
                if (group == null || string.IsNullOrEmpty(group.Name) || !CombatGroup(group)) return default;
                return new GroupRead(true, group.GetCost(), hq.CmdGetDelaySpawnConvoy((byte)index));
            }
            catch (Exception) { return default; }
        }

        private static bool CombatGroup(Faction.ConvoyGroup group)
        {
            List<Faction.ConvoyUnit> units = group.Constituents;
            if (units == null || units.Count == 0 || units.Count > MaximumGroupUnits) return false;
            int total = 0, combat = 0;
            foreach (Faction.ConvoyUnit unit in units)
            {
                if (unit == null || unit.Type == null || unit.Count <= 0 || unit.Count > 64 || !(unit.Type is VehicleDefinition))
                    return false;
                total += unit.Count;
                RoleIdentity role = unit.Type.roleIdentity;
                // Role weights are 0..1. A token defensive gun should not make a
                // munitions or logistics truck count as a front-line combat unit.
                if (unit.Type.captureStrength > 0f ||
                    role.antiSurface >= CombatRoleThreshold ||
                    role.antiAir >= CombatRoleThreshold ||
                    role.antiRadar >= CombatRoleThreshold ||
                    role.antiMissile >= CombatRoleThreshold)
                    combat += unit.Count;
            }
            return total > 0 && combat * 2 >= total;
        }

#if !PUBLIC_RELEASE
        private static int Supply(FactionHQ hq, Faction.ConvoyGroup group)
        {
            int total = 0;
            foreach (Faction.ConvoyUnit unit in group.Constituents) total += hq.GetUnitSupply(unit.Type);
            return total;
        }
#endif
    }
}

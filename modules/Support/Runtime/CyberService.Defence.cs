using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The other side of CYBER (protocol 42). Every host second each faction gets the list of enemy holds on its own network (read from the holders' desks), a native HQ radio warning
    /// the first time a hold appears, and, when its PURGE lands, every hold it still finds is cut from the holder's desk (the holder hears that too, and its trace jumps).
    /// </summary>
    internal sealed partial class CyberService
    {
        private sealed partial class FactionCyber
        {
            public readonly List<CyberBreach> Found = new List<CyberBreach>(CyberRules.MaxBreaches);
            /// <summary>Holds already announced to this faction: (holder key, the holder's node id).</summary>
            public readonly HashSet<long> Announced = new HashSet<long>();
        }

        private readonly HashSet<long> stillHeld = new HashSet<long>();

        partial void TickDefence(float now)
        {
            foreach (var pair in factions)
            {
                FactionCyber victim = pair.Value;
                Collect(victim);
                victim.Desk.SetBreaches(victim.Found);
                Announce(victim);
                if (victim.Desk.TakePurgeDue(now)) LandPurge(victim);
            }
        }

        /// <summary>Every node another faction holds whose real unit belongs to <paramref name="victim"/>.</summary>
        private void Collect(FactionCyber victim)
        {
            victim.Found.Clear();
            foreach (var other in factions)
            {
                FactionCyber holder = other.Value;
                if (holder == victim) continue;
                foreach (CyberIntrusion x in holder.Desk.Network.Active)
                    foreach (HeldNode h in x.Held)
                    {
                        if (victim.Found.Count >= CyberRules.MaxBreaches) return;
                        if (!TryVisible(holder.Desk, h.NodeId, out CyberNode n) || n.Victim != victim.Key) continue;
                        victim.Found.Add(new CyberBreach(holder.Key, n.Id, n.Kind, n.X, n.Z, h.Since));
                    }
            }
        }

        private void Announce(FactionCyber victim)
        {
            stillHeld.Clear();
            CyberBreach fresh = default;
            bool any = false;
            foreach (CyberBreach b in victim.Found)
            {
                long key = ((long)b.Attacker << 32) | (uint)b.NodeId;
                stillHeld.Add(key);
                if (victim.Announced.Add(key) && !any) { fresh = b; any = true; }
            }
            victim.Announced.IntersectWith(stillHeld);
            if (any) RodGuard.Tell(victim.Owner, "NETWORK BREACH — ENEMY HOLDS OUR " + CyberNetWords.Name(fresh.Kind) + " · PURGE IT FROM THE CYBER FRONT");
        }

        private void LandPurge(FactionCyber victim)
        {
            int cut = 0;
            foreach (CyberBreach b in victim.Found)
                foreach (var other in factions)
                {
                    if (other.Value == victim || other.Value.Key != b.Attacker) continue;
                    int n = other.Value.Desk.Evict(b.NodeId);
                    if (n == 0) continue;
                    cut += n;
                    RodGuard.Tell(other.Key, "NODE PURGED — THE ENEMY CUT OUR HOLD ON THEIR " + CyberNetWords.Name(b.Kind) + " · TRACE +" + (int)CyberRules.PurgeTraceKick + " %");
                }
            RodGuard.Tell(victim.Owner, cut > 0 ? "PURGE COMPLETE — " + cut + " ENEMY HOLD" + (cut == 1 ? "" : "S") + " CUT" : "PURGE COMPLETE — NOTHING LEFT TO CUT");
            Plugin.Logger?.LogInfo("[Support.Cyber] " + victim.Owner.name + " PURGE landed: " + cut + " enemy hold(s) cut.");
        }

        /// <summary>The faction whose key is <paramref name="victimKey"/> has a PURGE running (the holder's node row says so).</summary>
        private bool VictimPurging(int victimKey, float now)
        {
            foreach (var pair in factions) if (pair.Value.Key == victimKey) return pair.Value.Desk.PurgeEndsAt > now;
            return false;
        }

        /// <summary>Another faction with a CYBER desk (the sims' enemy).</summary>
        internal FactionHQ EnemyOf(FactionHQ me)
        {
            foreach (var pair in factions) if (pair.Key != me && pair.Key != null) return pair.Key;
            return null;
        }

        /// <summary>The faction's node list is rebuilt on its next host second instead of up to 10 s later (the sims' sighting step).</summary>
        internal void RefreshNodesSoon(FactionHQ owner)
        {
            if (owner != null && factions.TryGetValue(owner, out FactionCyber f)) f.NextNodeRefresh = 0f;
        }

        private static bool TryVisible(CyberDesk desk, int id, out CyberNode node)
        {
            IReadOnlyList<CyberNode> v = desk.Visible;
            for (int i = 0; i < v.Count; i++) if (v[i].Id == id) { node = v[i]; return true; }
            node = default;
            return false;
        }
    }
}

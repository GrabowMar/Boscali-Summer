using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The other side of SOF (protocol 42). Every host second each faction gets the enemy teams it can detect: within 3 km of one of its own sites (camps, airbases, uplinks, EW trucks and
    /// data centers) or already in contact with its forces (exposure 50 % or more). A new one is announced over the native HQ radio; a landing SWEEP hunts every detected team it still finds
    /// and tells the team's own faction.
    /// </summary>
    internal sealed partial class SofService
    {
        private const float SiteRefreshSeconds = 10f;

        private sealed partial class FactionSof
        {
            public readonly List<SofHostile> Found = new List<SofHostile>(SofRules.MaxHostiles);
            /// <summary>When each detected hostile (attacker key * 8 ^ slot) was first seen near our sites.</summary>
            public readonly Dictionary<int, float> Seen = new Dictionary<int, float>();
            public bool Fresh;
            public readonly List<Vector2> Sites = new List<Vector2>(24);
            public float NextSites;
        }

        private readonly HashSet<int> stillNear = new HashSet<int>();
        private readonly List<int> gone = new List<int>(8);

        partial void TickDefence(float now)
        {
            foreach (var pair in factions)
            {
                FactionSof victim = pair.Value;
                if (now >= victim.NextSites) { victim.NextSites = now + SiteRefreshSeconds; CollectSites(victim); }
                Detect(victim, now);
                victim.Desk.SetHostiles(victim.Found);
                Announce(victim);
                if (victim.Desk.TakeSweepDue(now)) LandSweep(victim, now);
            }
        }

        /// <summary>Where this faction's own sites stand: camps, airbases it holds, its uplinks, EW trucks and data centers.</summary>
        private void CollectSites(FactionSof f)
        {
            f.Sites.Clear();
            for (int i = 0; i < f.Camps.Count; i++) if (f.Camps.TryPosition(i, out float x, out float z)) f.Sites.Add(new Vector2(x, z));
            if (FactionRegistry.airbaseLookup != null)
            {
                int seen = 0;
                foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
                {
                    if (++seen > 128 || f.Sites.Count >= 20) break;
                    if (a == null || a.disabled || a.AttachedAirbase || a.CurrentHQ != f.Owner || a.center == null) continue;
                    GlobalPosition p = a.center.position.ToGlobalPosition();
                    f.Sites.Add(new Vector2((float)p.x, (float)p.z));
                }
            }
            if (space != null)
                foreach (Unit u in space.UplinksFor(f.Owner))
                    if (u != null && !u.disabled && f.Sites.Count < 24) { GlobalPosition p = u.transform.position.ToGlobalPosition(); f.Sites.Add(new Vector2((float)p.x, (float)p.z)); }
            if (cyber != null && cyber.TryDesk(f.Owner, out CyberDesk desk))
                for (int k = 0; k < 2; k++)
                    for (int i = 0; i < desk.Anchors.Count((AnchorKind)k); i++)
                        if (f.Sites.Count < 24 && desk.Anchors.TryPosition((AnchorKind)k, i, out float x, out float z)) f.Sites.Add(new Vector2(x, z));
        }

        private void Detect(FactionSof victim, float now)
        {
            victim.Found.Clear();
            stillNear.Clear();
            victim.Fresh = false;
            foreach (var pair in factions)
            {
                FactionSof foe = pair.Value;
                if (foe == victim) continue;
                foreach (SofTeam t in foe.Desk.Teams)
                {
                    if (!t.Active || t.Carried || (t.State != TeamState.Moving && t.State != TeamState.OnSite && t.State != TeamState.Pinned && t.State != TeamState.Returning)) continue;
                    if (victim.Found.Count >= SofRules.MaxHostiles) break;
                    if (t.Exposure < SofRules.DetectExposure && !NearSite(victim, t.X, t.Z)) continue;
                    int key = (foe.Key * 8) ^ t.Slot;
                    stillNear.Add(key);
                    if (!victim.Seen.TryGetValue(key, out float since)) { since = now; victim.Seen[key] = now; victim.Fresh = true; }
                    victim.Found.Add(new SofHostile(foe.Key, t.Slot, t.X, t.Z, t.HuntedUntil > now, since));
                }
            }
        }

        private static bool NearSite(FactionSof f, float x, float z)
        {
            for (int i = 0; i < f.Sites.Count; i++) if (SofRules.Distance(x, z, f.Sites[i].x, f.Sites[i].y) <= SofRules.DetectMetres) return true;
            return false;
        }

        private void Announce(FactionSof victim)
        {
            gone.Clear();
            foreach (int key in victim.Seen.Keys) if (!stillNear.Contains(key)) gone.Add(key);
            foreach (int key in gone) victim.Seen.Remove(key);
            if (victim.Fresh) RodGuard.Tell(victim.Owner, "HOSTILE SOF TEAM DETECTED NEAR OUR SITES · SWEEP IT FROM THE SOF FRONT");
        }

        private void LandSweep(FactionSof victim, float now)
        {
            int hunted = 0;
            foreach (SofHostile h in victim.Found)
                foreach (var pair in factions)
                {
                    if (pair.Value == victim || pair.Value.Key != h.Attacker || !pair.Value.Desk.Hunt(h.Slot, now + SofRules.HuntSeconds)) continue;
                    hunted++;
                    RodGuard.Tell(pair.Key, "ENEMY QRF ON " + SofRules.Callsign(h.Slot) + " · EXPOSURE CLIMBING · PUSH, EXFIL OR CALL COVER");
                }
            RodGuard.Tell(victim.Owner, hunted > 0 ? "QRF IN CONTACT — HUNTING " + hunted + " HOSTILE TEAM" + (hunted == 1 ? "" : "S") : "QRF FOUND NOTHING — THE HOSTILES SLIPPED AWAY");
            Plugin.Logger?.LogInfo("[Support.Sof] " + victim.Owner.name + " SWEEP landed: " + hunted + " hostile team(s) hunted.");
        }
    }
}

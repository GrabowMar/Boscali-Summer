using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The player-helicopter lift (spec 2.2), the cover kills and the claims of the SOF posts. A lift needs a real player helicopter (an aircraft carrying
    /// a <c>RotorShaft</c>) that is landed (<c>Aircraft.IsLanded()</c>, or at rest on the ground by <c>Unit.radarAlt</c> and <c>Unit.speed</c>); the pure desk owns the
    /// 150 m / 300 m circles, the 10 s dwell, the ride and the pay. AI never lifts: only human players' aircraft are sampled.
    /// </summary>
    internal sealed partial class SofService
    {
        private readonly Dictionary<int, bool> heliCache = new Dictionary<int, bool>(16);
        private readonly List<Player> playerScratch = new List<Player>(16);

        partial void TickLift(FactionSof f, float now)
        {
            bool work = false;
            for (int i = 0; i < f.Desk.Teams.Length && !work; i++) work = f.Desk.Teams[i].Active && (f.Desk.Teams[i].LiftWaiting || f.Desk.Teams[i].Carried);
            if (!work) return;
            List<Player> players = f.Owner.GetPlayers(false);
            if (players == null) return;
            playerScratch.Clear();
            for (int i = 0; i < players.Count && playerScratch.Count < 16; i++) playerScratch.Add(players[i]); // GetPlayers hands out a shared list
            f.Desk.BeginHeliPass();
            foreach (Player player in playerScratch)
            {
                if (player == null) continue;
                ulong id = PlayerIdentity.Of(player);
                Aircraft aircraft = player.Aircraft;
                if (id == PlayerIdentity.None || aircraft == null || aircraft.disabled || !IsHelicopter(aircraft)) continue;
                int heli = unchecked((int)aircraft.persistentID.Id);
                if (heli == 0) continue;
                GlobalPosition p = aircraft.transform.position.ToGlobalPosition();
                f.Desk.NoteHeli(heli, id, (float)p.x, (float)p.z, Landed(aircraft));
            }
            f.Desk.EndHeliPass();
        }

        private bool IsHelicopter(Aircraft aircraft)
        {
            int id = aircraft.GetInstanceID();
            if (heliCache.TryGetValue(id, out bool known)) return known;
            if (heliCache.Count > 64) heliCache.Clear();
            bool heli = aircraft.GetComponentInChildren<RotorShaft>(true) != null;
            heliCache[id] = heli;
            return heli;
        }

        private static bool Landed(Aircraft aircraft) => aircraft.IsLanded() || (aircraft.radarAlt < 3f && Mathf.Abs(aircraft.speed) < 1.5f);

        // ---- Cover ---------------------------------------------------------------------------------------

        /// <summary>An enemy ground unit died: every faction's pinned team within 2 km of it is relieved and its lost-timer extended (answering a COVER post is killing what is on the team).</summary>
        internal void NoteKill(Unit target)
        {
            if (!GameAccess.IsServer() || factions.Count == 0 || target == null || target.NetworkHQ == null || !(target is GroundVehicle)) return;
            GlobalPosition p = target.transform.position.ToGlobalPosition();
            foreach (var pair in factions)
            {
                if (pair.Key == target.NetworkHQ) continue;
                if (pair.Value.Desk.CoverKill((float)p.x, (float)p.z) > 0)
                    Plugin.Logger?.LogInfo("[Support.Sof] " + pair.Key.name + " cover kill near a pinned team.");
            }
        }

        /// <summary>A claimed SOF post fires: COVER extends the pinned team's lost-timer and pays the pilot when the team breaks free; LASE acknowledges a team that is still lasing.</summary>
        internal bool FirePost(FactionHQ owner, TaskedLaunchJob job)
        {
            if (!factions.TryGetValue(owner, out FactionSof f) || job.Call.MarkCount == 0) return false;
            int slot = job.Call.MarkAt(0).Id - 1;
            if (slot < 0 || slot >= SofRules.MaxTeams) return false;
            if (job.Action == SupportActionId.SofCover) return f.Desk.CoverClaimed(slot, job.Pilot);
            return f.Desk.IsLasing(slot);
        }
    }
}

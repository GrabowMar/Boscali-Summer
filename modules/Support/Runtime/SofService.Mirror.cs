using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The faction-only view and the host verbs of <see cref="SofService"/>. Everything a client may learn is derived here from the host's own desk, never read from a client.</summary>
    internal sealed partial class SofService
    {
        /// <summary>
        /// Fills the viewer's faction view (always the full state). False when the faction has no SOF (<c>Active</c> stays false). Own teams, camps, revealed targets,
        /// held buildings and, only where this faction's SPACE bird is looking (the Overwatch window), an enemy team's position.
        /// </summary>
        internal bool FillState(Player viewer, SofStateData into)
        {
            into.Active = false; into.TeamCap = 0; into.TapIntrusions = 0; into.TapUntil = 0f;
            into.Camps.Clear(); into.Teams.Clear(); into.Targets.Clear(); into.Held.Clear(); into.Enemies.Clear(); into.Events.Clear();
            if (viewer == null || viewer.HQ == null || manager?.Settings == null || !manager.Settings.SofEnabled.Value || !factions.TryGetValue(viewer.HQ, out FactionSof f)) return false;
            float now = SupportManager.MissionNow();
            SofDesk desk = f.Desk;
            into.Active = true;
            into.TeamCap = (byte)Math.Min(SofRules.MaxTeams, SofRules.TeamCap(manager.HumanCount(viewer.HQ), f.FobUntil > now));
            foreach (CampSlot slot in f.Slots)
            {
                if (into.Camps.Count >= SofWire.MaxCamps || !f.Camps.TryPosition(slot.Index, out float x, out float z)) continue;
                AnchorHealth health = f.Camps.Health(slot.Index);
                into.Camps.Add(new SofCampRow { Health = health, X = x, Z = z, Rebuild = health == AnchorHealth.Down ? (byte)Mathf.Clamp(Mathf.RoundToInt(slot.Bar.Fraction * 100f), 0, 100) : (byte)0 });
            }
            foreach (SofTeam t in desk.Teams)
            {
                if (!t.Active || into.Teams.Count >= SofWire.MaxTeams) continue;
                float ends = t.State == TeamState.Raising ? t.RaiseEndsAt : t.State == TeamState.OnSite ? t.OnSiteEnd : t.State == TeamState.Recovering ? t.RecoverUntil :
                    t.State == TeamState.Pinned ? Math.Max(t.PinDeadline, t.CoverUntil) : 0f;
                into.Teams.Add(new SofTeamRow
                {
                    Slot = (byte)t.Slot, State = t.State, Insert = t.Insert, Push = t.PushOn, Hold = t.HoldOn, Wounded = t.Wounded, LiftWaiting = t.LiftWaiting, Carried = t.Carried,
                    Lasing = t.LaseActive, HasDest = t.HasDest, Exploit = t.Exploit, X = t.X, Z = t.Z, DestX = t.DestX, DestZ = t.DestZ, TargetX = t.TargetX, TargetZ = t.TargetZ,
                    Exposure = (byte)Mathf.Clamp(Mathf.RoundToInt(t.Exposure), 0, 100), Ammo = (byte)Mathf.Clamp(Mathf.RoundToInt(t.Ammo), 0, 100),
                    Odds = (byte)Mathf.Clamp(t.Odds, 0, 100), Mission = t.Mission, TargetId = t.TargetId, EndsAt = ends
                });
            }
            foreach (SofTarget n in desk.Visible)
            {
                if (into.Targets.Count >= SofWire.MaxTargets) break;
                into.Targets.Add(new SofTargetRow { Id = n.Id, Kind = n.Kind, Sub = n.Sub, X = n.X, Z = n.Z, Exploit = SofRules.Exploit(n.Kind, n.Sub), Resisted = SofRules.Resisted(n.Kind, n.Sub) });
            }
            foreach (HeldBuilding h in desk.Held)
            {
                if (into.Held.Count >= SofWire.MaxHeld) break;
                into.Held.Add(new SofHeldRow { Id = h.Id, X = h.X, Z = h.Z, Until = h.Until });
            }
            FillEnemies(viewer.HQ, now, into);
            if (f.TapUntil > now)
            {
                into.TapUntil = f.TapUntil;
                into.TapIntrusions = (byte)Math.Min(255, cyber != null ? cyber.EnemyIntrusionCount(viewer.HQ) : 0);
            }
            for (int i = 0; i < f.Ring.Count && into.Events.Count < SofWire.MaxEvents; i++)
            {
                SofEvent e = f.Ring[i].Value;
                into.Events.Add(new SofEventRow { Seq = f.Ring[i].Key, Kind = e.Kind, Slot = (byte)e.Slot, Mission = e.Mission });
            }
            return true;
        }

        /// <summary>Another faction's team is listed only where the viewer's own SPACE bird is looking (a live reveal window covers it), and only as a position.</summary>
        private void FillEnemies(FactionHQ viewer, float now, SofStateData into)
        {
            SpaceObservations obs = space != null ? space.ObservationsFor(viewer) : null;
            if (obs == null) return;
            foreach (var pair in factions)
            {
                if (pair.Key == viewer) continue;
                foreach (SofTeam t in pair.Value.Desk.Teams)
                {
                    if (!t.Active || t.Carried || (t.State != TeamState.Moving && t.State != TeamState.OnSite && t.State != TeamState.Pinned && t.State != TeamState.Returning)) continue;
                    if (into.Enemies.Count >= SofWire.MaxEnemies) return;
                    if (obs.Covers(t.X, t.Z, now)) into.Enemies.Add(new SofEnemyRow { X = t.X, Z = t.Z });
                }
            }
        }

        /// <summary>
        /// RAISE, ORDER, MISSION or DIVERT for one player. Faction and identity come from the transport-authenticated player, never from the message; the desk
        /// answers the same NO TARGET for an unknown, hidden or foreign target id.
        /// </summary>
        internal SofResult Verb(Player player, in SpaceCommand command)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null || manager?.Settings == null || !manager.Settings.Enabled.Value ||
                !manager.Settings.SofEnabled.Value || !factions.TryGetValue(player.HQ, out FactionSof f)) return new SofResult(SofOutcome.Unavailable);
            ulong op = PlayerIdentity.Of(player);
            if (op == PlayerIdentity.None) return new SofResult(SofOutcome.Unavailable);
            switch (command.Kind)
            {
                case SpaceCommandKind.SofRaise:
                    return f.Desk.Raise(op);
                case SpaceCommandKind.SofOrder:
                    return SofCodes.TryUnpackOrder(command.Target, out int slot, out TeamVerb verb) ? f.Desk.Order(op, slot, verb) : new SofResult(SofOutcome.BadOrder);
                case SpaceCommandKind.SofMission:
                    if (command.Ids == null || command.Ids.Length != 2 || !SofCodes.TryUnpackMission(command.Ids[0], out int mslot, out MissionKind kind)) return new SofResult(SofOutcome.NoTarget);
                    if (kind == MissionKind.Recon)
                        return SofCodes.TryUnpackPoint(command.Ids[1], out float px, out float pz) ? f.Desk.Send(op, mslot, kind, 0, px, pz) : new SofResult(SofOutcome.OutOfTheater);
                    return f.Desk.Send(op, mslot, kind, command.Ids[1], 0f, 0f);
                case SpaceCommandKind.SofDivert:
                    if (command.Ids == null || command.Ids.Length != 2 || command.Ids[0] < 0 || command.Ids[0] >= SofRules.MaxTeams) return new SofResult(SofOutcome.NoTeam);
                    return SofCodes.TryUnpackPoint(command.Ids[1], out float dx, out float dz) ? f.Desk.Divert(op, command.Ids[0], dx, dz) : new SofResult(SofOutcome.OutOfTheater);
                default:
                    return new SofResult(SofOutcome.Unavailable);
            }
        }
    }
}

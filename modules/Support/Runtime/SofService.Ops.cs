using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The seams OPERATIONS reads and writes on SOF: held buildings as FOB targets, and the FORWARD OPERATING BASE itself. A FOB is a held building made permanent for 20 minutes: its team
    /// cap (+1) and the raise point live in the desk, the rearm and refuel points are real vehicles with a vanilla <c>Rearmer</c> / <c>Refueler</c> parked beside it when the encyclopedia allows them. A field spawn
    /// point for aircraft is NOT built: vanilla spawns only at airbases and the mod must not invent one (see the plan).
    /// </summary>
    internal sealed partial class SofService
    {
        private const float FobSeconds = OpsRules.FobSeconds;

        private sealed partial class FactionSof
        {
            public float FobUntil;
            public int FobHeldId;
            public Unit FobSupply, FobFuel;
        }

        /// <summary>Raised after every SOF event of a faction.</summary>
        internal event Action<FactionHQ, SofEvent> Observed;

        partial void TickFob(FactionSof f, float now)
        {
            if (f.FobUntil <= 0f) return;
            bool held = false;
            foreach (HeldBuilding h in f.Desk.Held) if (h.Id == f.FobHeldId) { held = true; break; }
            if (now >= f.FobUntil || !held) EndFob(f, held ? "expired" : "the building was retaken or destroyed");
        }

        /// <summary>A held building the faction can see as a FOB target: its id, position. Unknown ids answer false.</summary>
        internal bool TryHeldTarget(FactionHQ owner, int heldId, out OpTarget target)
        {
            target = default;
            if (owner == null || !factions.TryGetValue(owner, out FactionSof f)) return false;
            foreach (HeldBuilding h in f.Desk.Held)
                if (h.Id == heldId) { target = new OpTarget(h.Id, h.X, h.Z, 0); return true; }
            return false;
        }

        internal bool HeldAlive(FactionHQ owner, int heldId) => TryHeldTarget(owner, heldId, out _);

        internal bool FobUp(FactionHQ owner) => owner != null && factions.TryGetValue(owner, out FactionSof f) && f.FobUntil > SupportManager.MissionNow();

        /// <summary>
        /// The FOB goes live on a held building: 20 minutes (renewing adds 20 more), the team cap and raise point, and a rearm and a fuel vehicle beside it when they exist.
        /// Returns the mission second the FOB ends. False when the building is no longer held.
        /// </summary>
        internal bool StartFob(FactionHQ owner, int heldId, out float until)
        {
            until = 0f;
            if (owner == null || !factions.TryGetValue(owner, out FactionSof f) || !TryHeldTarget(owner, heldId, out OpTarget spot)) return false;
            float now = SupportManager.MissionNow();
            until = Mathf.Max(f.FobUntil, now) + FobSeconds;
            if (f.FobUntil > now && f.FobHeldId != heldId) DiscardFobVehicles(f); // a renewal on another building: the old vehicles go, new ones are placed there
            f.FobUntil = until; f.FobHeldId = heldId;
            f.Desk.FobActive = true; f.Desk.FobX = spot.X; f.Desk.FobZ = spot.Z;
            f.Desk.ExtendHeld(heldId, until);
            if (SupportTargeting.TryMapPoint(new GlobalPosition(spot.X, 0f, spot.Z), out Vector3 ground))
            {
                GlobalPosition at = ground.ToGlobalPosition();
                f.FobSupply = EnsureFobVehicle(owner, f.FobSupply, at, false, 0);
                f.FobFuel = EnsureFobVehicle(owner, f.FobFuel, at, true, 1);
            }
            Plugin.Logger?.LogInfo("[Support.Sof] " + owner.name + " FOB up on held building " + heldId + " until " + (int)until + " s, rearm=" + (f.FobSupply != null ? "yes" : "no") + ", refuel=" + (f.FobFuel != null ? "yes" : "no") + ".");
            return true;
        }

        /// <summary>Keeps one FOB vehicle standing: a living one stays, a dead or missing one is replaced when the encyclopedia allows it.</summary>
        private Unit EnsureFobVehicle(FactionHQ owner, Unit current, GlobalPosition at, bool fuel, int ordinal)
        {
            if (current != null && !current.disabled) return current;
            if (!ReferenceEquals(current, null)) spawner.DiscardGroup(current); // a destroyed truck still owns its spawner slot
            return spawner.TryCreateFobSupply(owner, ordinal, at, fuel, out Unit made) ? made : null;
        }

        private void DiscardFobVehicles(FactionSof f)
        {
            if (!ReferenceEquals(f.FobSupply, null)) spawner.DiscardGroup(f.FobSupply);
            if (!ReferenceEquals(f.FobFuel, null)) spawner.DiscardGroup(f.FobFuel);
            f.FobSupply = null; f.FobFuel = null;
        }

        private void EndFob(FactionSof f, string why)
        {
            f.FobUntil = 0f; f.FobHeldId = 0;
            f.Desk.FobActive = false;
            DiscardFobVehicles(f);
            Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " FOB ended: " + why + ".");
        }
    }
}

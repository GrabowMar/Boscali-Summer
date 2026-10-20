using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The host seams the new OPS FRONTS perks and the anchor rebuilds go through. The perk starters hand the effect to the CYBER and SOF services; the
    /// <c>*RebuildFunding</c> methods are the single hook per anchor kind that S1 replaces with the front budget (S0: a plain timer).
    /// </summary>
    internal sealed partial class SupportManager
    {
        bool ISupportHost.StartCyberPerk(FactionHQ owner, SupportActionId package, GlobalPosition target, float power, float durationBonus) =>
            cyber != null && cyber.StartPerkEffect(owner, package, target, power, durationBonus);

        SupportResult ISupportHost.StartSofRecon(FactionHQ owner, GlobalPosition target, float power) =>
            sof != null ? sof.PerkRecon(owner, target, power) : SupportResult.NoCamp;

        SupportResult ISupportHost.StartSofSabotage(FactionHQ owner, GlobalPosition target, float power, out bool succeeded)
        {
            succeeded = false;
            return sof != null ? sof.PerkSabotage(owner, target, power, out succeeded) : SupportResult.NoCamp;
        }

        FrontSides ISupportHost.Facts(FactionHQ owner) => readiness.Facts(owner);

        /// <summary>The faction's own satellites that are up and whose footprint holds the point (the SAT CAMERA's room bonus counts the ones besides its own).</summary>
        int ISupportHost.BirdsCovering(FactionHQ owner, GlobalPosition at)
        {
            if (!TryGetSpaceStateCoarse(owner, out SpaceState state)) return 0;
            Vector2 span = TheaterFrame.Resolve();
            float now = MissionNow(), u = GeoSpace.U((float)at.x, span.x), v = GeoSpace.V((float)at.z, span.y);
            int covering = 0;
            for (int i = 0; i < SpaceRules.BirdCount; i++) if (state.Covers((BirdKind)i, now, u, v)) covering++;
            return covering;
        }

        SupportResult ISupportHost.StartOverwatch(FactionHQ owner, GlobalPosition at, float radius, int units, float seconds, out int followed)
        {
            followed = 0;
            return space != null ? space.Overwatch(owner, at, radius, units, seconds, out followed) : SupportResult.CapabilityUnavailable;
        }

#if !PUBLIC_RELEASE
        /// <summary>TEST ONLY (<see cref="OpsAutomation"/>): the units OVERWATCH follows for <paramref name="owner"/> (host).</summary>
        internal void CopyFollowed(FactionHQ owner, System.Collections.Generic.List<Unit> into) => space?.ObservationsFor(owner)?.CopyFollowed(into);
#endif

        SupportResult ISupportHost.StartSiphon(FactionHQ owner, float power, out int taken)
        {
            taken = 0;
            return frontService != null ? frontService.Siphon(owner, power, out taken) : SupportResult.CapabilityUnavailable;
        }

        SupportResult ISupportHost.StartDirectAction(Player pilot, GlobalPosition at, float power) =>
            sof != null ? sof.PerkDirectAction(pilot, at, power) : SupportResult.NoReadyTeam;

        // ---- Rebuild funding seams (S1 hooks the front budget here) ----------------------------------------------------

        /// <summary>
        /// Bar progress an EW truck's rebuild gains over <paramref name="seconds"/>. With the FRONTS running the bar does not move on a timer: the auto-queued EW TRUCK programme fills it when it
        /// completes. The plain timer stays only as the slow fallback when no programme could be queued (a full queue) or no FrontService runs.
        /// </summary>
        internal float EwTruckRebuildFunding(FactionHQ owner, float seconds) => RebuildFunding(owner, Domain.Fronts.ProgrammeId.EwTruck, seconds);

        /// <summary>Bar progress a data center's rebuild gains over <paramref name="seconds"/> (see <see cref="EwTruckRebuildFunding"/>).</summary>
        internal float DataCenterRebuildFunding(FactionHQ owner, float seconds) => RebuildFunding(owner, Domain.Fronts.ProgrammeId.DataCenter, seconds);

        /// <summary>Bar progress a SOF camp's rebuild gains over <paramref name="seconds"/> (see <see cref="EwTruckRebuildFunding"/>).</summary>
        internal float CampRebuildFunding(FactionHQ owner, float seconds) => RebuildFunding(owner, Domain.Fronts.ProgrammeId.Camp, seconds);

        /// <summary>Bar progress a dead satellite's rebuild gains over <paramref name="seconds"/> (see <see cref="EwTruckRebuildFunding"/>).</summary>
        internal float BirdRebuildFunding(FactionHQ owner, float seconds) => RebuildFunding(owner, Domain.Fronts.ProgrammeId.LaunchSatellite, seconds);

        private float RebuildFunding(FactionHQ owner, Domain.Fronts.ProgrammeId programme, float seconds) =>
            frontService != null && frontService.Running && frontService.HasProgramme(owner, programme) ? 0f : AnchorRules.TimerProgress(seconds);
    }
}

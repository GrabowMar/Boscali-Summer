using BoscaliSummer.Modules.Support.Domain.Cyber;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The host seams the new OPS FRONTS perks and the anchor rebuilds go through. The perk starters hand the effect to the CYBER and SOF services; the
    /// <c>*RebuildFunding</c> methods are the single hook per anchor kind that S1 replaces with the front budget (S0: a plain timer).
    /// </summary>
    internal sealed partial class SupportManager
    {
        bool ISupportHost.StartCyberPerk(FactionHQ owner, SupportActionId package, GlobalPosition target, float quality) =>
            cyber != null && cyber.StartPerkEffect(owner, package, target, quality);

        SupportResult ISupportHost.StartSofRecon(FactionHQ owner, GlobalPosition target, float quality) =>
            sof != null ? sof.PerkRecon(owner, target, quality) : SupportResult.NoCamp;

        SupportResult ISupportHost.StartSofSabotage(FactionHQ owner, GlobalPosition target) =>
            sof != null ? sof.PerkSabotage(owner, target) : SupportResult.NoCamp;

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

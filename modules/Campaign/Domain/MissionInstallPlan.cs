namespace BoscaliSummer.Features.Campaign.Domain
{
    /// <summary>What the installer should do with the campaign mission file.</summary>
    internal enum MissionInstallAction
    {
        /// <summary>No mission file: write the shipped one.</summary>
        Install = 0,

        /// <summary>The mod's own older copy: refresh it.</summary>
        Update = 1,

        /// <summary>Already at the shipped revision: leave it alone.</summary>
        Skip = 2,

        /// <summary>A mission of the same name with no mod marker: someone else's file.</summary>
        Foreign = 3,
    }

    /// <summary>
    /// The whole overwrite policy, as pure arithmetic, so it is testable without a disk.
    /// </summary>
    internal static class MissionInstallPlan
    {
        /// <summary>Bump when the shipped mission JSON changes so existing installs refresh.</summary>
        public const int Revision = 1;

        public static MissionInstallAction Decide(bool missionExists, bool markerExists, int markerRevision)
        {
            if (!missionExists) return MissionInstallAction.Install;
            if (!markerExists) return MissionInstallAction.Foreign;
            return markerRevision < Revision ? MissionInstallAction.Update : MissionInstallAction.Skip;
        }
    }
}

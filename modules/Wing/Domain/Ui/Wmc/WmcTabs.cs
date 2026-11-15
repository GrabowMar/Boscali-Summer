namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The bezel's tabs (spec bezel v2 §2): the flying tabs, a group rule, then the logistics tabs.</summary>
    internal static class WmcTabs
    {
        public const int Roster = 0, Behaviour = 1, Supply = 2;

        /// <summary>Panels fold 2026-11-14: ROSTER (was TACTICAL) holds the wing table and ORDERS · FORMATION · ROUTE·AP; SUPPLY ends
        /// with the loadout editor and MY AIRCRAFT (was LOADOUT, 2026-11-15); the old WING tab (pilots, studio) is PILOT STUDIO's SQUADRON.</summary>
        public static readonly string[] Labels = { "ROSTER", "BEHAVIOUR", "SUPPLY" };
    }
}

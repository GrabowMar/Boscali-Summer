namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The bezel's tabs (spec bezel v2 §2): the flying tabs, a group rule, then the logistics tabs.</summary>
    internal static class WmcTabs
    {
        public const int Tactical = 0, Behaviour = 1, Supply = 2, Loadout = 3, Wing = 4;

        /// <summary>Spec FUI §tabs (the user, 2026-09-28): the 0.9 tabs plus BEHAVIOUR. TACTICAL holds ORDERS · FORMATION · ROUTE·AP;
        /// BEHAVIOUR holds STANCES · SORTIE; WING holds the roster with INSPECT, and the STUDIO.</summary>
        public static readonly string[] Labels = { "TACTICAL", "BEHAVIOUR", "SUPPLY", "LOADOUT", "WING" };
    }
}

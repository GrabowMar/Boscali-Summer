using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>The bezel's tabs (spec bezel v2 §2): the flying tabs, a group rule, then the logistics tabs. Each control id's prefix
    /// names its tab, so a press from automation shows the page it lives on.</summary>
    internal static class WmcTabs
    {
        public const int Tactical = 0, Behaviour = 1, Supply = 2, Loadout = 3, Wing = 4;
        /// <summary>1.0 called the WING tab SQUADRON.</summary>
        public const int Squadron = Wing;

        /// <summary>Spec FUI §tabs (the user, 2026-09-28): the 0.9 tabs plus BEHAVIOUR. TACTICAL holds ORDERS · FORMATION · ROUTE·AP;
        /// BEHAVIOUR holds TUNING · PLAN · TIMELINE · LOG; WING holds the roster with INSPECT, and the STUDIO.</summary>
        public static readonly string[] Labels = { "TACTICAL", "BEHAVIOUR", "SUPPLY", "LOADOUT", "WING" };
    }
}

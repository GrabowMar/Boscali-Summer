using System;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
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

        private static readonly string[][] prefixes =
        {
            // ROUTE·AP kept its plan.* ids when it moved to TACTICAL; they are matched before BEHAVIOUR's plan.*.
            new[] { "tac.", "form.", "plan.route.", "plan.ap.", "plan.scope." }, new[] { "opt.", "plan." }, new[] { "sup." }, new[] { "lo." },
            new[] { "wing.", "sq.", "insp." },
        };

        /// <summary>A tab by its label (any case; older names open the tab that holds them now); −1 when there is none. Numbers are
        /// refused: the 4-tab numbers meant other tabs (critic §14.9), so a scenario that still uses one fails instead of opening the
        /// wrong page.</summary>
        public static int Index(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (Is(name, "SQUADRON") || Is(name, "INSPECT")) return Wing;
            if (Is(name, "FORM") || Is(name, "FORMATION") || Is(name, "ROUTE") || Is(name, "ORDERS")) return Tactical;
            if (Is(name, "PLAN") || Is(name, "TUNING") || Is(name, "OPTIONS") || Is(name, "STANCES") || Is(name, "SORTIE") || Is(name, "RECORD")) return Behaviour;
            if (Is(name, "ROSTER") || Is(name, "AIRCRAFT") || Is(name, "STUDIO")) return Wing;
            for (int i = 0; i < Labels.Length; i++)
                if (string.Equals(name, Labels[i], StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>The tab a control id belongs to; −1 for the header's own controls and unknown ids.</summary>
        public static int Of(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < prefixes.Length; i++)
                foreach (string p in prefixes[i])
                    if (id.StartsWith(p, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // LOADOUT for Dev/Automation.Wmc (F14: picks without an id of their own get arguments) and its report.
    internal sealed partial class WmcLoadout
    {
        /// <summary>"next", "prev", an index into the faction's list or a label.</summary>
        public bool Livery(string arg)
        {
            if (airframe == null) return false;
            RefreshLivery();
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            int to = Same(arg, "next") ? LiveryChoice.Step(i, liveries.Count, 1) : Same(arg, "prev") ? LiveryChoice.Step(i, liveries.Count, -1) : -1;
            if (to < 0 && int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) to = index;
            for (int k = 0; to < 0 && k < liveries.Count; k++)
                if (Same(liveries[k].Label, arg)) to = k;
            if (to < 0 || to >= liveries.Count) return false;
            WingLoadoutTemplates.SetLivery(airframe, liveryTokens[to]);
            return true;
        }

        private static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

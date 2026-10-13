using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SUPPLY for Dev/Automation.Wmc (F14: picks without an id of their own get arguments) and its report.
    internal sealed partial class WmcSupply
    {
        /// <summary>Selects a listed airframe by jsonKey, unit name or code and shows its page; false when none is listed.</summary>
        public bool Pick(string airframe)
        {
            List<AircraftDefinition> cat = WingRequisition.Catalogue;
            for (int i = 0; i < cat.Count; i++)
            {
                AircraftDefinition d = cat[i];
                if (d == null || !(Same(d.jsonKey, airframe) || Same(d.unitName, airframe) || Same(d.code, airframe))) continue;
                WingRequisition.Selected = d;
                tilePage = Pages.Of(i, tiles.PerPage);
                returnGate = new ConfirmGate();
                return true;
            }
            return false;
        }

        private static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

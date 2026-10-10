using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook for the C-key menu (called from tests/ingame/missile-hud.json); inert outside a sim run.
    /// Reports one category's rows exactly as the menu builds them from the live game right now.
    /// </summary>
    public static class AceRadialAutomation
    {
        /// <summary>Args: <c>category</c> (default <c>view</c>). Returns ok, category_found, available, badge and one entry per row.</summary>
        public static Dictionary<string, object> Menu(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("AceRadialAutomation", "Menu", "requires a nomodkit scenario");
            string id = Text(args, "category") ?? "view";
            MenuModel model;
            try { model = MenuGame.Build(); }
            catch (Exception e) { return Failure("AceRadialAutomation", "Menu", "catalog threw: " + e.Message); }
            MenuCategory category = model.Category(id);
            var entries = new List<object>();
            if (category != null)
                foreach (MenuRow row in category.Rows)
                    entries.Add(new Dictionary<string, object>
                    {
                        { "id", row.Id },
                        { "label", row.Label },
                        { "state", row.State },
                        { "blocked", row.IsBlocked },
                        { "hold", row.NeedsHold },
                    });
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "category_found", category != null },
                { "available", category != null && category.Available },
                { "badge", category?.Badge ?? "" },
                { "entries", entries },
            };
            Debug.Log("[AceRadialAutomation] Menu: " + id + " rows=" + entries.Count);
            return result;
        }
    }
}

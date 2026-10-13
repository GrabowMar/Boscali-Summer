#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Autopilot.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// The ACE radial menu's half of the nomodkit single-player test hook
    /// (tests/ingame/missile-hud.json); inert outside a sim run. Reports the C-menu
    /// missile entries exactly as the menu publishes them. It lives here, not beside
    /// HudAutomation, because the catalog it reads belongs to this module.
    /// </summary>
    public static class AutopilotAutomation
    {
        private static bool Guard(out Dictionary<string, object> failure)
        {
            failure = null;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                failure = Failure("AutopilotAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            return failure == null;
        }

        /// <summary>C-menu missile entries: visibility/enabled/status of the four VIEW leaves.</summary>
        public static Dictionary<string, object> Menu(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            MenuModel model;
            try { model = MenuGame.Build(); }
            catch (Exception e) { return Failure("AutopilotAutomation", "Menu", "catalog threw: " + e.Message); }
            MenuCategory view = model.Category("view");
            var entries = new List<object>();
            if (view != null)
                foreach (MenuRow leaf in view.Rows)
                {
                    if (!leaf.Id.StartsWith("view/msl", StringComparison.Ordinal)) continue;
                    entries.Add(new Dictionary<string, object>
                    {
                        { "id", leaf.Id },
                        { "label", leaf.Label },
                        { "enabled", !leaf.IsBlocked },
                        { "status", leaf.State ?? "" },
                    });
                }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "view_found", view != null },
                { "entries", entries },
            };
            Debug.Log("[AutopilotAutomation] Menu: view=" + (view != null) + " msl_entries=" + entries.Count);
            return result;
        }
    }
}
#endif

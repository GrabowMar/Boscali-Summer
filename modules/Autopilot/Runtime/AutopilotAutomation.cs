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
            AceRadialNode root;
            try { root = AceRadialNode.Collect(AceRadialCatalog.Build(), AceRadialMenuTree.MaxNodes, AceRadialMenuTree.MaxDepth); }
            catch (Exception e) { return Failure("AutopilotAutomation", "Menu", "catalog threw: " + e.Message); }
            AceRadialNode view = null;
            if (root != null)
                foreach (AceRadialNode child in root.Children)
                    if (child.Action != null && child.Action.Id == "view") view = child;
            var entries = new List<object>();
            if (view != null)
                foreach (AceRadialNode leaf in view.Children)
                {
                    if (leaf.Action == null || !leaf.Action.Id.StartsWith("msl-", StringComparison.Ordinal)) continue;
                    entries.Add(new Dictionary<string, object>
                    {
                        { "id", leaf.Action.Id },
                        { "label", leaf.Action.Label },
                        { "enabled", leaf.Enabled },
                        { "status", leaf.Status.Text ?? "" },
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

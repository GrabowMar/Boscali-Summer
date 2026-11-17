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

        /// <summary>Engage (default) or cancel the autoland, as the C-menu AUTOLAND row does: {engage bool}.</summary>
        public static Dictionary<string, object> Autoland(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            AutopilotLandController land = AutopilotLandController.Instance;
            if (land == null) return Failure("AutopilotAutomation", "Autoland", "no landing controller");
            bool engage = !args.TryGetValue("engage", out object value) || Convert.ToBoolean(value);
            if (engage != land.IsEngaged) land.Toggle();
            return new Dictionary<string, object> { { "ok", true }, { "engaged", land.IsEngaged } };
        }

        /// <summary>C-menu missile entries: visibility/enabled/status of the four VIEW leaves.</summary>
        public static Dictionary<string, object> Menu(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            MenuModel model;
            try { model = MenuGame.Build(); }
            catch (Exception e) { return Failure("AutopilotAutomation", "Menu", "catalog threw: " + e.Message); }
            // Defaults: the VIEW missile leaves; {category, prefix} reads any other rows the same way.
            string category = args.TryGetValue("category", out object c) && c is string cs ? cs : "view";
            string prefix = args.TryGetValue("prefix", out object p) && p is string ps ? ps : "view/msl";
            MenuCategory view = model.Category(category);
            var entries = new List<object>();
            var rows = new Dictionary<string, object>(); // the same entries by id (sim paths cannot index lists)
            if (view != null)
                foreach (MenuRow leaf in view.Rows)
                {
                    if (!leaf.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var entry = new Dictionary<string, object>
                    {
                        { "id", leaf.Id },
                        { "label", leaf.Label },
                        { "enabled", !leaf.IsBlocked },
                        { "status", leaf.State ?? "" },
                    };
                    entries.Add(entry);
                    rows[leaf.Id] = entry;
                }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "view_found", view != null },
                { "entries", entries },
                { "rows", rows },
            };
            Debug.Log("[AutopilotAutomation] Menu: view=" + (view != null) + " msl_entries=" + entries.Count);
            return result;
        }
    }
}
#endif

#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using HarmonyLib;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (<c>tests/ingame/quick-map.json</c>); inert outside a sim run.
    /// Drives the Quick Map (<c>MapHoldKey</c>) hold state, zoom, orientation, and overlays in memory without touching saved config.
    /// </summary>
    public static class QuickMapAutomation
    {
        private static readonly AccessTools.FieldRef<DynamicMap, Dictionary<Airbase, AirbaseMapIcon>> AirbasesRef =
            AccessTools.FieldRefAccess<DynamicMap, Dictionary<Airbase, AirbaseMapIcon>>("airbaseIconLookup");

        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("QuickMapAutomation", "guard", "requires a hosted single-player nomodkit scenario");

            MapHoldKey service = MapHoldKey.Live;
            if (service == null) return Failure("QuickMapAutomation", "Configure", "no MapHoldKey service");

            if (Bool(args, "restore"))
            {
                service.HoldOverride = null;
                service.ZoomOverride = null;
                service.NorthUpOverride = null;
                service.OverlaysOverride = null;
            }
            else
            {
                if (Has(args, "held")) service.HoldOverride = Bool(args, "held");
                if (Has(args, "zoom")) service.ZoomOverride = Number(args, "zoom", 1f);
                if (Has(args, "northUp")) service.NorthUpOverride = Bool(args, "northUp");
                if (Has(args, "overlays")) service.OverlaysOverride = Bool(args, "overlays");
            }

            return State(service);
        }

        private static Dictionary<string, object> State(MapHoldKey service)
        {
            int airbasesShown = 0;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null)
            {
                Dictionary<Airbase, AirbaseMapIcon> lookup = AirbasesRef(map);
                if (lookup != null)
                    foreach (AirbaseMapIcon icon in lookup.Values)
                        if (icon != null && icon.gameObject.activeInHierarchy && (icon.iconImage == null || icon.iconImage.enabled))
                            airbasesShown++;
            }

            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "saved", false },
                { "active", service.Active },
                { "progress", service.Progress },
                { "zoom", service.ActiveZoom },
                { "northUp", service.ActiveNorthUp },
                { "overlays", service.ActiveOverlays },
                { "sidePx", service.LastSidePx },
                { "ringStepM", service.LastRingStepMetres },
                { "airbasesShown", airbasesShown },
                { "nearestBase", service.LastNearestBase },
                { "grid", service.LastGrid },
                { "quickMapFlag", MapTool.QuickMapActive },
            };
            Debug.Log("[QuickMapAutomation] Configure: " + Describe(result));
            return result;
        }
    }
}
#endif

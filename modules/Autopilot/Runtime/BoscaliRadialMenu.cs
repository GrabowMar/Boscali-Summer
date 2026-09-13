using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>Adds the Boscali Summer root slice and a bounded submenu to the native wheel by
    /// swapping actionsMain and calling the game's own SetupMain. Stock content is restored after
    /// a leaf action or the timeout; foreign submenus are never touched.</summary>
    internal static class BoscaliRadialMenu
    {
        private const string RootLabel = "Boscali Summer";
        private const string LandLabel = "Autopilot: Land";
        private const string CancelLabel = "Autopilot: Cancel";
        private const float RestoreAfterSeconds = 6f;

        private static BoscaliMenuAction rootEntry;
        private static BoscaliMenuAction[] submenu;
        private static RadialMenuAction[] stockActions;
        private static RadialMenuAction[] baselineWheel;
        private static bool inSubmenu;
        private static float lastInUseTime;
        private static readonly HashSet<string> traced = new HashSet<string>();

        internal static void Tick()
        {
            if (!RadialMenuAccess.Available) return;
            RadialMenuMain main = SceneSingleton<RadialMenuMain>.i;
            if (main == null) return;
            bool inUse;
            try { inUse = RadialMenuMain.IsInUse(); }
            catch { return; }
            if (inUse)
            {
                lastInUseTime = Time.unscaledTime;
                return;
            }
            if (inSubmenu && Time.unscaledTime - lastInUseTime > RestoreAfterSeconds) RestoreStockWheel();
        }

        internal static bool EnsureRootInjected(RadialMenuMain menu, bool openingRoot = false)
        {
            if (!RadialMenuAccess.Available || menu == null || inSubmenu) return false;
            RadialMenuAction[] current = RadialMenuAccess.GetActions(menu);
            if (current == null) return false;

            if (openingRoot && baselineWheel == null) baselineWheel = current;
            else if (baselineWheel == null || !SharesAnyEntry(current, baselineWheel)) return false;

            BuildMenu(current);
            if (Array.IndexOf(current, rootEntry) >= 0) return false;

            var grown = new RadialMenuAction[current.Length + 1];
            current.CopyTo(grown, 0);
            grown[grown.Length - 1] = rootEntry;
            RadialMenuAccess.SetActions(menu, grown);
            baselineWheel = grown;
            Trace(openingRoot, "injected (" + grown.Length + " entries)");
            return true;
        }

        private static void BuildMenu(RadialMenuAction[] templates)
        {
            if (rootEntry == null)
                rootEntry = BoscaliMenuAction.Create(RootLabel, _ => ShowSubmenu());
            if (submenu == null)
            {
                submenu = new[]
                {
                    BoscaliMenuAction.Create(LandLabel, _ =>
                    {
                        AutopilotLandController.Instance?.Toggle();
                        RestoreStockWheel();
                    }, LandAllowed),
                    BoscaliMenuAction.Create("Back", _ => RestoreStockWheel())
                };
            }

            rootEntry.CopyAppearanceFrom(Template(templates, 0));
            for (int i = 0; i < submenu.Length; i++) submenu[i].CopyAppearanceFrom(Template(templates, i + 1));
        }

        private static RadialMenuAction Template(RadialMenuAction[] templates, int index) =>
            templates != null && templates.Length > 0 ? templates[index % templates.Length] : null;

        private static void ShowSubmenu()
        {
            if (submenu == null) return;
            AutopilotLandController controller = AutopilotLandController.Instance;
            submenu[0].DisplayName = controller != null && controller.IsEngaged ? CancelLabel : LandLabel;
            Swap(submenu, submenu: true);
        }

        private static bool LandAllowed(Aircraft candidate)
        {
            AutopilotLandController controller = AutopilotLandController.Instance;
            if (controller == null) return false;
            return controller.IsEngaged ? controller.IsEngagedOn(candidate) : controller.CanEngage(candidate);
        }

        internal static void RestoreStockWheel()
        {
            if (!inSubmenu || stockActions == null) return;
            RadialMenuMain main = SceneSingleton<RadialMenuMain>.i;
            inSubmenu = false;
            if (main != null && RadialMenuAccess.GetAircraft(main) != null)
            {
                RadialMenuAccess.SetActions(main, stockActions);
                try { RadialMenuAccess.SetupMain(main); }
                catch (Exception e) { Plugin.Logger?.LogError("Autopilot: radial restore failed: " + e); }
            }
            stockActions = null;
        }

        private static void Swap(RadialMenuAction[] actions, bool submenu)
        {
            RadialMenuMain main = SceneSingleton<RadialMenuMain>.i;
            if (main == null || actions == null) return;
            if (RadialMenuAccess.GetAircraft(main) == null) return;
            if (stockActions == null && !submenu) return;
            if (submenu && !inSubmenu) stockActions = RadialMenuAccess.GetActions(main);

            RadialMenuAccess.SetActions(main, (RadialMenuAction[])actions.Clone());
            inSubmenu = submenu;
            lastInUseTime = Time.unscaledTime;
            try
            {
                RadialMenuAccess.SetupMain(main);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError("Autopilot: radial submenu rebuild failed: " + e);
                RadialMenuAccess.SetActions(main, stockActions);
                stockActions = null;
                inSubmenu = false;
                try { RadialMenuAccess.SetupMain(main); } catch { }
            }
        }

        private static bool SharesAnyEntry(RadialMenuAction[] a, RadialMenuAction[] b)
        {
            foreach (RadialMenuAction candidate in a)
            {
                if (candidate == null || candidate == rootEntry) continue;
                if (Array.IndexOf(b, candidate) >= 0) return true;
            }
            return false;
        }

        internal static void Reset()
        {
            stockActions = null;
            baselineWheel = null;
            inSubmenu = false;
            lastInUseTime = 0f;
            traced.Clear();
            if (submenu != null)
            {
                foreach (BoscaliMenuAction entry in submenu)
                    if (entry != null) UnityEngine.Object.Destroy(entry);
                submenu = null;
            }
            if (rootEntry != null)
            {
                UnityEngine.Object.Destroy(rootEntry);
                rootEntry = null;
            }
        }

        private static void Trace(bool openingRoot, string what)
        {
            string line = "[Autopilot radial] " + (openingRoot ? "root" : "rebuild") + ": " + what;
            if (traced.Add(line)) Plugin.Logger?.LogInfo(line);
        }
    }
}

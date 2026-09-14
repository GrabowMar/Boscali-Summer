using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    internal static class BoscaliRadialMenu
    {
        private const string RootLabel = "Boscali Summer";
        private const string LandLabel = "Autopilot: Land";
        private const string CancelLabel = "Autopilot: Cancel";
        private const string BackLabel = "Back";
        private const int MaxPageEntries = 6;
        private const float RestoreAfterSeconds = 6f;

        private static BoscaliMenuAction rootEntry;
        private static BoscaliMenuAction landEntry;
        private static BoscaliMenuAction submenuBack;
        private static BoscaliMenuAction pageEntry;
        private static BoscaliMenuAction[] rootSubmenu;
        private static BoscaliMenuAction[] pageActions;
        private static RadialMenuAction[] appearanceTemplates;
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

            BuildRootActions(current);
            if (Array.IndexOf(current, rootEntry) >= 0) return false;

            var grown = new RadialMenuAction[current.Length + 1];
            current.CopyTo(grown, 0);
            grown[grown.Length - 1] = rootEntry;
            RadialMenuAccess.SetActions(menu, grown);
            baselineWheel = grown;
            Trace(openingRoot, "injected (" + grown.Length + " entries)");
            return true;
        }

        private static void BuildRootActions(RadialMenuAction[] templates)
        {
            if (templates != null && templates.Length > 0) appearanceTemplates = templates;
            if (rootEntry == null)
                rootEntry = BoscaliMenuAction.Create(RootLabel, _ => ShowSubmenu());
            if (landEntry == null)
                landEntry = BoscaliMenuAction.Create(LandLabel, _ =>
                {
                    AutopilotLandController.Instance?.Toggle();
                    RestoreStockWheel();
                }, LandAllowed);
            if (submenuBack == null)
                submenuBack = BoscaliMenuAction.Create(BackLabel, _ => RestoreStockWheel());

            rootEntry.CopyAppearanceFrom(Template(appearanceTemplates, 0));
            landEntry.CopyAppearanceFrom(Template(appearanceTemplates, 1));
            submenuBack.CopyAppearanceFrom(Template(appearanceTemplates, 2));
        }

        private static RadialMenuAction Template(RadialMenuAction[] templates, int index) =>
            templates != null && templates.Length > 0 ? templates[index % templates.Length] : null;

        private static void ShowSubmenu()
        {
            AutopilotLandController controller = AutopilotLandController.Instance;
            landEntry.DisplayName = controller != null && controller.IsEngaged ? CancelLabel : LandLabel;

            IRadialMenuPage page = Contributed();
            int count = SafeEntryCount(page) > 0 ? 3 : 2;
            if (rootSubmenu == null || rootSubmenu.Length != count)
                rootSubmenu = new BoscaliMenuAction[count];

            int index = 0;
            if (count == 3)
            {
                if (pageEntry == null)
                    pageEntry = BoscaliMenuAction.Create(SafeTitle(page), _ => ShowPage(Contributed()));
                pageEntry.DisplayName = SafeTitle(page);
                pageEntry.CopyAppearanceFrom(Template(appearanceTemplates, 0));
                rootSubmenu[index++] = pageEntry;
            }
            rootSubmenu[index++] = landEntry;
            rootSubmenu[index] = submenuBack;
            Swap(rootSubmenu, submenu: true);
        }

        private static void ShowPage(IRadialMenuPage page)
        {
            if (page == null)
            {
                RestoreStockWheel();
                return;
            }

            int count = Mathf.Clamp(SafeEntryCount(page), 0, MaxPageEntries);
            if (count == 0)
            {
                ShowSubmenu();
                return;
            }

            if (pageActions == null || pageActions.Length != count + 1)
            {
                DestroyActions(ref pageActions);
                pageActions = new BoscaliMenuAction[count + 1];
            }

            for (int i = 0; i < count; i++)
            {
                int entry = i;
                if (pageActions[i] == null)
                    pageActions[i] = BoscaliMenuAction.Create("", null);
                pageActions[i].Configure(SafePageLabel(page, entry),
                    _ => SafeInvoke(page, entry),
                    _ => SafeAllowed(page, entry));
                pageActions[i].CopyAppearanceFrom(Template(appearanceTemplates, entry + 1));
            }

            if (pageActions[count] == null)
                pageActions[count] = BoscaliMenuAction.Create(BackLabel, _ => ShowSubmenu());
            pageActions[count].CopyAppearanceFrom(Template(appearanceTemplates, 1));
            Swap(pageActions, submenu: true);
        }

        private static IRadialMenuPage Contributed()
        {
            try
            {
                ModServices.TryGet(out IRadialMenuPage page);
                return page;
            }
            catch (Exception e)
            {
                Trace(false, "contributor lookup failed: " + e.Message);
                return null;
            }
        }

        private static int SafeEntryCount(IRadialMenuPage page)
        {
            if (page == null) return 0;
            try { return page.EntryCount; }
            catch { return 0; }
        }

        private static string SafeTitle(IRadialMenuPage page)
        {
            try
            {
                string title = page.Title;
                return string.IsNullOrEmpty(title) ? "Page" : title;
            }
            catch { return "Page"; }
        }

        private static string SafePageLabel(IRadialMenuPage page, int index)
        {
            try { return page.EntryLabel(index) ?? ""; }
            catch { return ""; }
        }

        private static void SafeInvoke(IRadialMenuPage page, int index)
        {
            try { page.InvokeEntry(index); }
            catch (Exception e) { Trace(false, "page entry failed: " + e.Message); }
        }

        private static bool SafeAllowed(IRadialMenuPage page, int index)
        {
            try { return page.EntryAllowed(index); }
            catch { return false; }
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
            appearanceTemplates = null;
            inSubmenu = false;
            lastInUseTime = 0f;
            traced.Clear();
            // The shared entries are owned once; rootSubmenu only holds references to them.
            rootSubmenu = null;
            DestroyActions(ref pageActions);
            DestroyAction(ref pageEntry);
            DestroyAction(ref submenuBack);
            DestroyAction(ref landEntry);
            DestroyAction(ref rootEntry);
        }

        private static void DestroyActions(ref BoscaliMenuAction[] actions)
        {
            if (actions == null) return;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i] != null) UnityEngine.Object.Destroy(actions[i]);
            actions = null;
        }

        private static void DestroyAction(ref BoscaliMenuAction action)
        {
            if (action == null) return;
            UnityEngine.Object.Destroy(action);
            action = null;
        }

        private static void Trace(bool openingRoot, string what)
        {
            string line = "[Autopilot radial] " + (openingRoot ? "root" : "rebuild") + ": " + what;
            if (traced.Add(line)) Plugin.Logger?.LogInfo(line);
        }
    }
}

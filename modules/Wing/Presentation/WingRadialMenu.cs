using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
// Harmony calls prefixes and postfixes by reflection.
#pragma warning disable IDE0051

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Adds a Wing Command slice to the game's radial wheel.
    /// <list type="bullet">
    /// <item>Pages are shown by swapping <c>actionsMain</c> and rebuilding the native wheel.</item>
    /// <item>The stock wheel comes back after a leaf action, or 6 s after the wheel closes.</item>
    /// <item>Four leaves (spec 2026-10-04 §2): FORM UP, STANCE (a sub-page of the six stance slots), ENGAGE, RTB. The rest of
    /// the orders live on the WMC and the wing key's Call Ladder.</item>
    /// </list></summary>
    internal static class WingRadialMenu
    {
        private const string RootLabel = "Wing Command";
        private const float RestoreAfterSeconds = 6f;

        private static WingMenuAction rootEntry;
        private static WingMenuAction[] mainMenu, stanceMenu;
        private static RadialMenuAction[] appearance;
        private static RadialMenuAction[] stockActions;
        private static RadialMenuAction[] baselineWheel;
        private static bool inSubmenu;
        private static float lastInUseTime;

        /// <summary>Restore the stock wheel after the timeout; called every frame by <see cref="WingHotkeys"/>.</summary>
        public static void Tick()
        {
            if (!GameAccess.Available) return;
            RadialMenuMain menu = SceneSingleton<RadialMenuMain>.i;
            if (menu == null) return;
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

        /// <summary>Keep the root slice in the wheel across native SetupMain rebuilds.</summary>
        internal static bool EnsureRootInjected(RadialMenuMain menu, bool openingRoot = false)
        {
            if (menu == null || inSubmenu) { Trace(openingRoot, "menu null or in submenu"); return false; }
            RadialMenuAction[] current = GameAccess.GetActionsMain(menu);
            if (current == null) { Trace(openingRoot, "actionsMain is null"); return false; }

            // Capture the baseline only at OpenMenu's root boundary; SetupMain also rebuilds other mods' submenus.
            if (openingRoot && baselineWheel == null)
                baselineWheel = current;
            else if (baselineWheel == null || !SharesAnyEntry(current, baselineWheel))
            {
                Trace(openingRoot, "not the root wheel");
                return false;
            }

            BuildMenus(menu);
            if (Array.IndexOf(current, rootEntry) >= 0) return false;

            var grown = new RadialMenuAction[current.Length + 1];
            current.CopyTo(grown, 0);
            grown[grown.Length - 1] = rootEntry;
            GameAccess.SetActionsMain(menu, grown);
            baselineWheel = grown;
            Trace(openingRoot, "injected, wheel now " + grown.Length + " entries");
            return true;
        }

        private static readonly HashSet<string> traced = new HashSet<string>();

        private static void Trace(bool openingRoot, string what)
        {
            string line = "[Radial] " + (openingRoot ? "root" : "rebuild") + ": " + what;
            if (traced.Add(line)) WingLog.Verbose(line);
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

        private static void BuildMenus(RadialMenuMain menu)
        {
            if (rootEntry != null && mainMenu != null) return;

            // Existing native actions are the appearance templates.
            RadialMenuAction[] templates = GameAccess.GetActionsMain(menu);
            Func<int, RadialMenuAction> template = i =>
                templates != null && templates.Length > 0 ? templates[i % templates.Length] : null;

            if (rootEntry == null) rootEntry = WingMenuAction.Create(RootLabel, _ => Swap(mainMenu, submenu: true));

            appearance = templates;
            mainMenu = new[]
            {
                Leaf("Form Up", WingCommands.FormUp, "rejoin"),
                Icon(WingMenuAction.Create("Stance", _ => ShowStances()), "posture"),
                Leaf("Engage", () => WingCommands.Engage(), "selection"),
                Leaf("RTB", () => WingCommands.Rtb(), "rtb"),
            };
            ApplyAppearance(rootEntry, template(0), "root");
            ApplyAll(mainMenu, template);
        }

        /// <summary>The stance sub-page: the six slots by name (an empty slot is left out), then Back. Built each time it is
        /// opened, so a renamed or reordered slot reads true.</summary>
        private static void ShowStances()
        {
            Destroy(ref stanceMenu);
            var entries = new List<WingMenuAction>();
            for (int i = 0; i < StanceBook.Slots; i++)
            {
                Stance st = WmcStanceActions.Book.Slot(i);
                if (st == null) continue;
                int slot = i;
                entries.Add(Leaf(st.Name, () => WingCallLadder.RunStance(slot), "posture"));
            }
            entries.Add(Back());
            stanceMenu = entries.ToArray();
            Func<int, RadialMenuAction> template = i =>
                appearance != null && appearance.Length > 0 ? appearance[i % appearance.Length] : null;
            ApplyAll(stanceMenu, template);
            Swap(stanceMenu, submenu: true);
        }

        private static void ApplyAll(WingMenuAction[] entries, Func<int, RadialMenuAction> template)
        {
            for (int i = 0; i < entries.Length; i++) ApplyAppearance(entries[i], template(i), null);
        }

        private static WingMenuAction Icon(WingMenuAction action, string iconKey)
        {
            action.IconKey = iconKey;
            return action;
        }

        private static WingMenuAction Back() => Icon(WingMenuAction.Create("Back", _ => Swap(mainMenu, submenu: true)), "back");

        /// <summary>A command that runs and then restores the stock wheel.</summary>
        private static WingMenuAction Leaf(string label, Action action, string iconKey) =>
            Icon(WingMenuAction.Create(label, _ =>
            {
                action();
                RestoreStockWheel();
            }), iconKey);

        private static void ApplyAppearance(WingMenuAction action, RadialMenuAction template, string iconKey)
        {
            action.CopyAppearanceFrom(template);
            string key = iconKey ?? action.IconKey;
            if (string.IsNullOrEmpty(key)) return;
            try
            {
                GameAccess.SetIconSprite(action, IconFactory.Get(key));
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("Could not build icon '" + key + "': " + e.Message);
            }
        }

        internal static void RestoreStockWheel()
        {
            if (!inSubmenu || stockActions == null) return;
            Swap(stockActions, submenu: false);
            stockActions = null;
        }

        private static void Swap(RadialMenuAction[] actions, bool submenu)
        {
            RadialMenuMain menu = SceneSingleton<RadialMenuMain>.i;
            if (menu == null || actions == null) return;
            // Stock AllowedOnAircraft dereferences the cached aircraft, so SetupMain needs it.
            if (GameAccess.GetMenuAircraft(menu) == null) return;
            if (stockActions == null && !submenu) return;
            if (submenu && !inSubmenu) stockActions = GameAccess.GetActionsMain(menu);

            GameAccess.SetActionsMain(menu, (RadialMenuAction[])actions.Clone());
            inSubmenu = submenu;
            lastInUseTime = Time.unscaledTime;
            try
            {
                GameAccess.SetupMain(menu);
            }
            catch (Exception e)
            {
                WingLog.Logger.LogError("Radial page rebuild failed, restoring the stock wheel: " + e);
                GameAccess.SetActionsMain(menu, stockActions);
                inSubmenu = false;
                try { GameAccess.SetupMain(menu); } catch { /* leave the wheel as it is */ }
            }
        }

        /// <summary>Clear mission radial state.</summary>
        internal static void Reset()
        {
            stockActions = null;
            baselineWheel = null;
            inSubmenu = false;
            Destroy(ref mainMenu);
            Destroy(ref stanceMenu);
            appearance = null;
            if (rootEntry != null) UnityEngine.Object.Destroy(rootEntry);
            rootEntry = null;
        }

        private static void Destroy(ref WingMenuAction[] actions)
        {
            if (actions == null) return;
            foreach (WingMenuAction action in actions)
                if (action != null) UnityEngine.Object.Destroy(action);
            actions = null;
        }
    }

    [HarmonyPatch(typeof(RadialMenuMain))]
    internal static class WingRadialMenuPatches
    {
        private static bool reportedInactive;

        private static void ReportInactive(string where)
        {
            if (reportedInactive) return;
            reportedInactive = true;
            WingLog.Logger.LogWarning("[Radial] " + where + ": the game's wheel is left alone because the reflection it " +
                "needs did not resolve" + (GameAccess.UnavailableReason == null ? "" : " (" + GameAccess.UnavailableReason + ")") +
                ". Use the Keys/* hotkeys instead.");
        }

        /// <summary>Seed actionsMain in the inherited SceneSingleton Awake; RadialMenuMain does not declare it.</summary>
        [HarmonyPatch]
        internal static class AwakePatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(typeof(SceneSingleton<RadialMenuMain>), "Awake");

            [HarmonyPostfix]
            private static void Postfix(SceneSingleton<RadialMenuMain> __instance)
            {
                // Mono shares generic reference-type method bodies; ignore every other singleton.
                if (!(__instance is RadialMenuMain menu)) return;
                if (!GameAccess.Available) { ReportInactive("Awake"); return; }
                try
                {
                    WingRadialMenu.EnsureRootInjected(menu, openingRoot: true);
                }
                catch (Exception e)
                {
                    WingLog.Logger.LogError("Failed to seed the wing menu entry at Awake: " + e);
                }
            }
        }

        [HarmonyPatch("SetupMain")]
        [HarmonyPrefix]
        private static void SetupMain_Prefix(RadialMenuMain __instance)
        {
            if (!GameAccess.Available) { ReportInactive("SetupMain"); return; }
            try
            {
                WingRadialMenu.EnsureRootInjected(__instance);
            }
            catch (Exception e)
            {
                WingLog.Logger.LogError("Failed to inject the wing menu entry: " + e);
            }
        }

        [HarmonyPatch(nameof(RadialMenuMain.OpenMenu))]
        [HarmonyPostfix]
        private static void OpenMenu_Postfix(RadialMenuMain __instance)
        {
            if (!GameAccess.Available) { ReportInactive("OpenMenu"); return; }
            try
            {
                if (WingRadialMenu.EnsureRootInjected(__instance, openingRoot: true)) GameAccess.SetupMain(__instance);
            }
            catch (Exception e)
            {
                WingLog.Logger.LogError("Failed to inject the wing menu entry while opening: " + e);
            }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix() => WingRadialMenu.Reset();
    }
}

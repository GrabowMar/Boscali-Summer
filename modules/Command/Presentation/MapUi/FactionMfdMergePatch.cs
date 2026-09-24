using System.Collections.Generic;
using BoscaliSummer.Runtime;
using HarmonyLib;
using TMPro;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>Keeps both native HQ controllers while offering one faction bezel.</summary>
    [HarmonyPatch]
    internal static class FactionMfdMergePatch
    {
        private static VirtualMFD owner;
        private static MFDScreen currentScreen;
        private static MFDScreen otherScreen;
        private static InfoPanel_Faction other;
        private static List<MFDScreen> otherSlots;
        private static int otherIndex = -1;
        private static bool started;

        public static MFDScreen Screen => currentScreen;
        public static InfoPanel_Faction Other => other;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.SetupButtons))]
        private static void Before(VirtualMFD __instance)
        {
            if (!GameAccess.MfdAvailable) return;
            if (owner != __instance || currentScreen == null || otherScreen == null)
                Discover(__instance);

            // Vanilla expects its original screen at every button index. Give it that
            // entry only for SetupButtons; the released slot remains claimable afterward.
            if (otherSlots != null && otherIndex >= 0 && otherIndex < otherSlots.Count &&
                otherSlots[otherIndex] == null)
                otherSlots[otherIndex] = otherScreen;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VirtualMFD), nameof(VirtualMFD.SetupButtons))]
        private static void After(VirtualMFD __instance)
        {
            if (!GameAccess.MfdAvailable) return;
            if (owner != __instance || currentScreen == null || otherScreen == null)
                Discover(__instance);
            if (otherSlots == null || otherIndex < 0 || otherIndex >= otherSlots.Count) return;

            if (started) ReleaseOther(__instance);

            SetFactionLabel(GameAccess.GetLeftMfdScreens(__instance),
                GameAccess.GetLeftMfdButtons(__instance));
            SetFactionLabel(GameAccess.GetRightMfdScreens(__instance),
                GameAccess.GetRightMfdButtons(__instance));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VirtualMFD), "Start")]
        private static void AfterStart(VirtualMFD __instance)
        {
            if (!GameAccess.MfdAvailable) return;
            if (owner != __instance || currentScreen == null || otherScreen == null)
                Discover(__instance);
            started = true;
            ReleaseOther(__instance);
            SetFactionLabel(GameAccess.GetLeftMfdScreens(__instance),
                GameAccess.GetLeftMfdButtons(__instance));
            SetFactionLabel(GameAccess.GetRightMfdScreens(__instance),
                GameAccess.GetRightMfdButtons(__instance));
        }

        private static void ReleaseOther(VirtualMFD mfd)
        {
            if (otherSlots == null || otherIndex < 0 || otherIndex >= otherSlots.Count) return;

            // Never evict another mod that claimed the former PALA slot.
            if (otherSlots[otherIndex] == otherScreen)
            {
                otherSlots[otherIndex] = null;
                List<Button> buttons = otherSlots == GameAccess.GetLeftMfdScreens(mfd)
                    ? GameAccess.GetLeftMfdButtons(mfd)
                    : GameAccess.GetRightMfdButtons(mfd);
                if (buttons != null && otherIndex < buttons.Count && buttons[otherIndex] != null)
                    buttons[otherIndex].enabled = false;
            }
        }

        private static void SetFactionLabel(List<MFDScreen> screens,
                                            List<Button> buttons)
        {
            if (screens == null || buttons == null) return;
            for (int i = 0; i < screens.Count && i < buttons.Count; i++)
            {
                if (screens[i] != currentScreen || buttons[i] == null) continue;
                TMP_Text text = buttons[i].GetComponentInChildren<TMP_Text>(true);
                if (text != null) text.text = "FAC";
                break;
            }
        }

        private static void Discover(VirtualMFD mfd)
        {
            bool wasStarted = owner == mfd && started;
            owner = mfd;
            started = wasStarted;
            currentScreen = otherScreen = null;
            other = null;
            otherSlots = null;
            otherIndex = -1;
            Find(GameAccess.GetLeftMfdScreens(mfd));
            Find(GameAccess.GetRightMfdScreens(mfd));
            if (currentScreen == null || otherScreen == null)
            {
                otherSlots = null;
                otherIndex = -1;
            }
        }

        private static void Find(List<MFDScreen> screens)
        {
            if (screens == null) return;
            for (int i = 0; i < screens.Count; i++)
            {
                MFDScreen screen = screens[i];
                if (screen == null) continue;
                InfoPanel_Faction faction = screen.GetComponent<InfoPanel_Faction>();
                if (faction == null) continue;
                if (faction.selectFaction == InfoPanel_Faction.SelectFaction.Current)
                {
                    currentScreen = screen;
                }
                else if (faction.selectFaction == InfoPanel_Faction.SelectFaction.Other)
                {
                    otherScreen = screen;
                    other = faction;
                    otherSlots = screens;
                    otherIndex = i;
                }
            }
        }

        public static void Reset()
        {
            owner = null;
            currentScreen = otherScreen = null;
            other = null;
            otherSlots = null;
            otherIndex = -1;
            started = false;
        }
    }
}

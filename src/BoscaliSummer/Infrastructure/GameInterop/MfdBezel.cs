using System.Collections.Generic;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Named MFD bezel claim on top of <see cref="BezelRegistry"/>. The physical slot
    /// comes from the live VirtualMFD lists; the registry stops a same-frame race with
    /// Wing Command's WMC screen.
    /// </summary>
    internal static class MfdBezel
    {
        public static bool TryClaim(
            string id, bool preferLeft, VirtualMFD mfd,
            out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left)
        {
            buttons = null;
            screens = null;
            slot = -1;
            left = preferLeft;
            if (mfd == null || !GameAccess.MfdAvailable) return false;

            List<Button> leftButtons = GameAccess.GetLeftMfdButtons(mfd);
            List<Button> rightButtons = GameAccess.GetRightMfdButtons(mfd);
            List<MFDScreen> leftScreens = GameAccess.GetLeftMfdScreens(mfd);
            List<MFDScreen> rightScreens = GameAccess.GetRightMfdScreens(mfd);

            if (!BezelRegistry.TryClaim(
                id, preferLeft,
                leftButtons == null ? 0 : leftButtons.Count,
                rightButtons == null ? 0 : rightButtons.Count,
                (isLeft, index) => IsFree(
                    isLeft ? leftButtons : rightButtons,
                    isLeft ? leftScreens : rightScreens,
                    index),
                out left, out slot))
                return false;

            buttons = left ? leftButtons : rightButtons;
            screens = left ? leftScreens : rightScreens;
            return buttons != null && screens != null && slot >= 0 && slot < buttons.Count;
        }

        public static void Bind(VirtualMFD mfd, List<Button> buttons, List<MFDScreen> screens,
            int slot, bool left, MFDScreen screen)
        {
            while (screens.Count <= slot) screens.Add(null);
            screens[slot] = screen;
            mfd.SetupButtons();

            Button bezel = buttons[slot];
            bezel.enabled = true;
            bezel.interactable = true;
            if (bezel.onClick.GetPersistentEventCount() == 0)
            {
                VirtualMFD owner = mfd;
                bool onLeft = left;
                bezel.onClick.AddListener(() =>
                {
                    if (onLeft) owner.PressLeftButton(bezel);
                    else owner.PressRightButton(bezel);
                });
            }

            screen.CloseScreen(Screen.width * (left ? Vector3.left : Vector3.right));
        }

        public static MFDScreen FindTemplate(VirtualMFD mfd)
        {
            return FindTemplate(GameAccess.GetLeftMfdScreens(mfd)) ??
                   FindTemplate(GameAccess.GetRightMfdScreens(mfd));
        }

        public static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i] != null && screens[i].transform.parent != null) return screens[i];
            return null;
        }

        private static bool IsFree(List<Button> buttons, List<MFDScreen> screens, int index)
        {
            if (buttons == null || index < 0 || index >= buttons.Count || buttons[index] == null)
                return false;
            return screens == null || index >= screens.Count || screens[index] == null;
        }
    }
}

using System.Collections.Generic;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Game
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
            if (buttons != null && screens != null && IsFree(buttons, screens, slot)) return true;

            BezelRegistry.Release(id);
            buttons = null;
            screens = null;
            slot = -1;
            left = preferLeft;
            return false;
        }

        public static void Release(string id) => BezelRegistry.Release(id);

        public static bool Bind(VirtualMFD mfd, List<Button> buttons, List<MFDScreen> screens,
            int slot, bool left, MFDScreen screen)
        {
            if (mfd == null || screens == null || screen == null || !IsFree(buttons, screens, slot))
                return false;

            while (screens.Count <= slot) screens.Add(null);
            screens[slot] = screen;
            mfd.SetupButtons();

            Button bezel = buttons[slot];
            MatchLabel(bezel, buttons);
            AvInput.StripNavigation(bezel); // a flight stick must never steer focus across the bezel and fire a screen
            bezel.gameObject.SetActive(DynamicMap.mapMaximized);
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
            return true;
        }

        /// <summary>
        /// The classic layout shows the bezel button itself, so its label must read like the stock ones: the first
        /// stock button's type sizes, on one line. A spare slot or a hosted button kept TMP's word wrap, so autosize
        /// broke a three-letter code over two lines at a larger size ("CO / M").
        /// </summary>
        private static void MatchLabel(Button bezel, List<Button> buttons)
        {
            TMP_Text label = bezel.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            TMP_Text model = buttons.Count > 0 && buttons[0] != null && buttons[0] != bezel ? buttons[0].GetComponentInChildren<TMP_Text>(true) : null;
            if (model != null)
            {
                label.font = model.font;
                label.fontStyle = model.fontStyle;
                label.fontSizeMin = model.enableAutoSizing ? model.fontSizeMin : model.fontSize;
                label.fontSizeMax = model.enableAutoSizing ? model.fontSizeMax : model.fontSize;
                label.fontSize = model.fontSize;
            }
            label.enableAutoSizing = true;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.Center;
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

        /// <summary>True when the bezel button exists and no screen is bound to that slot (the Wing WMC bezel claims through the same test).</summary>
        internal static bool IsFree(List<Button> buttons, List<MFDScreen> screens, int index)
        {
            if (buttons == null || index < 0 || index >= buttons.Count || buttons[index] == null)
                return false;
            return screens == null || index >= screens.Count || screens[index] == null;
        }
    }
}

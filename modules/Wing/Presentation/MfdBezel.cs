using System.Collections.Generic;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

using CoreGameAccess = BoscaliSummer.Core.Game.GameAccess;
using CoreMfdBezel = BoscaliSummer.Core.Game.MfdBezel;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Finds a free MFD bezel slot on the live <see cref="VirtualMFD"/> and binds a companion
    /// screen to it. There is no cross-mod reservation: each plugin scans the same vanilla button/screen
    /// lists and writes its screen immediately, so Unity's single-threaded frame order resolves a
    /// same-frame contest, and <see cref="Bind"/> re-checks the slot before committing.</summary>
    internal static class MfdBezel
    {
        public static bool TryClaim(
            bool preferLeft, VirtualMFD mfd,
            out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left) =>
            CoreMfdBezel.TryClaim(BezelRegistry.Wmc, preferLeft, mfd, out buttons, out screens, out slot, out left);

        /// <summary>Bind <paramref name="screen"/> to the claimed slot. Returns false if another plugin
        /// took the slot in the same frame between the scan and here; the caller retries next second.</summary>
        public static bool Bind(VirtualMFD mfd, List<Button> buttons, List<MFDScreen> screens,
            int slot, bool left, MFDScreen screen)
        {
            while (screens.Count <= slot) screens.Add(null);
            if (screens[slot] != null && screens[slot] != screen) return false;
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
            return true;
        }

        public static MFDScreen FindTemplate(VirtualMFD mfd)
        {
            return FindTemplate(CoreGameAccess.GetLeftMfdScreens(mfd)) ??
                   FindTemplate(CoreGameAccess.GetRightMfdScreens(mfd));
        }

        public static MFDScreen FindTemplate(List<MFDScreen> screens)
        {
            if (screens == null) return null;
            // Measure stable option pages rather than variable-length faction or target lists.
            foreach (MFDScreen s in screens)
            {
                if (s != null && (s.shortName == "MAP" || s.shortName == "HUD") &&
                    s.transform.parent != null && HasNativeLayout(s)) return s;
            }
            foreach (MFDScreen s in screens)
            {
                if (s != null && s.transform.parent != null && HasNativeLayout(s)) return s;
            }
            return null;
        }

        private static bool HasNativeLayout(MFDScreen template) =>
            template != null && template.displayPanel != null && template.transform is RectTransform;
    }
}

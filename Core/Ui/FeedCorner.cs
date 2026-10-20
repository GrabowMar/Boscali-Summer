using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The bottom-right column of video cards, shared by two modules on two canvases (2026-10-20, user: "uav on bottom, missile and target on top"):
    /// the VANGUARD UAV card holds the bottom and publishes where it is; the HUD's target and missile cards stack on it. Screen pixels.
    /// </summary>
    internal static class FeedCorner
    {
        /// <summary>The UAV card's screen rectangle (pixels, origin bottom left) while it is up; zero size when it is not. Written every frame by its owner.</summary>
        public static Rect Uav;
    }
}

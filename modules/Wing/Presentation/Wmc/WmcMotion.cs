using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The WMC's cues (spec FUI §motion, game-feel tiers): a punch on an order press, a blink on a new urgent alert, a one-
    /// refresh rail flash on a state change. <c>Wmc/ReduceMotion</c> turns each into its steady state (through
    /// <see cref="AvReveal.Snap"/> for the punch).</summary>
    internal static class WmcMotion
    {
        public const float PunchFrom = 0.94f, PunchSeconds = 0.12f, BlinkSeconds = 3f, BlinkHz = 2f;
        private static AvReveal reveal;

        public static bool Reduced => WingSettings.Instance != null && WingSettings.Instance.ReduceMotion.Value;

        /// <summary>The driver lives on the panel root (built once).</summary>
        public static void Attach(GameObject root)
        {
            if (root != null && reveal == null) reveal = root.AddComponent<AvReveal>();
        }

        public static void Punch(Component target)
        {
            if (reveal == null || target == null) return;
            AvReveal.Snap = Reduced;
            reveal.Punch((RectTransform)target.transform, PunchFrom, PunchSeconds);
        }
    }
}

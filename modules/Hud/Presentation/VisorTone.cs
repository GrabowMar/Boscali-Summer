using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The one-beat flash of the visor's rim and brow rail (2026-11-16, <see cref="VisorFx"/>): the HUD ink toned toward the warning red (a top warning came up) or lifted toward white (the power-on finished). The beat is quantised
    /// to four steps so a layer repaints at most four times in its 0.3 s, never per frame, and an unflashed layer's ink is exactly the HUD's own.
    /// </summary>
    internal static class VisorTone
    {
        public const int Steps = 3;

        /// <summary>The flash as a step 0..<see cref="Steps"/>.</summary>
        public static int Step(float flash) => flash <= 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(flash * Steps), 1, Steps);

        /// <summary>The ink at <paramref name="step"/> of a flash of <paramref name="kind"/>.</summary>
        public static Color Ink(Color baseInk, FlashKind kind, int step)
        {
            if (step <= 0 || kind == FlashKind.None) return baseInk;
            float k = step / (float)Steps;
            return kind == FlashKind.Warning
                ? Color.Lerp(baseInk, VanillaHudStyle.Colours.Alert, k)
                : Color.Lerp(baseInk, Color.white, 0.85f * k);
        }

        /// <summary>A line's alpha during a flash: it rises to full ink at the peak.</summary>
        public static float Alpha(float baseAlpha, int step) => baseAlpha + (1f - baseAlpha) * (step / (float)Steps);
    }
}

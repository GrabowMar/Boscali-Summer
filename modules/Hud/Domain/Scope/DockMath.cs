using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The DOCK page's rules (HUD v8, SKYWELL receiver), pure: the bullseye's scale and funnel, the director steps, the ribbon, the closure bar, time to contact.</summary>
    internal static class DockMath
    {
        /// <summary>Pixels per metre of the bullseye and its radius in pixels (the prototype's 3.2 px per metre, ring at 96).</summary>
        public const float PxPerM = 3.2f, BullseyeR = 96f;
        /// <summary>The funnel is the capture cone: 0.12 x range, 6..90 m; its ring never draws smaller than 19 px.</summary>
        public const float ConeRatio = 0.12f, ConeMinM = 6f, ConeMaxM = 90f, ConeMinPx = 19f;
        /// <summary>A director steps from OK (inside 5 m) to a plain cue to a bold one past 10 m.</summary>
        public const float OkM = 5f, BoldM = 10f;
        /// <summary>Closing faster than this share of the hook-up's limit is a caution (the prototype's 25 of 30 m/s); the bar's full scale is this much of the limit.</summary>
        public const float FastShare = 25f / 30f, BarShare = 40f / 30f;
        /// <summary>The gate ribbon runs linearly from this range to contact; its first tick is a plain range mark.</summary>
        public const float RibbonM = 240f, RibbonMarkM = 150f;

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Radius of the capture cone at <paramref name="rangeM"/>: wide far out, tight at the contact point.</summary>
        public static float ConeM(float rangeM) => Clamp(ConeRatio * rangeM, ConeMinM, ConeMaxM);

        public static float ConePx(float coneM) => Clamp(coneM * PxPerM, ConeMinPx, BullseyeR);

        public static float ErrM(float x, float y) => (float)Math.Sqrt(x * x + y * y);

        public static bool Inside(float errM, float coneM) => errM <= coneM;

        /// <summary>Close to the funnel's edge: past 75 percent of the cone with more than 30 m to go (inside that the hook-up pulls the airframe in).</summary>
        public static bool NearEdge(float errM, float coneM, float rangeM) => errM > 0.75f * coneM && rangeM > 30f;

        public static float TtcS(float rangeM, float closureMps) => closureMps > 0.8f && rangeM > 0f ? rangeM / closureMps : 0f;

        public static bool TooFast(float closureMps, float maxClosingMps, float rangeM) => maxClosingMps > 0f && closureMps >= FastShare * maxClosingMps && rangeM > 0f;

        /// <summary>The closure bar's fill (0..1) against its full scale of 4/3 of the limit.</summary>
        public static float ClosureFill(float closureMps, float maxClosingMps) => maxClosingMps > 0f ? Clamp(closureMps / (BarShare * maxClosingMps), 0f, 1f) : 0f;

        /// <summary>Where the limit's tick sits on that bar (0.75).</summary>
        public static float LimitTick => 1f / BarShare;

        /// <summary>Scale of the glider's faint rear silhouette behind the bullseye (it grows as 1 / range), and how visible it is.</summary>
        public static float SilhouetteScale(float rangeM) => 8600f / Math.Max(rangeM, 40f);

        public static float SilhouetteFade(float scale) => Clamp(1f - (scale - 110f) / 150f, 0f, 1f);

        /// <summary>Ribbon x for a range, 0 at <see cref="RibbonM"/> out, 1 at contact.</summary>
        public static float RibbonAt(float rangeM) => 1f - Clamp(rangeM, 0f, RibbonM) / RibbonM;

        /// <summary>The directors: how far to move up, down, left and right to centre the probe (0 when already past it on that axis).</summary>
        public static void Directors(float relX, float relY, out float up, out float down, out float left, out float right)
        {
            up = relY < 0f ? -relY : 0f;
            down = relY > 0f ? relY : 0f;
            left = relX > 0f ? relX : 0f;
            right = relX < 0f ? -relX : 0f;
        }
    }
}

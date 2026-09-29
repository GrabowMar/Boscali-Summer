using System;

namespace BoscaliSummer.Features.Hud.Domain
{
    /// <summary>
    /// A screen-space rectangle, centre-origin (0,0 is screen centre) with Y up, in reference
    /// pixels at the screen's actual resolution -- the same convention <c>RectTransform</c>
    /// anchored-position math uses under a full-stretch parent. Pure value type, no UnityEngine.
    /// </summary>
    internal readonly struct RectF
    {
        public readonly float CenterX, CenterY, Width, Height;

        public RectF(float centerX, float centerY, float width, float height)
        {
            CenterX = centerX;
            CenterY = centerY;
            Width = Math.Max(0f, width);
            Height = Math.Max(0f, height);
        }

        public float Left => CenterX - Width * 0.5f;
        public float Right => CenterX + Width * 0.5f;
        public float Top => CenterY + Height * 0.5f;
        public float Bottom => CenterY - Height * 0.5f;

        public RectF WithCenter(float x, float y) => new RectF(x, y, Width, Height);
    }

    /// <summary>
    /// Pure layout for the Wingview third-person HUD's screen-fixed elements
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Third-person HUD").
    /// Every rect is derived from the live screen size so it holds at any aspect ratio; the
    /// presentation layer (<c>Presentation/ThirdPersonHudCluster.cs</c>) only turns these into
    /// RectTransform anchors, it does not compute geometry itself.
    ///
    /// The aircraft-frame guard is authoritative: <see cref="KeepClear"/> nudges an element clear
    /// of the frame rather than trusting the nominal offsets alone, so "never gets our elements"
    /// holds even where the spec's screen-fraction figures for the frame and an element (the HDG
    /// box in particular) would otherwise land inside the same band.
    /// </summary>
    internal static class ThirdPersonHudLayout
    {
        public const float BoxWidth = 110f, BoxHeight = 44f;
        public const float HdgWidth = 72f, HdgHeight = 28f;
        public const float SubLineHeight = 14f;
        public const float BarWidth = 5f, BarHeight = 44f, BarGap = 4f;

        public const float BoxOffsetXFraction = 0.17f;
        public const float HdgOffsetYFraction = 0.30f;

        public const float FrameWidthFraction = 0.32f;
        public const float FrameHeightFraction = 0.30f;

        public const float GuardMargin = 6f;
        public const float SafeMargin = 24f;

        public const float TargetCardWidth = 240f;
        public const float TargetCardAspect = 16f / 9f;
        public const float TargetCardHeaderHeight = 16f;

        public static RectF Screen(float width, float height) => new RectF(0f, 0f, width, height);

        /// <summary>Screen-centre-bottom box the vanilla aircraft model occupies in the Wingview
        /// composition; bottom-anchored, centred horizontally.</summary>
        public static RectF AircraftFrame(float width, float height)
        {
            float h = FrameHeightFraction * height;
            float centerY = -height * 0.5f + h * 0.5f;
            return new RectF(0f, centerY, FrameWidthFraction * width, h);
        }

        public static RectF SpeedBox(float width, float height) =>
            KeepClear(new RectF(-BoxOffsetXFraction * width, 0f, BoxWidth, BoxHeight), AircraftFrame(width, height));

        public static RectF AltitudeBox(float width, float height) =>
            KeepClear(new RectF(BoxOffsetXFraction * width, 0f, BoxWidth, BoxHeight), AircraftFrame(width, height));

        public static RectF HeadingBox(float width, float height) =>
            KeepClear(new RectF(0f, HdgOffsetYFraction * height, HdgWidth, HdgHeight), AircraftFrame(width, height));

        /// <summary>The thin sub-line under a box (e.g. "M .46  G 1.0" under SPD).</summary>
        public static RectF SubLine(RectF box) => new RectF(box.CenterX, box.Bottom - SubLineHeight * 0.5f - 2f, box.Width, SubLineHeight);

        /// <summary>Slim vertical bar beside a box, on its outward side (away from screen centre).</summary>
        public static RectF SideBar(RectF box, bool outwardIsLeft)
        {
            float x = outwardIsLeft ? box.Left - BarGap - BarWidth * 0.5f : box.Right + BarGap + BarWidth * 0.5f;
            return new RectF(x, box.CenterY, BarWidth, BarHeight);
        }

        /// <summary>Bottom-right target-camera card, above the safe margin, width-capped and
        /// aspect-fit; collapses (zero height) when there is nothing to show.</summary>
        public static RectF TargetCard(float width, float height, bool visible)
        {
            if (!visible) return new RectF(width * 0.5f - SafeMargin - TargetCardWidth * 0.5f, -height * 0.5f + SafeMargin, TargetCardWidth, 0f);
            float feedHeight = TargetCardWidth / TargetCardAspect;
            float cardHeight = TargetCardHeaderHeight + feedHeight;
            float centerX = width * 0.5f - SafeMargin - TargetCardWidth * 0.5f;
            float centerY = -height * 0.5f + SafeMargin + cardHeight * 0.5f;
            return new RectF(centerX, centerY, TargetCardWidth, cardHeight);
        }

        public static bool Overlaps(RectF a, RectF b) =>
            a.Left < b.Right && a.Right > b.Left && a.Bottom < b.Top && a.Top > b.Bottom;

        public static bool Inside(RectF r, RectF screen) =>
            r.Left >= screen.Left - 0.01f && r.Right <= screen.Right + 0.01f &&
            r.Bottom >= screen.Bottom - 0.01f && r.Top <= screen.Top + 0.01f;

        /// <summary>
        /// Nudges <paramref name="r"/> straight up, clear of <paramref name="frame"/>'s top edge
        /// plus <see cref="GuardMargin"/>, when it would otherwise overlap. A no-op rect (already
        /// clear) is returned unchanged. Vertical-only: every guarded element in this layout sits
        /// on the frame's vertical axis, so a vertical push is always the minimal correction.
        /// </summary>
        public static RectF KeepClear(RectF r, RectF frame)
        {
            if (!Overlaps(r, frame)) return r;
            float clearY = frame.Top + GuardMargin + r.Height * 0.5f;
            return r.WithCenter(r.CenterX, clearY);
        }
    }
}

using System;

namespace BoscaliSummer.Modules.Hud.Domain
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
    /// box in particular) would otherwise land inside the same band. The TAC BOARD's box is the
    /// second thing the cluster keeps clear of (<see cref="KeepClearOf"/>): an element it would
    /// overlap moves the shortest way out.
    /// </summary>
    internal static class ThirdPersonHudLayout
    {
        public const float BoxWidth = 120f, BoxHeight = 56f;
        public const float HdgWidth = 100f, HdgHeight = 32f;
        public const float BarWidth = 6f, BarHeight = 56f, BarGap = 5f;

        public const float BoxOffsetXFraction = 0.17f;
        public const float HdgOffsetYFraction = 0.30f;

        public const float FrameWidthFraction = 0.32f;
        public const float FrameHeightFraction = 0.30f;

        public const float GuardMargin = 6f;
        public const float SafeMargin = 24f;

        /// <summary>The missile-cam readout: centred, below the middle of the screen (the missile and its target own the centre).</summary>

        /// <summary>The native HUD text size slider's default; the cluster and the readout scale with the player's own setting.</summary>
        public const float NativeTextSize = 40f, MinScale = 0.75f, MaxScale = 1.6f;

        /// <summary>The player's native <c>hudTextSize</c> as a layout scale (1 at the default 40; clamped; 1 when unknown).</summary>
        public static float TextScale(float hudTextSize) =>
            float.IsNaN(hudTextSize) || hudTextSize <= 0f ? 1f : Math.Max(MinScale, Math.Min(MaxScale, hudTextSize / NativeTextSize));

        public const float TargetCardWidth = 300f;
        /// <summary>Clear space between the target card and the missile feed card stacked on it.</summary>
        public const float CardGap = 12f;
        public const float TargetCardAspect = 16f / 9f;
        public const float TargetCardHeaderHeight = 22f;

        /// <summary>Screen-centre-bottom box the vanilla aircraft model occupies in the Wingview
        /// composition; bottom-anchored, centred horizontally.</summary>
        public static RectF AircraftFrame(float width, float height)
        {
            float h = FrameHeightFraction * height;
            float centerY = -height * 0.5f + h * 0.5f;
            return new RectF(0f, centerY, FrameWidthFraction * width, h);
        }

        /// <summary>The SPD and ALT boxes carry a slim bar on their outer side; the board is kept this far from the box so the bar is clear of it too.</summary>
        private const float BarClear = BarGap + BarWidth;

        /// <summary>The boxes, clear of the aircraft frame and of <paramref name="board"/> (the TAC BOARD's box in the same space; the default, no size, is no board).</summary>
        public static RectF SpeedBox(float width, float height, RectF board = default) =>
            KeepClear(KeepClearOf(new RectF(-BoxOffsetXFraction * width, 0f, BoxWidth, BoxHeight), board, GuardMargin + BarClear), AircraftFrame(width, height));

        public static RectF AltitudeBox(float width, float height, RectF board = default) =>
            KeepClear(KeepClearOf(new RectF(BoxOffsetXFraction * width, 0f, BoxWidth, BoxHeight), board, GuardMargin + BarClear), AircraftFrame(width, height));

        public static RectF HeadingBox(float width, float height, RectF board = default) =>
            KeepClear(KeepClearOf(new RectF(0f, HdgOffsetYFraction * height, HdgWidth, HdgHeight), board, GuardMargin), AircraftFrame(width, height));

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

        /// <summary>The missile feed card: the target card's box, <paramref name="raised"/> one slot up (over the target card in third person, over NAV DATA in the cockpit).</summary>
        public static RectF MissileCard(float width, float height, bool raised)
        {
            RectF card = TargetCard(width, height, true);
            return raised ? card.WithCenter(card.CenterX, card.CenterY + card.Height + CardGap) : card;
        }

        public static bool Overlaps(RectF a, RectF b) =>
            a.Left < b.Right && a.Right > b.Left && a.Bottom < b.Top && a.Top > b.Bottom;

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

        /// <summary>
        /// Moves <paramref name="r"/> clear of <paramref name="board"/> grown by <paramref name="margin"/>, by the shortest of the four moves that do it (left, right, up or down): the board hangs from the top of the
        /// screen, so an element in its way usually goes down or sideways. A rect already clear, or no board (no size), is returned unchanged.
        /// </summary>
        public static RectF KeepClearOf(RectF r, RectF board, float margin)
        {
            if (board.Width <= 0f || board.Height <= 0f) return r;
            float left = board.Left - margin, right = board.Right + margin, top = board.Top + margin, bottom = board.Bottom - margin;
            if (!(r.Left < right && r.Right > left && r.Bottom < top && r.Top > bottom)) return r;
            float toLeft = r.Right - left, toRight = right - r.Left, down = r.Top - bottom, up = top - r.Bottom;
            float best = Math.Min(Math.Min(toLeft, toRight), Math.Min(down, up));
            if (best == down) return r.WithCenter(r.CenterX, r.CenterY - down);
            if (best == toLeft) return r.WithCenter(r.CenterX - toLeft, r.CenterY);
            if (best == toRight) return r.WithCenter(r.CenterX + toRight, r.CenterY);
            return r.WithCenter(r.CenterX, r.CenterY + up);
        }
    }

    /// <summary>How close the ground is, from radar altitude and sink rate.</summary>
    internal enum TerrainCue { None, Low, PullUp }

    /// <summary>Pure cue rules for the third-person cluster (2026-10-07). No UnityEngine.</summary>
    internal static class HudCues
    {
        /// <summary>Radar altitude below this is shown on the ALT box instead of its caption.</summary>
        public const float ShowRadarBelowM = 300f;
        /// <summary>LOW: under this AGL while sinking faster than <see cref="LowSinkMps"/>.</summary>
        public const float LowAglM = 150f;
        public const float LowSinkMps = 5f;
        /// <summary>PULL UP: ground reached in under this many seconds at the current sink rate.</summary>
        public const float PullUpSeconds = 5f;

        public static TerrainCue Terrain(float radarAltM, float climbMps)
        {
            if (float.IsNaN(radarAltM) || float.IsNaN(climbMps) || radarAltM >= ShowRadarBelowM || climbMps >= 0f)
                return TerrainCue.None;
            if (radarAltM / -climbMps < PullUpSeconds) return TerrainCue.PullUp;
            return radarAltM < LowAglM && -climbMps > LowSinkMps ? TerrainCue.Low : TerrainCue.None;
        }

        private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>The nearest of the eight compass points for a heading in degrees.</summary>
        public static string Cardinal(float headingDeg)
        {
            float h = ((headingDeg % 360f) + 360f) % 360f;
            return Points[(int)((h + 22.5f) / 45f) % 8];
        }
    }
}

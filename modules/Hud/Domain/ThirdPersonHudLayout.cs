using System;
using BoscaliSummer.Core.Ui;

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

        /// <summary>The feed column's cards are all one size (2026-10-20 rework; the UAV card uses the same 264 / 18, Core/Ui/FeedCorner).</summary>
        public const float TargetCardWidth = 264f;
        /// <summary>Clear space between the target card and the missile feed card stacked on it.</summary>
        public const float CardGap = 10f;
        public const float TargetCardAspect = 16f / 9f;
        public const float TargetCardHeaderHeight = 18f;

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

        /// <summary>The bar's label (THR / FUEL nn) hangs under the bar: this far down and this wide either side of it.</summary>
        public const float BarLabelDrop = 18f, BarLabelHalfWidth = 35f;

        /// <summary>The ALT box with its fuel bar and the label hanging under it: what a feed card stepping left must keep off.</summary>
        public static RectF AltitudeBlock(float width, float height, RectF board = default)
        {
            RectF box = AltitudeBox(width, height, board);
            float right = SideBar(box, outwardIsLeft: false).CenterX + BarLabelHalfWidth, bottom = box.Bottom - BarLabelDrop;
            return new RectF((box.Left + right) * 0.5f, (box.Top + bottom) * 0.5f, right - box.Left, box.Top - bottom);
        }

        /// <summary>Slim vertical bar beside a box, on its outward side (away from screen centre).</summary>
        public static RectF SideBar(RectF box, bool outwardIsLeft)
        {
            float x = outwardIsLeft ? box.Left - BarGap - BarWidth * 0.5f : box.Right + BarGap + BarWidth * 0.5f;
            return new RectF(x, box.CenterY, BarWidth, BarHeight);
        }

        /// <summary>A feed card's height: the header over a 16:9 picture.</summary>
        public const float CardHeight = TargetCardHeaderHeight + TargetCardWidth / TargetCardAspect;

        // The bottom-right feed column (2026-10-20; TargetCam moved here 2026-11-27): UAV holds the bottom-right slot when up,
        // TARGET CAM stacks on UAV (or takes the bottom-right floor when no UAV is up), and MISSILE CAM stacks on the one
        // right-column card or steps left along the bottom-right floor so the column never grows past two cards tall.

        private static float RightColumnX(float width) => width * 0.5f - HudZones.Margin - TargetCardWidth * 0.5f;

        private static float FloorY(float height) => -height * 0.5f + SafeMargin;

        private static bool InRightColumn(RectF card, float rightCx) =>
            card.Height > 0f && Math.Abs(card.CenterX - rightCx) < TargetCardWidth * 0.5f;

        /// <summary>The smallest scale the target card's size follows (position keeps the true scale).</summary>
        public const float TargetCardMinScale = 0.5f;

        /// <summary>
        /// Bottom-right target-camera card: takes the bottom-right floor (or stacks on <paramref name="uav"/> when up),
        /// stepping left along the bottom-right floor if <paramref name="board"/> or <paramref name="keepOff"/> blocks the column.
        /// Collapses (zero height) when <paramref name="visible"/> is false.
        /// </summary>
        public static RectF TargetCard(float width, float height, bool visible, RectF uav = default, RectF board = default, RectF keepOff = default)
        {
            if (!visible) return new RectF(RightColumnX(width), FloorY(height), TargetCardWidth, 0f);
            return PlaceCornerCard(width, height, uav, default, board, keepOff);
        }

        /// <summary>
        /// The missile feed card, bottom right: stacks on <paramref name="uav"/> or <paramref name="target"/> when only one holds
        /// the right column, takes the bottom-right floor when neither is up, or steps left along the bottom-right floor when two
        /// cards already occupy the column or <paramref name="board"/> / <paramref name="keepOff"/> blocks it.
        /// </summary>
        public static RectF MissileCard(float width, float height, RectF uav = default, RectF board = default, RectF keepOff = default, RectF target = default) =>
            PlaceCornerCard(width, height, uav, target, board, keepOff);

        private static RectF PlaceCornerCard(float width, float height, RectF first, RectF second, RectF board, RectF keepOff)
        {
            float rightCx = RightColumnX(width), floor = FloorY(height);
            float minCx = -width * 0.5f + HudZones.Margin + TargetCardWidth * 0.5f;
            RectF paddedBoard = board.Width > 0f && board.Height > 0f
                ? new RectF(board.CenterX, board.CenterY, board.Width + 2f * GuardMargin, board.Height + 2f * GuardMargin)
                : default;

            bool firstRight = InRightColumn(first, rightCx), secondRight = InRightColumn(second, rightCx);
            int rightCount = (firstRight ? 1 : 0) + (secondRight ? 1 : 0);

            if (rightCount <= 1)
            {
                float bottom = rightCount == 0
                    ? floor
                    : Math.Max((firstRight ? first.Top : second.Top) + CardGap, floor);
                RectF primary = new RectF(rightCx, bottom + CardHeight * 0.5f, TargetCardWidth, CardHeight);
                if ((paddedBoard.Width <= 0f || !Overlaps(primary, paddedBoard)) &&
                    (keepOff.Height <= 0f || !Overlaps(primary, keepOff)) &&
                    (first.Height <= 0f || !Overlaps(primary, first)) &&
                    (second.Height <= 0f || !Overlaps(primary, second)))
                    return primary;
            }

            float cx = rightCx;
            if (firstRight) cx = Math.Min(cx, first.Left - CardGap - TargetCardWidth * 0.5f);
            if (secondRight) cx = Math.Min(cx, second.Left - CardGap - TargetCardWidth * 0.5f);
            float stepBottom = firstRight ? Math.Max(first.Bottom, floor) : secondRight ? Math.Max(second.Bottom, floor) : floor;

            RectF card = default;
            for (int step = 0; step < 4; step++)
            {
                cx = Math.Max(minCx, cx);
                card = new RectF(cx, stepBottom + CardHeight * 0.5f, TargetCardWidth, CardHeight);
                if (cx <= minCx + 0.01f) break;
                if (paddedBoard.Width > 0f && Overlaps(card, paddedBoard))
                {
                    cx = Math.Min(cx - CardGap - TargetCardWidth, board.Left - CardGap - TargetCardWidth * 0.5f);
                    continue;
                }
                if (first.Height > 0f && Overlaps(card, first))
                {
                    cx = first.Left - CardGap - TargetCardWidth * 0.5f;
                    continue;
                }
                if (second.Height > 0f && Overlaps(card, second))
                {
                    cx = second.Left - CardGap - TargetCardWidth * 0.5f;
                    continue;
                }
                break;
            }
            if (keepOff.Height > 0f && Overlaps(card, keepOff))
                card = card.WithCenter(card.CenterX, keepOff.Bottom - GuardMargin - CardHeight * 0.5f);
            return card;
        }

        internal static bool Overlaps(RectF a, RectF b) =>
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

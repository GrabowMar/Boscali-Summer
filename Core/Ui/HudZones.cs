
namespace BoscaliSummer.Core.Ui
{
    /// <summary>The HUD's zones (spec 2026-11-16 section 3): every element lives in exactly one.</summary>
    internal enum HudZone { Log, Comms, Cue, Notice, Route, TargetCam, Map, Lane, Callout, BoardTop, BoardSpine, Feeds, Centre }

    /// <summary>A rectangle, top-left origin, y down. Touching edges do not overlap.</summary>
    internal readonly struct ZoneRect
    {
        public readonly float X, Y, W, H;
        public ZoneRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right => X + W;
        public float Bottom => Y + H;
        public bool Overlaps(in ZoneRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    }

    /// <summary>
    /// The zone map of the HUD re-layout, in reference pixels of a 1080-high screen. Left zones keep their x, right zones their distance to the right edge, centre zones their distance to
    /// the centre line. The LANE is <see cref="TopCentre"/>'s three slots (width 312, 8..188). Pure: the test project links it.
    /// </summary>
    internal static class HudZones
    {
        public const float Margin = 12f, Gutter = 16f, RefHeight = 1080f, FullWidth = 1920f;
        private const float NarrowMin = 281f;
        /// <summary>The wide LOG / COMMS / NOTICE column's width.</summary>
        public const float WideLog = 562f;

        private enum Side { Left, Centre, Right }

        // x, y, w, h on the 1920 x 1080 reference screen.
        private static void Base(HudZone z, out Side side, out float x, out float y, out float w, out float h)
        {
            switch (z)
            {
                case HudZone.Log: side = Side.Left; x = 12; y = 12; w = 562; h = 101; break;
                case HudZone.Comms: side = Side.Left; x = 12; y = 129; w = 562; h = 154; break;
                case HudZone.Cue: side = Side.Left; x = 12; y = 299; w = 281; h = 96; break;
                case HudZone.Notice: side = Side.Left; x = 12; y = 411; w = 562; h = 16; break;
                case HudZone.Route: side = Side.Left; x = 12; y = 443; w = 281; h = 133; break;
                case HudZone.TargetCam: side = Side.Left; x = 12; y = 592; w = 281; h = 200; break;
                case HudZone.Map: side = Side.Left; x = 12; y = 808; w = 281; h = 260; break;
                case HudZone.Lane: side = Side.Centre; x = 960f - TopCentre.Width * 0.5f; y = TopCentre.StripTop; w = TopCentre.Width; h = TopCentre.BannerTop + TopCentre.BannerHeight - TopCentre.StripTop; break;
                case HudZone.Callout: side = Side.Centre; x = 888; y = 594; w = 140; h = 38; break;
                case HudZone.BoardTop: side = Side.Right; x = 1132; y = 6; w = 383; h = 134; break;
                case HudZone.BoardSpine: side = Side.Right; x = 1531; y = 6; w = 381; h = 660; break;
                case HudZone.Feeds: side = Side.Right; x = 1632; y = 714; w = 264; h = 342; break;
                default: side = Side.Centre; x = 576; y = 324; w = 768; h = 432; break;
            }
        }

        /// <summary>Reference pixels on a 1080-high screen that is <paramref name="refW"/> wide.</summary>
        public static ZoneRect Ref(HudZone zone, float refW)
        {
            Base(zone, out Side side, out float x, out float y, out float w, out float h);
            if (side == Side.Right) x = refW - (FullWidth - x);
            else if (side == Side.Centre) x = refW * 0.5f + (x - 960f);
            if (zone == HudZone.Log || zone == HudZone.Comms || zone == HudZone.Notice)
            {
                float laneLeft = refW * 0.5f - TopCentre.Width * 0.5f;
                w = System.Math.Max(NarrowMin, System.Math.Min(WideLog, laneLeft - Gutter - Margin));
            }
            return new ZoneRect(x, y, w, h);
        }

        /// <summary>Canvas pixels: <see cref="Ref"/> at the canvas's reference width, scaled by canvasH / 1080.</summary>
        public static ZoneRect Of(HudZone zone, float canvasW, float canvasH)
        {
            if (canvasH <= 0f) return default;
            float k = canvasH / RefHeight;
            ZoneRect r = Ref(zone, canvasW / k);
            return new ZoneRect(r.X * k, r.Y * k, r.W * k, r.H * k);
        }

        public static bool Narrow(float refW) => refW < FullWidth;

        /// <summary>Where COMMS starts: its own slot, or 16 under a LOG that grew past it.</summary>
        public static float CommsTop(float logBottom) => System.Math.Max(129f, logBottom + Gutter);

        /// <summary>Where CUE starts: its own slot, or 16 under a COMMS that grew past it.</summary>
        public static float CueTop(float commsBottom) => System.Math.Max(299f, commsBottom + Gutter);

        /// <summary>Where NOTICE starts: its own slot, or 16 under a CUE stack whose ink reaches past it (<paramref name="cueBottom"/> 0: no cue drawn).</summary>
        public static float NoticeTop(float cueBottom) => System.Math.Max(411f, cueBottom + Gutter);

        /// <summary>
        /// Whether the NOTICE line may be drawn at <paramref name="noticeTop"/>: ROUTE has a fixed home (its zone's top), so while a ROUTE face is shown the NOTICE line yields when its bottom plus the gutter
        /// would pass that top (a slid line, under a tall CUE stack). With no ROUTE face it is always drawn.
        /// </summary>
        public static bool NoticeFits(float noticeTop, bool routeShown)
        {
            Base(HudZone.Notice, out _, out _, out _, out _, out float h);
            Base(HudZone.Route, out _, out _, out float routeTop, out _, out _);
            return !routeShown || noticeTop + h + Gutter <= routeTop;
        }
    }
}

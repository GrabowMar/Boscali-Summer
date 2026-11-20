
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
    /// the centre line. The LANE is <see cref="TopCentre"/>'s slot above the game's heading tape (width 312, 144..210): the warning line or the ace card. The visor's frame and the tape's threat marks take no zone. Pure: the test project links it.
    /// The contract (bar v3):
    /// R1 one outer margin of 12 (FEEDS' bottom stays 24 up: the native name label sits at 1064..1080).
    /// R2 one gutter of 16.
    /// R3 the bar's SYS + INFO cells are the spine column (BOARD SPINE's x is SYS's).
    /// R4 one row grid for all the bar's cells.
    /// R5 the centre zones are symmetric about x 960.
    /// R6 LANE's bottom + the gutter stays at or above 232 (the heading tape).
    /// R7 the right and left columns are 281, 374 or 264 wide, nothing else.
    /// R8 the top band reads LOG | LANE | BAR, the bar's left edge at least LANE's right edge + 16.
    /// Under 16:9 (<see cref="Narrow"/>) the board is the 448-wide STACK: BOARD TOP and BOARD SPINE take its size, CENTRE shrinks between the two columns and NOTICE stops a gutter short of CENTRE.
    /// </summary>
    internal static class HudZones
    {
        public const float Margin = 12f, Gutter = 16f, RefHeight = 1080f, FullWidth = 1920f;
        private const float NarrowMin = 281f;
        /// <summary>The wide LOG / COMMS / NOTICE column's width.</summary>
        public const float WideLog = 562f, CommsHeight = 154f;
        /// <summary>The STACK board at the default Scale 0.85: 448 x 0.85 wide, its two bar lines (202) x 0.85 high, the spine round(12 + 218 x 0.85) down; the spine's bottom stays 666.</summary>
        public const float StackW = 381f, StackTopH = 172f, StackSpineTop = 197f, SpineBottom = 666f;
        private const float CentreW = 740f;

        private enum Side { Left, Centre, Right }

        // x, y, w, h on the 1920 x 1080 reference screen.
        private static void Base(HudZone z, out Side side, out float x, out float y, out float w, out float h)
        {
            switch (z)
            {
                case HudZone.Log: side = Side.Left; x = 12; y = 12; w = 562; h = 101; break;
                case HudZone.Comms: side = Side.Left; x = 12; y = 129; w = 562; h = CommsHeight; break;
                case HudZone.Cue: side = Side.Left; x = 12; y = 299; w = 281; h = 96; break;
                case HudZone.Notice: side = Side.Left; x = 12; y = 411; w = 562; h = 16; break;
                case HudZone.Route: side = Side.Left; x = 12; y = 443; w = 281; h = 133; break;
                case HudZone.TargetCam: side = Side.Left; x = 12; y = 592; w = 281; h = 200; break;
                case HudZone.Map: side = Side.Left; x = 12; y = 808; w = 281; h = 260; break;
                case HudZone.Lane: side = Side.Centre; x = 960f - TopCentre.Width * 0.5f; y = TopCentre.LineTop; w = TopCentre.Width; h = TopCentre.CardHeight; break;
                case HudZone.Callout: side = Side.Centre; x = 890; y = 641; w = 140; h = 38; break;      // centred on x 960 and on CombatAlerts.CalloutDown (y 660)
                case HudZone.BoardTop: side = Side.Right; x = 1105; y = 12; w = 803; h = 82; break;      // ONE BAR: the 944 x 96 bar at the default Scale 0.85, right edge 1908
                case HudZone.BoardSpine: side = Side.Right; x = 1534; y = 107; w = 374; h = SpineBottom - 107; break;      // the 440 column, 16 under the bar; its bottom stays 666
                case HudZone.Feeds: side = Side.Right; x = 1644; y = 714; w = 264; h = 342; break;
                default: side = Side.Centre; x = 960f - CentreW * 0.5f; y = 324; w = CentreW; h = 432; break;      // left = NOTICE's right (574) + 16
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
            if (!Narrow(refW)) return new ZoneRect(x, y, w, h);
            // Under 16:9 the board is STACK: the two right zones take its size, CENTRE fits between the two columns and NOTICE stops a gutter short of it (no recursion: CENTRE's left is worked out here).
            float centreW = System.Math.Max(0f, System.Math.Min(CentreW, refW - 2f * (Margin + StackW + Gutter))), centreLeft = refW * 0.5f - centreW * 0.5f;
            switch (zone)
            {
                case HudZone.BoardTop: return new ZoneRect(refW - Margin - StackW, Margin, StackW, StackTopH);
                case HudZone.BoardSpine: return new ZoneRect(refW - Margin - StackW, StackSpineTop, StackW, SpineBottom - StackSpineTop);
                case HudZone.Centre: return new ZoneRect(centreLeft, y, centreW, h);
                case HudZone.Notice: return new ZoneRect(x, y, System.Math.Min(w, centreLeft - Gutter - Margin), h);
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

        /// <summary>The LOG / COMMS / NOTICE column's width on a screen <paramref name="refW"/> wide in 1080-high design units: the one place painters take it from.</summary>
        public static float ColumnWidth(float refW) => Ref(HudZone.Comms, refW).W;

        // Half a pixel of slack: the reference width is a float quotient, and a 16:9 screen can land an ulp under 1920.
        public static bool Narrow(float refW) => refW < FullWidth - 0.5f;

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

using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>
    /// The player's own autopilot as a gauge of the TAC BOARD's row (HUD v8): the lateral hold as the mode word (LVL, NAV 2/5 with the leg being flown, HDG 090), the vertical
    /// or speed hold under it (ALT5000, VS+2.0, SPD600), and a NAV route's progress on the rail. Shown only while a hold is engaged. It runs wherever the player's state
    /// runs, host and client. The landing autopilot is the Autopilot module's gauge ("land").
    /// </summary>
    internal static class PlayerAutopilotGauge
    {
        private static string lateral, vertical;
        private static int lateralKey = -1, verticalKey = -1;

        public static void Register() => ScopeGauges.Register("ap", Read);

        public static bool Read(out GaugeSpec gauge)
        {
            gauge = default;
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null || !ap.Session.Engaged) return false;
            HoldSpec h = ap.Session.Spec;
            bool nav = h.Lateral == LateralHold.Nav && ap.Nav.Count > 0;
            int heading = ((int)Math.Round(h.HeadingDeg) % 360 + 360) % 360;
            int key = (int)h.Lateral * 1000000 + (h.Lateral == LateralHold.Heading ? heading : nav ? ap.Nav.Index * 100 + ap.Nav.Count : 0);
            if (key != lateralKey) { lateralKey = key; lateral = LateralWord(h.Lateral, heading, ap.Nav.Index, ap.Nav.Count); }
            int vkey = h.Vertical == VerticalHold.Altitude ? 1000000 + (int)Math.Round(h.AltitudeM / 10f) : h.Vertical == VerticalHold.VerticalSpeed ? 2000000 + (int)Math.Round(h.VerticalSpeedMps * 10f) : h.Speed ? 3000000 + (int)Math.Round(h.SpeedMps * 3.6f) : 0;
            if (vkey != verticalKey) { verticalKey = vkey; vertical = VerticalWord(h); }
            gauge = new GaugeSpec
            {
                Id = "ap", Label = "AUTOPILOT", Kind = GaugeKind.Mode, Text = lateral, Sub = vertical, Value = nav ? Math.Min(1f, (ap.Nav.Index + 0.5f) / ap.Nav.Count) : 0f, Tone = ScopeTone.Ink, Rank = 45,
            };
            return true;
        }

        private static string LateralWord(LateralHold mode, int heading, int index, int count) =>
            mode == LateralHold.Level ? "LVL" : mode == LateralHold.Heading ? "HDG " + heading.ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            : mode == LateralHold.Nav ? (count > 0 && index >= 0 ? "NAV " + (index + 1) + "/" + count : "NAV") : "HOLD";

        /// <summary>The vertical hold, else the speed hold, in seven characters or fewer.</summary>
        private static string VerticalWord(in HoldSpec h)
        {
            if (h.Vertical == VerticalHold.Altitude) return h.AltitudeM < 10000f ? "ALT" + (int)Math.Round(h.AltitudeM) : "ALT" + (int)Math.Round(h.AltitudeM / 1000f) + "K";
            if (h.Vertical == VerticalHold.VerticalSpeed) return "VS" + h.VerticalSpeedMps.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);
            return h.Speed ? "SPD" + (int)Math.Round(h.SpeedMps * 3.6f) : null;
        }
    }
}

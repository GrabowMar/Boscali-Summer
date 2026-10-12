using System;
using System.Globalization;
using NOAvionics;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Every sentence and number format of the Met Office window, pure so the wording is testable. All numbers go through <see cref="AvNum"/> (no culture can turn '.' into ',').</summary>
    internal static class MetOfficeText
    {
        public static string Hex8(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>"x3 · 27 M/S": the pace and the speed it gives (spec 5.4), so a fast sky is never mistaken for a bug. Pace 0 (a client, whose own Pace setting says nothing about the host's) prints the speed alone.</summary>
        public static string PaceValue(int pace, float speedMps) => (pace > 0 ? "x" + pace + " · " : "") + AvNum.Fixed(speedMps, 0) + " M/S";

        /// <summary>The pace stepper's value: "x3 · 27 M/S · WIND 12" (the mission wind the speed was scaled by).</summary>
        public static string PaceRow(int pace, float speedMps, float windMps) => PaceValue(pace, speedMps) + " · WIND " + AvNum.Fixed(windMps, 0);

        public static string TapeLine(bool running, int pace, float speedMps) =>
            (running ? "RUNNING" : "HELD") + " // TAPE " + PaceValue(pace, speedMps);

        public static string HoldLabel(bool running) => running ? "HOLD TAPE" : "RESUME TAPE";

        public static string WindLine(int fromDeg, float mps, float kts) =>
            "FROM " + fromDeg.ToString("000", CultureInfo.InvariantCulture) + "° · " + AvNum.Fixed(mps, 0) + " M/S " + AvNum.Fixed(kts, 0) + " KT";

        /// <summary>The chart's wind strip: "WIND 270° · 12 M/S 23 KT", or "WIND CALM" when there is none (a bearing of nothing means nothing). A non-finite wind reads calm.</summary>
        public static string WindStrip(int fromDeg, float mps)
        {
            if (!(mps >= 0.3f) || float.IsInfinity(mps)) return "WIND CALM";
            return "WIND " + fromDeg.ToString("000", CultureInfo.InvariantCulture) + "° · " + AvNum.Fixed(mps, 0) + " M/S " + AvNum.Fixed(mps * 1.943844f, 0) + " KT";
        }

        /// <summary>The theater frame's label on two lines, whole kilometres (one line with the mockup's decimals is a third of the chart wide and cannot stand between two fronts): "THEATER" over "164 X 82 KM"; empty without a frame.</summary>
        public static string Theater(float halfXKm, float halfZKm) =>
            halfXKm > 0f && halfZKm > 0f ? "THEATER\n" + AvNum.Fixed(halfXKm * 2f, 0) + " X " + AvNum.Fixed(halfZKm * 2f, 0) + " KM" : "";

        public static string FrontWord(MetFrontKind kind)
        {
            switch (kind)
            {
                case MetFrontKind.Warm: return "WARM FRONT";
                case MetFrontKind.Occluded: return "OCCLUDED FRONT";
                case MetFrontKind.Squall: return "SQUALL LINE";
                default: return "COLD FRONT";
            }
        }

        /// <summary>"62 KM W" for a front's distance and the compass word of its bearing.</summary>
        public static string Where(float km, float bearingDeg) => AvNum.Fixed(km, 0) + " KM " + WeatherWords.Cardinal(bearingDeg);

        /// <summary>A placed set-piece's distance and bearing from the camera (CamX/CamZ are 0 with no camera), like every other "where" in the console.</summary>
        public static string SetPieceWhere(float x, float z, float camX, float camZ)
        {
            float dx = x - camX, dz = z - camZ;
            return Where((float)Math.Sqrt(dx * dx + dz * dz), WeatherMath.VectorToHeading(dx, dz));
        }

        /// <summary>"ETA 30'" while a front moves, "STATIC" while it stands still (today every front does).</summary>
        public static string Eta(float minutes) =>
            float.IsNaN(minutes) || float.IsInfinity(minutes) || minutes < 0f ? "STATIC" : "ETA " + AvNum.Fixed(minutes, 0) + "'";

        public static string OutlookLabel(int minutes) => minutes == 0 ? "NOW" : "+" + minutes;

        public static string Cover(float cover01) => AvNum.PercentWhole(cover01);

        /// <summary>The METAR-style group of one layer: "BKN105" (oktas, base in hundreds of feet), or "" under 5% cover.</summary>
        public static string LayerGroup(float cover, float baseMetres)
        {
            string oktas = WeatherMetar.Oktas(cover);
            return oktas.Length == 0 ? "" : oktas + WeatherMetar.Height(baseMetres);
        }

        /// <summary>"8.3 KM" for a layer base.</summary>
        public static string BaseKm(float metres) => AvNum.Fixed(metres / 1000f, 1) + " KM";

        public static string SkyHead(int skyNumber, string name) => skyNumber > 0 ? "#" + skyNumber + " " + name : name;

        public static string StepperValue(int skyNumber, string classCode, int indexInClass, int countInClass) =>
            "#" + skyNumber + " " + classCode + " " + indexInClass + " / " + countInClass;

        public static string PinLabel(int skyNumber) => "PIN #" + skyNumber;

        public static string RerollSub(int salt) => salt + "→" + ((salt + 1) & 255);

        /// <summary>The identity card's third line: the mockup's "▲ 7→8" on the RE-ROLL button, as words and the glyph.</summary>
        public static string RerollLine(int salt) => "NEXT RE-ROLL ▲ CUT · LAYOUT " + RerollSub(salt);

        /// <summary>The identity card: mode and seed on the first line (a client has no mode of the host's to show: the seed alone), layout and tape on the second.</summary>
        public static string IdentityHead(string mode, string seedHex) => string.IsNullOrEmpty(mode) ? "SEED " + seedHex : mode.ToUpperInvariant() + " · SEED " + seedHex;
        public static string IdentitySub(int salt, string tapeHex) => "LAYOUT " + salt + " / 255 · TAPE T-" + tapeHex;

        /// <summary>
        /// Which of HERE / 30 KM AHEAD / DEFAULT the anchor row latches, as an index into the row (-1 none). The anchor is fixed in the world and the camera is not, so the choice is the
        /// adapter's record of the host's last command (<see cref="MetOfficeModel.AnchorMark"/>), not a comparison with the camera now (that would unlatch HERE after four seconds of flight).
        /// </summary>
        public static int AnchorChoice(MetOfficeModel m) => !m.HasAnchor ? 2 : m.AnchorMark;

        /// <summary>The chart's anchor label: "ANCHOR", plus "+30 KM" when it stands off the camera (the mockup).</summary>
        public static string AnchorLabel(float offsetKm) => offsetKm >= 1f ? "ANCHOR +" + AvNum.Fixed(offsetKm, 0) + " KM" : "ANCHOR";

        /// <summary>The chart's camera label: "CAM 045°". The heading rounds to a whole degree, as the label prints it.</summary>
        public static string CameraLabel(float headingDeg) => "CAM " + ((int)Math.Round(float.IsNaN(headingDeg) || float.IsInfinity(headingDeg) ? 0f : headingDeg)).ToString("000", CultureInfo.InvariantCulture) + "°";

        /// <summary>The short chart label of set-piece <paramref name="i"/>: "SC 62%", "LENT 100%", "EYE".</summary>
        public static string SetShortLabel(int i, float strength) => i == 3 || i == 5 ? MetCatalog.SetShort[i] : MetCatalog.SetShort[i] + " " + Cover(strength);

        /// <summary>Mission seconds as the title bar's "T+00:41:07".</summary>
        public static string MissionClock(float seconds)
        {
            int s = seconds < 0f || float.IsNaN(seconds) ? 0 : (int)seconds;
            return "T+" + (s / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" + (s / 60 % 60).ToString("00", CultureInfo.InvariantCulture) +
                ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"111538Z OCT 26": cosmetic flavour taken from the real clock (the mockup's DTG).</summary>
        public static string Dtg(DateTime utc) =>
            utc.ToString("ddHHmm", CultureInfo.InvariantCulture) + "Z " + utc.ToString("MMM yy", CultureInfo.InvariantCulture).ToUpperInvariant();

        /// <summary>The log card holds 43 mono-12 columns per line (315 px at 7.2 px). A line is "hh:mm:ss HOST " (14) plus what happened, so what is at most this long.</summary>
        public const int LogMaxWhat = 28, LogColumns = 43;

        /// <summary>One event-log line: the mission time, who acted and what happened. What is cut at <see cref="LogMaxWhat"/>, so a line can never wrap out of the card (the notes below already fit).</summary>
        public static string LogLine(float missionSeconds, string who, string what)
        {
            int s = missionSeconds < 0f || float.IsNaN(missionSeconds) || float.IsInfinity(missionSeconds) ? 0 : (int)missionSeconds;
            what = what ?? "";
            if (what.Length > LogMaxWhat) what = what.Substring(0, LogMaxWhat);
            return (s / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" + (s / 60 % 60).ToString("00", CultureInfo.InvariantCulture) +
                ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture) + " " + who + " " + what;
        }

        // The host-log notes, one per command.
        public static string HoldNote(bool moves) => "TAPE " + (moves ? "RESUMED" : "HELD");
        public static string PaceNote(int before, int after) => "PACE " + before + "→" + after;
        public static string PinNote(int skyIndex) => "PIN #" + (skyIndex + 1) + " " + MetCatalog.ClassCodes[(int)SkyLibrary.ClassOf(skyIndex)] + " ▲ CUT";
        public static string AutoNote() => "AUTO SKY + RESUME ▲ CUT";
        public static string RerollNote(int salt) => "RE-ROLL " + RerollSub(salt) + " ▲ CUT";
        public static string SetNote(int set, bool on) => MetCatalog.SetNames[set] + (on ? " ON" : " OFF") + " FADE 20 S";
        public static string AnchorNote(float metres) => metres < 1f ? "ANCHOR HERE FADE 20 S" : "ANCHOR +" + AvNum.Fixed(metres / 1000f, 0) + " KM FADE 20 S";
        public static string DefaultNote() => "ANCHOR DEFAULT FADE 20 S";
        public static string RainNote(float? intensity) =>
            "RAIN " + (!intensity.HasValue ? "AUTO" : intensity.Value <= 0.01f ? "DRY" : (intensity.Value < 0.5f ? "LIGHT " : "HEAVY ") + AvNum.Fixed(intensity.Value * 100f, 0) + "%");
        public static string ScenarioNote(int scenario) => MetCatalog.ScenarioNames[scenario] + " ▲ CUT";
        /// <summary>The STORY stepper's value: "FRONTAL SYSTEM 1 / 5" for a chosen story, "AUTO · COLD FRONT" while the mission class picks (the story under the camera), "— / 5" with no tape.</summary>
        public static string StoryValue(int pick, int count, string storyUnderCamera, bool hasStory) =>
            pick >= 0 ? (storyUnderCamera ?? "").ToUpperInvariant() + " " + (pick + 1) + " / " + count
            : hasStory ? "AUTO · " + (storyUnderCamera ?? "").ToUpperInvariant() : "— / " + count;
        public static string StoryNote(int story) => story < 0 || story >= WeatherStories.Count ? "STORY AUTO ▲ CUT" : "STORY " + (story + 1) + " " + WeatherStories.Name(story).ToUpperInvariant() + " ▲ CUT";
    }
}

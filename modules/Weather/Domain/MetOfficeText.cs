using System;
using System.Globalization;
using NOAvionics;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Every sentence and number format of the weather console (and the ENV briefing's front line), pure so the wording is testable. All numbers go through <see cref="AvNum"/> (no culture can turn '.' into ',').</summary>
    internal static class MetOfficeText
    {
        /// <summary>"WIND 270° · 12 M/S 23 KT", or "WIND CALM" when there is none (a bearing of nothing means nothing). A non-finite wind reads calm.</summary>
        public static string WindStrip(int fromDeg, float mps)
        {
            if (!(mps >= 0.3f) || float.IsInfinity(mps)) return "WIND CALM";
            return "WIND " + fromDeg.ToString("000", CultureInfo.InvariantCulture) + "° · " + AvNum.Fixed(mps, 0) + " M/S " + AvNum.Fixed(mps * 1.943844f, 0) + " KT";
        }

        public static string FrontWord(MetFrontKind kind) => kind switch
        {
            MetFrontKind.Warm => "WARM FRONT",
            MetFrontKind.Occluded => "OCCLUDED FRONT",
            MetFrontKind.Squall => "SQUALL LINE",
            _ => "COLD FRONT",
        };

        /// <summary>"62 KM W" for a front's distance and the compass word of its bearing.</summary>
        public static string Where(float km, float bearingDeg) => AvNum.Fixed(km, 0) + " KM " + WeatherWords.Cardinal(bearingDeg);

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

        /// <summary>Mission seconds as the title bar's "T+00:41:07".</summary>
        public static string MissionClock(float seconds)
        {
            int s = seconds < 0f || float.IsNaN(seconds) ? 0 : (int)seconds;
            return "T+" + (s / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" + (s / 60 % 60).ToString("00", CultureInfo.InvariantCulture) +
                ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"111538Z OCT 26": cosmetic flavour taken from the real clock.</summary>
        public static string Dtg(DateTime utc) =>
            utc.ToString("ddHHmm", CultureInfo.InvariantCulture) + "Z " + utc.ToString("MMM yy", CultureInfo.InvariantCulture).ToUpperInvariant();
    }
}

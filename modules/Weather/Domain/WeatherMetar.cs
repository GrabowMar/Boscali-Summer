using System;
using System.Text;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The METAR-style line of the ENV screen (spec 8.4), derived only from the active layers at a point: each layer's cover to
    /// oktas (under 0.05 none, under 0.19 FEW, under 0.44 SCT, under 0.81 BKN, else OVC) and its base in hundreds of feet, lowest
    /// first, then the weather (-RA, RA, +RA by rate; TS where lightning is active). No hand-written string per sky.
    /// On a map with a climate the precipitation reads SN at freezing surface air and the line ends with the temperature group ("M09/M12").
    /// </summary>
    internal static class WeatherMetar
    {
        public const float FeetPerMetre = 3.28084f;

        /// <summary>FEW, SCT, BKN or OVC for a layer cover; empty under 0.05.</summary>
        public static string Oktas(float cover)
        {
            if (cover < 0.05f) return "";
            if (cover < 0.19f) return "FEW";
            if (cover < 0.44f) return "SCT";
            if (cover < 0.81f) return "BKN";
            return "OVC";
        }

        /// <summary>A base height in hundreds of feet, three digits, capped at 999.</summary>
        public static string Height(float metres)
        {
            int hundreds = (int)Math.Round(Math.Max(0f, metres) * FeetPerMetre / 100f);
            return Math.Min(999, hundreds).ToString("000");
        }

        public static string Rain(float mmPerHour)
        {
            if (mmPerHour < 0.1f) return "";
            return mmPerHour < 2.5f ? "-RA" : mmPerHour < 10f ? "RA" : "+RA";
        }

        public static string Line(in WeatherPoint p)
        {
            var text = new StringBuilder();
            void Group(float cover, float baseMetres)
            {
                string oktas = Oktas(cover);
                if (oktas.Length == 0) return;
                if (text.Length > 0) text.Append(' ');
                text.Append(oktas).Append(Height(baseMetres));
            }
            Group(p.LowCover, p.CloudBase);
            Group(p.MidCover, p.MidBase);
            Group(p.HighCover, p.HighBase);
            if (text.Length == 0) text.Append("SKC");
            bool climate = WeatherClimate.Local != null;
            string rain = Rain(p.RainRate);
            if (rain.Length > 0) text.Append(' ').Append(climate && WeatherClimate.Snow(p.Temperature) >= 0.5f ? rain.Replace("RA", "SN") : rain);
            if (p.LightningRate > 0.05f) text.Append(" TS");
            if (climate) text.Append(' ').Append(WeatherClimate.MetarDegrees(p.Temperature)).Append('/').Append(WeatherClimate.MetarDegrees(p.Dewpoint));
            return text.ToString();
        }
    }
}

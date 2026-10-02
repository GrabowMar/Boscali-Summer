using System;
using System.Globalization;
using System.Text;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum FlightCategory : byte
    {
        Vfr = 0,
        Mvfr = 1,
        Ifr = 2,
        Lifr = 3,
    }

    /// <summary>One line of the forecast: the same field, sampled ahead.</summary>
    internal struct ForecastEntry
    {
        public float OffsetSeconds;
        public WeatherRegimeType Regime;
        public float RainRate;
        public float Cover;
        public float WindFrom;
        public float WindSpeed;
        public float Gust;
        public float VisibilityKm;
        public float LightningRate;
    }

    /// <summary>
    /// Everything the weather says in words. Status always reads in words beside any colour.
    /// <see cref="WindFrom"/> is the one place a downwind heading becomes the bearing a pilot
    /// reads (where the wind comes <i>from</i>); every display goes through it.
    /// </summary>
    internal static class WeatherWords
    {
        private static readonly string[] Cardinals =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW",
        };

        public static float WindFrom(float downwindHeading) => WeatherMath.WrapHeading(downwindHeading + 180f);

        public static string Cardinal(float bearing)
        {
            int index = (int)Math.Round(WeatherMath.WrapHeading(bearing) / 22.5f) % 16;
            return Cardinals[index];
        }

        /// <summary>Bearing from (x0, z0) to (x1, z1), degrees true.</summary>
        public static float Bearing(float x0, float z0, float x1, float z1)
            => WeatherMath.VectorToHeading(x1 - x0, z1 - z0);

        public static string Precipitation(PrecipitationKind kind)
        {
            switch (kind)
            {
                case PrecipitationKind.Drizzle: return "DRIZZLE";
                case PrecipitationKind.Light: return "LIGHT RAIN";
                case PrecipitationKind.Moderate: return "RAIN";
                case PrecipitationKind.Heavy: return "HEAVY RAIN";
                case PrecipitationKind.Violent: return "VIOLENT RAIN";
                default: return "NO PRECIP";
            }
        }

        public static string Turbulence(float turbulence)
        {
            if (turbulence < 0.15f) return "SMOOTH";
            if (turbulence < 0.35f) return "LIGHT TURB";
            if (turbulence < 0.6f) return "MOD TURB";
            return "SEVERE TURB";
        }

        public static string Stage(StormStage stage)
        {
            switch (stage)
            {
                case StormStage.Towering: return "BUILDING";
                case StormStage.Mature: return "MATURE";
                default: return "DECAYING";
            }
        }

        public static FlightCategory Category(float visibilityKm, float ceilingMetres)
        {
            float ceilingFeet = ceilingMetres * 3.28084f;
            if (visibilityKm < 1.6f || ceilingFeet < 500f) return FlightCategory.Lifr;
            if (visibilityKm < 4.8f || ceilingFeet < 1000f) return FlightCategory.Ifr;
            if (visibilityKm < 8f || ceilingFeet < 3000f) return FlightCategory.Mvfr;
            return FlightCategory.Vfr;
        }

        public static string CategoryName(FlightCategory category)
        {
            switch (category)
            {
                case FlightCategory.Mvfr: return "MVFR";
                case FlightCategory.Ifr: return "IFR";
                case FlightCategory.Lifr: return "LIFR";
                default: return "VFR";
            }
        }

        /// <summary>
        /// METAR-style report of a sample, e.g. <c>BSCL 181530Z 27015G25KT 2000 +TSRA BKN008 OVC030CB 18/17 Q0998</c>.
        /// Cloud height goes in hundreds of feet as in a real report; visibility in metres.
        /// </summary>
        public static string Metar(WeatherPoint p, int day, float hourOfDay, string station = "BSCL")
        {
            var sb = new StringBuilder(96);
            int hour = (int)Math.Floor(WeatherMath.Clamp(hourOfDay, 0f, 23.999f));
            int minute = (int)Math.Floor((hourOfDay - hour) * 60f);
            sb.Append(station).Append(' ');
            sb.Append((day % 100).ToString("00", CultureInfo.InvariantCulture));
            sb.Append(hour.ToString("00", CultureInfo.InvariantCulture));
            sb.Append((minute - minute % 5).ToString("00", CultureInfo.InvariantCulture)).Append("Z ");

            int from = (int)Math.Round(WindFrom(p.WindHeading) / 10f) * 10 % 360;
            if (from == 0) from = 360;
            int knots = (int)Math.Round(p.WindSpeed * 1.94384f);
            int gustKnots = (int)Math.Round((p.WindSpeed + p.Gust) * 1.94384f);
            if (knots < 2) sb.Append("00000KT");
            else
            {
                sb.Append(from.ToString("000", CultureInfo.InvariantCulture));
                sb.Append(Math.Min(knots, 99).ToString("00", CultureInfo.InvariantCulture));
                if (gustKnots >= knots + 10) sb.Append('G').Append(Math.Min(gustKnots, 99).ToString("00", CultureInfo.InvariantCulture));
                sb.Append("KT");
            }

            int visMetres = (int)Math.Round(p.VisibilityKm * 1000f);
            sb.Append(' ').Append(visMetres >= 9999 ? "9999" : (visMetres - visMetres % 100).ToString("0000", CultureInfo.InvariantCulture));

            string weather = PresentWeather(p);
            if (weather.Length > 0) sb.Append(' ').Append(weather);

            sb.Append(' ').Append(CloudGroup(p));

            sb.Append(' ').Append(Temp(p.Temperature)).Append('/').Append(Temp(p.Dewpoint));
            sb.Append(" Q").Append(((int)Math.Round(p.Qnh)).ToString("0000", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>METAR present-weather group: intensity, descriptor and phenomenon.</summary>
        public static string PresentWeather(WeatherPoint p)
        {
            PrecipitationKind kind = p.Precipitation;
            bool thunder = p.LightningRate > 0.5f;
            if (kind == PrecipitationKind.None) return thunder ? "VCTS" : "";

            string intensity = kind <= PrecipitationKind.Light ? "-" : kind >= PrecipitationKind.Heavy ? "+" : "";
            string descriptor = thunder ? "TS" : p.ConvectiveShare > 0.5f ? "SH" : "";
            string phenomenon = kind == PrecipitationKind.Drizzle ? "DZ" : p.Hail ? "GRRA" : "RA";
            return intensity + descriptor + phenomenon;
        }

        public static string CloudGroup(WeatherPoint p)
        {
            string amount;
            if (p.Cover < 0.1f) return "NSC";
            if (p.Cover < 0.3f) amount = "FEW";
            else if (p.Cover < 0.55f) amount = "SCT";
            else if (p.Cover < 0.88f) amount = "BKN";
            else amount = "OVC";
            int hundreds = (int)Math.Round(p.CloudBase * 3.28084f / 100f);
            string group = amount + Math.Max(hundreds, 1).ToString("000", CultureInfo.InvariantCulture);
            if (p.LightningRate > 0.5f || p.CoreDepth > 0.3f) group += "CB";
            else if (p.ConvectiveShare > 0.5f) group += "TCU";
            return group;
        }

        /// <summary>
        /// Up to <paramref name="buffer"/>.Length forecast lines for one point, sampled from the
        /// same field the sky uses — so the forecast is never wrong about the model, only about
        /// where you will be.
        /// </summary>
        public static int Forecast(WeatherField scratch, WeatherKey key, float now, float stepSeconds,
            float x, float z, float halfX, float halfZ, float hourOfDay, ForecastEntry[] buffer)
        {
            int count = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                float offset = (i + 1) * stepSeconds;
                float hour = (hourOfDay + offset / 3600f) % 24f;
                scratch.Build(key, now + offset, halfX, halfZ, hour);
                WeatherPoint p = scratch.Sample(x, z);
                buffer[count++] = new ForecastEntry
                {
                    OffsetSeconds = offset,
                    Regime = scratch.Timeline.Dominant,
                    RainRate = p.RainRate,
                    Cover = p.Cover,
                    WindFrom = WindFrom(p.WindHeading),
                    WindSpeed = p.WindSpeed,
                    Gust = p.Gust,
                    VisibilityKm = p.VisibilityKm,
                    LightningRate = p.LightningRate,
                };
            }
            return count;
        }

        private static string Temp(float celsius)
        {
            int t = (int)Math.Round(celsius);
            return t < 0 ? "M" + (-t).ToString("00", CultureInfo.InvariantCulture) : t.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}

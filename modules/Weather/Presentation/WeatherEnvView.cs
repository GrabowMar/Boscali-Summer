using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>One outlook line, already reduced to what the row shows.</summary>
    internal sealed class EnvForecastRow
    {
        public int OffsetMinutes;
        public WeatherRegimeType Regime;
        public string Code = "";
        public float Cover, Deck, Rain;
    }

    /// <summary>
    /// A plain snapshot of the environment for the ENV console. The MFD panel fills it from the live
    /// weather; the view turns it into words, states and pictures. No game types cross this line, so
    /// the offline harness can drive the whole console with synthetic values.
    /// </summary>
    internal sealed class EnvData
    {
        public bool HasMission = true;
        public string Title = "METOC / BATTLEFIELD";
        public string Chip0 = "", Chip1 = "", Footer = "";
        public AvState Chip0State = AvState.Ready, FooterState = AvState.Info;

        public WeatherRegimeType Regime;
        public string Code = "", Name = "", Briefing = "";
        public float Cover, Deck, Top, VisibilityKm = -1f, Rain, Turbulence;
        public float WindKts;
        public int WindFrom, WindTo;

        public bool HasCamera;
        public float CameraAlt, AirDensity, SoundSpeed;

        public float SunElevation, SunAzimuth, TimeOfDay, Sunrise, Sunset;
        public bool PolarDay, PolarNight;
        public string SunEvent = "";

        public string MoonPhase = "";
        public float MoonLit, MoonGlow;
        public bool MoonWaxing = true, Moonless;

        public EnvForecastRow[] Forecast;
    }

    /// <summary>
    /// The ENV console: chips and metrics for the top line, WEATHER (METAR-style condition card, vertical
    /// profile, 60-minute outlook) and SKY &amp; AIR (watch strip, sun arc, moon phase, wind dial, air
    /// density). Pure presentation over <see cref="EnvData"/>; the panel owns the game side.
    /// </summary>
    internal sealed class WeatherEnvView
    {
        public const int PageWeather = 0, PageSky = 1;

        private readonly AvChip[] chips;
        private readonly AvMetric[] metrics;
        private readonly List<AvPart>[] pageParts = { new List<AvPart>(16), new List<AvPart>(16) };
        private readonly AvRow[] emptyRows = new AvRow[2];

        // WEATHER
        private EnvConditionCard card;
        private AvSection profileSection;
        private EnvProfile profile;
        private EnvStrip localAir;
        private readonly List<ForecastRowPart> outlook = new List<ForecastRowPart>(6);

        // SKY & AIR
        private EnvStrip watch;
        private EnvSunCard sun;
        private EnvMoonCard moon;
        private EnvWindCard wind;
        private EnvMeter density;
        private EnvStrip airStrip;

        private bool empty;

        public AvConsole Console { get; }

        public WeatherEnvView(RectTransform root, string id, float width, float height)
        {
            Console = AvConsole.Build(root, id, "BATTLEFIELD ENVIRONMENT", 2, width, height);
            chips = Console.Chips(2);
            metrics = Console.Metrics("COVER", "BASE", "WIND", "DENSITY");
            Console.Tabs((AvIcon.Cloud, "WEATHER"), (AvIcon.Wind, "SKY & AIR"));
            BuildWeatherPage(Console.Page(PageWeather));
            BuildSkyPage(Console.Page(PageSky));
        }

        public void Finish() => Console.Finish();

        private T Track<T>(int page, T part) where T : AvPart { pageParts[page].Add(part); return part; }

        private void BuildWeatherPage(AvFlow p)
        {
            const int pg = PageWeather;
            Track(pg, p.Section(AvIcon.Cloud, "CURRENT CONDITIONS", "LIVE WEATHER"));
            card = Track(pg, p.Add(new EnvConditionCard(p.Content)));
            profileSection = Track(pg, p.Section(AvIcon.LayersSubtract, "VERTICAL PROFILE", ""));
            profile = Track(pg, p.Add(new EnvProfile(p.Content)));
            localAir = Track(pg, p.Add(new EnvStrip(p.Content, "Local air", false, "DENSITY", "SOUND", "TURBULENCE")));
            Track(pg, p.Section(AvIcon.Clock, "NEXT 60 MINUTES", "MODEL OUTLOOK"));
            Track(pg, p.Add(new EnvOutlookHeader(p.Content)));
            int rows = WeatherForecast.DefaultOffsetsMinutes.Length;
            outlook.Clear();
            for (int i = 0; i < rows; i++)
                outlook.Add(Track(pg, p.Add(new ForecastRowPart(p.Content, "Forecast " + i))));
            AddEmpty(p, pg);
        }

        private void BuildSkyPage(AvFlow p)
        {
            const int pg = PageSky;
            Track(pg, p.Section(AvIcon.InfoCircle, "FLIGHT CONDITIONS", "ENVIRONMENTAL WATCH"));
            watch = Track(pg, p.Add(new EnvStrip(p.Content, "Watch", true, "LIGHT", "CEILING", "WIND")));
            Track(pg, p.Section(AvIcon.Circle, "DAYLIGHT", "SOLAR EPHEMERIS"));
            sun = Track(pg, p.Add(new EnvSunCard(p.Content)));
            Track(pg, p.Section(AvIcon.Star, "MOONLIGHT", "LUNAR EPHEMERIS"));
            moon = Track(pg, p.Add(new EnvMoonCard(p.Content)));
            Track(pg, p.Section(AvIcon.Wind, "WIND & TURBULENCE", "MISSION WIND"));
            wind = Track(pg, p.Add(new EnvWindCard(p.Content)));
            Track(pg, p.Section(AvIcon.Gauge, "AIR AT CAMERA ALTITUDE", "DENSITY"));
            density = Track(pg, p.Add(new EnvMeter(p.Content, "AIR DENSITY")));
            airStrip = Track(pg, p.Add(new EnvStrip(p.Content, "Air", false, "SPEED OF SOUND", "SAMPLED ALTITUDE")));
            AddEmpty(p, pg);
        }

        private void AddEmpty(AvFlow p, int page)
        {
            var row = p.Add(new AvRow(p.Content));
            row.Set("NO MISSION LOADED", "Battlefield weather appears here once a mission is running.", "", AvState.Inert);
            row.SetShown(false);
            emptyRows[page] = row;
        }

        // ---- Apply -----------------------------------------------------------------------

        /// <summary>Writes the snapshot into the console. Only the visible page is filled unless <paramref name="allPages"/>.</summary>
        public void Apply(EnvData d, bool allPages = false)
        {
            SetEmpty(!d.HasMission);
            Console.SetTitle(d.Title);
            chips[0].Set(d.Chip0, d.Chip0State);
            chips[1].Set(d.Chip1, AvState.Info);
            Console.Footer.Set(d.Footer, d.FooterState);
            if (!d.HasMission)
            {
                for (int i = 0; i < metrics.Length; i++) metrics[i].Set("—", "", 0f, AvState.Inert);
                return;
            }

            AvState regimeState = RegimeState(d.Regime);
            metrics[0].Set(AvNum.Percent(d.Cover), d.Code, d.Cover, regimeState);
            bool lowDeck = d.Deck < 1600f;
            metrics[1].Set(AvNum.Fixed(d.Deck, 0), "M", Mathf.Clamp01(d.Deck / 4000f), lowDeck ? AvState.Caution : AvState.Ready);
            metrics[2].Set(AvNum.Fixed(d.WindKts, 0), "KT " + AvNum.Fixed(d.WindFrom, 0) + "°",
                Mathf.Clamp01(d.WindKts / 40f), d.WindKts > 25f ? AvState.Caution : AvState.Ready);
            metrics[3].Set(d.HasCamera ? AvNum.Percent(d.AirDensity) : "—", "SL", Mathf.Clamp01(d.AirDensity), AvState.Ready);

            if (allPages || Console.CurrentPage == PageWeather) ApplyWeather(d);
            if (allPages || Console.CurrentPage == PageSky) ApplySky(d);
        }

        private void SetEmpty(bool value)
        {
            if (value == empty) return;
            empty = value;
            for (int pg = 0; pg < pageParts.Length; pg++)
            {
                foreach (AvPart part in pageParts[pg]) part.SetShown(!value);
                emptyRows[pg].SetShown(value);
            }
        }

        private void ApplyWeather(EnvData d)
        {
            float rain = Mathf.Clamp01(d.Rain);
            FlightCat(d, out string cat, out AvState catState);
            string windText = d.WindKts < 1f ? "CALM"
                : AvNum.Fixed(d.WindFrom, 0).PadLeft(3, '0') + "°/" + AvNum.Fixed(d.WindKts, 0) + "KT";
            string vis = d.VisibilityKm < 0f ? "—" : d.VisibilityKm >= 10f ? "10+ KM" : AvNum.Fixed(d.VisibilityKm, 1) + " KM";
            card.Set(new EnvSky
            {
                Code = d.Code,
                Word = d.Name,
                Briefing = d.Briefing,
                Regime = d.Regime,
                Cover = d.Cover,
                State = RegimeState(d.Regime),
                Category = cat,
                CategoryState = catState,
                Base = AvNum.Fixed(d.Deck, 0) + " M",
                Top = AvNum.Fixed(d.Top, 0) + " M",
                Wind = windText,
                Visibility = vis,
                Precip = rain <= 0.02f ? "DRY" : "RAIN " + AvNum.Percent(rain),
                PrecipState = rain > 0.6f ? AvState.Caution : AvState.Info,
            });

            string status; AvState statusState;
            if (!d.HasCamera) { status = "CAMERA UNAVAILABLE"; statusState = AvState.Inert; }
            else if (d.CameraAlt < d.Deck - 50f) { status = "BELOW CLOUD BASE"; statusState = AvState.Info; }
            else if (d.CameraAlt < d.Top - 50f) { status = "IN CLOUD LAYER"; statusState = AvState.Caution; }
            else { status = "ABOVE CLOUD TOP"; statusState = AvState.Ready; }
            profileSection.SetCaption(status);
            profile.Set(new EnvProfileData
            {
                Base = d.Deck, Top = d.Top, CameraAlt = d.CameraAlt, HasCamera = d.HasCamera, State = statusState, Status = status,
            });

            localAir.Set(0, d.HasCamera ? AvNum.Percent(d.AirDensity) + " SL" : "—");
            localAir.Set(1, d.HasCamera ? AvNum.Fixed(d.SoundSpeed, 0) + " M/S" : "—");
            localAir.Set(2, AvNum.Fixed(d.Turbulence, 2));

            EnvForecastRow[] rows = d.Forecast;
            if (rows == null) return;
            for (int i = 0; i < rows.Length && i < outlook.Count; i++)
            {
                EnvForecastRow r = rows[i];
                AvState st = r.Regime == WeatherRegimeType.Storm ? AvState.Danger
                    : r.Rain > 0.2f ? AvState.Caution : RegimeState(r.Regime);
                outlook[i].Set(i == 0 ? "NOW" : "+" + r.OffsetMinutes, i == 0, r.Regime, r.Code, r.Cover, r.Deck, r.Rain, st);
            }
        }

        private void ApplySky(EnvData d)
        {
            bool lowLight = d.SunElevation <= -6f, lowDeck = d.Deck < 1600f, strongWind = d.WindKts > 30f;
            watch.Set(0, d.SunElevation > 0f ? "DAYLIGHT" : lowLight ? "NIGHT" : "TWILIGHT",
                lowLight ? "LOW LIGHT" : "NORMAL", lowLight ? AvState.Caution : AvState.Info);
            watch.Set(1, AvNum.Fixed(d.Deck, 0) + " M", lowDeck ? "LOW CEILING" : "NORMAL", lowDeck ? AvState.Caution : AvState.Info);
            watch.Set(2, AvNum.Fixed(d.WindKts, 0) + " KT", strongWind ? "HIGH WIND" : "NORMAL", strongWind ? AvState.Caution : AvState.Info);

            string solar = d.PolarDay ? "POLAR DAY" : d.PolarNight ? "POLAR NIGHT"
                : d.SunElevation > 0f ? "DAYLIGHT" : d.SunElevation > -6f ? "CIVIL TWILIGHT" : "NIGHT";
            string events = d.PolarDay ? "Continuous polar daylight, no sunset."
                : d.PolarNight ? "Continuous polar night, no sunrise." : d.SunEvent;
            sun.Set(new EnvSun
            {
                Elevation = d.SunElevation, Azimuth = d.SunAzimuth, TimeOfDay = d.TimeOfDay, Sunrise = d.Sunrise, Sunset = d.Sunset,
                PolarDay = d.PolarDay, PolarNight = d.PolarNight, Light = solar, Event = events,
                LightState = lowLight ? AvState.Caution : AvState.Info,
            });

            moon.Set(new EnvMoon
            {
                Phase = d.MoonPhase, Lit = d.MoonLit, Glow = d.MoonGlow, Waxing = d.MoonWaxing, Moonless = d.Moonless,
                Guidance = d.Moonless ? "Low natural light — visual identification may be harder."
                    : "Moonlight present — check cloud cover for visibility.",
            });

            string turbWord = WeatherWords.Turbulence(d.Turbulence);
            AvState turbState = d.Turbulence >= 0.6f ? AvState.Danger : d.Turbulence >= 0.35f ? AvState.Caution : AvState.Ready;
            wind.Set(new EnvWind
            {
                Kts = d.WindKts, From = d.WindFrom, To = d.WindTo, Turbulence = d.Turbulence,
                Cardinal = d.WindKts < 1f ? "" : WeatherWords.Cardinal(d.WindFrom),
                TurbWord = turbWord, TurbState = turbState,
                Advisory = strongWind ? "Strong wind — expect drift and turbulence."
                    : d.WindKts < 3f ? "Calm — no significant drift." : "Normal wind — watch for drift.",
                State = strongWind ? AvState.Caution : AvState.Info,
            });

            if (d.HasCamera)
            {
                bool thin = d.AirDensity < 0.6f;
                density.Set((thin ? "THIN AIR  " : "") + AvNum.Percent(d.AirDensity) + " SL", d.AirDensity / 1.5f,
                    thin ? AvState.Caution : AvState.Ready, 1f / 1.5f, "SEA LEVEL = 100%");
                airStrip.Set(0, AvNum.Fixed(d.SoundSpeed, 0) + " M/S");
                airStrip.Set(1, AvNum.Signed(d.CameraAlt, 0) + " M");
            }
            else
            {
                density.Set("—", 0f, AvState.Inert);
                airStrip.Set(0, "—");
                airStrip.Set(1, "—");
            }
        }

        private static void FlightCat(EnvData d, out string name, out AvState state)
        {
            FlightCategory c = WeatherWords.Category(d.VisibilityKm < 0f ? 50f : d.VisibilityKm, d.Deck);
            name = WeatherWords.CategoryName(c);
            state = c == FlightCategory.Vfr ? AvState.Ready
                : c == FlightCategory.Mvfr ? AvState.Info
                : c == FlightCategory.Ifr ? AvState.Caution : AvState.Danger;
        }

        internal static AvState RegimeState(WeatherRegimeType type)
        {
            switch (type)
            {
                case WeatherRegimeType.Clear:
                case WeatherRegimeType.Fair:
                    return AvState.Info;
                case WeatherRegimeType.Scattered:
                    return AvState.Ready;
                case WeatherRegimeType.Broken:
                case WeatherRegimeType.Overcast:
                case WeatherRegimeType.RainSquall:
                    return AvState.Caution;
                case WeatherRegimeType.Storm:
                    return AvState.Danger;
                default:
                    return AvState.Info;
            }
        }
    }
}

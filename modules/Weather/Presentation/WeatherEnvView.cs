using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
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
        public string Footer = "", Next = "";
        public AvState FooterState = AvState.Info;
        /// <summary>How far the weather is through its current step (0..1); <see cref="Next"/> is the countdown text.</summary>
        public float NextFrac;
        /// <summary>Air density at 0..12 km in 1 km steps (null = unknown); drawn as the density profile on SKY &amp; AIR.</summary>
        public float[] DensityByAlt;

        public WeatherRegimeType Regime;
        public string Code = "", Name = "", Briefing = "";
        public float Cover, Deck, Top, VisibilityKm = -1f, Rain, Turbulence;
        public float WindKts;
        public int WindFrom, WindTo;

        public bool HasCamera;
        public float CameraAlt, AirDensity, SoundSpeed;
        public bool HasViewAir;
        public float ViewCloud, ViewRain, ViewMoisture, ViewTemperature;

        public float SunElevation, SunAzimuth, TimeOfDay, Sunrise, Sunset;
        public bool PolarDay, PolarNight;
        public string SunEvent = "";

        public string MoonPhase = "";
        public float MoonLit, MoonGlow;
        public bool MoonWaxing = true, Moonless;

        public EnvForecastRow[] Forecast;
    }

    /// <summary>
    /// The ENV console: metrics for the top line, WEATHER (state icon row, condition card, visibility / rain /
    /// turbulence / next-step rings, storm-risk bar, growing vertical profile, 60-minute icon outlook, cover trend) and SKY &amp; AIR
    /// (sun arc, moon phase, wind dial, air rings, daylight and density charts). Pure presentation over <see cref="EnvData"/>; the panel owns the game side.
    /// </summary>
    internal sealed class WeatherEnvView
    {
        public const int PageWeather = 0, PageSky = 1;

        private readonly AvMetric[] metrics;
        private readonly List<AvPart>[] pageParts = { new List<AvPart>(16), new List<AvPart>(16) };
        private readonly AvRow[] emptyRows = new AvRow[2];

        // WEATHER
        private EnvStateRow stateRow;
        private EnvConditionCard card;
        private AvGauge visGauge, rainGauge, turbGauge, nextGauge;
        private AvHazardBar stormBar;
        private AvRow viewAirRow;
        private EnvProfile profile;
        private EnvOutlookStrip outlook;
        private AvEqualizer coverEq;
        private AvRow forecastMissing;

        // SKY & AIR
        private EnvSunCard sun;
        private EnvMoonCard moon;
        private EnvWindCard wind;
        private AvGauge densityGauge, soundGauge, altGauge;
        private AvEqualizer densityEq;
        private readonly float[] densityBuf = new float[13];

        private bool empty;

        public AvConsole Console { get; }

        public WeatherEnvView(RectTransform root, string id, float width, float height)
        {
            Console = AvConsole.Build(root, id, "BATTLEFIELD ENVIRONMENT", 2, width, height);
            metrics = Console.Metrics("COVER", "BASE", "WIND", "DENSITY");
            HelpOn(metrics[0], "COVER: how much of the sky the cloud hides, with the sky code (CLR clear up to OVC overcast, TS storm). The bar is the cover fraction.");
            HelpOn(metrics[1], "BASE: height of the cloud bottom above sea level, in metres. Amber under 1600 m: a low ceiling limits how far you can fly under the deck.");
            HelpOn(metrics[2], "WIND: speed in knots and the bearing it blows from. Amber above 25 kt: expect drift on approach and a rougher ride.");
            HelpOn(metrics[3], "DENSITY: air density at your altitude as a share of sea level. Thin air cuts lift and engine power; see the SKY & AIR page for the profile.");
            Console.Tabs((AvIcon.Cloud, "WEATHER"), (AvIcon.Wind, "SKY & AIR"));
            BuildWeatherPage(Console.Page(PageWeather));
            BuildSkyPage(Console.Page(PageSky));
        }

        public void Finish() => Console.Finish();

        private T Track<T>(int page, T part) where T : AvPart { pageParts[page].Add(part); return part; }

        /// <summary>A clear hit layer over a part that shows <paramref name="text"/> in the console footer on hover.</summary>
        private static void HelpOn(AvPart part, string text)
        {
            if (part == null || part.Rect == null) return;
            Image hit = AvLay.Solid(part.Rect, "Help", Color.clear);
            AvLay.Fill(hit.rectTransform);
            hit.raycastTarget = true;
            AvHelpTip.Attach(hit.gameObject, text);
        }

        private void BuildWeatherPage(AvFlow p)
        {
            const int pg = PageWeather;
            stateRow = Track(pg, p.Add(new EnvStateRow(p.Content)));
            card = Track(pg, p.Add(new EnvConditionCard(p.Content)));
            card.SetHelp("SKY: the sky code and word for the current weather state, how much of the sky the cloud covers, and the flight category from visibility and ceiling (VFR good, MVFR marginal, IFR poor, LIFR very poor).");
            visGauge = new AvGauge(p.Content, "VIS KM", AvGaugeShape.Segments, 64f);
            rainGauge = new AvGauge(p.Content, "RAIN", AvGaugeShape.Segments, 64f);
            turbGauge = new AvGauge(p.Content, "TURB", AvGaugeShape.Segments, 64f);
            visGauge.Help = "VIS KM: horizontal visibility in kilometres. The ladder is full at 10 km or more.";
            rainGauge.Help = "RAIN: current rain intensity, as a percentage of the heaviest rain.";
            turbGauge.Help = "TURB: air turbulence, 0 to 0.8. Higher means a rougher ride.";
            nextGauge = new AvGauge(p.Content, "NEXT", AvGaugeShape.Segments, 64f);
            nextGauge.Help = "NEXT: time to the next weather step. The ladder fills as the current state runs out; the sky then holds or moves one state along. HELD means the host or the mission froze it.";
            p.Row(Track(pg, visGauge), Track(pg, rainGauge), Track(pg, turbGauge), Track(pg, nextGauge));
            viewAirRow = Track(pg, p.Add(new AvRow(p.Content)));
            viewAirRow.Help = "VIEW AIR: conditions at the current camera. Cloud density follows visible cloud bodies, including clear gaps. Rain is falling precipitation; cloud moisture can wet the glass without rain. Temperature is the local weather estimate at altitude.";
            // Storm risk is only drawn while it is real; it is not a page part so the empty state cannot re-show it.
            stormBar = p.Add(new AvHazardBar(p.Content, "STORM RISK"));
            stormBar.SetShown(false);
            profile = Track(pg, p.Add(new EnvProfile(p.Content), 1f));
            profile.SetHelp("PROFILE: the cloud layer as a slab between its base and top on a metre scale, with your altitude as a dashed level. The line under the top edge says whether you are below, inside or above the cloud.");
            outlook = Track(pg, p.Add(new EnvOutlookStrip(p.Content, WeatherForecast.DefaultOffsetsMinutes.Length)));
            outlook.SetHelp("NEXT 60 MIN: the expected sky now and at +5, +10, +15, +30 and +60 minutes. The bar under each icon is the chance of rain; amber or red marks rain or a storm.");
            coverEq = Track(pg, p.Add(new AvEqualizer(p.Content, "COVER 60 MIN", 36f), 1f));
            coverEq.Help = "COVER 60 MIN: expected cloud cover from now (left) to +60 minutes (right), one bar per five minutes. Growing bars mean the sky is closing in; the text gives cover now and at +60.";
            forecastMissing = p.Add(new AvRow(p.Content));
            forecastMissing.Set("OUTLOOK UNAVAILABLE", "Waiting for mission weather data.", "", AvState.Inert);
            forecastMissing.SetShown(false);
            AddEmpty(p, pg);
        }

        private void BuildSkyPage(AvFlow p)
        {
            const int pg = PageSky;
            sun = Track(pg, p.Add(new EnvSunCard(p.Content)));
            sun.SetHelp("SUN: the strip spans midnight to midnight. Bright hourly blocks indicate daylight; the vertical cursor is the current time. Elevation, bearing and sunrise/sunset are listed beside it. Below about -6 degrees expect poor visual range.");
            moon = Track(pg, p.Add(new EnvMoonCard(p.Content)));
            moon.SetHelp("MOON: phase, illuminated fraction and natural moonlight level. The labeled meter shows illumination; a new moon means dark nights.");
            wind = Track(pg, p.Add(new EnvWindCard(p.Content)));
            wind.SetHelp("WIND: the tape runs N-E-S-W-N and its cursor marks the direction the wind comes FROM. Exact FROM and TO bearings are listed beside it. The bar is turbulence: amber above 0.35, red above 0.6.");
            altGauge = new AvGauge(p.Content, "ALT M", AvGaugeShape.Segments, 64f);
            altGauge.Help = "ALT M: your camera altitude in metres; the ladder is full at 12 000 m. Density and the speed of sound are read at this height.";
            densityGauge = new AvGauge(p.Content, "DENSITY", AvGaugeShape.Segments, 64f);
            soundGauge = new AvGauge(p.Content, "SOUND M/S", AvGaugeShape.Segments, 64f);
            densityGauge.Help = "DENSITY: air density at your altitude as a percentage of the mission's sea-level density. The ladder is full at 100% SL; thin air cuts lift and engine power.";
            soundGauge.Help = "SOUND M/S: speed of sound at your altitude, in metres per second. The ladder is full at 400.";
            p.Row(Track(pg, altGauge), Track(pg, densityGauge), Track(pg, soundGauge));
            // Shown only while the density profile is known; not a tracked page part so the empty state cannot re-show it.
            densityEq = p.Add(new AvEqualizer(p.Content, "AIR DENSITY 0-12 KM", 36f), 1f);
            densityEq.Help = "AIR DENSITY 0-12 KM: one bar per kilometre of altitude, left is sea level. The bars shrink as the air thins, so climbing costs lift and engine power.";
            densityEq.SetShown(false);
            AddEmpty(p, pg);
        }

        private void AddEmpty(AvFlow p, int page)
        {
            var row = p.Add(new AvRow(p.Content));
            row.Set("NO MISSION LOADED", "", "", AvState.Inert);
            row.SetShown(false);
            emptyRows[page] = row;
        }

        // ---- Apply -----------------------------------------------------------------------

        /// <summary>Writes the snapshot into the console. Only the visible page is filled unless <paramref name="allPages"/>.</summary>
        public void Apply(EnvData d, bool allPages = false)
        {
            SetEmpty(!d.HasMission);
            Console.SetTitle(d.Title);
            Console.Footer.Set(d.Footer, d.FooterState);
            if (!d.HasMission)
            {
                for (int i = 0; i < metrics.Length; i++) metrics[i].Set("—", "", 0f, AvState.Inert);
                stormBar.SetShown(false);
                densityEq.SetShown(false);
                forecastMissing.SetShown(false);
                return;
            }

            AvState regimeState = RegimeState(d.Regime);
            metrics[0].Set(AvNum.Percent(d.Cover), d.Code, d.Cover, regimeState);
            bool lowDeck = d.Deck < 1600f;
            metrics[1].Set(AvNum.Fixed(d.Deck, 0), "M", Mathf.Clamp01(d.Deck / 4000f), lowDeck ? AvState.Caution : AvState.Ready);
            metrics[2].Set(AvNum.Fixed(d.WindKts, 0), "KT " + AvNum.Fixed(d.WindFrom, 0) + "°",
                Mathf.Clamp01(d.WindKts / 40f), d.WindKts > 25f ? AvState.Caution : AvState.Ready);
            float density = DensityRatio(d);
            bool hasDensity = d.HasCamera && density >= 0f;
            metrics[3].Set(hasDensity ? AvNum.Percent(density) : "—", "SL", hasDensity ? Mathf.Clamp01(density) : 0f,
                !hasDensity ? AvState.Inert : density < 0.6f ? AvState.Caution : AvState.Ready);

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

        private static string Tag(string text, AvState st) =>
            (st == AvState.Caution || st == AvState.Danger ? AvStates.Glyph(st) : "") + text;

        private void ApplyWeather(EnvData d)
        {
            float rain = Mathf.Clamp01(d.Rain);
            viewAirRow.SetShown(d.HasViewAir);
            if (d.HasViewAir)
            {
                bool cloud = d.ViewCloud > 0.08f;
                bool wet = d.ViewRain > 0.02f;
                bool coldWet = d.ViewTemperature < 0f && Mathf.Max(d.ViewRain, d.ViewMoisture) > 0.02f;
                string air = coldWet ? "COLD MOISTURE" : wet ? "RAIN" : cloud ? "IN CLOUD" : "CLEAR AIR";
                viewAirRow.Set("VIEW AIR / " + air,
                    "CLOUD " + AvNum.Percent(d.ViewCloud) + " · RAIN " + AvNum.Percent(d.ViewRain) +
                    " · MOISTURE " + AvNum.Percent(d.ViewMoisture),
                    AvNum.Fixed(d.ViewTemperature, 0) + "°C EST", coldWet ? AvState.Caution : AvState.Info);
            }
            FlightCat(d, out string cat, out AvState catState);

            int lit = EnvStateRow.LitFor(d.Regime);
            if (d.VisibilityKm >= 0f && d.VisibilityKm < 5f) lit |= EnvStateRow.Mist;
            stateRow.Set(lit, RegimeState(d.Regime));
            stateRow.SetHelp(d.Briefing);

            card.Set(new EnvSky
            {
                Code = d.Code,
                Word = d.Name,
                Regime = d.Regime,
                Cover = d.Cover,
                State = RegimeState(d.Regime),
                Category = cat,
                CategoryState = catState,
            });

            float vis = d.VisibilityKm;
            visGauge.Set(vis < 0f ? 0f : Mathf.Clamp01(vis / 10f), vis < 0f ? "—" : vis >= 10f ? "10+" : AvNum.Fixed(vis, 1),
                vis < 0f ? AvState.Inert : vis < 1.5f ? AvState.Danger : vis < 5f ? AvState.Caution : AvState.Ready);
            AvState rainState = rain > 0.6f ? AvState.Caution : AvState.Ready;
            rainGauge.Set(rain, Tag(AvNum.Percent(rain), rainState), rainState);
            AvState turbState = d.Turbulence >= 0.6f ? AvState.Danger : d.Turbulence >= 0.35f ? AvState.Caution : AvState.Ready;
            turbGauge.Set(Mathf.Clamp01(d.Turbulence / 0.8f), Tag(AvNum.Fixed(d.Turbulence, 2), turbState), turbState);
            nextGauge.Set(Mathf.Clamp01(d.NextFrac), d.Next, AvState.Ready);

            // Storm risk: only while a storm is on the field or the outlook really contains one.
            EnvForecastRow[] rows = d.Forecast;
            int stormAt = -1;
            if (rows != null)
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i].Regime == WeatherRegimeType.Storm) { stormAt = rows[i].OffsetMinutes; break; }
            if (d.Regime == WeatherRegimeType.Storm)
            {
                stormBar.SetShown(true);
                stormBar.Set(1f, "ACTIVE", AvState.Danger);
            }
            else if (stormAt >= 0)
            {
                stormBar.SetShown(true);
                stormBar.Set(Mathf.Clamp01(1f - stormAt / 90f), "+" + stormAt + " MIN", AvState.Caution);
            }
            else stormBar.SetShown(false);

            string status; AvState statusState;
            if (!d.HasCamera) { status = "CAMERA UNAVAILABLE"; statusState = AvState.Inert; }
            else if (d.CameraAlt < d.Deck - 50f) { status = "BELOW CLOUD BASE"; statusState = AvState.Info; }
            else if (d.CameraAlt < d.Top - 50f) { status = "IN CLOUD LAYER"; statusState = AvState.Caution; }
            else { status = "ABOVE CLOUD TOP"; statusState = AvState.Ready; }
            profile.Set(new EnvProfileData
            {
                Base = d.Deck, Top = d.Top, CameraAlt = d.CameraAlt, HasCamera = d.HasCamera, State = statusState, Status = status,
            });

            bool hasForecast = rows != null && rows.Length > 0;
            outlook.SetShown(hasForecast);
            coverEq.SetShown(hasForecast);
            forecastMissing.SetShown(!hasForecast);
            if (!hasForecast) return;
            for (int i = 0; i < rows.Length; i++)
            {
                EnvForecastRow r = rows[i];
                AvState st = r.Regime == WeatherRegimeType.Storm ? AvState.Danger
                    : r.Rain > 0.2f ? AvState.Caution : RegimeState(r.Regime);
                outlook.Set(i, i == 0 ? "NOW" : "+" + r.OffsetMinutes, i == 0, r.Regime, r.Code, r.Rain, st);
            }
            // 13 bars on a true time axis (0..60 min, 5 min apiece), interpolated between the forecast steps.
            var bars = new float[13];
            for (int b = 0; b < bars.Length; b++)
            {
                float minute = b * 5f;
                int k = 0;
                while (k < rows.Length - 1 && rows[k + 1].OffsetMinutes <= minute) k++;
                EnvForecastRow a = rows[k], z = rows[Mathf.Min(k + 1, rows.Length - 1)];
                float span = z.OffsetMinutes - a.OffsetMinutes;
                float f = span > 0f ? Mathf.Clamp01((minute - a.OffsetMinutes) / span) : 0f;
                bars[b] = Mathf.Clamp01(Mathf.Lerp(a.Cover, z.Cover, f));
            }
            float first = Mathf.Clamp01(rows[0].Cover), last = Mathf.Clamp01(rows[rows.Length - 1].Cover);
            coverEq.Set(bars, AvNum.Percent(first) + " > " + AvNum.Percent(last), last > 0.75f ? AvState.Caution : AvState.Ready);
        }

        private void ApplySky(EnvData d)
        {
            bool lowLight = d.SunElevation <= -6f, strongWind = d.WindKts > 30f;
            AvState lightState = lowLight ? AvState.Caution : AvState.Info;
            AvState windState = strongWind ? AvState.Caution : AvState.Info;

            string solar = d.PolarDay ? "POLAR DAY" : d.PolarNight ? "POLAR NIGHT"
                : d.SunElevation > 0f ? "DAYLIGHT" : d.SunElevation > -6f ? "CIVIL TWILIGHT" : "NIGHT";
            string events = d.PolarDay || d.PolarNight ? "" : d.SunEvent;
            sun.Set(new EnvSun
            {
                Elevation = d.SunElevation, Azimuth = d.SunAzimuth, TimeOfDay = d.TimeOfDay, Sunrise = d.Sunrise, Sunset = d.Sunset,
                PolarDay = d.PolarDay, PolarNight = d.PolarNight, Light = solar, Event = events,
                LightState = lightState,
            });

            moon.Set(new EnvMoon
            {
                Phase = d.MoonPhase, Lit = d.MoonLit, Glow = d.MoonGlow, Waxing = d.MoonWaxing, Moonless = d.Moonless,
            });

            string turbWord = WeatherWords.Turbulence(d.Turbulence);
            AvState turbState = d.Turbulence >= 0.6f ? AvState.Danger : d.Turbulence >= 0.35f ? AvState.Caution : AvState.Ready;
            wind.Set(new EnvWind
            {
                Kts = d.WindKts, From = d.WindFrom, To = d.WindTo, Turbulence = d.Turbulence,
                Cardinal = d.WindKts < 1f ? "" : WeatherWords.Cardinal(d.WindFrom),
                TurbWord = turbWord, TurbState = turbState,
                State = windState,
            });

            float[] dens = d.DensityByAlt;
            float density = DensityRatio(d);
            bool hasDensityProfile = dens != null && dens.Length > 0 && dens[0] > 0f;
            densityEq.SetShown(hasDensityProfile);
            if (hasDensityProfile)
            {
                int m = Mathf.Min(dens.Length, densityBuf.Length);
                var shown = new float[m];
                for (int i = 0; i < m; i++) shown[i] = Mathf.Clamp01(dens[i] / dens[0]);
                densityEq.Set(shown, d.HasCamera ? AvNum.Percent(density) + " SL @ " + AvNum.Fixed(d.CameraAlt / 1000f, 1) + " KM" : "",
                    d.HasCamera && density < 0.6f ? AvState.Caution : AvState.Ready);
            }

            if (d.HasCamera)
            {
                altGauge.Set(Mathf.Clamp01(d.CameraAlt / 12000f), AvNum.Fixed(d.CameraAlt, 0), AvState.Ready);
                bool thin = density < 0.6f;
                AvState densState = density < 0f ? AvState.Inert : thin ? AvState.Caution : AvState.Ready;
                densityGauge.Set(Mathf.Clamp01(density), density < 0f ? "—" : Tag(AvNum.Percent(density), densState), densState);
                soundGauge.Set(Mathf.Clamp01(d.SoundSpeed / 400f), AvNum.Fixed(d.SoundSpeed, 0), AvState.Ready);
            }
            else
            {
                altGauge.Set(0f, "—", AvState.Inert);
                densityGauge.Set(0f, "—", AvState.Inert);
                soundGauge.Set(0f, "—", AvState.Inert);
            }
        }

        private static void FlightCat(EnvData d, out string name, out AvState state)
        {
            if (d.VisibilityKm < 0f) { name = "N/A"; state = AvState.Inert; return; }
            FlightCategory c = WeatherWords.Category(d.VisibilityKm, d.Deck);
            name = WeatherWords.CategoryName(c);
            state = c == FlightCategory.Vfr ? AvState.Ready
                : c == FlightCategory.Mvfr ? AvState.Info
                : c == FlightCategory.Ifr ? AvState.Caution : AvState.Danger;
        }

        private static float DensityRatio(EnvData d) => d.DensityByAlt != null && d.DensityByAlt.Length > 0 && d.DensityByAlt[0] > 0f
            ? d.AirDensity / d.DensityByAlt[0] : -1f;

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

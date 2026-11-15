using NOAvionics;
using System;
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
        public float Rain;
    }

    /// <summary>
    /// A plain snapshot of the environment for the ENV console. The MFD panel fills it from the live
    /// weather; the view turns it into words, states and pictures. No game types cross this line, so
    /// the offline harness can drive the whole console with synthetic values.
    /// </summary>
    internal sealed class EnvData
    {
        public bool HasMission = true;
        public string Footer = "", Next = "";
        public AvState FooterState = AvState.Info;
        /// <summary>How far the weather is through its current step (0..1); <see cref="Next"/> is the countdown text.</summary>
        public float NextFrac;

        public WeatherRegimeType Regime;
        public string Code = "", Name = "", Briefing = "";
        /// <summary>The tape's story and the role of the segment over the aircraft, and the next front ahead as "COLD FRONT 62 KM W" (spec 8.4); empty without a tape.</summary>
        public string Story = "", Role = "", NextMarker = "";
        /// <summary>The tape does not move: the outlook says HELD instead of drawing a forecast. AtPosition: the aircraft is slow enough for a forecast at its position to mean something.</summary>
        public bool ForecastHeld, AtPosition;
        /// <summary>The METAR-style line derived from the layers at the camera (WeatherMetar); empty when unknown.</summary>
        public string Metar = "";
        public float Cover, Deck, Top, VisibilityKm = -1f, Rain, Turbulence;
        public float WindKts;
        public int WindFrom, WindTo;

        public bool HasCamera;
        public float CameraAlt;
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
    /// The ENV console, one page without a tab bar: sky now (state icons, story, condition card with the flight category),
    /// visibility / rain / next-step rings, view air, the cloud profile against your altitude, the 60-minute outlook, then sun,
    /// moon and wind. Pure presentation over <see cref="EnvData"/>; the panel owns the game side.
    /// </summary>
    internal sealed class WeatherEnvView
    {
        private readonly List<AvPart> parts = new List<AvPart>(16);
        private AvRow emptyRow;

        private EnvStateRow stateRow;
        private AvRow briefRow;
        private EnvConditionCard card;
        private AvGauge visGauge, rainGauge, nextGauge;
        private AvHazardBar stormBar;
        private AvRow viewAirRow;
        private EnvProfile profile;
        private EnvOutlookStrip outlook;
        private EnvSunCard sun;
        private EnvMoonCard moon;
        private EnvWindCard wind;

        // Last-written keys: a tick that changes nothing builds no string and writes nothing.
        private string briefStory, briefRole, briefMarker, tileNext;
        private bool briefHeld;
        private long viewKey = long.MinValue, tileKey = long.MinValue;
        private int cardKey, profileKey, sunKey, moonKey, windKey;
        private bool cardSeen, profileSeen, sunSeen, moonSeen, windSeen;

        /// <summary>True when the hash of what a card shows moved; the first call always says yes.</summary>
        private static bool Moved(ref int last, ref bool seen, int now)
        {
            if (seen && last == now) return false;
            last = now; seen = true;
            return true;
        }

        private bool empty;

        public AvConsole Console { get; }

        public WeatherEnvView(RectTransform root, string id, float width, float height)
        {
            Console = AvConsole.Build(root, id, "WEATHER", 1, width, height);
            BuildPage(Console.Page(0));
            Console.Orders(0, "Read the flight category first: IFR and LIFR are instrument weather.");
        }

        public void Finish() => Console.Finish();

        private T Track<T>(T part) where T : AvPart { parts.Add(part); return part; }

        private void BuildPage(AvFlow p)
        {
            Track(p.Section(AvIcon.Cloud, "SKY NOW", "OVER YOUR POSITION"));
            stateRow = Track(p.Add(new EnvStateRow(p.Content)));
            briefRow = Track(p.Add(new AvRow(p.Content)));
            briefRow.Help = "STORY: the weather system the tape is playing and the part of it over you. The line under it is the next front ahead: its distance across the line and the side it comes from.";
            card = Track(p.Add(new EnvConditionCard(p.Content)));
            card.SetHelp("SKY: the sky code and word for the current weather state, how much of the sky the cloud covers, and the flight category from visibility and ceiling (VFR good, MVFR marginal, IFR poor, LIFR very poor).");
            visGauge = new AvGauge(p.Content, "VIS KM");
            rainGauge = new AvGauge(p.Content, "RAIN");
            nextGauge = new AvGauge(p.Content, "NEXT");
            visGauge.Help = "VIS KM: horizontal visibility in kilometres. The ladder is full at 10 km or more.";
            rainGauge.Help = "RAIN: current rain intensity, as a percentage of the heaviest rain.";
            nextGauge.Help = "NEXT: distance to the next front ahead, with the ladder full at 150 km. HELD means the tape is not moving, so nothing is coming.";
            p.Row(Track(visGauge), Track(rainGauge), Track(nextGauge));
            viewAirRow = Track(p.Add(new AvRow(p.Content)));
            viewAirRow.Help = "VIEW AIR: conditions at the current camera. Cloud density follows visible cloud bodies, including clear gaps. Rain is falling precipitation; cloud moisture can wet the glass without rain. Temperature is the local weather estimate at altitude.";
            // Storm risk is only drawn while it is real; it is not a page part so the empty state cannot re-show it.
            stormBar = p.Add(new AvHazardBar(p.Content, "STORM RISK"));
            stormBar.SetShown(false);
            profile = Track(p.Add(new EnvProfile(p.Content)));
            profile.SetHelp("PROFILE: the cloud layer as a slab between its base and top on a metre scale, with your altitude as a dashed level. The line under the top edge says whether you are below, inside or above the cloud.");
            outlook = Track(p.Add(new EnvOutlookStrip(p.Content, WeatherForecast.DefaultOffsetsMinutes.Length)));
            outlook.SetHelp("NEXT 60 MIN: the expected sky now and at +5, +10, +15, +30 and +60 minutes. The bar under each icon is the chance of rain; amber or red marks rain or a storm.");
            Track(p.Section(AvIcon.Wind, "SUN · MOON · WIND"));
            sun = Track(p.Add(new EnvSunCard(p.Content)));
            sun.SetHelp("SUN: the strip spans midnight to midnight. Bright hourly blocks indicate daylight; the vertical cursor is the current time. Elevation, bearing and sunrise/sunset are listed beside it. Below about -6 degrees expect poor visual range.");
            moon = Track(p.Add(new EnvMoonCard(p.Content)));
            moon.SetHelp("MOON: phase, illuminated fraction and natural moonlight level. The labeled meter shows illumination; a new moon means dark nights.");
            wind = Track(p.Add(new EnvWindCard(p.Content)));
            wind.SetHelp("WIND: the tape runs N-E-S-W-N and its cursor marks the direction the wind comes FROM. Exact FROM and TO bearings are listed beside it. The bar is turbulence: amber above 0.35, red above 0.6.");
            emptyRow = p.Add(new AvRow(p.Content));
            emptyRow.Set("NO MISSION LOADED", "", "", AvState.Inert);
            emptyRow.SetShown(false);
        }

        // ---- Apply -----------------------------------------------------------------------

        /// <summary>Writes the snapshot into the console.</summary>
        public void Apply(EnvData d)
        {
            SetEmpty(!d.HasMission);
            Console.Footer.Set(d.Footer, d.FooterState);
            if (!d.HasMission)
            {
                stormBar.SetShown(false);
                return;
            }
            ApplyWeather(d);
            ApplySky(d);
        }

        private void SetEmpty(bool value)
        {
            if (value == empty) return;
            empty = value;
            cardSeen = profileSeen = sunSeen = moonSeen = windSeen = false;
            viewKey = tileKey = long.MinValue;
            briefStory = null;
            foreach (AvPart part in parts) part.SetShown(!value);
            emptyRow.SetShown(value);
        }

        private static string Tag(string text, AvState st) =>
            (st == AvState.Caution || st == AvState.Danger ? AvStates.Glyph(st) : "") + text;

        private void ApplyWeather(EnvData d)
        {
            float rain = Mathf.Clamp01(d.Rain);
            viewAirRow.SetShown(d.HasViewAir);
            if (d.HasViewAir)
            {
                int cloudPct = Mathf.RoundToInt(Mathf.Clamp01(d.ViewCloud) * 100f), rainPct = Mathf.RoundToInt(Mathf.Clamp01(d.ViewRain) * 100f);
                int moistPct = Mathf.RoundToInt(Mathf.Clamp01(d.ViewMoisture) * 100f), temp = Mathf.RoundToInt(d.ViewTemperature);
                long key = (((cloudPct * 101L + rainPct) * 101L + moistPct) * 1000L + temp + 500L) * 4L
                    + (d.ViewCloud > 0.08f ? 1 : 0) + (d.ViewRain > 0.02f ? 2 : 0);
                if (key != viewKey)
                {
                    viewKey = key;
                    bool cloud = d.ViewCloud > 0.08f;
                    bool wet = d.ViewRain > 0.02f;
                    bool coldWet = d.ViewTemperature < 0f && Mathf.Max(d.ViewRain, d.ViewMoisture) > 0.02f;
                    string air = coldWet ? "COLD MOISTURE" : wet ? "RAIN" : cloud ? "IN CLOUD" : "CLEAR AIR";
                    viewAirRow.Set("VIEW AIR · " + air,
                        "CLOUD " + AvNum.Percent(d.ViewCloud) + " · RAIN " + AvNum.Percent(d.ViewRain) +
                        " · MOISTURE " + AvNum.Percent(d.ViewMoisture),
                        AvNum.Fixed(d.ViewTemperature, 0) + "°C EST", coldWet ? AvState.Caution : AvState.Info);
                }
            }
            FlightCat(d, out string cat, out AvState catState);

            int lit = EnvStateRow.LitFor(d.Regime);
            if (d.VisibilityKm >= 0f && d.VisibilityKm < 5f) lit |= EnvStateRow.Mist;
            stateRow.Set(lit, RegimeState(d.Regime));
            stateRow.SetHelp(d.Briefing);
            briefRow.SetShown(d.Story.Length > 0);
            if (d.Story.Length > 0 && (briefStory != d.Story || briefRole != d.Role || briefMarker != d.NextMarker || briefHeld != d.ForecastHeld))
            {
                briefStory = d.Story; briefRole = d.Role; briefMarker = d.NextMarker; briefHeld = d.ForecastHeld;
                briefRow.Set("STORY · " + d.Story.ToUpperInvariant() + (d.Role.Length > 0 ? " · " + d.Role.ToUpperInvariant() : ""),
                    d.NextMarker, d.ForecastHeld ? "HELD" : "MOVING", d.ForecastHeld ? AvState.Inert : AvState.Info);
            }

            if (Moved(ref cardKey, ref cardSeen, HashCode.Combine(d.Code, d.Name, d.Metar, (int)d.Regime, Mathf.RoundToInt(d.Cover * 100f), cat, (int)catState)))
                card.Set(new EnvSky
                {
                    Code = d.Code,
                    Word = d.Name,
                    Metar = d.Metar,
                    Regime = d.Regime,
                    Cover = d.Cover,
                    State = RegimeState(d.Regime),
                    Category = cat,
                    CategoryState = catState,
                });

            float vis = d.VisibilityKm;
            long tiles = (vis < 0f ? -1L : Mathf.RoundToInt(vis * 10f)) * 1000000L + Mathf.RoundToInt(rain * 100f) * 1000L + Mathf.RoundToInt(Mathf.Clamp01(d.NextFrac) * 100f);
            if (tiles != tileKey || tileNext != d.Next)
            {
                tileKey = tiles; tileNext = d.Next;
                visGauge.Set(vis < 0f ? 0f : Mathf.Clamp01(vis / 10f), vis < 0f ? "—" : vis >= 10f ? "10+" : AvNum.Fixed(vis, 1),
                    vis < 0f ? AvState.Inert : vis < 1.5f ? AvState.Danger : vis < 5f ? AvState.Caution : AvState.Ready);
                AvState rainState = rain > 0.6f ? AvState.Caution : AvState.Ready;
                rainGauge.Set(rain, Tag(AvNum.Percent(rain), rainState), rainState);
                nextGauge.Set(Mathf.Clamp01(d.NextFrac), d.Next, AvState.Ready);
            }

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
            if (Moved(ref profileKey, ref profileSeen, HashCode.Combine(Mathf.RoundToInt(d.Deck / 5f), Mathf.RoundToInt(d.Top / 5f),
                    Mathf.RoundToInt(d.CameraAlt / 5f), d.HasCamera, (int)statusState)))
                profile.Set(new EnvProfileData
                {
                    Base = d.Deck, Top = d.Top, CameraAlt = d.CameraAlt, HasCamera = d.HasCamera, State = statusState, Status = status,
                });

            // No forecast (held tape or no data): the strip simply goes; the story row already says HELD.
            bool hasForecast = rows != null && rows.Length > 0;
            outlook.SetShown(hasForecast);
            if (!hasForecast) return;
            outlook.SetHelp(d.AtPosition
                ? "NEXT 60 MIN, AT POSITION: the sky over where you are now, at +5, +10, +15, +30 and +60 minutes. The bar under each icon is the chance of rain; amber or red marks rain or a storm."
                : "NEXT 60 MIN: the sky over where you are now, at +5 to +60 minutes. You are moving fast, so you will not be here then. The bar is the chance of rain.");
            for (int i = 0; i < rows.Length; i++)
            {
                EnvForecastRow r = rows[i];
                AvState st = r.Regime == WeatherRegimeType.Storm ? AvState.Danger
                    : r.Rain > 0.2f ? AvState.Caution : RegimeState(r.Regime);
                outlook.Set(i, i == 0 ? "NOW" : "+" + r.OffsetMinutes, i == 0, r.Regime, r.Code, r.Rain, st);
            }
        }

        private void ApplySky(EnvData d)
        {
            bool lowLight = d.SunElevation <= -6f, strongWind = d.WindKts > 30f;
            AvState lightState = lowLight ? AvState.Caution : AvState.Info;
            AvState windState = strongWind ? AvState.Caution : AvState.Info;

            string solar = d.PolarDay ? "POLAR DAY" : d.PolarNight ? "POLAR NIGHT"
                : d.SunElevation > 0f ? "DAYLIGHT" : d.SunElevation > -6f ? "CIVIL TWILIGHT" : "NIGHT";
            string events = d.PolarDay || d.PolarNight ? "" : d.SunEvent;
            if (Moved(ref sunKey, ref sunSeen, HashCode.Combine(Mathf.RoundToInt(d.SunElevation * 10f), Mathf.RoundToInt(d.SunAzimuth),
                    Mathf.RoundToInt(d.TimeOfDay * 60f), Mathf.RoundToInt(d.Sunrise * 60f) * 4096 + Mathf.RoundToInt(d.Sunset * 60f),
                    d.PolarDay, d.PolarNight, solar, events)))
                sun.Set(new EnvSun
                {
                    Elevation = d.SunElevation, Azimuth = d.SunAzimuth, TimeOfDay = d.TimeOfDay, Sunrise = d.Sunrise, Sunset = d.Sunset,
                    PolarDay = d.PolarDay, PolarNight = d.PolarNight, Light = solar, Event = events,
                    LightState = lightState,
                });

            if (Moved(ref moonKey, ref moonSeen, HashCode.Combine(d.MoonPhase, Mathf.RoundToInt(d.MoonLit * 100f), Mathf.RoundToInt(d.MoonGlow * 100f), d.MoonWaxing, d.Moonless)))
                moon.Set(new EnvMoon
                {
                    Phase = d.MoonPhase, Lit = d.MoonLit, Glow = d.MoonGlow, Waxing = d.MoonWaxing, Moonless = d.Moonless,
                });

            AvState turbState = d.Turbulence >= 0.6f ? AvState.Danger : d.Turbulence >= 0.35f ? AvState.Caution : AvState.Ready;
            if (Moved(ref windKey, ref windSeen, HashCode.Combine(Mathf.RoundToInt(d.WindKts), d.WindFrom, d.WindTo, Mathf.RoundToInt(d.Turbulence * 100f), (int)windState)))
                wind.Set(new EnvWind
                {
                    Kts = d.WindKts, From = d.WindFrom, To = d.WindTo, Turbulence = d.Turbulence,
                    Cardinal = d.WindKts < 1f ? "" : WeatherWords.Cardinal(d.WindFrom),
                    TurbWord = WeatherWords.Turbulence(d.Turbulence), TurbState = turbState,
                    State = windState,
                });
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

        internal static AvState RegimeState(WeatherRegimeType type) => type switch
        {
            WeatherRegimeType.Scattered => AvState.Ready,
            WeatherRegimeType.Broken or WeatherRegimeType.Overcast or WeatherRegimeType.RainSquall => AvState.Caution,
            WeatherRegimeType.Storm => AvState.Danger,
            _ => AvState.Info,
        };
    }
}

using System;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>
    /// The Met Office window's only contact with the world, implemented against the tape: its front markers and story segments (MetOfficeTapeFill), its storm-lattice cells (MetOfficeCells) and set-pieces, the
    /// theater frame, the mission wind, the camera, the anchor and the 60-minute forecast. Slices 4b and 5 add the controls and the moving tape; the window stays.
    /// Commands go through the manager's console API and are written to a four-line host log.
    /// </summary>
    internal sealed class MetOfficeAdapter : IWeatherChartSource, IMetOfficeCommands
    {
        private const float KmPerMetre = 0.001f;
        private readonly WeatherManager w;
        private readonly MetOfficeLog log = new MetOfficeLog();
        private string logText = "";
        private int anchorMark = -1;          // 0 HERE, 1 30 KM AHEAD: the host's last anchor command ...
        private float anchorSetX, anchorSetZ; // ... and where it put the anchor (metres), so a later move of the anchor drops the latch

        public MetOfficeAdapter(WeatherManager manager) { w = manager; }

        // ---- Read side ---------------------------------------------------------------------------

        public void Fill(MetOfficeModel m)
        {
            m.Reset();
            m.Host = w.CanCommand;
            // The outlook cells always know their minutes, so the window never reads WeatherForecast: slice 4b changes the offsets here, not in the window.
            for (int i = 0; i < MetOfficeModel.OutlookCount && i < WeatherForecast.DefaultOffsetsMinutes.Length; i++) m.Outlook[i].Minutes = WeatherForecast.DefaultOffsetsMinutes[i];
            WeatherKey key = w.LiveKey;
            WeatherField field = w.Field;
            if (key == null || field == null || !w.IsCaptured || !w.TryMissionSeconds(out float now)) return;
            m.Captured = true;
            m.MissionSeconds = now;
            m.LogText = m.Host ? logText : "";

            // Sky now.
            WeatherPoint p = w.LocalWeather;
            WeatherTape tape = field.TapeSource;
            float camX = w.FieldPosition.x, camZ = w.FieldPosition.y;
            float sCam = field.Frame.S(camX, camZ);
            int skyHere = tape != null ? tape.SkyIndexAt(sCam) : -1;
            bool moving = key.Dynamic && field.Moving;
            m.Class = SkyMath.ClassOf(field.RowAt(camX, camZ));
            m.Cover = p.SkyCover;
            m.PinnedSky = key.SkyPin < SkyLibrary.Count ? key.SkyPin : -1;
            m.SkyNumber = skyHere + 1;
            m.SkyName = skyHere >= 0 ? SkyLibrary.Get(skyHere).Name : RegimeSnapshot.FromType(m.Class).Name;
            m.Metar = WeatherMetar.Line(p);
            SetLayer(m, 0, "HIGH", p.HighCover, p.HighBase);
            SetLayer(m, 1, "MID", p.MidCover, p.MidBase);
            SetLayer(m, 2, "LOW", p.LowCover, p.CloudBase);

            // Time.
            m.Running = key.Dynamic;
            m.Pace = m.Host ? w.Settings.Pace.Value : 0;   // a client's Pace setting is its own, not the host's: only the speed travels in the key
            m.SpeedMps = key.SystemSpeed;
            Vector3 wind = w.CurrentWindVelocity;
            WeatherForecast.FormatWind(wind.x, wind.z, out float kts, out _, out int from);
            m.WindFromDeg = from;
            m.WindMps = kts / 1.943844f;

            // The STORY strip is the tape's own segments and markers around the camera; a held tape counts minutes at the tape speed and says HELD.
            if (tape != null) MetOfficeTapeFill.Story(m, tape, sCam, key.SystemSpeed, moving);

            // Chart: theater, camera, anchor, fronts, cells, set-pieces.
            Vector2 half = w.TheaterHalfSpan;
            m.HalfXKm = half.x * KmPerMetre; m.HalfZKm = half.y * KmPerMetre;
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (camera != null)
            {
                GlobalPosition at = camera.transform.GlobalPosition();
                m.HasCamera = true;
                m.CamX = (float)at.x * KmPerMetre; m.CamZ = (float)at.z * KmPerMetre;
                Vector3 forward = camera.transform.forward;
                m.CamHeadingDeg = forward.x * forward.x + forward.z * forward.z < 1e-6f ? 0f : WeatherMath.VectorToHeading(forward.x, forward.z);
            }
            m.HasAnchor = key.HasAnchor;
            m.AnchorX = key.AnchorX * KmPerMetre; m.AnchorZ = key.AnchorZ * KmPerMetre;
            // HERE / 30 KM is what the host chose, valid while the anchor still stands where that choice put it (a scenario or a client's own key moves it).
            m.AnchorMark = key.HasAnchor && anchorMark >= 0 && Math.Abs(key.AnchorX - anchorSetX) < 1f && Math.Abs(key.AnchorZ - anchorSetZ) < 1f ? anchorMark : -1;
            if (tape != null) MetOfficeTapeFill.Fronts(m, tape, field.Frame, camera != null ? m.CamX * 1000f : 0f, camera != null ? m.CamZ * 1000f : 0f, key.SystemSpeed, moving);
            MetOfficeCells.Fill(m, field);
            for (int i = 0; i < MetOfficeModel.SetCount; i++) m.Sets[i].Forced = (key.Sets & MetCatalog.SetBits[i]) != 0;
            for (int i = 0; i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                int slot = Array.IndexOf(MetCatalog.SetBits, s.Set);
                if (slot >= 0) m.Sets[slot] = new MetSetPiece { Forced = m.Sets[slot].Forced, Present = true, Placed = true, X = s.X * KmPerMetre, Z = s.Z * KmPerMetre, Strength = s.Strength };
            }
            if (m.Sets[5].Forced) m.Sets[5].Present = true; // the fog bank is a low layer, not a formation: Present, never Placed, so the chart marks and labels nothing for it

            m.ForcedRain = w.ForcedRainIntensity;

            // Outlook: the legacy forecast at the aircraft; HELD while the tape is held.
            m.OutlookHeld = !key.Dynamic;
            ForecastStep[] steps = w.GetForecastTimeline();
            if (steps != null && steps.Length >= MetOfficeModel.OutlookCount)
            {
                m.HasOutlook = true;
                for (int i = 0; i < MetOfficeModel.OutlookCount; i++)
                    m.Outlook[i] = new MetOutlookCell { Minutes = steps[i].OffsetMinutes, Class = steps[i].Regime.Type, Code = steps[i].Regime.Code, Rain = steps[i].RainProbability };
            }

            // Identity.
            m.SeedMode = m.Host ? w.Settings.SeedMode.Value.ToString() : "—";   // the client's own SeedMode setting says nothing about the host's mission
            m.SeedHex = MetOfficeText.Hex8(key.Seed);
            m.TapeHex = MetOfficeText.Hex8(key.TapeId);
            m.SerialHex = m.TapeHex.Substring(0, 4);
            m.LayoutSalt = key.LayoutSalt;
            m.Sanitize();
        }

        private static void SetLayer(MetOfficeModel m, int i, string label, float cover, float baseMetres) =>
            m.Layers[i] = new MetLayer { Present = cover >= 0.05f, Label = label, Cover = cover, BaseMetres = baseMetres };

        // ---- Write side --------------------------------------------------------------------------

        private bool Ready => w.CanCommand && w.IsCaptured;

        private void Note(string what)
        {
            w.TryMissionSeconds(out float now);
            log.Add(MetOfficeText.LogLine(now, "HOST", what));
            logText = log.Joined();
        }

        public bool SetTapeMoves(bool moves)
        {
            bool ok = Ready && w.SetTapeMoves(moves);
            if (ok) Note(MetOfficeText.HoldNote(moves));
            return ok;
        }

        public bool SetPace(int pace)
        {
            int before = w.Settings.Pace.Value;
            bool ok = Ready && w.SetPace(pace);
            if (ok) Note(MetOfficeText.PaceNote(before, w.Settings.Pace.Value));
            return ok;
        }

        public bool PinSky(int sky)
        {
            if (!Ready) return false;
            float? rain = w.ForcedRainIntensity;
            bool ok = w.PinSky(sky < 0 ? WeatherKey.None : (byte)sky);
            // AUTO resumes through ResumeChanging -> ClearManualOverride, which also clears a forced rain: put the override back (the tape resume stays and is labelled).
            if (ok && sky < 0 && rain.HasValue) w.SetForcedRain(rain);
            if (ok) Note(sky < 0 ? MetOfficeText.AutoNote() : MetOfficeText.PinNote(sky));
            return ok;
        }

        public bool Reroll()
        {
            if (!Ready) return false;
            int before = w.LiveKey.LayoutSalt;
            w.RerollLayout();
            Note(MetOfficeText.RerollNote(before));
            return true;
        }

        public bool SetSets(byte mask)
        {
            if (!Ready) return false;
            byte before = w.ForcedSets;
            w.SetForcedSets(mask);
            for (int i = 0; i < MetCatalog.SetBits.Length; i++)
                if (((before ^ mask) & MetCatalog.SetBits[i]) != 0) Note(MetOfficeText.SetNote(i, (mask & MetCatalog.SetBits[i]) != 0));
            return true;
        }

        public bool PlaceAhead(float metres)
        {
            if (!Ready || SceneSingleton<CameraStateManager>.i?.mainCamera == null) return false;
            w.PlaceAhead(metres);
            anchorMark = metres < 1f ? 0 : 1;
            anchorSetX = w.LiveKey.AnchorX; anchorSetZ = w.LiveKey.AnchorZ;
            Note(MetOfficeText.AnchorNote(metres));
            return true;
        }

        public bool ClearPlacement()
        {
            if (!Ready || !w.HasAnchor) return false;
            w.ClearPlacement();
            anchorMark = -1;
            Note(MetOfficeText.DefaultNote());
            return true;
        }

        public bool SetRain(float? intensity)
        {
            if (!Ready) return false;
            w.SetForcedRain(intensity);
            Note(MetOfficeText.RainNote(intensity));
            return true;
        }

        public bool Scenario(WeatherScenario scenario)
        {
            bool ok = Ready && w.ApplyScenario(scenario);
            if (ok) Note(MetOfficeText.ScenarioNote(Array.IndexOf(MetCatalog.Scenarios, scenario)));
            return ok;
        }

        // Seam stubs: the window routes the 4B and S5 controls here; they stay refused (and disabled by MetOfficeRules) until the tape console and tape motion land.
        public bool SetStory(int story) => false;
        public bool FrontIn(int minutes) => false;
    }
}

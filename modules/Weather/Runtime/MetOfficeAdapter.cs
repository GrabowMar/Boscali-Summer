using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>
    /// The weather console's only contact with the world: the sky over the camera, the mission wind, the nearest fronts and the tape's segments along the wind (MetOfficeTapeFill).
    /// Commands go through the manager's console API.
    /// </summary>
    internal sealed class MetOfficeAdapter : IWeatherChartSource, IMetOfficeCommands
    {
        private const float KmPerMetre = 0.001f;
        private readonly WeatherManager w;

        public MetOfficeAdapter(WeatherManager manager) { w = manager; }

        public void Fill(MetOfficeModel m)
        {
            m.Reset();
            m.Host = w.CanCommand;
            WeatherKey key = w.LiveKey;
            WeatherField field = w.Field;
            if (key == null || field == null || !w.IsCaptured || !w.TryMissionSeconds(out float now)) return;
            m.Captured = true;
            m.MissionSeconds = now;

            // Sky overhead.
            WeatherPoint p = w.LocalWeather;
            WeatherTape tape = field.TapeSource;
            float camX = w.FieldPosition.x, camZ = w.FieldPosition.y;
            float sCam = field.Frame.S(camX, camZ);
            int skyHere = tape != null ? tape.SkyIndexAt(sCam) : -1;
            m.Class = SkyMath.ClassOf(field.RowAt(camX, camZ));
            m.Cover = p.SkyCover;
            m.PinnedSky = key.SkyPin < SkyLibrary.Count ? key.SkyPin : -1;
            // A mission script can force set-pieces, a story, an anchor or rain (IWeatherDirector): AUTO clears those too.
            m.Forced = key.Sets != 0 || key.Story < WeatherStories.Count || key.HasAnchor || w.ForcedRainIntensity.HasValue;
            m.SkyNumber = skyHere + 1;
            m.SkyName = skyHere >= 0 ? SkyLibrary.Get(skyHere).Name : RegimeSnapshot.FromType(m.Class).Name;
            m.Metar = WeatherMetar.Line(p);
            SetLayer(m, 0, "HIGH", p.HighCover, p.HighBase);
            SetLayer(m, 1, "MID", p.MidCover, p.MidBase);
            SetLayer(m, 2, "LOW", p.LowCover, p.CloudBase);

            Vector3 wind = w.CurrentWindVelocity;
            WeatherForecast.FormatWind(wind.x, wind.z, out float kts, out _, out int from);
            m.WindFromDeg = from;
            m.WindMps = kts / 1.943844f;

            // Fronts and the strip are measured from the camera.
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (camera != null)
            {
                GlobalPosition at = camera.transform.GlobalPosition();
                m.HasCamera = true;
                m.CamX = at.x * KmPerMetre; m.CamZ = at.z * KmPerMetre;
            }
            if (tape != null)
            {
                MetOfficeTapeFill.Strip(m, tape, sCam);
                MetOfficeTapeFill.Fronts(m, tape, field.Frame, m.CamX * 1000f, m.CamZ * 1000f);
            }
            m.LayoutSalt = key.LayoutSalt;
            m.Sanitize();
        }

        private static void SetLayer(MetOfficeModel m, int i, string label, float cover, float baseMetres) =>
            m.Layers[i] = new MetLayer { Present = cover >= 0.05f, Label = label, Cover = cover, BaseMetres = baseMetres };

        private bool Ready => w.CanCommand && w.IsCaptured;

        public bool PinSky(int sky)
        {
            if (!Ready) return false;
            return sky < 0 ? w.ApplyScenario(WeatherScenario.ResetAll) : w.PinSky((byte)sky);
        }

        public bool Reroll()
        {
            if (!Ready) return false;
            w.RerollLayout();
            return true;
        }
    }
}

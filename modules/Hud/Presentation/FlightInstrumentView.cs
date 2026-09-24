using System.Globalization;
using BoscaliSummer.Features.Hud.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Presentation
{
    internal sealed class FlightInstrumentView
    {
        private readonly HudSurface surface;
        private readonly RectTransform left, right, heading;
        private readonly TMP_Text speed, altitude, radar, climb, fuel, throttle, bearing, g, aoa;
        private readonly Image fuelBar, throttleBar;
        private readonly AttitudeLines attitude;
        public FlightInstrumentView(Transform parent)
        {
            surface = new HudSurface("Boscali / flight instruments", parent);
            left = Card("Energy");
            right = Card("Height");
            heading = Card("Navigation");
            speed = Label(left, "Speed", 20, 12, 90, 144, 28);
            g = Label(left, "Mach and load", 15, 12, 66, 144, 22);
            aoa = Label(left, "Angle of attack", 15, 12, 44, 144, 22);
            fuel = Label(left, "Fuel", 15, 12, 14, 144, 22);
            altitude = Label(right, "Altitude", 20, 12, 90, 144, 28);
            radar = Label(right, "Radar altitude", 15, 12, 66, 144, 22);
            climb = Label(right, "Climb rate", 15, 12, 44, 144, 22);
            throttle = Label(right, "Throttle", 15, 12, 14, 144, 22);
            bearing = Label(heading, "Bearing", 20, 0, 0, 88, 28); bearing.alignment = TextAlignmentOptions.Center;
            fuelBar = HudSurface.Line("Fuel level", left, HudSurface.Ink);
            throttleBar = HudSurface.Line("Throttle level", right, HudSurface.Ink);
            attitude = HudSurface.Rect("Attitude", surface.Transform).gameObject.AddComponent<AttitudeLines>(); attitude.raycastTarget = false;
        }
        private RectTransform Card(string name)
        {
            RectTransform rect = HudSurface.Rect(name, surface.Transform);
            var backing = HudSurface.Rect("Value backing", rect).gameObject.AddComponent<HudPanel>();
            backing.raycastTarget = false;
            return rect;
        }
        private static void Backing(RectTransform group, int contrast, bool compact = false)
        {
            HudPanel backing = group.GetComponentInChildren<HudPanel>();
            // Glass is a narrow value ribbon, never a cockpit-sized dashboard card.
            HudSurface.Place(backing.rectTransform, 0, compact || contrast == 2 ? 0 : 90,
                compact ? 88 : 156, compact ? 28 : contrast == 2 ? 120 : 28);
            backing.Style(contrast, false, false);
        }
        private static TMP_Text Label(Transform parent, string name, int size, float x, float y, float w, float h)
        { TMP_Text text = HudSurface.Text(name, parent, size); HudSurface.Place(text.rectTransform, x, y, w, h); return text; }
        public void Present(FlightReading reading, bool hideAttitude, int scale, int opacity, int contrast)
        {
            if (!reading.Valid) { Hide(); return; }
            surface.Resize(HudLayout.Scale(scale));
            Rect safe = surface.Safe;
            float centerX = safe.center.x, centerY = safe.center.y;
            float separation = Mathf.Min(230, safe.width * .15f);
            HudSurface.Place(left, centerX - separation - 156, centerY - 25, 156, 120);
            HudSurface.Place(right, centerX + separation, centerY - 25, 156, 120);
            HudSurface.Place(heading, centerX - 44, centerY + 208, 88, 28);
            Backing(left, contrast); Backing(right, contrast); Backing(heading, contrast, true);
            HudSurface.Write(speed, UnitConverter.SpeedReading(reading.Speed), HudSurface.Ink);
            HudSurface.Write(altitude, UnitConverter.AltitudeReading(reading.Altitude), HudSurface.Ink);
            HudSurface.Write(radar, "RAD " + UnitConverter.AltitudeReading(reading.RadarAltitude), HudSurface.Ink);
            HudSurface.Write(climb, "V/S " + UnitConverter.ClimbRateReading(reading.Climb), HudSurface.Ink);
            HudSurface.Write(g, "M " + reading.Mach.ToString("0.00", CultureInfo.InvariantCulture) + "  G " + (float.IsNaN(reading.G) ? "--" : reading.G.ToString("0.0", CultureInfo.InvariantCulture)), HudSurface.Ink);
            HudSurface.Write(aoa, "AOA  " + (float.IsNaN(reading.AoA) ? "--" : reading.AoA.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "\u00b0"), HudSurface.Ink);
            HudSurface.Write(fuel, "FUEL  " + (reading.Fuel * 100).ToString("0") + "%", reading.Fuel < .15f ? HudSurface.Tone(HudTone.Caution) : HudSurface.Ink);
            HudSurface.Write(throttle, "THR  " + (reading.Throttle * 100).ToString("0") + "%", HudSurface.Ink);
            HudSurface.Write(bearing, (Mathf.RoundToInt(reading.Heading) % 360).ToString("000") + "\u00b0", HudSurface.Ink);
            HudSurface.Place(fuelBar.rectTransform, 0, 14, 2, 104 * Mathf.Clamp01(reading.Fuel));
            HudSurface.Place(throttleBar.rectTransform, 154, 14, 2, 104 * Mathf.Clamp01(reading.Throttle));
            fuelBar.color = reading.Fuel < .15f ? HudSurface.Tone(HudTone.Caution) : HudSurface.Ink;
            throttleBar.color = HudSurface.Ink;
            HudSurface.Place(attitude.rectTransform, centerX - 96, centerY + 70, 192, 108);
            attitude.gameObject.SetActive(!hideAttitude); attitude.Set(reading.Pitch, reading.Roll);
            surface.Group.alpha = HudLayout.Opacity(opacity); surface.Show(true);
        }
        public void Hide() => surface.Show(false);
        public void Destroy() => surface.Destroy();
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AttitudeLines : MaskableGraphic
    {
        private float pitch, roll;
        public void Set(float p, float r)
        { if (Mathf.Abs(pitch - p) < .1f && Mathf.Abs(roll - r) < .1f) return; pitch = p; roll = r; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Vector2 center = rectTransform.rect.center;
            Color ink = HudSurface.Ink;
            Quaternion rotation = Quaternion.Euler(0, 0, -roll);
            for (int degrees = -20; degrees <= 20; degrees += 10)
            {
                float y = (degrees - pitch) * 2;
                if (Mathf.Abs(y) > 38) continue;
                float length = degrees == 0 ? 70 : 38;
                HudPanel.Stroke(mesh, center + (Vector2)(rotation * new Vector3(-length, y)), center + (Vector2)(rotation * new Vector3(-12, y)), ink, 1.5f);
                HudPanel.Stroke(mesh, center + (Vector2)(rotation * new Vector3(12, y)), center + (Vector2)(rotation * new Vector3(length, y)), ink, 1.5f);
            }
            HudPanel.Stroke(mesh, center + new Vector2(-9, 0), center, ink, 2);
            HudPanel.Stroke(mesh, center, center + new Vector2(9, 0), ink, 2);
        }
    }
}

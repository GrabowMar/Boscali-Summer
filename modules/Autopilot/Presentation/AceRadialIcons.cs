using BoscaliSummer.Modules.Autopilot.Domain;
using NOAvionics;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Line-art glyphs for the interaction menu's option discs, stroked into the shared vector
    /// surface (no sprites, no assets). Each glyph fits a box of <c>s</c> half-extent around
    /// (x, y), canvas units, +Y up.
    /// </summary>
    internal static class AceRadialIcons
    {
        public static void Draw(AvQuadBuffer b, AceIcon icon, float x, float y, float s, float w, Rgba c)
        {
            switch (icon)
            {
                case AceIcon.Aircraft:
                case AceIcon.Flight:
                    Plane(b, x, y, s, w, c);
                    break;
                case AceIcon.Autopilot:
                    Plane(b, x, y + (s * 0.2f), s * 0.75f, w, c);
                    AvStrokes.Line(b, x - s, y - (s * 0.75f), x + s, y - (s * 0.75f), w, c);
                    AvStrokes.Line(b, x - (s * 0.45f), y - (s * 0.5f), x, y - (s * 0.75f), w, c);
                    break;
                case AceIcon.Assist:
                    AvStrokes.Arc(b, x, y, s * 0.85f, 200f, 340f, 8, w, c);
                    AvStrokes.Line(b, x - s, y + (s * 0.1f), x + s, y + (s * 0.1f), w, c);
                    AvStrokes.Line(b, x, y + (s * 0.1f), x, y + (s * 0.7f), w, c);
                    break;
                case AceIcon.Hover:
                    AvStrokes.Line(b, x - s, y + (s * 0.55f), x + s, y + (s * 0.55f), w, c);
                    AvStrokes.Line(b, x, y + (s * 0.55f), x, y - (s * 0.1f), w, c);
                    AvStrokes.Chevron(b, x, y - (s * 0.55f), s * 0.6f, -90f, w, c);
                    AvStrokes.Chevron(b, x, y - (s * 0.1f), s * 0.6f, 90f, w, c);
                    break;
                case AceIcon.Gear:
                    AvStrokes.Ring(b, x, y - (s * 0.35f), s * 0.5f, 12, w, c);
                    AvStrokes.Line(b, x, y - (s * 0.35f), x, y + s, w, c);
                    AvStrokes.Line(b, x, y + (s * 0.45f), x + (s * 0.6f), y + s, w, c);
                    break;
                case AceIcon.Engine:
                    AvStrokes.Ring(b, x, y, s * 0.85f, 14, w, c);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (i * 120f + 30f) * UnityEngine.Mathf.Deg2Rad;
                        AvStrokes.Line(b, x, y, x + (UnityEngine.Mathf.Cos(a) * s * 0.7f), y + (UnityEngine.Mathf.Sin(a) * s * 0.7f), w, c);
                    }
                    break;
                case AceIcon.Eject:
                    AvStrokes.Chevron(b, x, y + (s * 0.45f), s, 90f, w, c);
                    AvStrokes.Line(b, x, y + (s * 0.5f), x, y - (s * 0.5f), w, c);
                    AvStrokes.Line(b, x - (s * 0.7f), y - s, x + (s * 0.7f), y - s, w, c);
                    break;
                case AceIcon.Lights:
                    AvStrokes.Ring(b, x, y + (s * 0.2f), s * 0.55f, 12, w, c);
                    AvStrokes.Line(b, x - (s * 0.3f), y - (s * 0.6f), x + (s * 0.3f), y - (s * 0.6f), w, c);
                    AvStrokes.Line(b, x - (s * 0.2f), y - (s * 0.9f), x + (s * 0.2f), y - (s * 0.9f), w, c);
                    break;
                case AceIcon.Beam:
                    AvStrokes.Line(b, x - s, y + (s * 0.5f), x - s, y - (s * 0.5f), w, c);
                    AvStrokes.Line(b, x - (s * 0.6f), y + (s * 0.4f), x + s, y + (s * 0.8f), w, c);
                    AvStrokes.Line(b, x - (s * 0.6f), y, x + s, y, w, c);
                    AvStrokes.Line(b, x - (s * 0.6f), y - (s * 0.4f), x + s, y - (s * 0.8f), w, c);
                    break;
                case AceIcon.NightVision:
                    AvStrokes.Ring(b, x - (s * 0.45f), y, s * 0.42f, 10, w, c);
                    AvStrokes.Ring(b, x + (s * 0.45f), y, s * 0.42f, 10, w, c);
                    AvStrokes.Line(b, x - (s * 0.05f), y + (s * 0.1f), x + (s * 0.05f), y + (s * 0.1f), w, c);
                    break;
                case AceIcon.Weapons:
                case AceIcon.Strike:
                    AvStrokes.Ring(b, x, y, s * 0.6f, 12, w, c);
                    AvStrokes.Line(b, x - s, y, x - (s * 0.3f), y, w, c);
                    AvStrokes.Line(b, x + (s * 0.3f), y, x + s, y, w, c);
                    AvStrokes.Line(b, x, y - s, x, y - (s * 0.3f), w, c);
                    AvStrokes.Line(b, x, y + (s * 0.3f), x, y + s, w, c);
                    break;
                case AceIcon.Next:
                    AvStrokes.Chevron(b, x - (s * 0.3f), y, s * 0.9f, 0f, w, c);
                    AvStrokes.Chevron(b, x + (s * 0.3f), y, s * 0.9f, 0f, w, c);
                    break;
                case AceIcon.Previous:
                    AvStrokes.Chevron(b, x - (s * 0.3f), y, s * 0.9f, 180f, w, c);
                    AvStrokes.Chevron(b, x + (s * 0.3f), y, s * 0.9f, 180f, w, c);
                    break;
                case AceIcon.Station:
                    AvStrokes.Line(b, x - s, y + (s * 0.7f), x + s, y + (s * 0.7f), w, c);
                    AvStrokes.Line(b, x, y + (s * 0.7f), x, y + (s * 0.35f), w, c);
                    AvStrokes.Line(b, x - (s * 0.8f), y + (s * 0.1f), x + (s * 0.5f), y + (s * 0.1f), w, c);
                    AvStrokes.Line(b, x - (s * 0.8f), y - (s * 0.3f), x + (s * 0.5f), y - (s * 0.3f), w, c);
                    AvStrokes.Line(b, x + (s * 0.5f), y + (s * 0.1f), x + s, y - (s * 0.1f), w, c);
                    AvStrokes.Line(b, x + (s * 0.5f), y - (s * 0.3f), x + s, y - (s * 0.1f), w, c);
                    break;
                case AceIcon.Link:
                    AvStrokes.Ring(b, x - (s * 0.4f), y, s * 0.45f, 10, w, c);
                    AvStrokes.Ring(b, x + (s * 0.4f), y, s * 0.45f, 10, w, c);
                    break;
                case AceIcon.Turret:
                    AvStrokes.Arc(b, x, y - (s * 0.4f), s * 0.6f, 0f, 180f, 8, w, c);
                    AvStrokes.Line(b, x - s, y - (s * 0.4f), x + s, y - (s * 0.4f), w, c);
                    AvStrokes.Line(b, x, y - (s * 0.1f), x + s, y + (s * 0.7f), w, c);
                    break;
                case AceIcon.Defence:
                    AvStrokes.Line(b, x - (s * 0.8f), y + (s * 0.8f), x + (s * 0.8f), y + (s * 0.8f), w, c);
                    AvStrokes.Line(b, x - (s * 0.8f), y + (s * 0.8f), x - (s * 0.8f), y, w, c);
                    AvStrokes.Line(b, x + (s * 0.8f), y + (s * 0.8f), x + (s * 0.8f), y, w, c);
                    AvStrokes.Line(b, x - (s * 0.8f), y, x, y - s, w, c);
                    AvStrokes.Line(b, x + (s * 0.8f), y, x, y - s, w, c);
                    break;
                case AceIcon.Flare:
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (i * 45f) * UnityEngine.Mathf.Deg2Rad;
                        float dx = UnityEngine.Mathf.Cos(a) * s, dy = UnityEngine.Mathf.Sin(a) * s;
                        AvStrokes.Line(b, x - dx, y - dy, x + dx, y + dy, w, c);
                    }
                    break;
                case AceIcon.Cycle:
                    AvStrokes.Arc(b, x, y, s * 0.75f, 30f, 300f, 12, w, c);
                    AvStrokes.Chevron(b, x + (s * 0.65f), y + (s * 0.4f), s * 0.5f, 90f, w, c);
                    break;
                case AceIcon.Wing:
                    // Three aircraft in a vic: the wing.
                    AvStrokes.Chevron(b, x, y + (s * 0.45f), s * 0.75f, 90f, w, c);
                    AvStrokes.Chevron(b, x - (s * 0.7f), y - (s * 0.4f), s * 0.6f, 90f, w, c);
                    AvStrokes.Chevron(b, x + (s * 0.7f), y - (s * 0.4f), s * 0.6f, 90f, w, c);
                    break;
                case AceIcon.Target:
                    AvStrokes.Ring(b, x, y, s * 0.62f, 14, w, c);
                    AvStrokes.Line(b, x, y + (s * 0.35f), x, y + s, w, c);
                    AvStrokes.Line(b, x, y - (s * 0.35f), x, y - s, w, c);
                    AvStrokes.Line(b, x + (s * 0.35f), y, x + s, y, w, c);
                    AvStrokes.Line(b, x - (s * 0.35f), y, x - s, y, w, c);
                    break;
                case AceIcon.Radar:
                    AvStrokes.Arc(b, x - (s * 0.6f), y - (s * 0.6f), s * 0.6f, 0f, 90f, 5, w, c);
                    AvStrokes.Arc(b, x - (s * 0.6f), y - (s * 0.6f), s * 1.2f, 0f, 90f, 8, w, c);
                    AvStrokes.Line(b, x - (s * 0.6f), y - (s * 0.6f), x + (s * 0.5f), y + (s * 0.5f), w, c);
                    break;
                case AceIcon.View:
                    AvStrokes.Arc(b, x, y - (s * 0.6f), s * 1.05f, 35f, 145f, 8, w, c);
                    AvStrokes.Arc(b, x, y + (s * 0.6f), s * 1.05f, 215f, 325f, 8, w, c);
                    AvStrokes.Ring(b, x, y, s * 0.3f, 8, w, c);
                    break;
                case AceIcon.Cockpit:
                    AvStrokes.Arc(b, x, y - (s * 0.5f), s, 0f, 180f, 10, w, c);
                    AvStrokes.Line(b, x - s, y - (s * 0.5f), x + s, y - (s * 0.5f), w, c);
                    AvStrokes.Line(b, x, y - (s * 0.5f), x, y + (s * 0.5f), w, c);
                    break;
                case AceIcon.Orbit:
                    AvStrokes.Arc(b, x, y, s * 0.9f, 20f, 320f, 12, w, c);
                    AvStrokes.Fill(b, x - (s * 0.2f), y - (s * 0.2f), s * 0.4f, s * 0.4f, c);
                    break;
                case AceIcon.Map:
                    AvStrokes.Line(b, x - s, y + (s * 0.7f), x - (s * 0.35f), y + s, w, c);
                    AvStrokes.Line(b, x - (s * 0.35f), y + s, x + (s * 0.35f), y + (s * 0.7f), w, c);
                    AvStrokes.Line(b, x + (s * 0.35f), y + (s * 0.7f), x + s, y + s, w, c);
                    AvStrokes.Line(b, x - s, y + (s * 0.7f), x - s, y - s, w, c);
                    AvStrokes.Line(b, x + s, y + s, x + s, y - (s * 0.7f), w, c);
                    AvStrokes.Line(b, x - s, y - s, x - (s * 0.35f), y - (s * 0.7f), w, c);
                    AvStrokes.Line(b, x - (s * 0.35f), y - (s * 0.7f), x + (s * 0.35f), y - s, w, c);
                    AvStrokes.Line(b, x + (s * 0.35f), y - s, x + s, y - (s * 0.7f), w, c);
                    break;
                case AceIcon.Mark:
                    AvStrokes.Diamond(b, x, y, s * 0.8f, w, c);
                    AvStrokes.Fill(b, x - (s * 0.15f), y - (s * 0.15f), s * 0.3f, s * 0.3f, c);
                    break;
                case AceIcon.Hud:
                    AvStrokes.Bracket(b, x - s, y - (s * 0.75f), s * 2f, s * 1.5f, s * 0.5f, w, c);
                    AvStrokes.Line(b, x - (s * 0.5f), y + (s * 0.2f), x + (s * 0.5f), y + (s * 0.2f), w, c);
                    AvStrokes.Line(b, x - (s * 0.5f), y - (s * 0.2f), x + (s * 0.2f), y - (s * 0.2f), w, c);
                    break;
                case AceIcon.Camera:
                    AvStrokes.Line(b, x - s, y + (s * 0.55f), x + (s * 0.4f), y + (s * 0.55f), w, c);
                    AvStrokes.Line(b, x - s, y - (s * 0.55f), x + (s * 0.4f), y - (s * 0.55f), w, c);
                    AvStrokes.Line(b, x - s, y + (s * 0.55f), x - s, y - (s * 0.55f), w, c);
                    AvStrokes.Line(b, x + (s * 0.4f), y + (s * 0.55f), x + (s * 0.4f), y - (s * 0.55f), w, c);
                    AvStrokes.Line(b, x + (s * 0.4f), y, x + s, y + (s * 0.45f), w, c);
                    AvStrokes.Line(b, x + (s * 0.4f), y, x + s, y - (s * 0.45f), w, c);
                    break;
                case AceIcon.Support:
                    AvStrokes.Line(b, x, y - s, x, y + (s * 0.3f), w, c);
                    AvStrokes.Chevron(b, x, y + (s * 0.4f), s * 1.1f, 90f, w, c);
                    AvStrokes.Line(b, x - (s * 0.6f), y - s, x + (s * 0.6f), y - s, w, c);
                    break;
                case AceIcon.Clear:
                case AceIcon.Stop:
                    AvStrokes.Line(b, x - (s * 0.7f), y - (s * 0.7f), x + (s * 0.7f), y + (s * 0.7f), w, c);
                    AvStrokes.Line(b, x - (s * 0.7f), y + (s * 0.7f), x + (s * 0.7f), y - (s * 0.7f), w, c);
                    break;
                case AceIcon.Radio:
                    AvStrokes.Line(b, x - (s * 0.5f), y - s, x - (s * 0.5f), y + (s * 0.2f), w, c);
                    AvStrokes.Arc(b, x - (s * 0.5f), y + (s * 0.2f), s * 0.5f, -50f, 50f, 5, w, c);
                    AvStrokes.Arc(b, x - (s * 0.5f), y + (s * 0.2f), s, -50f, 50f, 7, w, c);
                    break;
                case AceIcon.Play:
                    AvStrokes.Line(b, x - (s * 0.5f), y - (s * 0.8f), x - (s * 0.5f), y + (s * 0.8f), w, c);
                    AvStrokes.Line(b, x - (s * 0.5f), y + (s * 0.8f), x + (s * 0.8f), y, w, c);
                    AvStrokes.Line(b, x + (s * 0.8f), y, x - (s * 0.5f), y - (s * 0.8f), w, c);
                    break;
                case AceIcon.Power:
                    AvStrokes.Arc(b, x, y - (s * 0.1f), s * 0.8f, 120f, 420f, 12, w, c);
                    AvStrokes.Line(b, x, y + s, x, y + (s * 0.1f), w, c);
                    break;
                case AceIcon.Seek:
                    AvStrokes.Line(b, x - s, y - (s * 0.4f), x + s, y - (s * 0.4f), w, c);
                    AvStrokes.Line(b, x + (s * 0.2f), y - (s * 0.8f), x + (s * 0.2f), y + (s * 0.8f), w, c);
                    AvStrokes.Chevron(b, x + (s * 0.6f), y + (s * 0.35f), s * 0.5f, 0f, w, c);
                    break;
                case AceIcon.Comms:
                case AceIcon.Call:
                    AvStrokes.Line(b, x - s, y + (s * 0.7f), x + s, y + (s * 0.7f), w, c);
                    AvStrokes.Line(b, x - s, y + (s * 0.7f), x - s, y - (s * 0.3f), w, c);
                    AvStrokes.Line(b, x + s, y + (s * 0.7f), x + s, y - (s * 0.3f), w, c);
                    AvStrokes.Line(b, x - s, y - (s * 0.3f), x - (s * 0.2f), y - (s * 0.3f), w, c);
                    AvStrokes.Line(b, x + (s * 0.2f), y - (s * 0.3f), x + s, y - (s * 0.3f), w, c);
                    AvStrokes.Line(b, x - (s * 0.2f), y - (s * 0.3f), x - (s * 0.5f), y - s, w, c);
                    AvStrokes.Line(b, x - (s * 0.5f), y - s, x + (s * 0.2f), y - (s * 0.3f), w, c);
                    break;
                case AceIcon.Preset:
                    AvStrokes.Bracket(b, x - (s * 0.8f), y - (s * 0.8f), s * 1.6f, s * 1.6f, s * 0.5f, w, c);
                    AvStrokes.Fill(b, x - (s * 0.2f), y - (s * 0.2f), s * 0.4f, s * 0.4f, c);
                    break;
                case AceIcon.Confirm:
                    AvStrokes.Line(b, x - (s * 0.8f), y, x - (s * 0.2f), y - (s * 0.6f), w, c);
                    AvStrokes.Line(b, x - (s * 0.2f), y - (s * 0.6f), x + (s * 0.9f), y + (s * 0.7f), w, c);
                    break;
                default:
                    AvStrokes.Fill(b, x - (s * 0.35f), y - (s * 0.35f), s * 0.7f, s * 0.7f, c);
                    break;
            }
        }

        /// <summary>Top-down delta-wing silhouette pointing up.</summary>
        private static void Plane(AvQuadBuffer b, float x, float y, float s, float w, Rgba c)
        {
            AvStrokes.Line(b, x, y + s, x, y - (s * 0.85f), w, c);
            AvStrokes.Line(b, x, y + (s * 0.35f), x - s, y - (s * 0.35f), w, c);
            AvStrokes.Line(b, x, y + (s * 0.35f), x + s, y - (s * 0.35f), w, c);
            AvStrokes.Line(b, x, y - (s * 0.55f), x - (s * 0.45f), y - (s * 0.9f), w, c);
            AvStrokes.Line(b, x, y - (s * 0.55f), x + (s * 0.45f), y - (s * 0.9f), w, c);
        }
    }
}

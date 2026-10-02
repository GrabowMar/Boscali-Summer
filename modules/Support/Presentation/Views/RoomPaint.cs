using NOAvionics;
using System.IO;
using BoscaliSummer.Modules.Support.Presentation.Window;
using UnityEngine;
namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>
    /// Resolves a room stylesheet class from the Support module's own embedded <c>rooms.avss</c>.
    /// Callers never invent a colour. The sheet is parsed once; a missing or unreadable resource
    /// leaves every class at its caller's fallback.
    /// </summary>
    internal static class RoomPaint
    {
        public const string ResourceName = "BoscaliSummer.Support.rooms.avss";
        private static AvStyleSheet sheet;
        private static int sheetGeneration = -1;
        // Re-read whenever the kit sheet reloads (SetTheme), so an edited NOAvionics/rooms.avss shows without a restart.
        private static AvStyleSheet Sheet
        {
            get
            {
                if (sheet == null || sheetGeneration != AvStyleHost.FuiGeneration)
                {
                    sheet = Load();
                    sheetGeneration = AvStyleHost.FuiGeneration;
                }
                return sheet;
            }
        }
        public static Color Ink(string className, Color fallback) =>
            AvStyleHost.Resolve(Sheet.Resolve(className).Color, fallback);
        public static Color Fill(string className, Color fallback) =>
            AvStyleHost.Resolve(Sheet.Resolve(className).Background, fallback);
        public static Color Edge(string className, Color fallback) =>
            AvStyleHost.Resolve(Sheet.Resolve(className).Border, fallback);
        public static Color Ready => Ink("room-ops-ready", AvTheme.RailReady);
        public static Color Instrument => Ink("room-ops-instrument", AvTheme.RailInfo);
        public static Color Command => Ink("room-ops-command", AvTheme.RailCaution);
        /// <summary>Portal corner brackets (four L-shaped hairlines) on a top-left/Y-down rect.</summary>
        public static void Brackets(RectTransform parent, Rect a, float arm, Color c, float t = 1f)
        {
            float r = a.x + a.width, b = a.y - a.height;
            Chrome.Rule(parent, new Rect(a.x, a.y, arm, t), c);
            Chrome.Rule(parent, new Rect(a.x, a.y, t, arm), c);
            Chrome.Rule(parent, new Rect(r - arm, a.y, arm, t), c);
            Chrome.Rule(parent, new Rect(r - t, a.y, t, arm), c);
            Chrome.Rule(parent, new Rect(a.x, b + t, arm, t), c);
            Chrome.Rule(parent, new Rect(a.x, b + arm, t, arm), c);
            Chrome.Rule(parent, new Rect(r - arm, b + t, arm, t), c);
            Chrome.Rule(parent, new Rect(r - t, b + arm, t, arm), c);
        }
        /// <summary>A full hazard-stripe band (Portal progress-bar vocabulary) used as a divider.</summary>
        public static void HazardBand(RectTransform parent, Rect a, Color c)
        {
            AvHazardGraphic g = Chrome.Graphic<AvHazardGraphic>(parent, a, "HazardBand");
            g.FillColor = c;
            g.FrameColor = c.WithAlpha(0.35f);
            g.Value = 1f;
        }
        /// <summary>Recessed instrument plate with a dark lower edge and sparse drafting ticks.</summary>
        public static void Inset(RectTransform parent, Rect at, Color fill, Color edge)
        {
            Chrome.Panel(parent, new Rect(at.x + 3f, at.y - 4f, at.width, at.height), AvTheme.Ground.WithAlpha(0.9f));
            AvFrame plate = AvFrame.Add(parent, "RecessedInstrument", AvChamfer.All(6f));
            Chrome.Place(plate.rectTransform, at);
            plate.Paint(fill, edge.WithAlpha(.45f));
            OpsArtwork.DrawSurface(parent, new Rect(at.x + 8, at.y - 8, at.width - 16, at.height - 16), .15f);
            Chrome.Rule(parent, new Rect(at.x + 1f, at.y - 1f, at.width - 2f, 2f), edge.WithAlpha(0.13f));
            // Recessed metal plate: small fasteners, no luminous drafting grid.
            for (int i = 0; i < 4; i++)
            {
                float x = at.x + (i % 2 == 0 ? 5f : at.width - 8f);
                float y = at.y - (i < 2 ? 5f : at.height - 8f);
                Chrome.Panel(parent, new Rect(x, y, 3f, 3f), edge.WithAlpha(0.5f));
            }
        }
        private static AvStyleSheet Load()
        {
            try
            {
                string overrideText = AvStyleHost.ReadOverride("NOAvionics/rooms.avss");
                if (!string.IsNullOrEmpty(overrideText))
                {
                    AvStyleSheet edited = AvStyleSheet.Parse(overrideText);
                    if (edited.RuleCount > 0 && edited.Errors.Count == 0) return edited;
                }
                using (Stream stream = typeof(RoomPaint).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                        using (var reader = new StreamReader(stream)) return AvStyleSheet.Parse(reader.ReadToEnd());
                }
            }
            catch (System.Exception) { }
            return AvStyleSheet.Parse("");
        }
    }
}

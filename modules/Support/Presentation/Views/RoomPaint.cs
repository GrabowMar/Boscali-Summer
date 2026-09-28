using System.IO;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Views
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

        private static AvStyleSheet Sheet => sheet ?? (sheet = Load());

        public static Color Ink(string className, Color fallback) =>
            AvStyleHost.Resolve(Sheet.Resolve(className).Color, fallback);

        public static Color Fill(string className, Color fallback) =>
            AvStyleHost.Resolve(Sheet.Resolve(className).Background, fallback);

        public static Color Edge(string className, Color fallback) =>
            AvStyleHost.Resolve(Sheet.Resolve(className).Border, fallback);

        private static AvStyleSheet Load()
        {
            try
            {
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

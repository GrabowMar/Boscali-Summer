using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>Resolves a room stylesheet class. Callers never invent a colour.</summary>
    internal static class RoomPaint
    {
        public static Color Ink(string className, Color fallback) =>
            AvStyleHost.Resolve(AvStyleHost.Style(className).Color, fallback);

        public static Color Fill(string className, Color fallback) =>
            AvStyleHost.Resolve(AvStyleHost.Style(className).Background, fallback);
    }
}

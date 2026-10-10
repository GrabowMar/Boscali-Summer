using System.Collections.Generic;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>Logs once per surface when a vector buffer filled and dropped strokes (<c>AvQuadBuffer</c> drops silently).</summary>
    internal static class BufferGuard
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        public static void Check(AvQuadBuffer buffer, string surface)
        {
            if (buffer == null || !buffer.Overflowed || !Warned.Add(surface)) return;
            Plugin.Logger?.LogWarning("[Hud] " + surface + " vector buffer full (" + buffer.Capacity + " quads); some strokes were dropped.");
        }
    }
}

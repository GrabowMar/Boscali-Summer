using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// How the storm population is arranged. This is the difference between a sky with a few
    /// clouds in it and a sky that is actively doing something, and it is what the radar paints.
    /// </summary>
    internal enum StormMode
    {
        None = 0,

        /// <summary>A single cell with clear air around it. The classic afternoon thunderstorm.</summary>
        Isolated = 1,

        /// <summary>Scattered cells at different stages of life. The common case.</summary>
        Scattered = 2,

        /// <summary>Cells clustered into one mass. Widespread rain, hard to route around.</summary>
        Cluster = 3,

        /// <summary>A line along the front. The dangerous one: no gap, arrives all at once.</summary>
        SquallLine = 4
    }

    internal static class StormModes
    {
        private static readonly string[] Labels = { "NONE", "ISOLATED", "SCATTERED", "CLUSTER", "SQUALL LINE" };

        public static string Label(StormMode mode)
        {
            int index = (int)mode;
            return Labels[index < 0 ? 0 : index >= Labels.Length ? 0 : index];
        }

        public static bool IsOrganised(StormMode mode) =>
            mode == StormMode.Cluster || mode == StormMode.SquallLine;
    }
}

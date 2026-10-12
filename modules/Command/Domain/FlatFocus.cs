namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Pure maths of centring the flat sheet on a world point.</summary>
    internal static class FlatFocus
    {
        /// <summary>The game's area zoom a locate raises to, and the zoom range the game allows.</summary>
        public const float AreaZoom = 3f, MinZoom = 1f, MaxZoom = 40f;

        /// <summary>
        /// The value to write into the sheet's positionOffset so the world point sits at the viewport centre. The game draws the sheet at
        /// -(stationary + offset) in map units (world * displayFactor), so this is world * displayFactor - stationary, the same formula as
        /// Wing's MapFit.Offset. A non-finite input gives a non-finite result: the caller checks the point first.
        /// </summary>
        public static void Offset(float worldX, float worldZ, float displayFactor, float stationaryX, float stationaryZ, out float x, out float z)
        {
            x = worldX * displayFactor - stationaryX;
            z = worldZ * displayFactor - stationaryZ;
        }

        /// <summary>Area zoom when zoomIn: max(current, 3), clamped to the game's 1..40 (an unknown zoom counts as 3); otherwise the current zoom.</summary>
        public static float Zoom(float current, bool zoomIn)
        {
            if (!zoomIn) return current;
            float z = float.IsNaN(current) ? AreaZoom : System.Math.Max(current, AreaZoom);
            return z < MinZoom ? MinZoom : z > MaxZoom ? MaxZoom : z;
        }
    }
}

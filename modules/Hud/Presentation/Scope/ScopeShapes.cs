namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>The prototype's unit outlines (x, y pairs, nose / tip at y = -1, y down), shared by the pages.</summary>
    internal static class ScopeShapes
    {
        public const int JetPoints = 28;

        /// <summary>The half jet (nose to tail down the right side, <c>HUD.shapes.jetHalf</c>): 15 points.</summary>
        public static readonly float[] JetHalf =
        {
            0f, -1f, 0.045f, -0.9f, 0.085f, -0.66f, 0.11f, -0.42f, 0.14f, -0.22f, 0.76f, 0.2f, 0.78f, 0.33f, 0.7f, 0.4f,
            0.24f, 0.36f, 0.25f, 0.6f, 0.5f, 0.82f, 0.52f, 0.93f, 0.24f, 0.91f, 0.14f, 1f, 0f, 0.96f,
        };

        /// <summary>The whole jet outline (<c>HUD.shapes.jet</c>), 28 points, nose up, half-span about 0.78.</summary>
        public static readonly float[] Jet = BuildJet();

        /// <summary>Arrowhead marker with its tip at (0, -1): 4 points.</summary>
        public static readonly float[] Chevron = { 0f, -1f, 0.62f, 0.7f, 0f, 0.3f, -0.62f, 0.7f };

        public static readonly float[] Diamond = { 0f, -1f, 1f, 0f, 0f, 1f, -1f, 0f };

        public static readonly float[] Triangle = { 0f, -1f, 0.8f, 0.6f, -0.8f, 0.6f };

        private static float[] BuildJet()
        {
            float[] full = new float[JetPoints * 2];
            for (int i = 0; i < 30; i++) full[i] = JetHalf[i];
            int at = 15;
            for (int i = 13; i >= 1; i--)      // the right side's inner points, mirrored, tail to nose
            {
                full[2 * at] = -JetHalf[2 * i];
                full[2 * at + 1] = JetHalf[2 * i + 1];
                at++;
            }
            return full;
        }
    }
}

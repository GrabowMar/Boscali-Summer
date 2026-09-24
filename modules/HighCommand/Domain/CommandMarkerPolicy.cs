namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// What goes on a post's map marker, kept pure so the map obeys the same fog as the
    /// roster: a marker is drawn for a post the faction owns or has eyes on, and never for a
    /// dead one - a killed commander's building is rubble until it is re-established, and a
    /// marker says "there is someone to find here".
    /// </summary>
    internal static class CommandMarkerPolicy
    {
        public static bool Show(bool friendly, bool known, bool isKia) => !isKia && (friendly || known);

        /// <summary>
        /// A post's position inside the map's icon layer. Vanilla places every icon at global
        /// metres times the map's own display factor; skipping that factor, or feeding a local
        /// position instead of a global one, put every post in the wrong place. A non-finite
        /// or non-positive factor has no map position and is refused.
        /// </summary>
        public static bool MapPoint(float globalX, float globalZ, float displayFactor, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (!(displayFactor > 0f) || float.IsInfinity(displayFactor)) return false;
            x = globalX * displayFactor;
            z = globalZ * displayFactor;
            return !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(z) && !float.IsInfinity(z);
        }

        /// <summary>Theater posts read bigger than base posts, so the map shows the hierarchy.</summary>
        public static float Size(int tier) => tier <= 0 ? 15f : tier == 1 ? 12f : 10f;

        /// <summary>A post under fire pulses: a triangle wave over the fraction of a second.</summary>
        public static float Pulse(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0f;
            float phase = seconds - (float)System.Math.Floor(seconds);
            return phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
        }

        /// <summary>How much bigger than the post's own diamond the selection bracket sits.</summary>
        public const float ReticleScale = 1.9f;

        /// <summary>The side of the selection bracket drawn around a marker of <paramref name="size"/>.</summary>
        public static float ReticleBracket(float size) => size * ReticleScale;

        /// <summary>
        /// One rule of a selection bracket, in the marker's own top-left space with y running
        /// down, the way a Unity rect is placed inside a parent.
        /// </summary>
        public struct ReticleRule
        {
            public float X;
            public float Y;
            public float Width;
            public float Height;

            public ReticleRule(float x, float y, float width, float height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        private static readonly ReticleRule[] NoReticle = new ReticleRule[0];

        /// <summary>
        /// The eight rules of the bracket a selected post wears: four corners, each one short
        /// horizontal and short vertical rule, opening towards the middle so the diamond stays
        /// readable inside it. Kept pure so the geometry can be checked without a scene; the
        /// marker layer only places the rules and shows the group. A size a marker would never
        /// carry draws nothing rather than a degenerate box.
        /// </summary>
        public static ReticleRule[] Reticle(float size)
        {
            if (float.IsNaN(size) || float.IsInfinity(size) || size < 2f || size > 256f) return NoReticle;

            float bracket = ReticleBracket(size);
            float arm = (float)System.Math.Max(2.0, bracket * 0.34);
            float inner = bracket - arm;
            float bottom = -bracket;
            return new[]
            {
                new ReticleRule(0f, 0f, arm, 1f),
                new ReticleRule(0f, 0f, 1f, arm),
                new ReticleRule(inner, 0f, arm, 1f),
                new ReticleRule(bracket - 1f, 0f, 1f, arm),
                new ReticleRule(0f, bottom + 1f, arm, 1f),
                new ReticleRule(0f, bottom + arm, 1f, arm),
                new ReticleRule(inner, bottom + 1f, arm, 1f),
                new ReticleRule(bracket - 1f, bottom + arm, 1f, arm),
            };
        }
    }
}

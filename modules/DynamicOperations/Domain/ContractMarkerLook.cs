namespace BoscaliSummer.Modules.DynamicOperations.Domain
{

    /// <summary>
    /// The vanilla numbers a contract marker copies. Everything here is a constant the game
    /// itself uses, kept in one pure place so the map, the cockpit and the tests agree.
    /// </summary>
    internal static class ContractMarkerLook
    {
        /// <summary>Vanilla's label anti-overlap: start pushing under this separation, in px.</summary>
        public const float LabelSeparation = 50f;

        /// <summary>
        /// Vanilla's label glide and nudge, per frame: 20 % toward the anchor, push apart at
        /// these speeds, decay after. These are the shipped prefab's values rather than the
        /// manager's C# defaults (30 px / (2000, 500) / 0.8 / 0.5), which the prefab overrides.
        /// </summary>
        public const float LabelLerp = 0.2f;
        public const float NudgeSpeedX = 1000f;
        public const float NudgeSpeedY = 300f;
        public const float NudgeDecay = 0.8f;

        /// <summary>
        /// One frame of a marker label's offset: the accumulated push decays, and when another
        /// label drifted within <see cref="LabelSeparation"/>, this one is pushed away along the
        /// delta. Vanilla straightens a nearly-horizontal push the same way.
        /// </summary>
        public static void Step(ref float offsetX, ref float offsetY, float dx, float dy, float deltaTime)
        {
            offsetX *= NudgeDecay;
            offsetY *= NudgeDecay;
            if (!(dx * dx + dy * dy < LabelSeparation * LabelSeparation)) return;
            if (dy < 0.1f) dy += 5f;
            float length = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001f) return;
            float scale = deltaTime / length;
            offsetX -= dx * scale * NudgeSpeedX;
            offsetY -= dy * scale * NudgeSpeedY;
        }

        /// <summary>Vanilla's label glide: a fraction of the remaining distance each frame.</summary>
        public static float Glide(float current, float target) => current + (target - current) * LabelLerp;
    }
}

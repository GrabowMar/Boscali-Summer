namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// What the mod's night-vision look tells the rest of the mod (the NVG lamp, the C-menu gain row). Written by
    /// QoL's NightVisionLook; all false/zero means vanilla night vision is in charge.
    /// </summary>
    internal static class NightVisionLink
    {
        private static readonly string[] Legends = { "NVG A", "NVG 1", "NVG 2", "NVG 3" };

        /// <summary>The mod meters the picture and sets the tube gain itself, so the gain step means something.</summary>
        public static bool OwnsGain;
        /// <summary>The tube has cut its gain for a bright source and has not recovered yet.</summary>
        public static bool Gating;
        /// <summary>0 AUTO, 1 LOW, 2 MED, 3 HIGH.</summary>
        public static int GainStep;

        public static string Legend => !OwnsGain ? "NVG" : Gating ? "NVG GATE" : Legends[GainStep & 3];

        public static void CycleGain() => GainStep = (GainStep + 1) & 3;
    }
}

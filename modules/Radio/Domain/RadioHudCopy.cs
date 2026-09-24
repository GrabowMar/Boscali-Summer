namespace BoscaliSummer.Features.Radio.Domain
{
    /// <summary>
    /// The radio widget's copy, pure so the test project links it directly. It names what the
    /// deck or receiver is already playing; it never starts, stops or tunes anything.
    /// </summary>
    internal static class RadioHudCopy
    {
        /// <summary>"RADIO · AGRAPOL FM"; an unknown channel still reads as radio.</summary>
        public static string Text(string channel) =>
            string.IsNullOrEmpty(channel) ? "RADIO" : "RADIO · " + channel;

        /// <summary>The track, or the honest deck state when there is no title to show.</summary>
        public static string Detail(string title, bool paused)
        {
            if (!string.IsNullOrEmpty(title)) return paused ? "PAUSED · " + title : title;
            return paused ? "PAUSED" : "DEAD AIR";
        }

        /// <summary>Playback progress, clamped; 0 when the source has no clock.</summary>
        public static float Bar(float progress)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress)) return 0f;
            return progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }
    }
}

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Which class the sky gallery shows, and the sky the previous / next keys set. Pure state, no clicks. The gallery follows the sky overhead until the host browses,
    /// and jumps to a newly set sky's class once.
    /// </summary>
    internal sealed class MetSkyPicker
    {
        private bool touched;
        private int lastPinned = -2;

        public WeatherRegimeType Class { get; private set; }

        public void Sync(MetOfficeModel m)
        {
            if (m.PinnedSky != lastPinned)
            {
                lastPinned = m.PinnedSky;
                if (m.PinnedSky >= 0 && m.PinnedSky < SkyLibrary.Count) { Class = SkyLibrary.ClassOf(m.PinnedSky); touched = true; return; }
            }
            if (!touched) Class = m.Class;
        }

        public void Browse(WeatherRegimeType cls) { touched = true; Class = cls; }

        /// <summary>The sky <paramref name="direction"/> (+1 or -1) from <paramref name="from"/> inside the class on show, wrapping; the first or last of the class when <paramref name="from"/> is not in it.</summary>
        public int Step(int from, int direction)
        {
            int first = -1, last = -1, prev = -1, next = -1;
            bool seen = false;
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                if (SkyLibrary.ClassOf(i) != Class) continue;
                if (first < 0) first = i;
                last = i;
                if (i == from) seen = true;
                else if (!seen) prev = i;
                else if (next < 0) next = i;
            }
            if (first < 0) return 0;
            if (!seen) return direction > 0 ? first : last;
            return direction > 0 ? (next >= 0 ? next : first) : (prev >= 0 ? prev : last);
        }
    }
}

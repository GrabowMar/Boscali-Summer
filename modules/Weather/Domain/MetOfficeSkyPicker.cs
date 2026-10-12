namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The pin row's selection: one of the 47 library skies, walked class by class in number order (spec 4.4, brief C1). Pure state, no clicks:
    /// the window asks it what a press selects and then sends one PinSky request.
    /// </summary>
    internal sealed class MetSkyPicker
    {
        private bool touched;

        /// <summary>0-46.</summary>
        public int Selected { get; private set; }

        public WeatherRegimeType Class => SkyLibrary.ClassOf(Selected);

        public static int CountOf(WeatherRegimeType cls)
        {
            int n = 0;
            for (int i = 0; i < SkyLibrary.Count; i++) if (SkyLibrary.ClassOf(i) == cls) n++;
            return n;
        }

        public static int FirstOf(WeatherRegimeType cls)
        {
            for (int i = 0; i < SkyLibrary.Count; i++) if (SkyLibrary.ClassOf(i) == cls) return i;
            return 0;
        }

        /// <summary>1-based place of the selection inside its class.</summary>
        public int IndexInClass
        {
            get
            {
                int n = 0;
                for (int i = 0; i <= Selected; i++) if (SkyLibrary.ClassOf(i) == Class) n++;
                return n;
            }
        }

        public int CountInClass => CountOf(Class);

        /// <summary>The model's world wins while a sky is pinned; otherwise the selection starts on the first sky of the class showing and stays wherever the host last left it.</summary>
        public void Sync(MetOfficeModel m)
        {
            if (m.PinnedSky >= 0 && m.PinnedSky < SkyLibrary.Count) { Selected = m.PinnedSky; touched = true; }
            else if (!touched) Selected = FirstOf(m.Class);
        }

        public int SelectClass(WeatherRegimeType cls) { touched = true; Selected = FirstOf(cls); return Selected; }

        /// <summary>Walks the class by <paramref name="direction"/> (+1 or -1), wrapping at its ends.</summary>
        public int Step(int direction)
        {
            touched = true;
            WeatherRegimeType cls = Class;
            int n = CountOf(cls), at = IndexInClass - 1, first = FirstOf(cls);
            at = ((at + (direction < 0 ? -1 : 1)) % n + n) % n;
            Selected = first + at;
            return Selected;
        }
    }
}

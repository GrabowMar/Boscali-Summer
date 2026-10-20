using BoscaliSummer.Modules.Spooktober.Configuration;
using BoscaliSummer.Modules.Spooktober.Domain;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>This player's progress, read once from the hidden config entry and written back on <see cref="Save"/>.</summary>
    internal static class SpookStore
    {
        private static SpooktoberSettings settings;

        public static SpookProgress Current { get; private set; } = new SpookProgress();

        public static void Init(SpooktoberSettings s)
        {
            settings = s;
            Current = SpookProgress.Load(s.Progress.Value);
        }

        /// <summary>Persist <see cref="Current"/>.</summary>
        public static void Save()
        {
            if (settings != null) settings.Progress.Value = Current.Save();
        }
    }
}

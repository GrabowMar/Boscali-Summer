using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Mission mode selecting full behaviour or reduced optional work and geometry
    /// cadence.</summary>
    internal enum WingMode
    {
        Smart,
        Performance,
    }

    /// <summary>Snapshots AI/Mode at mission start for shared feature gates and timing. Member brains own
    /// decisions; fidelity never edits intent or aircraft controls.</summary>
    internal static class WingFidelity
    {
        private static bool performance;

        public static WingMode Mode { get; private set; } = WingMode.Smart;

        /// <summary>Freeze the selected mode for the next mission.</summary>
        public static void Begin(WingMode mode)
        {
            Mode = mode;
            performance = mode == WingMode.Performance;
            WingFrameGate.Reset();
        }

        /// <summary>Whether the complete Smart behaviour set is enabled.</summary>
        public static bool Full => !performance;

        /// <summary>Physics-tick interval between full formation geometry updates.</summary>
        public static int GeometryStride => performance ? 3 : 1;

        /// <summary>Multiplier for mode-scaled periodic and UI intervals.</summary>
        public static float IntervalScale => performance ? 2.5f : 1f;

        /// <summary>Scale a base interval by mode. Missile evasion, takeover interaction, and radio
        /// anti-spam use independent fixed timers.</summary>
        public static float Interval(float seconds) => seconds * IntervalScale;

        /// <summary>Expose targeted pod jamming.</summary>
        public static bool Jamming => Full;

        public static string Summary() =>
            $"mode={Mode} stride={GeometryStride} intervalScale={IntervalScale:0.0}";
    }
}

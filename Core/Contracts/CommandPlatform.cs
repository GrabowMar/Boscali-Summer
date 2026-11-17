using System;
using NuclearOption.Networking;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// "Is this aircraft a command-and-control platform right now?", answered by whichever module owns such an airframe (the Skylark, while its
    /// C2 mast is out) so Support and Wing can reward it without referencing it. No provider means no platform.
    /// </summary>
    internal static class CommandPlatform
    {
        /// <summary>Strike reach multiplier and the factors on call price and cooldown while the platform is active.</summary>
        internal const float RangeFactor = 1.5f, CostFactor = .85f, CooldownFactor = .8f;

        /// <summary>Set by the owning module at install.</summary>
        internal static Func<Aircraft, bool> Provider;

        internal static bool Is(Aircraft aircraft) => aircraft != null && Provider != null && Provider(aircraft);

        internal static bool Is(Player player) => player != null && Is(player.Aircraft);
    }
}

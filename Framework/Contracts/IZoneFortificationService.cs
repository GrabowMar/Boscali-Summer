using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface IZoneFortificationService
    {
        /// <summary>False while the owner's garrisons are switched off: nothing can be occupied.</summary>
        bool Available { get; }

        /// <summary>
        /// Reinforces occupied civilian shells in a zone the requester's faction controls
        /// with hidden vanilla defense proxies. At most <paramref name="shells"/> additional
        /// buildings are occupied, bounded by the zone and theater ceilings; true when at
        /// least one was placed.
        /// </summary>
        bool TryFortify(Airbase airbase, FactionHQ owner, Player requester, int shells);

        /// <summary>
        /// Occupies up to <paramref name="shells"/> unowned civilian shells nearest a global point,
        /// within <paramref name="radius"/>, for <paramref name="owner"/> — outside any zone's own
        /// garrison, so it works in hostile or neutral ground. Bounded by the theater ceiling.
        /// Host only; returns how many buildings were occupied (0 when none could be).
        /// </summary>
        int TrySeize(float x, float z, float radius, FactionHQ owner, int shells);
    }
}

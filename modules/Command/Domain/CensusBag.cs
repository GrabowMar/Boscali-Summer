using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Fills the theater bag the news ticker reads from the host's census, and from nothing else.
    /// An unknown count stays -1, an unknown ratio is NaN and an unknown threat level is 0, so a census that has not arrived reads as not measured, never as a calm theater.
    /// Lives beside <see cref="TacticalTheaterState"/> rather than in it because that file is copied alone into a Unity project that has no Core contracts.
    /// </summary>
    internal static class CensusBag
    {
        internal static void Apply(TacticalTheaterState s, in TheaterCensus c)
        {
            s.ContestedAirbaseCount = c.BasesContested;
            s.ActiveClashesCount = c.SectorsContested;
            s.TerritoryControlRatio = TheaterReadout.AlliedShare(c.SectorsFriendly, c.SectorsHostile);

            int air = c.AirFriendly + c.AirHostileTracked;
            s.AirSuperiorityRatio = c.AirFriendly >= 0 && c.AirHostileTracked >= 0 && air > 0 ? c.AirFriendly / (float)air : float.NaN;

            s.DefconLevel = c.Threat;
            s.ActiveThreatWarning = c.Threat == 0 ? "" : TheaterWords.Threat(c.Threat, c.ThreatWhy);
        }
    }
}

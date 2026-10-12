using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Fills the theater bag the news ticker (and, until the Station Board replaces it, the old panel) reads from the host's census, and from nothing else.
    /// An unknown count stays -1, an unknown ratio is NaN and an unknown threat level is 0, so a census that has not arrived reads as not measured, never as a calm theater.
    /// Lives beside <see cref="TacticalTheaterState"/> rather than in it because that file is copied alone into a Unity project that has no Core contracts.
    /// </summary>
    internal static class CensusBag
    {
        internal static void Apply(TacticalTheaterState s, in TheaterCensus c)
        {
            s.FriendlyAircraftCount = c.AirFriendly;
            s.HostileAircraftCount = c.AirHostileTracked;
            s.FriendlyAirbaseCount = c.BasesFriendly;
            s.HostileAirbaseCount = c.BasesHostile;
            s.NeutralAirbaseCount = c.BasesNeutral;
            s.ContestedAirbaseCount = c.BasesContested;
            s.FriendlyRadarCount = c.Radars;
            s.FriendlyGroundUnitsCount = c.GroundFriendly;
            s.HostileGroundUnitsCount = c.GroundHostileTracked;
            s.FriendlySectorCount = c.SectorsFriendly;
            s.HostileSectorCount = c.SectorsHostile;
            s.ContestedSectorCount = c.SectorsContested;
            s.NeutralSectorCount = c.SectorsNeutral;
            s.ActiveClashesCount = c.SectorsContested;
            s.FrontlineSegmentCount = -1;   // the census carries a length, not a segment count; the old panel that read it goes with the Station Board
            s.FrontlineLengthMetres = c.FrontMetres;
            s.TerritoryControlRatio = TheaterReadout.AlliedShare(c.SectorsFriendly, c.SectorsHostile);

            int air = c.AirFriendly + c.AirHostileTracked;
            s.AirSuperiorityRatio = c.AirFriendly >= 0 && c.AirHostileTracked >= 0 && air > 0 ? c.AirFriendly / (float)air : float.NaN;

            SortieTally tally = default;
            if (c.SortieCap >= 0 && c.SortieSead >= 0 && c.SortieCas >= 0 && c.SortieStrike >= 0 && c.SortieTransit >= 0)
            {
                tally.Cap = c.SortieCap; tally.Sead = c.SortieSead; tally.Cas = c.SortieCas; tally.Strike = c.SortieStrike; tally.Transit = c.SortieTransit;
                tally.Observed = tally.Cap + tally.Sead + tally.Cas + tally.Strike + tally.Transit;
            }
            s.Sorties = tally;

            s.DefconLevel = c.Threat;
            s.PrimaryThreatDescription = TheaterWords.ThreatTitle(c.ThreatWhy);
            s.ActiveThreatWarning = c.Threat == 0 ? "" : TheaterWords.Threat(c.Threat, c.ThreatWhy);
        }
    }
}

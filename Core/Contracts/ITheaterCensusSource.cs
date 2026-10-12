namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Host only: measures one faction's census. False on a client or without a seat.</summary>
    internal interface ITheaterCensusSource { bool TryMeasure(FactionHQ hq, out TheaterCensus census); }
}

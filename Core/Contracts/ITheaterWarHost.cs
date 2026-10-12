namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Host only. (hq.GetInstanceID(), morale delta) raised once when an operation pays its morale.</summary>
    internal interface ITheaterWarHost { event System.Action<int, float> MoraleAwarded; }
}

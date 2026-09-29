namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Team brevity calls (WINCHESTER, BINGO FUEL, SPLASH ONE) a pilot can send without
    /// opening the COM screen. Comms owns the catalogue, the channel, position marking,
    /// validation and delivery; the caller only lists labels and picks one.
    /// </summary>
    internal interface IQuickCalls
    {
        /// <summary>False while a call cannot be sent (no session, channel blocked).</summary>
        bool CanCall { get; }
        int CallCount { get; }
        string CallLabel(int index);
        void Call(int index);
    }
}

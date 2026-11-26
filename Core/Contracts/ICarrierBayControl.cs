namespace BoscaliSummer.Core.Contracts
{
    internal enum CarrierBayAction { Launch, Recall, WaveOff }

    internal struct CarrierBayStatus
    {
        internal string Slots;
        internal string LaunchBlocked;
        internal string RecallBlocked;
        internal string WaveOffBlocked;
    }

    /// <summary>Flight deck commands and status for the local player's fighter bay.</summary>
    internal interface ICarrierBayControl
    {
        bool TryRead(out CarrierBayStatus status);
        void Request(CarrierBayAction action);
    }
}

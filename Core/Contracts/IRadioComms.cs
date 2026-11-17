namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The military receiver, independent of station entertainment. Frequencies can later route this same channel.</summary>
    internal interface IRadioComms
    {
        bool ReceiveMilitary { get; }
        float MilitaryVolume { get; }
        void DuckMilitary(float seconds);
    }
}

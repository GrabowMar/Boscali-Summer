namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Narrow seam for the camera surface mark: its state and the three actions a target
    /// screen may offer. Support owns the delivery into an armed support action; QoL owns
    /// the camera/HUD behaviour behind <see cref="IObservationSource"/>.
    /// </summary>
    internal interface ICameraTargetService
    {
        bool Available { get; }
        bool HasMark { get; }
        string Status { get; }
        bool CanCapture { get; }
        bool CanCallAtMark { get; }
        string ArmedActionName { get; }
        ObservationPoint Mark { get; }
        float AgeSeconds { get; }
        bool Capture();
        bool CallAtMark();
        void Clear();
    }
}

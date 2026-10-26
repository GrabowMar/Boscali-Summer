namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Starts a fire at a point without the impact ignition roll (host only). Published by Fire and Destruction.</summary>
    internal interface IFireStarter
    {
        /// <summary>False on a client, with fires off, over water or at the active-fire limit.</summary>
        bool Ignite(GlobalPosition at);
    }
}

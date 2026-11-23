namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Read-only host view of a SKYWELL tanker's tender, published by the Vanguard module through <c>ModuleContext.AddService</c>
    /// and read late through <c>ModuleServices</c>. Other modules (the megaplane sim hooks) read it without naming Vanguard.
    /// </summary>
    internal interface ISkywellTenderView
    {
        /// <summary>The tender state of this tanker's kit (Stowed, Deploying, Tow, ...); "none" when the host tracks no kit on it.</summary>
        string TenderState(Aircraft tanker);
    }
}

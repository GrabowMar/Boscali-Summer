using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Read-only view of a SKYWELL tanker's tender, published by the Vanguard module through <c>ModuleContext.AddService</c>
    /// and read late through <c>ModuleServices</c>. Other modules (the megaplane sim hooks) read it without naming Vanguard.
    /// </summary>
    internal interface ISkywellTenderView
    {
        /// <summary>The tender state of this tanker's kit, or "none" before there is a state for it.</summary>
        string TenderState(Aircraft tanker);

        /// <summary>The tender's local world position once this peer has built its visual rig.</summary>
        bool TryGetPosition(Aircraft tanker, out Vector3 position);
    }
}

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Vanguard designation for the local aircraft (radial VANGUARD: DESIGNATE / RELEASE): every launched Vanguard
    /// loadout that follows the pilot's target reads it (REMORA volley/ram, HAWC-X nudge, GLAIVE hover/orbit).
    /// Vanguard owns the host message and validation; the radial menu only reads state and picks an order.
    /// </summary>
    internal interface IDroneCommand
    {
        /// <summary>True after DESIGNATE until RELEASE or the designated target dies (local view).</summary>
        bool Striking { get; }

        /// <summary>The local aircraft has a target selected to designate.</summary>
        bool CanStrike { get; }

        void Strike();
        void Screen();
    }
}

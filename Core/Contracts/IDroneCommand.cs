namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// REMORA escort-drone orders for the local aircraft. Vanguard owns the drones, the host
    /// message and validation; the radial menu only reads state and picks an order.
    /// </summary>
    internal interface IDroneCommand
    {
        /// <summary>True after STRIKE until SCREEN (local view of the last order sent).</summary>
        bool Striking { get; }

        /// <summary>The local aircraft has a target selected to send the drones at.</summary>
        bool CanStrike { get; }

        void Strike();
        void Screen();
    }
}

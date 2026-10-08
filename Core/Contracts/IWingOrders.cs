namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The player's own wing, as the C-key interaction menu sees it (2026-10-07): how many wingmen there are, the
    /// shape they fly, and the handful of whole-wing orders worth a single click. Implemented by the Wing module over
    /// the same order path its call ladder uses; a missing owner (Wing off) hides the menu's WING category.
    /// </summary>
    internal interface IWingOrders
    {
        /// <summary>Wingmen currently flying with the player; 0 = no wing.</summary>
        int Members { get; }
        /// <summary>Current formation name, upper case, or empty.</summary>
        string Shape { get; }
        void FormUp();
        void Engage();
        void BreakOff();
        void ClearMySix();
        void BogeyDope();
        void EscortMe();
        void NextShape();
        void ReturnToBase();
    }
}

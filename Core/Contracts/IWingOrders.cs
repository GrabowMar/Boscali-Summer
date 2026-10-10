namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The player's own wing, as the C-key interaction menu sees it (2026-10-07): how many wingmen there are, the
    /// shape they fly, the formations that suit the wing, and the handful of whole-wing orders worth a single click. Implemented by the Wing module over
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
        void ReturnToBase();

        /// <summary>The formations that suit the wing now, in catalog order (families run together).</summary>
        int ShapeCount { get; }
        /// <summary>Formation name, upper case, e.g. <c>FINGER FOUR (STRONG RIGHT)</c>.</summary>
        string ShapeLabel(int index);
        /// <summary>Family name, upper case: CLASSIC, ESCORT, ROTARY, TACTICAL.</summary>
        string ShapeFamily(int index);
        bool ShapeActive(int index);
        /// <summary>The same order as picking the formation in the wing console, for the whole wing.</summary>
        void SelectShape(int index);
    }
}

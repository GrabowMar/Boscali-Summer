namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The local pilot's usable OPS perks for the interaction menu. Support owns the catalogue, the readiness gates, the price and the
    /// arm-then-fire flow; a press here is the same press as the OPS page ARM button (first press arms, the second fires at the aim).
    /// The list holds only perks that are ready or already armed, ordered by front and rung, at most <see cref="MaxPerks"/>.
    /// </summary>
    internal interface IOpsPerks
    {
        const int MaxPerks = 6;

        int PerkCount { get; }
        string PerkLabel(int index);
        /// <summary>E.g. <c>SPACE R2 · 6 ALLOC</c>, or <c>ARMED · PRESS AGAIN</c>.</summary>
        string PerkNote(int index);
        bool PerkArmed(int index);
        void PerkPress(int index);
    }
}

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The local pilot's usable OPS perks for the interaction menu. Support owns the catalogue, the readiness gates, the price and the
    /// arm-then-fire flow; a press here is the same press as the OPS page ARM button (first press arms, the second fires at the aim).
    /// The list holds the perks that are ready, already armed or cooling down, ordered by front and rung, at most <see cref="MaxPerks"/> (cooling ones go first when it overflows).
    /// </summary>
    internal interface IOpsPerks
    {
        const int MaxPerks = 8;

        int PerkCount { get; }
        string PerkLabel(int index);
        /// <summary>E.g. <c>SPACE R2 · 6 ALLOC</c>, or <c>ARMED · PRESS AGAIN</c>.</summary>
        string PerkNote(int index);
        bool PerkArmed(int index);
        /// <summary><c>SPACE</c>, <c>CYBER</c> or <c>SOF</c>: the front the perk belongs to.</summary>
        string PerkFront(int index);
        /// <summary>Seconds left on the perk's cooldown, 0 when it is ready or armed.</summary>
        int PerkCooldown(int index);
        void PerkPress(int index);
    }
}

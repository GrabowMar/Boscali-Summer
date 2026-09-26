namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>
    /// Who is holding the vanilla soundtrack silent. The receiver holds it from the moment
    /// it first goes on air — including while the dial sits between stations, because dead
    /// air is still the player listening to the set — and gives it back only when they press
    /// STOP. The deck holds it while it is engaged.
    ///
    /// <para>Pure state on purpose: this is the rule that the game's music must not creep
    /// back in while a program is live, and it is worth an assertion rather than a comment.</para>
    /// </summary>
    internal sealed class VanillaMusicHold
    {
        private bool receiver;
        private bool deck;

        public bool Held => receiver || deck;

        /// <summary>Called when the receiver actually starts a programme.</summary>
        public void EngageReceiver() => receiver = true;

        /// <summary>The player signed off: the receiver's hold ends, the deck's is its own.</summary>
        public void ReleaseReceiver() => receiver = false;

        public void SetDeckEngaged(bool engaged) => deck = engaged;

        public void Reset()
        {
            receiver = false;
            deck = false;
        }
    }
}

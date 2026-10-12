namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>
    /// The faction bank of finished tied sorties. Three buy one wave; at most six are kept (ruling D4). The bank is the faction's, not the
    /// operation's: a redirect or the end of an operation never touches it, and a wave that cannot be bought yet leaves it as it is.
    /// </summary>
    internal sealed class WaveLedger
    {
        internal const int CreditsPerWave = 3, MaximumCredits = 6;

        internal int Credits { get; private set; }
        internal int Bought { get; private set; }
        internal bool Owed => Credits >= CreditsPerWave;

        /// <summary>One credit; false when the bank is already full.</summary>
        internal bool Credit()
        {
            if (Credits >= MaximumCredits) return false;
            Credits++;
            return true;
        }

        /// <summary>A wave was bought: three credits out, Bought up by one. Does nothing unless Owed, so a held wave keeps its credits.</summary>
        internal void Spend()
        {
            if (!Owed) return;
            Credits -= CreditsPerWave;
            Bought++;
        }

        internal void Reset()
        {
            Credits = 0;
            Bought = 0;
        }
    }
}

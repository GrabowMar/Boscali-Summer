namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Server-side OPS skills. A support action needs its system's grade (SPACE, CYBER or SOF);
    /// the lane words are the SQD lane captions. Lives here so Support need not import Progression.
    /// </summary>
    internal interface IPlayerPerks
    {
        /// <summary>
        /// The highest grade <paramref name="playerId"/> holds in <paramref name="lane"/>: 1..6 for
        /// an OPS lane (grade 1 is free), 0 for a lane the board does not have. Never bypassed: the consumer applies its own debug bypass.
        /// </summary>
        int Grade(ulong playerId, string lane);
    }
}

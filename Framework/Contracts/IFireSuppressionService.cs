namespace BoscaliSummer.Framework.Contracts
{
    internal interface IFireSuppressionService
    {
        int ActiveFireCount { get; }

        /// <summary>
        /// Copy recent wreck notices into <paramref name="dest"/>, oldest first.
        /// Returns how many were written, capped by dest.Length.
        /// </summary>
        int CopyWrecks(WreckNotice[] dest);
    }
}

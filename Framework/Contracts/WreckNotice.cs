namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// One recent ground-vehicle wreck the fire module observed. Positions are Unity local
    /// metres. Radio uses this as a cookoff intercept source; nothing here starts a fire.
    /// </summary>
    internal readonly struct WreckNotice
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly int SourceId;
        public readonly float Born;

        public WreckNotice(float x, float y, float z, int sourceId, float born)
        {
            X = x;
            Y = y;
            Z = z;
            SourceId = sourceId;
            Born = born;
        }
    }
}

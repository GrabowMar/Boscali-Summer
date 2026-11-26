namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>A faction's optical or radar order, projected onto the shared mission clock.</summary>
    internal struct OrbitCollection
    {
        public const int SensorCount = 2;
        public const byte Queued = 1, Collected = 2, Missed = 3;
        public const float RetainSeconds = 60f;

        public byte Status;
        public float U, V, At;
    }
}

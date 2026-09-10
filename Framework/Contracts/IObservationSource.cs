namespace BoscaliSummer.Framework.Contracts
{
    internal readonly struct ObservationPoint
    {
        public readonly float X, Y, Z, RecordedAt, Range;
        public readonly string Source;

        public ObservationPoint(float x, float y, float z, float recordedAt, float range, string source)
        {
            X = x; Y = y; Z = z; RecordedAt = recordedAt; Range = range; Source = source;
        }
    }

    internal interface IObservationSource
    {
        string Status { get; }
        bool CanCapture { get; }
        bool Capture();
        bool TryGet(out ObservationPoint point);
        void Clear();
    }
}

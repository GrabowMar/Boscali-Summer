namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>The faction's live operation as ground needs it; Phase is deliberately absent (the engine derives it from the ground report).</summary>
    internal readonly struct GroundOperation
    {
        internal readonly int Id;                       // the picked card's id, never reused
        internal readonly string Key, Label;            // stable place key of the target front; display name
        internal readonly BoscaliSummer.Core.Contracts.TheaterOpKind Kind;
        internal readonly float X, Z, StartedAt;        // the followed fix, metres; the mission second the operation began
        internal GroundOperation(int id, string key, string label, BoscaliSummer.Core.Contracts.TheaterOpKind kind, float x, float z, float startedAt)
        { Id = id; Key = key; Label = label; Kind = kind; X = x; Z = z; StartedAt = startedAt; }
    }

    /// <summary>One live front the groups that are off the effort may hold.</summary>
    internal readonly struct GroundFront
    {
        internal readonly string Key, Label;
        internal readonly float X, Z;
        internal GroundFront(string key, string label, float x, float z) { Key = key; Label = label; X = x; Z = z; }
    }

    internal interface IGroundWarSource
    {
        /// <summary>The faction's live operation, false when none or when this peer is not the host.</summary>
        bool TryGetOperation(FactionHQ hq, out GroundOperation operation);
        /// <summary>Clears into, then appends at most 8 live fronts of the faction; returns the count.</summary>
        int CopyFronts(FactionHQ hq, System.Collections.Generic.List<GroundFront> into);
    }
}

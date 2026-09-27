using System.Collections.Generic;

namespace BoscaliSummer.Framework.Contracts
{
    internal enum TheaterWarPosture : byte
    {
        Cautious = 0,
        Steady = 1,
        Bold = 2,
    }

    /// <summary>A faction-scoped, observed front. Unknown positions use NaN.</summary>
    internal sealed class TheaterFrontView
    {
        public string Key { get; }
        public string Label { get; }
        public float X { get; }
        public float Z { get; }
        public string Status { get; }
        public float Pressure { get; }
        public float Trend { get; }
        public bool Observed { get; }
        public float AgeSeconds { get; }

        public TheaterFrontView(string key, string label, float x, float z, string status,
            float pressure, float trend, bool observed, float ageSeconds)
        {
            Key = key;
            Label = label;
            X = x;
            Z = z;
            Status = status;
            Pressure = pressure;
            Trend = trend;
            Observed = observed;
            AgeSeconds = ageSeconds;
        }
    }

    /// <summary>A bounded, host-authored choice. Identity and revision guard stale clicks.</summary>
    internal sealed class TheaterProposalView
    {
        public int Id { get; }
        public int Revision { get; }
        public string Kind { get; }
        public string Label { get; }
        public string TargetKey { get; }
        public float X { get; }
        public float Z { get; }
        public string Brief { get; }
        public string Risk { get; }
        public string Forces { get; }
        public float SecondsRemaining { get; }

        public TheaterProposalView(int id, int revision, string kind, string label,
            string targetKey, float x, float z, string brief, string risk, string forces,
            float secondsRemaining)
        {
            Id = id;
            Revision = revision;
            Kind = kind;
            Label = label;
            TargetKey = targetKey;
            X = x;
            Z = z;
            Brief = brief;
            Risk = risk;
            Forces = forces;
            SecondsRemaining = secondsRemaining;
        }
    }

    internal sealed class TheaterLiveOperationView
    {
        public int Id { get; }
        public int Revision { get; }
        public string Kind { get; }
        public string TargetKey { get; }
        public string Label { get; }
        public float X { get; }
        public float Z { get; }
        public string Phase { get; }
        public string Summary { get; }
        public int GroundGroups { get; }
        public int AirGroups { get; }
        public int NavalGroups { get; }

        public TheaterLiveOperationView(int id, int revision, string kind, string targetKey,
            string label, float x, float z, string phase, string summary,
            int groundGroups, int airGroups, int navalGroups)
        {
            Id = id;
            Revision = revision;
            Kind = kind;
            TargetKey = targetKey;
            Label = label;
            X = x;
            Z = z;
            Phase = phase;
            Summary = summary;
            GroundGroups = groundGroups;
            AirGroups = airGroups;
            NavalGroups = navalGroups;
        }
    }

    /// <summary>The local faction's host-owned war, with validated intent requests.</summary>
    internal interface ITheaterWarView
    {
        bool Available { get; }
        bool CanCommand { get; }
        TheaterWarPosture Posture { get; }
        IReadOnlyList<TheaterFrontView> Fronts { get; }
        IReadOnlyList<TheaterProposalView> Proposals { get; }
        TheaterLiveOperationView ActiveOperation { get; }
        IReadOnlyList<string> StaffLog { get; }
        void Refresh();
        bool RequestPick(int proposalId, int revision);
        bool RequestCancel(int operationId, int revision);
        bool RequestPosture(TheaterWarPosture posture);
    }
}

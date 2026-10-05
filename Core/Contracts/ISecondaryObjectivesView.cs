using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    internal interface ISecondaryObjectivesView
    {
        IReadOnlyList<SecondaryObjectiveView> Objectives { get; }
        string Status { get; }

        /// <summary>
        /// How many contracts the local faction may hold accepted at once, or 0 when the
        /// host does not report a ceiling. A panel reads 0 as "no ceiling to show" and
        /// prints a plain count rather than inventing a limit.
        /// </summary>
        int ActiveLimit { get; }
        bool IsFresh { get; }
        float SnapshotAgeSeconds { get; }
        int SelectedForHud { get; }
        bool IsActionPending { get; }
        int PendingObjectiveId { get; }
        string ActionResult { get; }

        void Refresh();
        void RequestAccept(int id);
        void RequestCancel(int id);
        void SelectForHud(int id);
    }

    internal enum ObjectiveFamily : byte { Unknown, Control, Strike, AirCover, Electronic, Insertion, Recovery, Reconnaissance, Logistics }
    internal enum ObjectiveAsset : byte { Unknown, Base, GroundVehicle, Aircraft, Personnel, Site }
    internal enum ObjectiveContact : byte { Unavailable, Fixed, Known, LastKnown, Lost }
    internal enum ObjectiveLifecycle : byte { Offered, Active, Completed, Expired, Cancelled }
    internal enum ObjectivePhaseStatus : byte { Pending, Current, Done }
    internal enum ObjectiveAllegiance : byte { Unknown, Friendly, Hostile, Neutral }

    internal sealed class ObjectivePhase
    {
        public byte Id { get; }
        public string Title { get; }
        public string Condition { get; }
        public ObjectivePhaseStatus Status { get; }
        public float Progress { get; }

        public ObjectivePhase(byte id, string title, string condition, ObjectivePhaseStatus status, float progress = 0f)
        { Id = id; Title = title ?? string.Empty; Condition = condition ?? string.Empty; Status = status; Progress = progress; }
    }

    /// <summary>Bounded host facts for presentation; no route or inferred gameplay state.</summary>
    internal sealed class ObjectiveTasking
    {
        public ObjectiveFamily Family { get; }
        public ObjectiveAsset Asset { get; }
        public ObjectiveContact Contact { get; }
        public float ContactAgeSeconds { get; }
        public string NextAction { get; }
        public string Blocker { get; }
        public string Effect { get; }
        public ObjectivePhase[] Phases { get; }
        public ObjectiveLifecycle Lifecycle { get; }
        public string AbortConsequence { get; }
        public ObjectiveAllegiance Allegiance { get; }
        public int CurrentPhase
        {
            get
            {
                for (int i = 0; i < Phases.Length; i++)
                    if (Phases[i].Status == ObjectivePhaseStatus.Current) return i;
                return -1;
            }
        }

        public ObjectiveTasking(ObjectiveFamily family, ObjectiveAsset asset, ObjectiveContact contact, float contactAgeSeconds,
            string nextAction, string blocker, string effect, ObjectivePhase[] phases, ObjectiveLifecycle lifecycle = ObjectiveLifecycle.Active,
            string abortConsequence = "", ObjectiveAllegiance allegiance = ObjectiveAllegiance.Unknown)
        {
            Family = family; Asset = asset; Contact = contact; ContactAgeSeconds = contactAgeSeconds;
            NextAction = nextAction ?? string.Empty; Blocker = blocker ?? string.Empty; Effect = effect ?? string.Empty;
            Phases = phases ?? System.Array.Empty<ObjectivePhase>(); Lifecycle = lifecycle;
            AbortConsequence = abortConsequence ?? string.Empty; Allegiance = allegiance;
        }
    }

    internal sealed class SecondaryObjectiveView
    {
        public int Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Target { get; }
        public string Status { get; }
        public string Reward { get; }
        public float Progress { get; }
        public float SecondsRemaining { get; }
        public int Money { get; }
        public int Xp { get; }
        public bool IsComplete { get; }
        public bool IsOffered { get; }
        public bool IsActive { get; }
        public bool HasMarker { get; }
        public float X { get; }
        public float Z { get; }
        public float Radius { get; }
        /// <summary>The pilot who accepted this faction-wide contract, for display only.</summary>
        public string AcceptedBy { get; }
        public ObjectiveTasking Tasking { get; }

        public SecondaryObjectiveView(int id, string title, string description, string target,
            string status, string reward, float progress, float secondsRemaining, int money, int xp, bool isComplete,
            bool isOffered = false, bool isActive = false, bool hasMarker = false, float x = 0f, float z = 0f, float radius = 0f,
            string acceptedBy = null)
            : this(id, title, description, target, status, reward, progress, secondsRemaining, money, xp, isComplete,
                isOffered, isActive, hasMarker, x, z, radius, acceptedBy, null)
        { }

        public SecondaryObjectiveView(int id, string title, string description, string target,
            string status, string reward, float progress, float secondsRemaining, int money, int xp, bool isComplete,
            bool isOffered, bool isActive, bool hasMarker, float x, float z, float radius, string acceptedBy, ObjectiveTasking tasking)
        {
            Id = id; Title = title; Description = description; Target = target;
            Status = status; Reward = reward; Progress = progress; SecondsRemaining = secondsRemaining;
            Money = money; Xp = xp; IsComplete = isComplete;
            IsOffered = isOffered; IsActive = isActive; HasMarker = hasMarker; X = x; Z = z; Radius = radius;
            AcceptedBy = acceptedBy ?? string.Empty;
            Tasking = tasking;
        }
    }
}

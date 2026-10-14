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

    /// <summary>What a live operation asks of a sortie; None means the card is not tied.</summary>
    internal enum FocusRole : byte { None = 0, Assault = 1, Defend = 2, Recon = 3 }

    /// <summary>One card's link to the faction operation; the card's known site is also available before acceptance.</summary>
    internal sealed class SortieLink
    {
        public const int MaximumPlace = 40;
        public const float MaximumRadius = 1500f;

        /// <summary>The operation id at issue time; 0 for a general sortie that is not tied.</summary>
        public int OperationId { get; }
        /// <summary>None when OperationId is 0.</summary>
        public FocusRole Role { get; }
        /// <summary>Operation place name, at most 40 characters, empty when not tied.</summary>
        public string Place { get; }
        /// <summary>False once the operation closed or the staff moved on while the card lives.</summary>
        public bool OperationLive { get; }
        /// <summary>X, Z and Radius are valid; true for offered cards too.</summary>
        public bool SiteKnown { get; }
        /// <summary>Global metres; zero when the site is not known.</summary>
        public float X { get; }
        /// <summary>Global metres; zero when the site is not known.</summary>
        public float Z { get; }
        /// <summary>Site ring in metres, 0 to 1500 (the existing wire ceiling).</summary>
        public float Radius { get; }
        /// <summary>The host counts the wing on station for this card, and only while SEND WING tasks it.</summary>
        public bool WingCounts { get; }
        /// <summary>True when the card is tied to an operation.</summary>
        public bool Tied => OperationId > 0;

        public SortieLink(int operationId, FocusRole role, string place, bool operationLive, bool siteKnown,
            float x, float z, float radius, bool wingCounts)
        {
            OperationId = operationId; Role = role;
            Place = place == null ? string.Empty : place.Length > MaximumPlace ? place.Substring(0, MaximumPlace) : place;
            OperationLive = operationLive; SiteKnown = siteKnown;
            X = x; Z = z; Radius = radius; WingCounts = wingCounts;
        }
    }

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
        /// <summary>-1 = none; for a STRIKE COMMAND POST card the office id 0..63.</summary>
        public int SubjectId { get; }
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
            string abortConsequence = "", ObjectiveAllegiance allegiance = ObjectiveAllegiance.Unknown, int subjectId = -1)
        {
            SubjectId = subjectId;
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
        /// <summary>Null = not tied to an operation.</summary>
        public SortieLink Link { get; }

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
            : this(id, title, description, target, status, reward, progress, secondsRemaining, money, xp, isComplete,
                isOffered, isActive, hasMarker, x, z, radius, acceptedBy, tasking, null)
        { }

        public SecondaryObjectiveView(int id, string title, string description, string target,
            string status, string reward, float progress, float secondsRemaining, int money, int xp, bool isComplete,
            bool isOffered, bool isActive, bool hasMarker, float x, float z, float radius, string acceptedBy, ObjectiveTasking tasking,
            SortieLink link)
        {
            Link = link;
            Id = id; Title = title; Description = description; Target = target;
            Status = status; Reward = reward; Progress = progress; SecondsRemaining = secondsRemaining;
            Money = money; Xp = xp; IsComplete = isComplete;
            IsOffered = isOffered; IsActive = isActive; HasMarker = hasMarker; X = x; Z = z; Radius = radius;
            AcceptedBy = acceptedBy ?? string.Empty;
            Tasking = tasking;
        }
    }
}

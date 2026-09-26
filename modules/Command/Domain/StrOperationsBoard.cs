using System;

namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// The staff's posture switch: the one standing order the revamped STR keeps.
    /// Unknown values read steady — the staff never invents aggression it was not given.
    /// </summary>
    internal enum StrPosture : byte
    {
        Cautious = 0,
        Steady = 1,
        Bold = 2
    }

    /// <summary>
    /// Ground-truth phases of an operation card. Derived from group stages and hold
    /// change, never from escrow timers: the board states what the ground verified.
    /// </summary>
    internal enum StrOperationPhase : byte
    {
        Forming = 0,
        OnLine = 1,
        InContact = 2,
        Advancing = 3,
        Stalled = 4,
        Consolidating = 5,
        Repulsed = 6
    }

    /// <summary>
    /// One front-sector row: who holds the ground beyond it, where the hold is moving,
    /// and how hot the contact is. HoldDelta NaN means the trend is unknown and reads
    /// as a dash, never as steady.
    /// </summary>
    internal readonly struct StrSectorRow
    {
        public int SectorIndex { get; }
        public ControlState Owner { get; }
        public float HoldDelta { get; }
        public float Pressure01 { get; }
        public bool HasObjective { get; }
        public bool ObjectiveKnown { get; }

        public StrSectorRow(int sectorIndex, ControlState owner, float holdDelta,
            float pressure01, bool hasObjective, bool objectiveKnown)
        {
            SectorIndex = sectorIndex < 0 ? 0 : sectorIndex;
            Owner = owner;
            HoldDelta = holdDelta;
            Pressure01 = pressure01;
            HasObjective = hasObjective;
            ObjectiveKnown = objectiveKnown;
        }
    }

    /// <summary>
    /// The revamped STR operations board as pure view-model arithmetic: front-sector
    /// rows, ground-truth operation phases, and the posture switch. The escrow chest,
    /// reserve floor, per-objective axes and wave steppers are gone from this surface;
    /// the staff sizes spending from the pool and the board shows sectors and cards.
    /// Presentation binds to this; nothing here touches Unity or the game.
    /// </summary>
    internal static class StrOperationsBoard
    {
        /// <summary>Sector rows the FRONT block shows at once; the rest count in its note.</summary>
        internal const int MaxSectors = 8;

        /// <summary>Operation cards the OPERATIONS block shows at once.</summary>
        internal const int MaxOperations = 2;

        /// <summary>Staff-log lines the panel shows; the room shows the full ring.</summary>
        internal const int MaxLogLines = 3;

        /// <summary>Hold change that counts as movement rather than noise.</summary>
        internal const float TrendEpsilon = 0.02f;

        /// <summary>Formed share of committed groups that puts an operation on the line.</summary>
        internal const float FormedThreshold = 0.5f;

        internal const float DefaultStallSeconds = 120f;

        /// <summary>How the posture switch reads. Anything unrecognised reads steady.</summary>
        public static string PostureWord(StrPosture posture)
        {
            switch (posture)
            {
                case StrPosture.Cautious: return "CAUTIOUS";
                case StrPosture.Bold: return "BOLD";
                default: return "STEADY";
            }
        }

        /// <summary>How a sector owner reads in the row's owner column.</summary>
        public static string OwnerWord(ControlState owner)
        {
            switch (owner)
            {
                case ControlState.Friendly: return "FRIENDLY";
                case ControlState.Hostile: return "HOSTILE";
                case ControlState.Contested: return "CONTESTED";
                default: return "OPEN";
            }
        }

        /// <summary>
        /// The trend glyph for a hold delta: rising, falling, holding, or unknown.
        /// An unreadable trend is a dash, never a confident steady block.
        /// </summary>
        public static string TrendGlyph(float holdDelta)
        {
            if (float.IsNaN(holdDelta) || float.IsInfinity(holdDelta)) return "—";
            if (holdDelta > TrendEpsilon) return "▲";
            if (holdDelta < -TrendEpsilon) return "▼";
            return "■";
        }

        /// <summary>How a ground-truth phase reads on the operation card.</summary>
        public static string GroundPhaseWord(StrOperationPhase phase)
        {
            switch (phase)
            {
                case StrOperationPhase.Forming: return "FORMING";
                case StrOperationPhase.OnLine: return "ON LINE";
                case StrOperationPhase.InContact: return "IN CONTACT";
                case StrOperationPhase.Advancing: return "ADVANCING";
                case StrOperationPhase.Stalled: return "STALLED";
                case StrOperationPhase.Consolidating: return "CONSOLIDATING";
                case StrOperationPhase.Repulsed: return "REPULSED";
                default: return "FORMING";
            }
        }

        /// <summary>
        /// The phase of an active operation from what the ground reports: the formed
        /// share of its committed groups, whether any group is in contact, the hold
        /// change on its axis, and how long contact has run without movement. Unknown
        /// trend never reads as advancing.
        /// </summary>
        public static StrOperationPhase ClassifyOperation(float formedRatio, bool inContact,
            float holdDelta, float engagedSeconds, float stallSeconds)
        {
            float formed = float.IsNaN(formedRatio) || float.IsInfinity(formedRatio) ? 0f : formedRatio;
            if (formed < FormedThreshold) return StrOperationPhase.Forming;
            if (!inContact) return StrOperationPhase.OnLine;
            float delta = float.IsNaN(holdDelta) || float.IsInfinity(holdDelta) ? 0f : holdDelta;
            if (delta > TrendEpsilon) return StrOperationPhase.Advancing;
            float stall = stallSeconds > 0f && !float.IsNaN(stallSeconds) && !float.IsInfinity(stallSeconds)
                ? stallSeconds : DefaultStallSeconds;
            float engaged = engagedSeconds > 0f && !float.IsNaN(engagedSeconds) && !float.IsInfinity(engagedSeconds)
                ? engagedSeconds : 0f;
            if (engaged >= stall) return StrOperationPhase.Stalled;
            return StrOperationPhase.InContact;
        }

        /// <summary>
        /// The terminal active phase once the ground decides: a secured objective
        /// consolidates, a spent push without it is repulsed. Concluded copy stays
        /// with the staff log's outcome words.
        /// </summary>
        public static StrOperationPhase FinishOperation(bool objectiveSecured) =>
            objectiveSecured ? StrOperationPhase.Consolidating : StrOperationPhase.Repulsed;

        /// <summary>
        /// One sector row, clamped into the board's ceilings. Pressure outside 0..1 or
        /// unreadable stays as given for the figure column to dash; the row never
        /// invents a value.
        /// </summary>
        public static StrSectorRow BuildSectorRow(int sectorIndex, ControlState owner,
            float holdDelta, float pressure01, bool hasObjective, bool objectiveKnown) =>
            new StrSectorRow(sectorIndex, owner, holdDelta, pressure01,
                hasObjective, objectiveKnown);

        /// <summary>Sector rows the block draws: the first window, never more.</summary>
        public static int SectorRows(int sectors) =>
            sectors < 0 ? 0 : sectors > MaxSectors ? MaxSectors : sectors;

        /// <summary>Operation cards the block draws: the first window, never more.</summary>
        public static int OperationCards(int operations) =>
            operations < 0 ? 0 : operations > MaxOperations ? MaxOperations : operations;
    }
}

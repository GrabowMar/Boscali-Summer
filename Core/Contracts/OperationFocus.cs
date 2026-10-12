namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The faction's live operation as a sortie director sees it. Host only. Default value means no operation.</summary>
    internal readonly struct OperationFocus
    {
        /// <summary>Greater than 0 while an operation lives; a new id on every pick, redirect and new operation.</summary>
        public readonly int OperationId;
        public readonly FocusRole Role;
        /// <summary>Stable anchor: an airbase UniqueName key or objective name, never a grid cell. Empty when unknown.</summary>
        public readonly string AnchorKey;
        /// <summary>Place name, at most 40 characters.</summary>
        public readonly string Label;
        /// <summary>Global metres.</summary>
        public readonly float X, Z;
        /// <summary>Metres a sortie site must fall inside to count as on the operation; DO clamps it to 3000..15000.</summary>
        public readonly float Radius;

        public OperationFocus(int operationId, FocusRole role, string anchorKey, string label, float x, float z, float radius)
        {
            OperationId = operationId; Role = role; AnchorKey = anchorKey ?? string.Empty; Label = label ?? string.Empty;
            X = x; Z = z; Radius = radius;
        }

        public bool IsLive => OperationId > 0;

        /// <summary>True when a card tied at issue time to (operationId, anchorKey) still belongs to this operation.</summary>
        public bool Covers(int operationId, string anchorKey) =>
            OperationId > 0 && operationId > 0 && (operationId == OperationId ||
                !string.IsNullOrEmpty(anchorKey) && string.Equals(anchorKey, AnchorKey, System.StringComparison.Ordinal));
    }

    /// <summary>Implemented by the Living Front service; registered by TheaterOps. Read late, never at install.</summary>
    internal interface IOperationFocusSource
    {
        /// <summary>Host only; false on a client and when the faction has no live operation.</summary>
        bool TryGetFocus(int factionInstanceId, out OperationFocus focus);
    }

    /// <summary>One paid sortie, with identity. Built by DynamicOperations after the money is paid.</summary>
    internal readonly struct SortieOutcome
    {
        public readonly int FactionId;       // FactionHQ.GetInstanceID() of the board that issued it
        public readonly int ContractId;      // card id
        public readonly string Title;        // OperationTitles.Title(kind), at most 27 characters
        public readonly int OperationId;     // tie at issue time, 0 = general
        public readonly string AnchorKey;    // tie at issue time, empty = none
        public readonly float X, Z;          // site, global metres, finite
        public readonly string Pilot;        // acceptor callsign, at most 32 characters
        public readonly bool ByWing;         // SEND WING tasked the wing on this sortie when it finished

        public SortieOutcome(int factionId, int contractId, string title, int operationId, string anchorKey,
            float x, float z, string pilot, bool byWing)
        {
            FactionId = factionId; ContractId = contractId; Title = title ?? string.Empty; OperationId = operationId;
            AnchorKey = anchorKey ?? string.Empty; X = x; Z = z; Pilot = pilot ?? string.Empty; ByWing = byWing;
        }
    }

    /// <summary>Implemented by DynamicOperations' OperationsManager. Subscribe late; unsubscribe in reset.</summary>
    internal interface ISortieOutcomeSource
    {
        /// <summary>Host only; raised once per paid contract on the host thread, after pay and morale, inside try/catch.</summary>
        event System.Action<SortieOutcome> Finished;
    }
}

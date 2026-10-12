using System;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>What a sortie asks of a wing, decided by the host from the card's own state, never from its family.</summary>
    internal enum WingSortieIntent : byte
    {
        /// <summary>The wing cannot help with this card; WingSortieOrder.Why says why.</summary>
        None,
        /// <summary>A strike kind (Operation.IsStrike): destroy WingSortieOrder.TargetId.</summary>
        Destroy,
        /// <summary>A hold kind (Capture, Defend, Patrol, SupplyEscort, RepairCover): fly CAP over the site.</summary>
        Cover,
        /// <summary>A reconnaissance kind: scout the site; raises tracking, never completes the card.</summary>
        Observe,
    }

    /// <summary>Where a wing tasking stands.</summary>
    internal enum WingSortieState : byte
    {
        None,        // no wing task for this sortie id (never sent, replaced, or on a client)
        Tasked,      // ordered, wingmen on the way
        OnStation,   // a wingman is engaged on the target (Destroy) or inside the cover circle (Cover, Observe)
        Done,        // target down, or the card's clock ran out, or the sortie ended; the wing was formed up once
        Failed,      // no wingman is left flying it
        Recalled,    // RECALL pressed, another sortie sent, or the pilot gave the wing another order
    }

    /// <summary>One SEND WING press as the host sees the card. Built by ISortieWingOrderSource, never by the console.</summary>
    internal readonly struct WingSortieOrder
    {
        public readonly int SortieId;               // the card id (SecondaryObjectiveView.Id), unique per mission
        public readonly WingSortieIntent Intent;
        public readonly bool Accepted;              // the card is held (Active); a strike on an offered card cancels it
        public readonly float X, Z;                 // GLOBAL metres of the card marker; NaN when the card has no marker
        public readonly uint TargetId;              // persistent id of the hostile unit to destroy (Destroy only); 0 = none
        public readonly float SecondsRemaining;     // the card's clock in seconds; 0 = none
        public readonly string Why;                 // for Intent None: the reason in words with what to do; otherwise empty

        public WingSortieOrder(int sortieId, WingSortieIntent intent, bool accepted, float x, float z,
            uint targetId, float secondsRemaining, string why)
        {
            SortieId = sortieId; Intent = intent; Accepted = accepted; X = x; Z = z;
            TargetId = targetId; SecondsRemaining = secondsRemaining; Why = why ?? "";
        }
    }

    /// <summary>What the wing has done on one sortie since the press. Host only; a client gets no status (a dash).</summary>
    internal readonly struct WingSortieStatus
    {
        public readonly int SortieId;
        public readonly WingSortieState State;
        public readonly int Wingmen;                // wingmen still flying it, at the last 1 Hz step
        public readonly bool TargetDown;
        public readonly string Words;               // one honest line for the card, at most 40 characters

        public WingSortieStatus(int sortieId, WingSortieState state, int wingmen, bool targetDown, string words)
        { SortieId = sortieId; State = state; Wingmen = wingmen; TargetDown = targetDown; Words = words ?? ""; }
    }

    /// <summary>The one door through which another module gives the recruited wing a point or a target. An explicit pilot press only.</summary>
    internal interface IWingSortieOrders
    {
        /// <summary>null when SEND WING would be accepted now; otherwise the reason in words with what to do (at most 100 characters). A client may pass default.</summary>
        string Refusal(in WingSortieOrder order);
        /// <summary>Task the whole wing for this sortie. Replaces any earlier sortie tasking (that one reads Recalled, no FormUp).</summary>
        bool Send(in WingSortieOrder order, out string answer);
        /// <summary>RECALL: form the wing up on the pilot and end the tasking. Acts only when sortieId is the tasked sortie.</summary>
        bool Recall(int sortieId, out string answer);
        /// <summary>The sortie ended (completed, expired, cancelled). Idempotent; acts only on the tasked sortie; issues at most one FormUp.</summary>
        void Release(int sortieId);
        /// <summary>The last tasking's status when sortieId is its sortie; false on a client or for any other id.</summary>
        bool TryGetStatus(int sortieId, out WingSortieStatus status);
    }

    /// <summary>Host only: builds the wing order for a card of the local player's faction from the operation's own state.</summary>
    internal interface ISortieWingOrderSource
    {
        /// <summary>True with a filled order (Intent None carries Why) for a live card; false on a client, in a mission that is not running, for another faction's card, or once the card has ended.</summary>
        bool TryGetWingOrder(int sortieId, out WingSortieOrder order);
    }
}

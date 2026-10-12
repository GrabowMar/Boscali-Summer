using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    internal enum StrItemKind : byte { None = 0, Effort = 1, Proposal = 2, Front = 3, Sortie = 4, Post = 5 }

    /// <summary>Kind plus a key that survives snapshots: operation id, proposal id, platform number, sortie id or post id.</summary>
    internal readonly struct StrItemId : IEquatable<StrItemId>
    {
        public readonly StrItemKind Kind;
        public readonly int Key;
        public StrItemId(StrItemKind kind, int key) { Kind = kind; Key = key; }
        public bool IsNone => Kind == StrItemKind.None;
        public static StrItemId None => default;
        public bool Equals(StrItemId other) => Kind == other.Kind && Key == other.Key;
        public override bool Equals(object obj) => obj is StrItemId o && Equals(o);
        public override int GetHashCode() => ((int)Kind << 24) ^ Key;
        public static bool operator ==(StrItemId a, StrItemId b) => a.Equals(b);
        public static bool operator !=(StrItemId a, StrItemId b) => !a.Equals(b);
    }

    /// <summary>Which of the three overlay layers are on (the plate's toggles).</summary>
    [Flags] internal enum StrLayers : byte { None = 0, Fronts = 1, Operation = 2, Sorties = 4, All = 7 }

    /// <summary>Client-local interface state; only the hub writes it.</summary>
    internal struct StrUiState
    {
        public StrItemId Selected, Hover;
        public bool ShowFronts, ShowOperation, ShowSorties;
        public float ReplanArmedUntil;      // unscaled time until which a first REPLAN press stays armed; 0 = not armed
        public string Notice;               // one local result line (an accept or wing answer)
        public float NoticeUntil;           // unscaled time it stops showing (6 s)
    }

    internal enum StrActionKind : byte { Pick, Replan, Posture, Accept, SendWing, Recall }

    internal enum StrWhy : byte
    {
        None, NoBoard, Stale, Pending, Locked, Gone, NoOperation, SamePosture,
        NotOffered, BoardFull, HostOnly, WingRefused, NoWing,
    }

    /// <summary>The answer of a gate or an action: allowed or the reason, plus whether a verb was sent.</summary>
    internal readonly struct StrAct
    {
        public readonly StrWhy Why;
        public readonly string Text;
        public readonly bool Sent;
        public StrAct(StrWhy why, string text, bool sent = false) { Why = why; Text = text ?? ""; Sent = sent; }
        public bool Allowed => Why == StrWhy.None;
    }

    /// <summary>The engine, sorties and wing verbs behind one seam, so the runner is testable with a fake.</summary>
    internal interface IStrVerbs
    {
        bool Pick(int proposalId, int revision);                    // ITheaterWarView.RequestPick (a redirect while an operation lives)
        bool Cancel(int operationId, int revision);                 // ITheaterWarView.RequestCancel (CALL OFF; REPLAN)
        bool Posture(TheaterWarPosture posture);
        void Accept(int contractId);                                // ISecondaryObjectivesView.RequestAccept (void; the answer is ActionResult)
        bool SendWing(int contractId, out string answer);           // host only: ISortieWingOrderSource.TryGetWingOrder then IWingSortieOrders.Send
        bool Recall(out string answer);
    }

    /// <summary>What the panel and the overlay need from the hub. The real hub and the harness hub implement it.</summary>
    internal interface IStrHub
    {
        StrViewModel Current { get; }       // never null: Mode explains an empty model
        int Revision { get; }
        StrUiState Ui { get; }              // a copy
        void Select(StrItemId id, bool focusMap);
        void Hover(StrItemId id);
        void Unhover(StrItemId id);
        void ClearSelection();
        StrAct Run(StrActionKind kind, StrItemId id, int arg = 0);   // the single command path
        void SetLayer(StrLayers layer, bool on);
    }

    /// <summary>A confirmed enemy post, copied from the high command feed by the reader (enemy rows only). N-08: no Away, no Destroyed.</summary>
    internal struct StrPostIn
    {
        public int Id, Bounty;
        public string Name, Rank, Role, Site, Trait;   // Trait := CommanderView.Bonus (the strike line)
        public float IntelAge, X, Z;                   // seconds since last confirmed (600 = ten minutes or more); global metres
        public bool BountyCapped;                      // := CommanderView.BountyClaimed
    }

    /// <summary>What the wing did on one sortie, copied from IWingSortieOrders by the reader (host only). N-08: no Kills.</summary>
    internal struct StrWingIn
    {
        public int SortieId;
        public string Refusal;             // null when SEND WING would be accepted now; cached from the last 1 Hz refresh
        public WingSortieState State;
        public WingSortieIntent Intent;
        public string Words;               // WingSortieStatus.Words
    }

    /// <summary>One tick of everything the board reads. The reader copies it from the services; the builder reads nothing else.</summary>
    internal struct StrFeeds
    {
        public int Stamp;                  // change stamp: equal stamp, equal second and equal ui => the builder returns without touching the model
        public bool IsHost;
        public ulong LocalId;              // PlayerIdentity.Of(local pilot), 0 when none
        public string FactionCode;
        public bool HasOwn;
        public float OwnX, OwnZ;           // the local aircraft, global metres
        // war: ITheaterWarView
        public bool WarAvailable;
        public TheaterViewState WarState;
        public TheaterWarSnapshot War;
        public float WarAgeSeconds, WarSincePublished, WaitSeconds;   // SnapshotAgeSeconds (-1 none), SecondsSincePublished, seconds waited for a first report
        public bool WarCanCommand, WarPending;
        public TheaterCommandResult WarLast;                          // null once older than 6 s (engine rule)
        // sorties: ISecondaryObjectivesView
        public bool SortiesAvailable, SortiesFresh, SortieActionPending;
        public IReadOnlyList<SecondaryObjectiveView> Sorties;
        public int SortieActiveLimit;      // 0 = no ceiling reported
        // posts and wing
        public bool PostsAvailable;
        public float PostsAgeSeconds;
        public StrPostIn[] Posts;
        public int PostCount;
        public StrWingIn[] Wing;
        public int WingCount;
        public string PickerPrompt;        // MapPicker.Prompt, "" when no gesture is armed
        public int KnownAirDefence;        // -1 unknown
        // census: the measured counts behind the tiles, SITREP and the alert level
        public TheaterCensus Census;       // War.Census when a snapshot exists; else, on a host, the census CommandManager measured itself; else TheaterCensus.Unmeasured. default(TheaterCensus) has Known == false, so a reader that forgets to fill it prints dashes
        public float CensusAgeSeconds;     // seconds since that census was measured or received; -1 unknown
    }
}

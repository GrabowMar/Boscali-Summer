using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    // Pure payloads of the NETWORK and SHADOW rooms. The controller maps CyberStateData / SofStateData onto these (positions already
    // normalised to the theatre map, 0..1, y down); the rooms derive every line, ring and edge from the fields.

    internal enum NetNodeState : byte { OutOfReach, InReach, Hopping, Held, Traced }

    internal struct NetNodeView
    {
        public int Id;
        public NodeKind Kind;
        public float X, Y;
        public NetNodeState State;
        /// <summary>Hop progress 0..100 while <see cref="NetNodeState.Hopping"/>.</summary>
        public byte HopPct;
        /// <summary>Where the intrusion reached this node from: another node id, or -1.</summary>
        public int FromNode;
        /// <summary>... or the own anchor (index into <see cref="NetworkRoomView.Anchors"/>), or -1.</summary>
        public int FromAnchor;
        public string Label;
        /// <summary>Hostname-like handle, fixed per node from its kind and id ("rdr-04.vesper"); it carries no reading.</summary>
        public string Handle;
        /// <summary>Who holds or is hopping it: "YOU", an operator's name, "ALLY" when the mirror names none, "" when nobody.</summary>
        public string Owner;
        /// <summary>The nearest reach source of the operator (own truck, or own held node once a session runs) and its distance ("EW-1 · 11.3 KM"); "" when none stands.</summary>
        public string Reach;
    }

    internal struct NetAnchorView
    {
        public AnchorKind Kind;
        public AnchorHealth Health;
        public float X, Y;
        /// <summary>Reach as a share of the map width (EW trucks only).</summary>
        public float Reach;
        public string Label;
        public byte Rebuild;
        /// <summary>Seconds left on a trace lock (EW trucks only), 0 when free.</summary>
        public int LockLeft;
    }

    /// <summary>The selected node's inspector: already-worded lines (the quote is the host's own: same rules, same numbers).</summary>
    internal struct NetDossierView
    {
        public bool Has;
        public string Title, Handle, Stamp, Owner, Reach, Hold, Hop, Burn, Drop, Next;
        public AvState StampTone, NextTone;
        /// <summary>Allocation the HOP quote charges (0 when going deeper from a held node); the room weighs it against the pilot's balance.</summary>
        public int HopCost;
    }

    internal sealed class NetworkRoomView
    {
        public bool Active;
        /// <summary>Theatre size in kilometres, so the room can link nodes within 12 km.</summary>
        public float MapKmW = 90f, MapKmH = 60f;
        public readonly List<NetNodeView> Nodes = new List<NetNodeView>(16);
        public readonly List<NetAnchorView> Anchors = new List<NetAnchorView>(4);
        // YOUR session.
        public bool HasIntrusion;
        public byte Trace;
        public string Operator = "";
        public int YourHeld;
        public bool Hopping;
        public float HopFraction;
        public int HopLeft;
        /// <summary>Estimated seconds until the trace fills at the current rate (rules only, no counter-pressure); -1 when nothing climbs it.</summary>
        public int EstTraceSeconds = -1;
        public string HopLine = "";
        /// <summary>No host update for this long while the session should be changing it; blocks HOP.</summary>
        public bool Stale;
        public int AgeSeconds;
        /// <summary>The one next step (or the blocking reason) for the whole session.</summary>
        public string Next = "";
        public AvState NextTone = AvState.Caution;
        // The FACTION's network.
        public byte Infocon = 5;
        public string ThreatWord = "", ThreatNote = "";
        /// <summary>Enemy holds on our own network, and whether PURGE can run now (its label carries the countdown or the recharge).</summary>
        public int Breaches;
        public bool CanPurge;
        public string PurgeLabel = "PURGE";
        /// <summary>Nodes the whole faction holds or hops (faction total, never divided by a personal cap) and the running sessions against their cap.</summary>
        public int HeldTotal, IntrusionCap, Sessions;
        public int SelectedId = -1;
        public NetDossierView Dossier;
        public bool CanHop, CanBurn, CanDrop;
        // Session log: who is online, then the faction's real events (kept across refreshes, the mirror only carries the newest three).
        public const int MaxHistory = 8;
        public readonly List<FrontLogLine> Roster = new List<FrontLogLine>(3);
        public readonly List<FrontLogLine> History = new List<FrontLogLine>(MaxHistory);
        public int LastSeq;
        public string Prompt = "";
        public string Session = "";
        public string Meta = "";
    }

    internal struct SofCampView
    {
        public float X, Y;
        public AnchorHealth Health;
        public byte Rebuild;
        public string Name;
    }

    internal struct SofTeamView
    {
        public int Slot;
        public string Callsign, State, Mission, Flags, Eta;
        /// <summary>Fixed word per slot ("SABRE") that rides the callsign on the dossier tab; it carries no reading.</summary>
        public string Codename;
        /// <summary>Who commands the team (<see cref="SofPageWords.Control"/>: YOU, CTRL name, AUTO) and the stamp word of its state (STANDBY, TASKED, PINNED ...).</summary>
        public string Control, Stamp;
        public AvState Tone;
        public float X, Y, DestX, DestY, TargetX, TargetY;
        public bool HasDest, Lasing, Wounded, Selected, Present;
        /// <summary>Another operator commands this team: the room shows it and offers no order for it.</summary>
        public bool Locked;
        public byte Exposure, Ammo, Odds;
        public bool Helicopter;
    }

    internal struct SofTargetView
    {
        public int Id;
        public TargetKind Kind;
        public float X, Y;
        public bool Exploit, Resisted, Selected;
        public string Name;
        /// <summary>The list row's second line: grid square plus what it is good for (BEST mission, EXPLOIT or RESISTED).</summary>
        public string Detail;
    }

    internal struct SofHeldView
    {
        public float X, Y, SecondsLeft;
        public string Name;
    }

    internal struct SofMissionView
    {
        public MissionKind Kind;
        public string Name, Cost, Odds, Note;
        /// <summary>Allowed: the selected team could be sent now. Pick: the mission may be drafted (a team is picked and no other operator commands it). Drafted: it is the draft. Current: best fit for the picked target.</summary>
        public bool Allowed, Pick, Drafted, Exploit, Current;
    }

    internal sealed class ShadowRoomView
    {
        public bool Active;
        public readonly List<SofCampView> Camps = new List<SofCampView>(3);
        public readonly List<SofTeamView> Teams = new List<SofTeamView>(4);
        public readonly List<SofTargetView> Targets = new List<SofTargetView>(SofWire.MaxTargets);
        public readonly List<SofHeldView> Held = new List<SofHeldView>(4);
        public readonly List<NetPoint> Enemies = new List<NetPoint>(4);
        /// <summary>One glyph per enemy: ? seen by our bird, ! detected near our sites, Q hunted by our QRF.</summary>
        public readonly List<string> EnemyMark = new List<string>(4);
        /// <summary>The QRF card: one line, its tone, and whether SWEEP can roll now (its label carries the countdown or the refit).</summary>
        public string QrfLine = "", SweepLabel = "SWEEP";
        public AvState QrfTone = AvState.Inert;
        public bool CanSweep;
        public bool HasFob;
        public float FobX, FobY, FobRadius;
        public string FobName = "", FobNote = "";
        public int TeamCap;
        public string TapLine = "";
        public int SelectedSlot = -1;
        public int SelectedTargetId;
        public readonly List<SofMissionView> Missions = new List<SofMissionView>(5);
        /// <summary>PUSH, HOLD, EXFIL, LIFT: whether each may be pressed, and whether PUSH/HOLD is on.</summary>
        public readonly bool[] OrderOn = new bool[4], OrderLatched = new bool[4];
        public string Meta = "";
        /// <summary>The operator's draft RECON point: a click on the room's own map (map u,v in 0..1, y down) and its id for <see cref="IFrontActions.TeamMission"/>.
        /// Local state: the room writes it, the controller clears it once the order is sent.</summary>
        public bool HasDraft;
        public float DraftU, DraftV;
        public int DraftId;
        /// <summary>The mission the operator drafted (None = nothing). Local like the point: the room writes it, ISSUE sends it, the controller clears it once sent.</summary>
        public MissionKind DraftMission;
        /// <summary>Why a draft or pick was dropped (a team left the roster, a target left the list); the room clears it on the operator's next pick.</summary>
        public string Reason = "";

        // The order composer, one worded line per step (TEAM, MISSION rows above, AIM, QUOTE, ISSUE) and the movement caption.
        public string TeamLine = "", AimLine = "", AimNote = "", QuoteLine = "", QuoteNote = "", IssueLabel = "ISSUE ORDER", IssueNote = "", ExfilLabel = "EXFIL", ExfilNote = "";
        public AvState TeamTone, AimTone, QuoteTone, IssueTone;
        public bool CanIssue;
        /// <summary>The picked target's file card: the tab word and four worded lines (kind, grid and best mission, who is tasked on it, what its tag means).</summary>
        public string FileTab = "", FileA = "", FileB = "", FileC = "", FileD = "";

        // The roster's warning strip: the worst team (lost, pinned, low on ammo), whether a team is raised at all, and the events kept across refreshes.
        public int WarnSlot = -1;
        public string WarnText = "";
        public AvState WarnTone;
        public const int MaxHistory = 8;
        public readonly List<FrontLogLine> History = new List<FrontLogLine>(MaxHistory);
        public int LastSeq;
    }

    internal struct NetPoint { public float X, Y; }
}

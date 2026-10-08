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
    }

    /// <summary>The selected node's terminal page: already-worded lines.</summary>
    internal struct NetDossierView
    {
        public bool Has;
        public string Title, Kind, Grid, Defenses, Effect, Exploit, Status;
        public AvState StatusTone;
    }

    internal sealed class NetworkRoomView
    {
        public bool Active;
        /// <summary>Theatre size in kilometres, so the room can link nodes within 12 km.</summary>
        public float MapKmW = 90f, MapKmH = 60f;
        public readonly List<NetNodeView> Nodes = new List<NetNodeView>(16);
        public readonly List<NetAnchorView> Anchors = new List<NetAnchorView>(4);
        public bool HasIntrusion;
        public byte Trace;
        public string TraceNote = "";
        /// <summary>INFOCON style threat to our own network: 5 calm .. 1 attack.</summary>
        public byte Infocon = 5;
        public string ThreatWord = "", ThreatNote = "";
        public int HeldTotal, IntrusionCap;
        public int SelectedId = -1;
        public NetDossierView Dossier;
        public bool CanHop, CanBurn, CanDrop;
        public string ActionNote = "";
        public readonly List<FrontLogLine> Terminal = new List<FrontLogLine>(16);
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
        public AvState Tone;
        public float X, Y, DestX, DestY, TargetX, TargetY;
        public bool HasDest, Lasing, Wounded, Selected, Present;
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
        public bool Allowed, Exploit, Current;
    }

    internal sealed class ShadowRoomView
    {
        public bool Active;
        public readonly List<SofCampView> Camps = new List<SofCampView>(3);
        public readonly List<SofTeamView> Teams = new List<SofTeamView>(4);
        public readonly List<SofTargetView> Targets = new List<SofTargetView>(8);
        public readonly List<SofHeldView> Held = new List<SofHeldView>(4);
        public readonly List<NetPoint> Enemies = new List<NetPoint>(4);
        public bool HasFob;
        public float FobX, FobY, FobRadius;
        public string FobName = "", FobNote = "";
        public int TeamCap;
        public string TapLine = "";
        public int SelectedSlot = -1;
        public int SelectedTargetId;
        public string TargetLine = "";
        public readonly List<SofMissionView> Missions = new List<SofMissionView>(5);
        /// <summary>PUSH, HOLD, EXFIL, LIFT: whether each may be pressed, and whether PUSH/HOLD is on.</summary>
        public readonly bool[] OrderOn = new bool[4], OrderLatched = new bool[4];
        public string OrderNote = "";
        public string Meta = "";
    }

    internal struct NetPoint { public float X, Y; }
}

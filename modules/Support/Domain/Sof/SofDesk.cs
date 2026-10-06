using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    /// <summary>Typed host verdict of a SOF command. Wire values; never renumber. Every value has words.</summary>
    internal enum SofOutcome : byte
    {
        None = 0, Raised = 1, Ordered = 2, Sent = 3, Diverted = 4,
        NoTarget = 5, NoCamp = 6, CampDown = 7, TeamCap = 8, NoTeam = 9, NotReady = 10, Pinned = 11, Wounded = 12, NoAmmo = 13,
        LowCredit = 14, Frozen = 15, RateLimited = 16, Unavailable = 17, BadOrder = 18, OutOfTheater = 19, Raising = 20
    }

    internal static class SofOutcomeWords
    {
        public const byte MaxOutcome = (byte)SofOutcome.Raising;

        public static string Of(SofOutcome outcome, int detail = 0)
        {
            switch (outcome)
            {
                case SofOutcome.None: return "";
                case SofOutcome.Raised: return "TEAM RAISING — DEPLOYS AT THE CAMP IN " + SpaceRules.Clock(detail);
                case SofOutcome.Ordered: return "ORDER SENT";
                case SofOutcome.Sent: return "TEAM TASKED — MOVING OUT";
                case SofOutcome.Diverted: return "TEAM DIVERTED";
                case SofOutcome.NoTarget: return "NEGATIVE: NO SUCH TARGET — PICK ONE FROM THE MAP";
                case SofOutcome.NoCamp: return "NEGATIVE: NO CAMP — SOF OFFLINE, CALLS STILL LIVE";
                case SofOutcome.CampDown: return "NEGATIVE: CAMP DOWN — NO NEW TEAMS UNTIL IT IS RESTORED";
                case SofOutcome.TeamCap: return "NEGATIVE: TEAM CAP — EXFIL OR WAIT FOR ONE TO END";
                case SofOutcome.NoTeam: return "NEGATIVE: NO SUCH TEAM — RAISE ONE AT THE CAMP";
                case SofOutcome.NotReady: return "NEGATIVE: TEAM NOT READY — DIVERT OR EXFIL IT FIRST";
                case SofOutcome.Pinned: return "NEGATIVE: TEAM PINNED — COVER IT FROM THE AIR";
                case SofOutcome.Wounded: return "NEGATIVE: TEAM WOUNDED — RECOVERS IN " + Math.Max(0, detail) + " S";
                case SofOutcome.NoAmmo: return "NEGATIVE: TEAM OUT OF AMMO — EXFIL TO A CAMP TO RESUPPLY";
                case SofOutcome.LowCredit: return CallWords.Refusal(CallRefusal.LowCredit, need: Math.Max(0, detail));
                case SofOutcome.Frozen: return "NEGATIVE: CREDIT FROZEN — STAND BY";
                case SofOutcome.RateLimited: return "NEGATIVE: RATE LIMITED — SLOW DOWN";
                case SofOutcome.BadOrder: return "NEGATIVE: ORDER NOT POSSIBLE NOW — CHECK THE TEAM STATE";
                case SofOutcome.OutOfTheater: return "NEGATIVE: OUT OF THE THEATER — PICK A POINT ON THE MAP";
                case SofOutcome.Raising: return "NEGATIVE: TEAM STILL RAISING — DEPLOYS IN " + SpaceRules.Clock(detail);
                default: return "NEGATIVE: SOF OFFLINE — NO CAMP STANDING";
            }
        }
    }

    internal readonly struct SofResult
    {
        public readonly SofOutcome Outcome;
        public readonly int Slot, Charged, Detail;
        public SofResult(SofOutcome outcome, int slot = 0, int charged = 0, int detail = 0) { Outcome = outcome; Slot = slot; Charged = charged; Detail = detail; }
        public bool Ok => Outcome == SofOutcome.Raised || Outcome == SofOutcome.Ordered || Outcome == SofOutcome.Sent || Outcome == SofOutcome.Diverted;
        public string Words => SofOutcomeWords.Of(Outcome, Detail);
    }

    internal enum SofEventKind : byte
    {
        Raised = 1, Departed = 2, OnSite = 3, Pinned = 4, Unpinned = 5, Lost = 6, Success = 7, Failed = 8, Home = 9, LiftUp = 10, LiftDown = 11, Seized = 12, Retaken = 13
    }

    /// <summary>A real SOF event: the console, the notices and the mirror's event ring hang off this.</summary>
    internal readonly struct SofEvent
    {
        public readonly SofEventKind Kind;
        public readonly int Slot;
        public readonly MissionKind Mission;
        /// <summary>Who sent the mission the event belongs to (0 for an event with none): OVERLORD's work is told apart from a human's.</summary>
        public readonly ulong Operator;
        public SofEvent(SofEventKind kind, int slot, MissionKind mission, ulong op = 0) { Kind = kind; Slot = slot; Mission = mission; Operator = op; }
    }

    /// <summary>What the enemy looks like around one point, as the host sees it (counts only; the client never gets this).</summary>
    internal readonly struct SofScene
    {
        public readonly int Within300, Within1000, Within2000, Armored1000;
        public readonly bool Stared;
        public SofScene(int within300, int within1000, int within2000, int armored1000, bool stared)
        { Within300 = within300; Within1000 = within1000; Within2000 = within2000; Armored1000 = armored1000; Stared = stared; }
    }

    /// <summary>What the pure desk needs from the live game. Every effect is a host call; the desk never touches the engine.</summary>
    internal interface ISofPorts
    {
        float Now { get; }
        int Humans { get; }
        /// <summary>The faction key this desk acts for.</summary>
        int Owner { get; }
        bool Fob { get; }
        SofOutcome TrySpend(ulong op, int cr, out int detail);
        void Refund(ulong op, int cr);
        SofScene Scene(float x, float z);
        /// <summary>The faction's CYBER holds a node within <see cref="SofRules.RingBoostMetres"/> of the point.</summary>
        bool CyberNear(float x, float z);
        /// <summary>One odds roll: true = success.</summary>
        bool Roll(int chancePercent);
        void Reveal(float x, float z, float radius, float seconds);
        /// <summary>Holds a laser on the target. False when the target's unit cannot be resolved (the desk then sets no LASE state and posts nothing).</summary>
        bool LaseBegin(int slot, uint key, float x, float z);
        void LaseEnd(int slot, uint key);
        bool TargetAlive(TargetKind kind, AnchorSub sub, uint key);
        bool Sabotage(AnchorSub sub, uint key);
        bool Tap(TargetKind kind, uint key, float seconds);
        bool BuildingAlive(uint key);
        bool PostCover(int slot, float x, float z);
        bool PostLase(int slot, float x, float z);
        void Pay(ulong pilot, int cr);
    }

    internal sealed class SofTeam
    {
        public int Slot;
        public bool Active;
        public ulong Raiser;
        public TeamState State;
        public float X, Z, HomeX, HomeZ, DestX, DestZ, Exposure, Ammo = 100f;
        public bool HasDest, PushOn, HoldOn, Wounded, LiftWaiting, Carried, CarrierFlew;
        public Insertion Insert;
        public float RaiseEndsAt, RecoverUntil, LostAt, PinDeadline, CoverUntil, LiftUntil, OnSiteStart, OnSiteEnd;
        public TeamState PrePin;
        public MissionKind Mission;
        public int TargetId;
        public TargetKind TargetKind;
        public AnchorSub TargetSub;
        public uint TargetKey;
        /// <summary>The enemy unit that never adds exposure to this team (its last unit mission target): set at the order, kept resolving whatever the fog says, cleared at home, on loss or when the unit is truly dead.</summary>
        public uint ExemptKey;
        public float TargetX, TargetZ;
        public bool Exploit, LaseActive;
        public int Odds;
        public ulong CoverPilot, Carrier;
        public int CarrierId;
        public float CarrierSeenAt;
        /// <summary>The mission fee and who paid it; zero once the team is on site (a target lost before then refunds it).</summary>
        public int MissionCost;
        public ulong MissionPayer;
        /// <summary>Where the team boarded a helicopter (an extraction pays only for a real flight).</summary>
        public float BoardX, BoardZ;
        /// <summary>Pilots who killed an enemy ground unit within 2 km of the team while it was pinned (the only ones a COVER pay can go to).</summary>
        public readonly HashSet<ulong> CoverKillers = new HashSet<ulong>();

        public string Callsign => SofRules.Callsign(Slot);
    }

    internal struct HeldBuilding
    {
        public int Id;
        public uint Key;
        public float X, Z, Until, RetakeSince;
    }

    /// <summary>
    /// One faction's SOF host path: camps, teams, orders, odds-resolved missions, held buildings and the helicopter lift. Every unknown, hidden or
    /// foreign target id answers the same <see cref="SofOutcome.NoTarget"/>, so an id is never an oracle for what the enemy has.
    /// </summary>
    internal sealed class SofDesk
    {
        public const int MaxVisible = 20, MaxHelis = 16, MaxEventsPending = 32;
        public const float AmmoPerMission = 30f, AmmoRefillPerSecond = 5f, BaseRadius = 300f, LiftWaitSeconds = 300f, CarrierLostSeconds = 5f, LostLingerSeconds = 30f;
        private readonly ISofPorts ports;
        private readonly List<SofTarget> visible = new List<SofTarget>(MaxVisible);
        private readonly List<SofTarget> candidates = new List<SofTarget>(SofTargetIds.Capacity);
        private readonly HashSet<int> engaged = new HashSet<int>(), seen = new HashSet<int>(), listed = new HashSet<int>();
        private readonly List<int> idScratch = new List<int>(SofTargetIds.Capacity);
        private readonly List<HeldBuilding> held = new List<HeldBuilding>(SofRules.HeldCap);
        private readonly List<SofEvent> pending = new List<SofEvent>(16), drained = new List<SofEvent>(16);
        private readonly Dictionary<int, float> landedSince = new Dictionary<int, float>();
        private readonly HashSet<int> heliSeen = new HashSet<int>();
        private int nextHeldId;
        private float lastTick = -1f;

        public SofDesk(ISofPorts ports, SofCampSet camps)
        {
            this.ports = ports;
            Camps = camps;
            for (int i = 0; i < SofRules.MaxTeams; i++) Teams[i] = new SofTeam { Slot = i };
        }

        public SofCampSet Camps { get; }
        public SofTeam[] Teams { get; } = new SofTeam[SofRules.MaxTeams];
        public SofTargetIds Ids { get; } = new SofTargetIds();
        public NodeReveal Reveal { get; } = new NodeReveal();
        public IReadOnlyList<SofTarget> Visible => visible;
        public IReadOnlyList<HeldBuilding> Held => held;
        /// <summary>M6a FORWARD OPERATING BASE: while on, teams are raised at this point (a held building) instead of the camp, even with the camp down. The runtime sets it.</summary>
        public bool FobActive { get; set; }
        public float FobX { get; set; }
        public float FobZ { get; set; }
        /// <summary>Raised once per event after the desk applied it.</summary>
        public event Action<SofEvent> Happened;

        public int ActiveCount
        {
            get { int n = 0; for (int i = 0; i < Teams.Length; i++) if (Teams[i].Active) n++; return n; }
        }

        private SofTeam Get(int slot) => slot >= 0 && slot < Teams.Length && Teams[slot].Active ? Teams[slot] : null;

        /// <summary>A held building lasts until this mission second (a FOB keeps its building for the whole 20 minutes). False when the id is not held.</summary>
        public bool ExtendHeld(int id, float until)
        {
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i].Id != id) continue;
                HeldBuilding h = held[i];
                h.Until = Math.Max(h.Until, until);
                held[i] = h;
                return true;
            }
            return false;
        }

        public bool TryTarget(int id, out SofTarget target)
        {
            for (int i = 0; i < visible.Count; i++) if (visible[i].Id == id) { target = visible[i]; return true; }
            target = default; return false;
        }

        // ---- Fog ----------------------------------------------------------------------------------

        /// <summary>Rebuilds the visible target list: a sighted target is stamped, a destroyed one is lost, a target is listed while visible (fresh sighting or engaged by a team), capped.</summary>
        public void Refresh(IReadOnlyList<SofSeed> observed)
        {
            float now = ports.Now;
            candidates.Clear(); engaged.Clear(); seen.Clear(); listed.Clear();
            for (int i = 0; i < Teams.Length; i++) if (Teams[i].Active && Teams[i].TargetId != 0 && Teams[i].Mission != MissionKind.None) engaged.Add(Teams[i].TargetId);
            for (int i = 0; observed != null && i < observed.Count; i++)
            {
                SofSeed o = observed[i];
                // An id exists only for a target the faction can see (a fresh sighting) or a team is engaged on: the table stays small and never counts the enemy's hidden units.
                int id = Ids.Find(o.Kind, o.Sub, o.Key);
                if (o.Gone) { if (id != 0) { seen.Add(id); TargetLost(id); } continue; }
                if (id == 0)
                {
                    if (!o.Sighted) continue;
                    id = Ids.GetOrAdd(o.Kind, o.Sub, o.Key);
                    if (id == 0) continue;
                }
                seen.Add(id);
                if (o.Sighted) Reveal.Note(id, now);
                if (!Reveal.Visible(id, now, engaged.Contains(id))) continue;
                listed.Add(id);
                candidates.Add(new SofTarget(id, o.Kind, o.Sub, o.Key, o.X, o.Z, o.Front));
            }
            foreach (int id in engaged) if (!seen.Contains(id)) TargetLost(id);
            Select();
            Recycle();
        }

        /// <summary>Releases every id that is neither listed this refresh nor engaged by a team (a gone or no longer visible target), so ids are reused instead of exhausting the table.</summary>
        private void Recycle()
        {
            Ids.CopyIds(idScratch);
            for (int i = 0; i < idScratch.Count; i++)
            {
                int id = idScratch[i];
                if (listed.Contains(id) || IsEngaged(id)) continue;
                Ids.Release(id);
                Reveal.Forget(id);
            }
        }

        private bool IsEngaged(int id)
        {
            for (int i = 0; i < Teams.Length; i++) if (Teams[i].Active && Teams[i].TargetId == id && Teams[i].Mission != MissionKind.None) return true;
            return false;
        }

        /// <summary>The unit keys the runtime must keep resolving whatever the fog says: every team's mission target and every held building.</summary>
        public void CollectKeep(HashSet<uint> into)
        {
            into.Clear();
            for (int i = 0; i < Teams.Length; i++)
            {
                if (Teams[i].Active && Teams[i].Mission != MissionKind.None && Teams[i].Mission != MissionKind.Recon && Teams[i].TargetKey != 0) into.Add(Teams[i].TargetKey);
                if (Teams[i].Active && Teams[i].ExemptKey != 0) into.Add(Teams[i].ExemptKey); // the exposure exemption outlives the mission (EXFIL) and must not lapse with the fog
            }
            for (int i = 0; i < held.Count; i++) into.Add(held[i].Key);
        }

        private void Select()
        {
            visible.Clear();
            for (int i = 0; i < candidates.Count; i++) if (engaged.Contains(candidates[i].Id) && visible.Count < MaxVisible) visible.Add(candidates[i]);
            candidates.Sort((a, b) => a.Front != b.Front ? a.Front.CompareTo(b.Front) : a.Id.CompareTo(b.Id));
            for (int i = 0; i < candidates.Count && visible.Count < MaxVisible; i++)
                if (!engaged.Contains(candidates[i].Id)) visible.Add(candidates[i]);
        }

        /// <summary>The target of a mission vanished (its unit died): the mission ends where the team stands.</summary>
        public void TargetLost(int id)
        {
            for (int i = 0; i < Teams.Length; i++)
            {
                SofTeam t = Teams[i];
                if (!t.Active || t.TargetId != id || t.Mission == MissionKind.None) continue;
                if (t.Mission == MissionKind.Lase && t.LaseActive) EndLase(t);
                bool onSite = t.State == TeamState.OnSite;
                if (t.MissionCost > 0) { ports.Refund(t.MissionPayer, t.MissionCost); t.MissionCost = 0; } // lost before the team arrived: the order is refunded
                t.Mission = MissionKind.None; t.TargetId = 0;
                if (onSite) t.State = TeamState.Ready;
            }
        }

        // ---- Verbs --------------------------------------------------------------------------------

        public SofResult Raise(ulong op)
        {
            float now = ports.Now;
            float cx, cz;
            AnchorHealth health = AnchorHealth.Live;
            if (FobActive) { cx = FobX; cz = FobZ; }
            else
            {
                if (Camps.Count == 0) return new SofResult(SofOutcome.NoCamp);
                if (!Camps.TryBest(out _, out cx, out cz, out health)) return new SofResult(SofOutcome.CampDown);
            }
            if (ActiveCount >= SofRules.TeamCap(ports.Humans, ports.Fob)) return new SofResult(SofOutcome.TeamCap);
            int slot = -1;
            for (int i = 0; i < Teams.Length && slot < 0; i++) if (!Teams[i].Active) slot = i;
            if (slot < 0) return new SofResult(SofOutcome.TeamCap);
            SofOutcome paid = ports.TrySpend(op, SofRules.RaiseCost, out int detail);
            if (paid != SofOutcome.None) return new SofResult(paid, 0, 0, detail);
            float seconds = SofRules.RaiseSeconds * CampRules.RaiseFactor(health);
            Teams[slot] = new SofTeam
            {
                Slot = slot, Active = true, Raiser = op, State = TeamState.Raising, X = cx, Z = cz, HomeX = cx, HomeZ = cz, RaiseEndsAt = now + seconds, Ammo = 100f
            };
            return new SofResult(SofOutcome.Raised, slot, SofRules.RaiseCost, (int)Math.Ceiling(seconds));
        }

        /// <summary>Starts a mission. RECON takes a point (<paramref name="px"/>, <paramref name="pz"/>); every other kind takes an opaque target id.</summary>
        public SofResult Send(ulong op, int slot, MissionKind kind, int targetId, float px, float pz)
        {
            float now = ports.Now;
            SofTeam t = Get(slot);
            if (t == null) return new SofResult(SofOutcome.NoTeam);
            SofResult gate = Gate(t, now, true);
            if (gate.Outcome != SofOutcome.None) return gate;
            if (t.State != TeamState.Ready) return new SofResult(SofOutcome.NotReady, slot);
            if (t.Ammo + 0.01f < AmmoPerMission) return new SofResult(SofOutcome.NoAmmo, slot);
            TargetKind tk = TargetKind.Ground; AnchorSub sub = AnchorSub.Uplink; uint key = 0; float tx, tz; int id = 0;
            if (kind == MissionKind.Recon)
            {
                if (!Finite(px) || !Finite(pz) || Math.Abs(px) > 2000000f || Math.Abs(pz) > 2000000f) return new SofResult(SofOutcome.OutOfTheater, slot);
                tx = px; tz = pz;
            }
            else
            {
                if (!TryTarget(targetId, out SofTarget target) || !SofRules.Valid(kind, target.Kind, target.Sub)) return new SofResult(SofOutcome.NoTarget, slot);
                tk = target.Kind; sub = target.Sub; key = target.Key; tx = target.X; tz = target.Z; id = target.Id;
            }
            bool exploit = kind != MissionKind.Recon && SofRules.Exploit(tk, sub);
            int cost = SofRules.CostOf(kind, exploit);
            SofOutcome paid = ports.TrySpend(op, cost, out int detail);
            if (paid != SofOutcome.None) return new SofResult(paid, slot, 0, detail);
            t.MissionCost = cost; t.MissionPayer = op;
            t.Mission = kind; t.TargetId = id; t.TargetKind = tk; t.TargetSub = sub; t.TargetKey = key; t.TargetX = tx; t.TargetZ = tz; t.Exploit = exploit;
            t.ExemptKey = kind != MissionKind.Recon && (tk == TargetKind.Ground || tk == TargetKind.Anchor) ? key : 0;
            t.DestX = tx; t.DestZ = tz; t.HasDest = true; t.HoldOn = false; t.PushOn = false;
            t.Insert = t.Carried ? Insertion.Helicopter : Insertion.Ground;
            t.State = TeamState.Moving; t.LiftWaiting = false;
            Queue(SofEventKind.Departed, t);
            return new SofResult(SofOutcome.Sent, slot, cost);
        }

        public SofResult Divert(ulong op, int slot, float x, float z)
        {
            float now = ports.Now;
            SofTeam t = Get(slot);
            if (t == null) return new SofResult(SofOutcome.NoTeam);
            SofResult gate = Gate(t, now, false);
            if (gate.Outcome != SofOutcome.None) return gate;
            if (!Finite(x) || !Finite(z) || Math.Abs(x) > 2000000f || Math.Abs(z) > 2000000f) return new SofResult(SofOutcome.OutOfTheater, slot);
            if (t.State == TeamState.Raising || t.State == TeamState.Recovering) return new SofResult(SofOutcome.BadOrder, slot);
            ClearMission(t);
            t.DestX = x; t.DestZ = z; t.HasDest = true; t.HoldOn = false; t.LiftWaiting = false; t.Wounded = false;
            t.Insert = t.Carried ? Insertion.Helicopter : Insertion.Ground;
            t.State = TeamState.Moving;
            Queue(SofEventKind.Departed, t);
            return new SofResult(SofOutcome.Diverted, slot);
        }

        public SofResult Order(ulong op, int slot, TeamVerb verb)
        {
            float now = ports.Now;
            SofTeam t = Get(slot);
            if (t == null) return new SofResult(SofOutcome.NoTeam);
            SofResult gate = Gate(t, now, false);
            if (gate.Outcome != SofOutcome.None) return gate;
            bool moving = t.State == TeamState.Moving || t.State == TeamState.Returning;
            switch (verb)
            {
                case TeamVerb.Push:
                    if (!moving) return new SofResult(SofOutcome.BadOrder, slot);
                    t.PushOn = !t.PushOn; if (t.PushOn) t.HoldOn = false;
                    return new SofResult(SofOutcome.Ordered, slot);
                case TeamVerb.Hold:
                    if (!moving && t.State != TeamState.OnSite) return new SofResult(SofOutcome.BadOrder, slot);
                    t.HoldOn = !t.HoldOn; if (t.HoldOn) t.PushOn = false;
                    return new SofResult(SofOutcome.Ordered, slot);
                case TeamVerb.Exfil:
                    if (t.State == TeamState.Raising || t.State == TeamState.Recovering || t.Carried) return new SofResult(SofOutcome.BadOrder, slot);
                    float d = HomeDistance(t, out float bx, out float bz);
                    if (d <= 50f && t.State == TeamState.Ready) return new SofResult(SofOutcome.BadOrder, slot);
                    ClearMission(t);
                    t.DestX = bx; t.DestZ = bz; t.HasDest = true; t.HoldOn = false; t.LiftWaiting = false;
                    t.State = TeamState.Returning;
                    Queue(SofEventKind.Departed, t);
                    return new SofResult(SofOutcome.Ordered, slot);
                case TeamVerb.Lift:
                    if (t.State != TeamState.Ready || t.Carried) return new SofResult(SofOutcome.BadOrder, slot);
                    t.LiftWaiting = !t.LiftWaiting; t.LiftUntil = now + LiftWaitSeconds;
                    return new SofResult(SofOutcome.Ordered, slot);
                case TeamVerb.Cancel:
                    if (t.State == TeamState.Raising || t.State == TeamState.Recovering) return new SofResult(SofOutcome.BadOrder, slot);
                    ClearMission(t); t.LiftWaiting = false; t.HasDest = false; t.HoldOn = false; t.PushOn = false;
                    if (t.State == TeamState.Moving || t.State == TeamState.OnSite || t.State == TeamState.Returning) t.State = TeamState.Ready;
                    return new SofResult(SofOutcome.Ordered, slot);
                default:
                    return new SofResult(SofOutcome.BadOrder, slot);
            }
        }

        /// <summary>Pinned and wounded teams take no orders except the ones that make sense (exfil on a wounded team is the return itself).</summary>
        private SofResult Gate(SofTeam t, float now, bool strict)
        {
            if (t.State == TeamState.Pinned) return new SofResult(SofOutcome.Pinned, t.Slot);
            if (t.State == TeamState.Raising && strict) return new SofResult(SofOutcome.Raising, t.Slot, 0, (int)Math.Ceiling(Math.Max(0f, t.RaiseEndsAt - now)));
            if (t.State == TeamState.Recovering && strict) return new SofResult(SofOutcome.Wounded, t.Slot, 0, (int)Math.Ceiling(Math.Max(0f, t.RecoverUntil - now)));
            if (t.State == TeamState.Lost) return new SofResult(SofOutcome.NoTeam);
            return default;
        }

        private void ClearMission(SofTeam t)
        {
            if (t.Mission == MissionKind.Lase && t.LaseActive) EndLase(t);
            t.Mission = MissionKind.None; t.TargetId = 0; t.Exploit = false; t.MissionCost = 0;
        }

        private void EndLase(SofTeam t)
        {
            t.LaseActive = false;
            ports.LaseEnd(t.Slot, t.TargetKey);
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Distance to the nearest base a team can return to: a standing camp or a held building (falls back to where it was raised).</summary>
        private float HomeDistance(SofTeam t, out float bx, out float bz)
        {
            float best = Camps.NearestStanding(t.X, t.Z, out bx, out bz);
            for (int i = 0; i < held.Count; i++)
            {
                float d = SofRules.Distance(t.X, t.Z, held[i].X, held[i].Z);
                if (d < best) { best = d; bx = held[i].X; bz = held[i].Z; }
            }
            if (float.IsInfinity(best)) { bx = t.HomeX; bz = t.HomeZ; best = SofRules.Distance(t.X, t.Z, bx, bz); }
            return best;
        }

        // ---- Time ---------------------------------------------------------------------------------

        /// <summary>Call at about 1 Hz.</summary>
        public void Tick()
        {
            float now = ports.Now;
            float dt = lastTick < 0f ? 1f : Math.Max(0f, Math.Min(SofRules.MaxAdvanceSeconds, now - lastTick));
            lastTick = now;
            for (int i = 0; i < Teams.Length; i++) if (Teams[i].Active) Advance(Teams[i], now, dt);
            TickHeld(now);
            Pump();
        }

        private void Advance(SofTeam t, float now, float dt)
        {
            switch (t.State)
            {
                case TeamState.Raising:
                    if (now >= t.RaiseEndsAt) { t.State = TeamState.Ready; Queue(SofEventKind.Raised, t); }
                    return;
                case TeamState.Recovering:
                    if (now >= t.RecoverUntil) { t.State = TeamState.Ready; t.Wounded = false; }
                    return;
                case TeamState.Lost:
                    if (now >= t.LostAt + LostLingerSeconds) t.Active = false;
                    return;
            }
            if (t.LiftWaiting && now >= t.LiftUntil) t.LiftWaiting = false;
            if (t.Carried)
            {
                if (now - t.CarrierSeenAt >= CarrierLostSeconds) Lose(t, now);
                return; // the helicopter carries it; position and the drop come from NoteHeli
            }
            if (t.State == TeamState.Ready && NearBase(t.X, t.Z)) t.Ammo = Math.Min(100f, t.Ammo + AmmoRefillPerSecond * dt);
            SofScene scene = ports.Scene(t.X, t.Z);
            bool moving = t.State == TeamState.Moving || t.State == TeamState.Returning;
            int exposers = scene.Within2000;
            // The mission target's own unit never adds exposure, on the approach, on site and on the way home (fun over realism: an unaided team must be able to reach
            // a lightly defended target and get out). The target key stays on the team after EXFIL; once the unit is dead it is no longer counted by the host anyway.
            if (t.ExemptKey != 0)
            {
                if (!ports.TargetAlive(t.TargetKind, t.TargetSub, t.ExemptKey)) t.ExemptKey = 0; // truly dead (the host keeps the key resolving whatever the fog says)
                else if (exposers > 0 && SofRules.Distance(t.X, t.Z, t.TargetX, t.TargetZ) <= SofRules.ExposureRadius) exposers--;
            }
            t.Exposure = Math.Max(0f, Math.Min(100f, t.Exposure + SofRules.ExposureDelta(exposers, scene.Stared, t.PushOn && moving, t.HoldOn, dt)));
            t.Odds = SofRules.Odds(t.Exposure, scene.Armored1000, t.Insert == Insertion.Helicopter, t.Exploit, ports.CyberNear(t.X, t.Z));
            if (t.State == TeamState.Pinned) { AdvancePinned(t, now); return; }
            if (t.Exposure >= SofRules.PinExposure && (moving || t.State == TeamState.OnSite))
            {
                t.PrePin = t.State; t.State = TeamState.Pinned; t.PinDeadline = now + SofRules.PinnedLostSeconds; t.CoverUntil = 0f; t.CoverPilot = 0; t.CoverKillers.Clear();
                ports.PostCover(t.Slot, t.X, t.Z);
                Queue(SofEventKind.Pinned, t);
                return;
            }
            if (moving) { Step(t, now, dt); return; }
            if (t.State == TeamState.OnSite) OnSite(t, now);
        }

        private void AdvancePinned(SofTeam t, float now)
        {
            if (t.Exposure <= SofRules.UnpinExposure)
            {
                t.State = t.PrePin == TeamState.OnSite ? TeamState.OnSite : t.PrePin == TeamState.Returning ? TeamState.Returning : TeamState.Moving;
                // Cover pays only the claimant who killed something near the team while it was pinned; other relief unpins it for nothing.
                if (t.CoverPilot != 0) { if (t.CoverKillers.Contains(t.CoverPilot)) ports.Pay(t.CoverPilot, SofRules.CoverPay); t.CoverPilot = 0; }
                t.CoverKillers.Clear();
                if (t.State == TeamState.OnSite) t.OnSiteEnd += Math.Max(0f, now - (t.PinDeadline - SofRules.PinnedLostSeconds));
                Queue(SofEventKind.Unpinned, t);
                return;
            }
            if (now >= t.PinDeadline && now >= t.CoverUntil) Lose(t, now);
        }

        private void Step(SofTeam t, float now, float dt)
        {
            if (t.HoldOn || !t.HasDest) return;
            float speed = SofRules.SpeedMetresPerSecond * (t.PushOn ? SofRules.PushSpeedFactor : 1f);
            float dist = SofRules.Distance(t.X, t.Z, t.DestX, t.DestZ), step = speed * dt;
            if (dist > step + SofRules.ArrivalMetres)
            {
                float k = step / dist;
                t.X += (t.DestX - t.X) * k; t.Z += (t.DestZ - t.Z) * k;
                return;
            }
            t.X = t.DestX; t.Z = t.DestZ; t.HasDest = false; t.PushOn = false;
            if (t.State == TeamState.Returning)
            {
                if (t.Wounded) { t.State = TeamState.Recovering; t.RecoverUntil = now + SofRules.WoundedSeconds; }
                else t.State = TeamState.Ready;
                t.ExemptKey = 0;
                Queue(SofEventKind.Home, t);
                return;
            }
            if (t.Mission != MissionKind.None)
            {
                t.State = TeamState.OnSite; t.OnSiteStart = now; t.OnSiteEnd = now + SofRules.OnSiteSeconds(t.Mission);
                t.MissionCost = 0; // on site: the fee is spent
                Queue(SofEventKind.OnSite, t);
                if (t.Mission == MissionKind.Lase && !BeginLase(t))
                {
                    // The designation could not be placed (the unit does not resolve): no LASE state, no post; the mission ends as failed.
                    ClearMission(t);
                    t.State = TeamState.Ready;
                    Queue(SofEventKind.Failed, t, MissionKind.Lase);
                }
                return;
            }
            t.State = TeamState.Ready;
        }

        private bool BeginLase(SofTeam t)
        {
            if (!ports.LaseBegin(t.Slot, t.TargetKey, t.TargetX, t.TargetZ)) { t.LaseActive = false; return false; }
            t.LaseActive = true;
            ports.PostLase(t.Slot, t.TargetX, t.TargetZ);
            return true;
        }

        private void OnSite(SofTeam t, float now)
        {
            if (t.Mission == MissionKind.None) { t.State = TeamState.Ready; return; }
            if (t.Mission != MissionKind.Recon && !ports.TargetAlive(t.TargetKind, t.TargetSub, t.TargetKey)) { Finish(t, false, false); return; } // target gone: ends quietly, never a success
            if (now < t.OnSiteEnd) return;
            if (t.Mission == MissionKind.Lase) { Finish(t, true, true); return; }
            bool ok = ports.Roll(t.Odds <= 0 ? SofRules.Odds(t.Exposure, 0, t.Insert == Insertion.Helicopter, t.Exploit, ports.CyberNear(t.X, t.Z)) : t.Odds);
            if (!ok)
            {
                t.Ammo = Math.Max(0f, t.Ammo - AmmoPerMission);
                MissionKind failed = t.Mission;
                bool lost = t.Exposure >= SofRules.LostExposure;
                ClearMission(t);
                Queue(SofEventKind.Failed, t, failed);
                if (lost) { Lose(t, now); return; }
                t.Wounded = true;
                HomeDistance(t, out float bx, out float bz);
                t.DestX = bx; t.DestZ = bz; t.HasDest = true; t.State = TeamState.Returning;
                return;
            }
            Finish(t, true, true);
        }

        /// <summary>The mission ends: <paramref name="effect"/> applies it (success), otherwise it just stops (target gone).</summary>
        private void Finish(SofTeam t, bool success, bool effect)
        {
            MissionKind kind = t.Mission;
            float seconds = t.Exploit ? SofRules.ExploitDurationFactor : 1f;
            if (effect && success)
            {
                switch (kind)
                {
                    case MissionKind.Recon: ports.Reveal(t.TargetX, t.TargetZ, SofRules.ReconRadius, SofRules.ReconRevealSeconds * seconds); break;
                    case MissionKind.Sabotage: ports.Sabotage(t.TargetSub, t.TargetKey); break;
                    case MissionKind.Tap: ports.Tap(t.TargetKind, t.TargetKey, SofRules.TapSeconds * seconds); break;
                    case MissionKind.Seize:
                        if (held.Count < SofRules.HeldCap && ports.BuildingAlive(t.TargetKey))
                        {
                            held.Add(new HeldBuilding { Id = ++nextHeldId, Key = t.TargetKey, X = t.TargetX, Z = t.TargetZ, Until = ports.Now + SofRules.HeldSeconds });
                            Queue(SofEventKind.Seized, t, kind);
                        }
                        break;
                }
            }
            if (effect && kind != MissionKind.Lase) t.Ammo = Math.Max(0f, t.Ammo - AmmoPerMission);
            ClearMission(t);
            t.State = TeamState.Ready;
            if (success) Queue(SofEventKind.Success, t, kind);
        }

        private void Lose(SofTeam t, float now)
        {
            ClearMission(t); t.ExemptKey = 0;
            t.State = TeamState.Lost; t.LostAt = now; t.Carried = false; t.LiftWaiting = false; t.HasDest = false; t.CarrierId = 0;
            Queue(SofEventKind.Lost, t);
        }

        private bool NearBase(float x, float z)
        {
            if (Camps.NearestStanding(x, z, out _, out _) <= BaseRadius) return true;
            for (int i = 0; i < held.Count; i++) if (SofRules.Distance(x, z, held[i].X, held[i].Z) <= BaseRadius) return true;
            return false;
        }

        private void TickHeld(float now)
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                HeldBuilding h = held[i];
                bool gone = now >= h.Until || !ports.BuildingAlive(h.Key);
                if (!gone)
                {
                    SofScene scene = ports.Scene(h.X, h.Z);
                    if (scene.Within300 > 0) { if (h.RetakeSince <= 0f) h.RetakeSince = now; gone = now - h.RetakeSince >= SofRules.HeldRetakeSeconds; }
                    else h.RetakeSince = 0f;
                    held[i] = h;
                }
                if (gone) { held.RemoveAt(i); pending.Add(new SofEvent(SofEventKind.Retaken, 0, MissionKind.Seize)); }
            }
        }

        // ---- Cover and lift ------------------------------------------------------------------------

        /// <summary>An enemy ground unit died at (x, z): each pinned team within 2 km is relieved and its lost-timer extended.</summary>
        public int CoverKill(float x, float z)
        {
            float now = ports.Now;
            int n = 0;
            for (int i = 0; i < Teams.Length; i++)
            {
                SofTeam t = Teams[i];
                if (!t.Active || t.State != TeamState.Pinned || SofRules.Distance(t.X, t.Z, x, z) > SofRules.CoverRadius) continue;
                t.Exposure = Math.Max(0f, t.Exposure - SofRules.CoverKillRelief);
                t.PinDeadline = Math.Max(t.PinDeadline, now + SofRules.CoverExtendSeconds);
                n++;
            }
            return n;
        }

        /// <summary>A pilot killed an enemy ground unit at (x, z): each pinned team within 2 km remembers it, so a COVER claim by that pilot pays when the team breaks free.</summary>
        public int CoverKillBy(ulong pilot, float x, float z)
        {
            if (pilot == 0) return 0;
            int n = 0;
            for (int i = 0; i < Teams.Length; i++)
            {
                SofTeam t = Teams[i];
                if (!t.Active || t.State != TeamState.Pinned || SofRules.Distance(t.X, t.Z, x, z) > SofRules.CoverRadius) continue;
                if (t.CoverKillers.Count < 8) t.CoverKillers.Add(pilot);
                n++;
            }
            return n;
        }

        /// <summary>A pilot claimed the COVER post of a team: the lost-timer extends and the pilot is paid when the team breaks free. False when the team is not pinned.</summary>
        public bool CoverClaimed(int slot, ulong pilot)
        {
            SofTeam t = Get(slot);
            if (t == null || t.State != TeamState.Pinned) return false;
            float now = ports.Now;
            t.CoverUntil = now + 90f; t.PinDeadline = Math.Max(t.PinDeadline, now + 90f); t.CoverPilot = pilot;
            return true;
        }

        public void BeginHeliPass() => heliSeen.Clear();

        public void EndHeliPass()
        {
            if (landedSince.Count == 0) return;
            var gone = new List<int>();
            foreach (var pair in landedSince) if (!heliSeen.Contains(pair.Key)) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) landedSince.Remove(gone[i]);
        }

        /// <summary>One player helicopter this pass. A team waiting for a lift boards after the helicopter sits within 150 m for 10 s; a carried team lands after 10 s within 300 m of its destination or a base.</summary>
        public void NoteHeli(int heliId, ulong pilot, float x, float z, bool landed)
        {
            if (heliId == 0 || heliSeen.Count >= MaxHelis) return;
            float now = ports.Now;
            heliSeen.Add(heliId);
            if (!landed) { landedSince.Remove(heliId); }
            else if (!landedSince.ContainsKey(heliId)) landedSince[heliId] = now;
            bool dwelled = landed && landedSince.TryGetValue(heliId, out float since) && now - since >= SofRules.LiftDwellSeconds;
            for (int i = 0; i < Teams.Length; i++)
            {
                SofTeam t = Teams[i];
                if (!t.Active) continue;
                if (t.Carried && t.CarrierId == heliId)
                {
                    t.X = x; t.Z = z; t.CarrierSeenAt = now;
                    if (!landed) t.CarrierFlew = true; // a team boards on the ground and is only set down after the helicopter has flown
                    if (!dwelled || !t.CarrierFlew) continue;
                    bool toDest = t.HasDest && SofRules.Distance(x, z, t.DestX, t.DestZ) <= SofRules.LiftDropMetres;
                    float homeD = HomeDistance(t, out _, out _);
                    bool atBase = homeD <= SofRules.LiftDropMetres;
                    if (!toDest && !atBase) continue;
                    t.Carried = false; t.CarrierId = 0; t.CarrierFlew = false; t.Exposure = 0f; t.Insert = Insertion.Helicopter;
                    if (toDest && !(atBase && t.Mission == MissionKind.None)) ports.Pay(pilot, SofRules.LiftPay);
                    else if (SofRules.Distance(t.BoardX, t.BoardZ, x, z) >= SofRules.LiftMinPayMetres) ports.Pay(pilot, SofRules.ExtractionPay); // no pay for a hop shorter than 2 km
                    if (!t.HasDest) t.State = TeamState.Ready;
                    Queue(SofEventKind.LiftDown, t);
                }
                else if (t.LiftWaiting && !t.Carried && t.State == TeamState.Ready && dwelled && SofRules.Distance(x, z, t.X, t.Z) <= SofRules.LiftPickupMetres)
                {
                    t.BoardX = t.X; t.BoardZ = t.Z;
                    t.Carried = true; t.CarrierFlew = false; t.CarrierId = heliId; t.Carrier = pilot; t.LiftWaiting = false; t.CarrierSeenAt = now; t.Insert = Insertion.Helicopter;
                    Queue(SofEventKind.LiftUp, t);
                }
            }
        }

        // ---- Events -------------------------------------------------------------------------------

        private void Queue(SofEventKind kind, SofTeam t, MissionKind? mission = null)
        {
            if (pending.Count < MaxEventsPending) pending.Add(new SofEvent(kind, t.Slot, mission ?? t.Mission, t.MissionPayer));
        }

        private void Pump()
        {
            drained.Clear(); drained.AddRange(pending); pending.Clear();
            for (int i = 0; i < drained.Count; i++) Happened?.Invoke(drained[i]);
        }

        /// <summary>The scene ended: every team is gone.</summary>
        public void Reset()
        {
            for (int i = 0; i < Teams.Length; i++) Teams[i] = new SofTeam { Slot = i };
            visible.Clear(); held.Clear(); pending.Clear(); Ids.Clear(); Reveal.Clear(); landedSince.Clear(); heliSeen.Clear(); Camps.Clear();
            lastTick = -1f;
        }

        public bool IsLasing(int slot) { SofTeam t = Get(slot); return t != null && t.LaseActive && t.State == TeamState.OnSite; }

        /// <summary>The lased target point of the first team holding a laser on one (the AIM: TEAM source), if any.</summary>
        public bool TryLase(out int slot, out float x, out float z)
        {
            for (int i = 0; i < Teams.Length; i++)
                if (Teams[i].Active && Teams[i].LaseActive && Teams[i].State == TeamState.OnSite) { slot = i; x = Teams[i].TargetX; z = Teams[i].TargetZ; return true; }
            slot = -1; x = z = 0f;
            return false;
        }
    }
}

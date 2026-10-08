using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// WATCH OFFICER OVERLORD for CYBER and SOF, and the AI-controlled factions (spec section 4), the host adapter. It runs only on the host and does nothing a human could not: it works the
    /// same <see cref="CyberDesk"/> and <see cref="SofDesk"/> a player does, as the reserved WATCH OFFICER identity. It spends nothing (no allocation, no faction funds): the fronts are funded only through programmes. All
    /// decisions live in the pure brains (<see cref="CyberWatchBrain"/>, <see cref="SofWatchBrain"/>); this class gathers the few facts the desks do not hold (the
    /// humans, the pilots, the board room, the census) and carries the plans out. Fog of war holds: the brains see only the desks' own lists, which are real sightings.
    /// <list type="bullet">
    /// <item>OPS FRONTS S2: a faction with humans is directed always, at the normal pace and only when WATCH OFFICER is on. Its choices follow the front's directive and focus pin (<c>DirectorBias</c>); a human's live intrusion or team is never fought (an engaged node, a team another raised, one slot kept free).</item>
    /// <item>A faction with no humans is run by the same brains at one domain action every 30 s (one limiter for both domains) when AI FACTIONS is on; the FrontService queues its programmes. CYBER and SOF are
    /// staffed for it, SPACE is not (an AI-only faction is never staffed, see <c>SpaceWatchOfficer.Tick</c>): a SPACE scan stamps the AI faction's native tracking with the human side's bases, a CYBER hop or a SOF
    /// mission does not (a held node is an effect, a sabotage destroys an anchor the faction has revealed), and the one SOF path that does stamp it, RECON, is off for it.</item>
    /// <item>Every action it takes is logged with its reason string and kept in a three-row ring that reaches the faction's own console through the OPERATIONS mirror.</item>
    /// </list>
    /// </summary>
    internal sealed class OverlordService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumPilots = 32;
        private const float TickSeconds = 0.5f, PilotRefreshSeconds = 2f;

        private SupportManager manager;
        private SpaceService space;
        private CyberService cyber;
        private SofService sof;
        private readonly Dictionary<FactionHQ, FactionWatch> runs = new Dictionary<FactionHQ, FactionWatch>();
        private float nextTick, nextWarning;

        public void Configure(SupportManager support, SpaceService spaceService, CyberService cyberService, SofService sofService, OpsService opsService)
        {
            manager = support; space = spaceService; cyber = cyberService; sof = sofService; // OPS FRONTS S1b: the programmes pay for operations, so OVERLORD no longer plans or funds them (opsService stays in the signature for the module wiring)
        }

        public void ResetForScene()
        {
            foreach (var pair in runs) pair.Value.Reset();
            runs.Clear();
            nextTick = 0f;
        }

        private void OnDestroy() => ResetForScene();

        // ---- Reads for the mirror and the verbs ------------------------------------------------------

        /// <summary>A human of <paramref name="owner"/> did a CYBER or SOF domain verb that the desk accepted: that faction's OVERLORD yields in that domain. Host only.</summary>
        internal void NoteHuman(FactionHQ owner, WatchDomain domain)
        {
            if (!GameAccess.IsServer() || owner == null) return;
            FactionWatch run = RunFor(owner);
            if (run == null) return;
            float now = SupportManager.MissionNow();
            if (domain == WatchDomain.Cyber) run.Cyber.RecordHuman(now); else if (domain == WatchDomain.Sof) run.Sof.RecordHuman(now);
        }

        /// <summary>The last OVERLORD actions of the faction (newest last), for its own console. Nothing about any other faction.</summary>
        internal int CopyLog(FactionHQ owner, List<WatchLogRow> into)
        {
            into.Clear();
            return owner != null && runs.TryGetValue(owner, out FactionWatch run) ? run.Log.CopyTo(into) : 0;
        }

        // ---- Host tick -----------------------------------------------------------------------------

        private void Update()
        {
            SupportSettingsView s = SettingsView();
            if (!s.Enabled)
            {
                if (runs.Count > 0) { foreach (var pair in runs) Release(pair.Value); ResetForScene(); }
                return;
            }
            if (!GameAccess.IsServer() || (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)) return;
            float now = SupportManager.MissionNow();
            if (now < nextTick) return;
            nextTick = now + TickSeconds;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                FactionWatch run = RunFor(hq);
                if (run == null) continue;
                try { Tick(run, s, now); }
                catch (Exception e)
                {
                    // One faction's fault never stops another's; the log says so at most every ten seconds.
                    if (Time.unscaledTime >= nextWarning) { nextWarning = Time.unscaledTime + 10f; Plugin.Logger?.LogWarning("[Support.Overlord] " + hq.name + " step failed: " + e.Message); }
                }
            }
        }

        private readonly struct SupportSettingsView
        {
            public readonly bool Enabled, Watch, Ai, Cyber, Sof, Ops;
            public SupportSettingsView(bool enabled, bool watch, bool ai, bool cyber, bool sof, bool ops) { Enabled = enabled; Watch = watch; Ai = ai; Cyber = cyber; Sof = sof; Ops = ops; }
        }

        private SupportSettingsView SettingsView()
        {
            var s = manager?.Settings;
            return s == null ? default : new SupportSettingsView(s.Enabled.Value, s.WatchOfficerEnabled.Value, s.AiFactionsEnabled.Value, s.CyberEnabled.Value, s.SofEnabled.Value, s.OpsEnabled.Value);
        }

        private void Tick(FactionWatch run, SupportSettingsView s, float now)
        {
            FactionHQ owner = run.Owner;
            int humans = manager.HumanCount(owner);
            bool ai = humans == 0;
            if (!(ai ? s.Ai : s.Watch))
            {
                run.Pacer.SetUrgent(WatchDomain.Cyber, false); run.Pacer.SetUrgent(WatchDomain.Sof, false);
                run.LastSeed = now;
                Release(run); // WATCH OFFICER or AI FACTIONS switched off mid-mission: what OVERLORD holds goes, once per transition
                return;
            }
            run.Released = false;
            if (!(s.Cyber && cyber != null && cyber.HasCyber(owner))) run.Pacer.SetUrgent(WatchDomain.Cyber, false);
            if (!(s.Sof && sof != null && sof.HasSof(owner))) run.Pacer.SetUrgent(WatchDomain.Sof, false);
            FrontService fronts = manager.Fronts;
            if (s.Cyber && cyber != null && cyber.TryDesk(owner, out CyberDesk cyberDesk))
            {
                if (fronts != null) run.Cyber.Bias = fronts.BiasFor(owner, Domain.Fronts.Front.Cyber); // the CYBER directive and focus pin steer the director
                CyberWatchPlan p = run.Cyber.Step(cyberDesk, run.CyberWorld, run.Pacer, ai, now);
                if (p.Action != CyberWatchAction.None) Note(run, WatchDomain.Cyber, p.Code, p.A, p.B, p.Reason);
                else if (p.Why == CyberWatchWhy.Failed) Plugin.Logger?.LogDebug("[Support.Overlord] " + owner.name + " CYBER " + p.Reason + " refused: " + p.Outcome + ".");
            }
            if (s.Sof && sof != null && sof.TryDesk(owner, out SofDesk sofDesk))
            {
                if (fronts != null) run.Sof.Bias = fronts.BiasFor(owner, Domain.Fronts.Front.Sof);
                SofWatchPlan p = run.Sof.Step(sofDesk, run.SofWorld, run.Pacer, ai, now);
                if (p.Action != SofWatchAction.None) Note(run, WatchDomain.Sof, p.Code, p.A, p.B, p.Reason);
                else if (p.Why == SofWatchWhy.Failed) Plugin.Logger?.LogDebug("[Support.Overlord] " + owner.name + " SOF " + p.Reason + " refused: " + p.Outcome + ".");
            }
        }

        /// <summary>
        /// OVERLORD stops: its intrusion is dropped and every team it raised is ordered home, so nothing keeps running (and tracing, and walking into fire) with nobody at the controls.
        /// Once per transition (the flag clears when the faction is staffed again); a refused order (a pinned team) is simply left to the desk's own rules.
        /// </summary>
        private void Release(FactionWatch run)
        {
            if (run.Released) return;
            run.Released = true;
            const ulong me = SpaceContacts.WatchOfficerId;
            FactionHQ owner = run.Owner;
            if (cyber != null && cyber.TryDesk(owner, out CyberDesk cyberDesk) && cyberDesk.Network.Of(me) != null) cyberDesk.Drop(me, 0);
            if (sof != null && sof.TryDesk(owner, out SofDesk sofDesk))
            {
                for (int i = 0; i < sofDesk.Teams.Length; i++)
                {
                    SofTeam t = sofDesk.Teams[i];
                    if (t != null && t.Active && t.Raiser == me && t.State != TeamState.Lost) sofDesk.Order(me, t.Slot, TeamVerb.Exfil);
                }
            }
        }

        private static void Note(FactionWatch run, WatchDomain domain, WatchCode code, int a, int b, string reason)
        {
            run.Log.Add(domain, code, a, b);
            Plugin.Logger?.LogInfo("[Support.Overlord] " + run.Owner.name + " " + WatchWords.Domain(domain) + " · " + reason + ".");
        }

        private FactionWatch RunFor(FactionHQ owner)
        {
            if (runs.TryGetValue(owner, out FactionWatch run)) return run;
            if (runs.Count >= MaximumFactions) return null;
            run = new FactionWatch(this, owner);
            run.LastSeed = SupportManager.MissionNow(); // a run created mid-mission seeds from now, never from time zero
            runs.Add(owner, run);
            return run;
        }

        // ---- One faction -----------------------------------------------------------------------------

        private sealed class FactionWatch
        {
            public readonly FactionHQ Owner;
            public readonly WatchPacer Pacer = new WatchPacer();
            public readonly CyberWatchBrain Cyber = new CyberWatchBrain();
            public readonly SofWatchBrain Sof = new SofWatchBrain();
            public readonly WatchLogRing Log = new WatchLogRing();
            public readonly CyberWorld CyberWorld;
            public readonly SofWorld SofWorld;
            public readonly List<Vector2> Pilots = new List<Vector2>(MaximumPilots);
            public float NextSeed, LastSeed, PilotsAt;
            public bool Released;

            public FactionWatch(OverlordService service, FactionHQ owner)
            {
                Owner = owner;
                CyberWorld = new CyberWorld(service, this);
                SofWorld = new SofWorld(service, this);
            }

            public void Reset()
            {
                Pacer.Reset(); Cyber.Reset(); Sof.Reset(); Log.Clear(); Pilots.Clear();
                NextSeed = LastSeed = PilotsAt = 0f; Released = false;
            }
        }

        /// <summary>The humans, the pilots and the board as OVERLORD's CYBER reads them.</summary>
        private sealed class CyberWorld : ICyberWatchWorld
        {
            private readonly OverlordService service;
            private readonly FactionWatch run;
            public CyberWorld(OverlordService service, FactionWatch run) { this.service = service; this.run = run; }

            public int Humans => service.manager.HumanCount(run.Owner);

            public float NearestPilotMeters(float x, float z) => service.NearestPilot(run, x, z);

            public bool PackageRoom
            {
                get
                {
                    if (Humans <= 0 || service.space == null || !service.space.TryGetDesk(run.Owner, out TaskedDesk desk)) return false;
                    float now = SupportManager.MissionNow();
                    return desk.Board.Count + 1 <= desk.Capacity - TaskedDesk.WatchReserveSlots &&
                        desk.Board.CountWatchOfficer(now, TaskedDomain.Cyber) < TaskedDesk.MaxWatchPostsPerDomain;
                }
            }
        }

        /// <summary>The humans, the recon rule and the weight of a revealed contact as OVERLORD's SOF reads them.</summary>
        private sealed class SofWorld : ISofWatchWorld
        {
            private readonly OverlordService service;
            private readonly FactionWatch run;
            public SofWorld(OverlordService service, FactionWatch run) { this.service = service; this.run = run; }

            public int Humans => service.manager.HumanCount(run.Owner);

            /// <summary>RECON reveals ground units through <c>RpcUpdateTrackingInfo</c>, a native sighting like a SPACE scan: a faction with no humans may not run it.</summary>
            public bool AllowRecon => Humans > 0;

            public bool TryGround(in SofTarget target, out float value, out WatchKind kind)
            {
                value = 0f; kind = WatchKind.Other;
                return service.sof != null && service.sof.TryGroundInfo(run.Owner, target.Key, out value, out kind);
            }

            public bool CyberNear(float x, float z) => service.cyber != null && service.cyber.HoldsNodeNear(run.Owner, x, z, SofRules.RingBoostMetres);
        }

        /// <summary>Metres to the nearest airborne human pilot of the faction, from positions refreshed every two wall seconds (never a per-call scan of the players).</summary>
        private float NearestPilot(FactionWatch run, float x, float z)
        {
            float wall = Time.unscaledTime;
            if (wall >= run.PilotsAt)
            {
                run.PilotsAt = wall + PilotRefreshSeconds;
                run.Pilots.Clear();
                List<Player> players = run.Owner.GetPlayers(false);
                for (int i = 0; players != null && i < players.Count && run.Pilots.Count < MaximumPilots; i++)
                {
                    Player player = players[i];
                    if (player == null || PlayerIdentity.Of(player) == PlayerIdentity.None) continue;
                    Aircraft aircraft = player.Aircraft;
                    // The same "airborne" test the credit trickle uses: radar altitude over the 0.2 m vanilla threshold.
                    if (aircraft == null || aircraft.disabled || float.IsNaN(aircraft.radarAlt) || aircraft.radarAlt <= 0.2f) continue;
                    GlobalPosition p = aircraft.transform.position.ToGlobalPosition();
                    run.Pilots.Add(new Vector2((float)p.x, (float)p.z));
                }
            }
            float best = float.PositiveInfinity;
            for (int i = 0; i < run.Pilots.Count; i++)
            {
                float dx = run.Pilots[i].x - x, dz = run.Pilots[i].y - z;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return float.IsInfinity(best) ? best : Mathf.Sqrt(best);
        }
    }
}

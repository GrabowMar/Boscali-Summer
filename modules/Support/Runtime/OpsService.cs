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
    /// Host-owned OPERATIONS (CYBER/SOF spec 3, core 7a): one <see cref="OpsDesk"/> per faction, the work feed from CYBER hops and SOF mission successes, the counter-trace from an
    /// enemy hop on the data center, the effects of the three operations, the faction view and the host verbs. Everything here is host-only; clients read the OPERATIONS state through
    /// the mirror. A 1 Hz host tick and scene reset like the other services. This file is the desks, the verbs and the view; <c>OpsService.Effects</c> holds the effects.
    /// </summary>
    internal sealed partial class OpsService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8;

        private sealed partial class FactionOps
        {
            public FactionHQ Owner;
            public int Key;
            public OpsDesk Desk;
            public int EventSeq;
            public string Name;
            public readonly List<KeyValuePair<int, OpEvent>> Ring = new List<KeyValuePair<int, OpEvent>>(OpsWire.MaxEvents);
        }

        /// <summary>What the pure desk needs from the live game: the clock, the wallet and the anchor verdicts.</summary>
        private sealed class Ports : IOpsPorts
        {
            private readonly OpsService service;
            private readonly FactionOps faction;
            public Ports(OpsService service, FactionOps faction) { this.service = service; this.faction = faction; }
            public float Now => SupportManager.MissionNow();
            public int Humans => service.manager != null ? service.manager.HumanCount(faction.Owner) : 1;
            public int Owner => faction.Key;
            public OpOutcome TrySpend(ulong op, int cr, out int detail) => service.manager.OpsSpend(faction.Owner, op, cr, out detail);
            public void Refund(ulong op, int cr) => service.manager.OpsRefund(faction.Owner, op, cr);
            public bool AnchorUp(OpKind kind, bool executing) => service.AnchorUp(faction, kind, executing);
            public int NextPingSeq() => ++service.pingSeq;
            public bool InFaction(ulong op) => service.manager != null && service.manager.InFaction(faction.Owner, op);
        }

        private readonly Dictionary<FactionHQ, FactionOps> factions = new Dictionary<FactionHQ, FactionOps>();
        private SupportManager manager;
        private SpaceService space;
        private CyberService cyber;
        private SofService sof;
        private float nextTick, nextWarning;
        private int pingSeq;

        // Optional hooks: each is implemented by the effects file (the call is removed when it is not).
        partial void ResetEffects();
        partial void TickEffects(float now);

        internal static OpsService Active { get; private set; }

        public void Configure(SupportManager support, SpaceService spaceService, CyberService cyberService, SofService sofService)
        {
            manager = support; space = spaceService; cyber = cyberService; sof = sofService;
            Active = this;
            if (cyber != null) cyber.Observed += OnCyber;
            if (sof != null) sof.Observed += OnSof;
        }

        public void ResetForScene()
        {
            foreach (var pair in factions) pair.Value.Desk.Reset();
            ResetEffects();
            factions.Clear();
            nextTick = 0f;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (cyber != null) cyber.Observed -= OnCyber;
            if (sof != null) sof.Observed -= OnSof;
            ResetForScene();
        }

        // ---- Reads ---------------------------------------------------------------------------------

        internal bool TryDesk(FactionHQ owner, out OpsDesk desk)
        {
            desk = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionOps f)) return false;
            desk = f.Desk;
            return true;
        }

        private bool Enabled => manager?.Settings != null && manager.Settings.Enabled.Value && manager.Settings.OpsEnabled.Value;

        /// <summary>The vulnerable anchor of an operation stands: the data center for CYBER operations (and, while an ASAT counts down, its launcher), the held building for the FOB.</summary>
        private bool AnchorUp(FactionOps f, OpKind kind, bool executing)
        {
            // The FOB's anchor is the held building it will be built on: it must still be held (not retaken, not destroyed).
            if (kind == OpKind.Fob) return sof != null && sof.HeldAlive(f.Owner, f.Desk.Slot(OpDomain.Sof).Target.Id);
            if (cyber == null || !cyber.DataCenterUp(f.Owner)) return false;
            if (kind == OpKind.Asat && executing && f.LauncherSpawned && !LauncherUp(f)) return false;
            return true;
        }

        // ---- Host tick -----------------------------------------------------------------------------

        private void Update()
        {
            if (!Enabled)
            {
                if (factions.Count > 0)
                {
                    // Switched off mid-mission: every unfinished operation is withdrawn and every member gets their pledge back.
                    foreach (var pair in factions) pair.Value.Desk.Withdraw();
                    ResetForScene();
                }
                return;
            }
            if (!GameAccess.IsServer() || (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)) return;
            float now = SupportManager.MissionNow();
            if (now < nextTick) return;
            nextTick = now + 1f;
            try { Tick(now); }
            catch (Exception e)
            {
                if (Time.unscaledTime >= nextWarning) { nextWarning = Time.unscaledTime + 10f; Plugin.Logger?.LogWarning("[Support.Ops] Tick failed: " + e.Message); }
            }
        }

        private void Tick(float now)
        {
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || factions.ContainsKey(hq) || factions.Count >= MaximumFactions) continue;
                if ((cyber != null && cyber.HasCyber(hq)) || (sof != null && sof.HasSof(hq))) Establish(hq);
            }
            foreach (var pair in factions) pair.Value.Desk.Tick();
            TickEffects(now);
        }

        private void Establish(FactionHQ hq)
        {
            var f = new FactionOps { Owner = hq, Key = manager.FactionKeyOf(hq) };
            f.Desk = new OpsDesk(new Ports(this, f));
            f.Desk.Happened += e => OnOp(f, e);
            factions.Add(hq, f);
            Plugin.Logger?.LogInfo("[Support.Ops] " + hq.name + ": OPERATIONS ready (" + CyberAnchorSpawner.DescribeLauncher() + ", " + CyberAnchorSpawner.DescribeFobSupply() + ").");
        }

        private void OnOp(FactionOps f, OpEvent e)
        {
            f.Ring.Add(new KeyValuePair<int, OpEvent>(++f.EventSeq, e));
            while (f.Ring.Count > OpsWire.MaxEvents) f.Ring.RemoveAt(0);
            Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " " + OpsWords.Event(e.Kind, e.Op) + ".");
            try
            {
                OpEventReact(f, e);
                if (e.Kind == OpEventKind.Fired) Fire(f, e);
            }
            catch (Exception ex) { Plugin.Logger?.LogWarning("[Support.Ops] " + e.Kind + " effect failed: " + ex.Message); }
        }

        partial void OpEventReact(FactionOps f, OpEvent e);

        // ---- Work and counter-trace ----------------------------------------------------------------

        /// <summary>A CYBER hop success is +8 work toward the faction's open CYBER operation; a hop that takes an enemy data center node counter-traces that faction's operation.</summary>
        private void OnCyber(FactionHQ owner, CyberEvent e, int victimKey)
        {
            if (!factions.TryGetValue(owner, out FactionOps f)) return;
            // OVERLORD's hops feed an AI faction's own operation only: with humans in the faction their operation is theirs (core 7a: no credit for AI-staff-only work).
            bool staffWork = e.Operator == SpaceContacts.WatchOfficerId && manager.HumanCount(owner) > 0;
            if (e.Kind == CyberEventKind.NodeHeld && !staffWork) f.Desk.Work(OpDomain.Cyber, OpsRules.HopWork);
            if (victimKey == 0) return;
            foreach (var pair in factions)
                if (pair.Value.Key == victimKey && pair.Key != owner) pair.Value.Desk.CounterTrace(OpDomain.Cyber);
        }

        /// <summary>A SOF mission success is +40 work toward the faction's open SOF operation.</summary>
        private void OnSof(FactionHQ owner, SofEvent e)
        {
            if (e.Kind != SofEventKind.Success || !factions.TryGetValue(owner, out FactionOps f)) return;
            if (e.Operator == SpaceContacts.WatchOfficerId && manager.HumanCount(owner) > 0) return; // OVERLORD's work never advances a human faction's operation
            f.Desk.Work(OpDomain.Sof, OpsRules.SofWork);
        }

        // ---- Verbs ---------------------------------------------------------------------------------

        /// <summary>
        /// FUND, PLAN or CANCEL for one player. Faction and identity come from the transport-authenticated player, never from the message; an unknown, hidden or
        /// foreign target id answers the same NO TARGET.
        /// </summary>
        internal OpResult Verb(Player player, in SpaceCommand command)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null || !Enabled) return new OpResult(OpOutcome.Unavailable);
            ulong op = PlayerIdentity.Of(player);
            if (op == PlayerIdentity.None) return new OpResult(OpOutcome.Unavailable);
            if (!factions.TryGetValue(player.HQ, out FactionOps f)) return new OpResult(OpOutcome.Offline);
            switch (command.Kind)
            {
                case SpaceCommandKind.OpFund:
                    if (command.Target < 0 || command.Target > 3) return new OpResult(OpOutcome.BadAmount);
                    return f.Desk.Fund(op, (OpDomain)(command.Target & 1), (command.Target & 2) != 0 ? OpsRules.FundLarge : OpsRules.FundSmall);
                case SpaceCommandKind.OpPlan:
                    if (command.Ids == null || command.Ids.Length != 2 || command.Ids[0] < 1 || command.Ids[0] > (int)OpKind.Fob) return new OpResult(OpOutcome.NoOperation);
                    var kind = (OpKind)command.Ids[0];
                    if (!DomainOnline(f, OpsRules.DomainOf(kind))) return new OpResult(OpOutcome.Offline, kind);
                    if (!TryTarget(f, kind, command.Ids[1], out OpTarget target)) return new OpResult(OpOutcome.NoTarget, kind);
                    return f.Desk.Plan(op, kind, target);
                case SpaceCommandKind.OpCancel:
                    if (command.Target < 0 || command.Target > 1) return new OpResult(OpOutcome.NoOperation);
                    return f.Desk.Cancel(op, (OpDomain)command.Target);
                default:
                    return new OpResult(OpOutcome.Unavailable);
            }
        }

        private bool DomainOnline(FactionOps f, OpDomain domain) =>
            domain == OpDomain.Cyber ? cyber != null && cyber.HasCyber(f.Owner) : sof != null && sof.HasSof(f.Owner);

        /// <summary>Resolves a client id against this faction's own fog: ZERO-DAY takes a SAM C2 node it can see.</summary>
        private bool TryTarget(FactionOps f, OpKind kind, int id, out OpTarget target)
        {
            target = default;
            switch (kind)
            {
                case OpKind.Asat:
                    // A satellite class, not a unit: the enemy constellation is never listed, so every valid class is accepted and a strike on a dead one fizzles.
                    if (id < 0 || id >= SpaceRules.BirdCount) return false;
                    FactionHQ victim = ChooseVictim(f, (BirdKind)id);
                    target = new OpTarget(id, 0f, 0f, victim != null ? manager.FactionKeyOf(victim) : 0);
                    return true;
                case OpKind.ZeroDay: return cyber != null && cyber.TryNodeTarget(f.Owner, id, out target);
                case OpKind.Fob: return sof != null && sof.TryHeldTarget(f.Owner, id, out target);
                default: return false;
            }
        }

        // ---- The faction view ----------------------------------------------------------------------

        /// <summary>
        /// Fills the viewer's OPERATIONS view. Own rows and own satellites for the viewer's faction only; another faction's operation appears only as a ping (never a bar or a
        /// contributor); an ASAT in flight is everyone's. False when the faction has no OPERATIONS (<c>Active</c> stays false).
        /// </summary>
        internal bool FillState(Player viewer, OpsStateData into)
        {
            into.Active = false; into.CyberOps = false; into.SofOps = false; into.BirdsDown = 0;
            Array.Clear(into.BirdPercent, 0, into.BirdPercent.Length);
            into.Rows.Clear(); into.Pings.Clear(); into.Events.Clear(); into.Flights.Clear(); into.Log.Clear();
            if (viewer == null || viewer.HQ == null || !Enabled || !factions.TryGetValue(viewer.HQ, out FactionOps f))
            {
                if (viewer != null && viewer.HQ != null) manager.CopyOverlordLog(viewer.HQ, into.Log); // OVERLORD still talks to its own console when OPERATIONS is off
                return false;
            }
            float now = SupportManager.MissionNow();
            ulong id = PlayerIdentity.Of(viewer);
            into.Active = true;
            into.CyberOps = cyber != null && cyber.HasCyber(viewer.HQ);
            into.SofOps = sof != null && sof.HasSof(viewer.HQ);
            for (int d = 0; d < OpsDesk.Slots; d++)
            {
                var domain = (OpDomain)d;
                OpSlot s = f.Desk.Slot(domain);
                if (s.Kind == OpKind.None || s.State == OpState.Idle) continue;
                into.Rows.Add(new OpsRow
                {
                    Domain = domain, Kind = s.Kind, State = s.State, Paused = s.Paused, HasTarget = s.HasTarget, Percent = (byte)s.Percent, WorkPercent = (byte)s.WorkPercent,
                    Goal = s.Goal, TargetId = s.Target.Id, MyCr = (int)Mathf.Round(s.Mine(id)), X = s.Target.X, Z = s.Target.Z,
                    EndsAt = s.State == OpState.Execute ? s.CountdownEnds : s.State == OpState.Done ? s.EffectEnds : 0f
                });
            }
            int viewerKey = f.Key;
            foreach (var pair in factions)
            {
                if (pair.Key == viewer.HQ) continue;
                foreach (OpPing p in pair.Value.Desk.Pings)
                {
                    // A ping reaches only the faction it concerns: the victim (a FOB, which has none, reaches every other faction). A third party learns nothing.
                    if (into.Pings.Count >= OpsWire.MaxPings || now >= p.Until) continue;
                    if (p.Victim != viewerKey && !(p.Victim == 0 && p.Kind == OpKind.Fob)) continue;
                    into.Pings.Add(new OpsPingRow { Kind = p.Kind, Phase = p.Phase, Seq = p.Seq, Until = p.Until, Detail = (byte)p.Detail, Name = pair.Value.Name ??= FactionName(pair.Key) });
                }
            }
            for (int i = 0; i < f.Ring.Count && into.Events.Count < OpsWire.MaxEvents; i++)
            {
                OpEvent e = f.Ring[i].Value;
                into.Events.Add(new OpsEventRow { Seq = f.Ring[i].Key, Kind = e.Kind, Domain = e.Domain, Op = e.Op });
            }
            FillBirdsAndFlights(viewer.HQ, now, into);
            manager.CopyOverlordLog(viewer.HQ, into.Log); // OVERLORD's own actions and their reasons: this faction's console only
            return true;
        }

        private static string FactionName(FactionHQ hq)
        {
            string name = hq != null && hq.faction != null ? hq.faction.factionName : "";
            return string.IsNullOrWhiteSpace(name) ? "ENEMY" : SpaceWire.Clean(name.Trim().ToUpperInvariant(), OpsWire.MaxName);
        }

        partial void FillBirdsAndFlights(FactionHQ viewer, float now, OpsStateData into);
    }
}

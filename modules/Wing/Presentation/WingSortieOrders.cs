using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Runtime;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The Wing module's side of SEND WING: refuses with words, tasks through <see cref="WingOrders"/>, steps a 1 Hz tally. One tasking at a time; an explicit
    /// pilot press only (nothing else calls Send). The only automatic wing order is one FormUp when a tasking ends as Done.</summary>
    internal sealed class WingSortieOrders : IWingSortieOrders, IWingService
    {
        private readonly WingSortieTally tally = new WingSortieTally();
        private Unit target;
        private float x, z;
        private bool own, attacking;
        private float accumulated;

        /// <summary>The live instance, for the WingOrders funnel hook; null outside a mission.</summary>
        public static WingSortieOrders Current { get; private set; }

        /// <summary>FormUps this service issued itself (the sim reads it: exactly one per ended tasking).</summary>
        internal int AutoFormUps { get; private set; }
        /// <summary>Largest horizontal distance of a sendable wingman from the tasked point since Send, metres.</summary>
        internal float OrbitMax { get; private set; }

        /// <summary>The sortie the last tasking was for (the sim reads its status).</summary>
        internal int LastSortieId => tally.SortieId;

        public string Name => "Sorties";
        public void Activate() { Current = this; tally.Clear(); target = null; AutoFormUps = 0; OrbitMax = 0f; accumulated = 0f; }
        public void Deactivate() { tally.Clear(); target = null; if (Current == this) Current = null; }
        public void FixedTick(float dt) { }

        /// <summary>Called by <see cref="WingOrders.Run"/> for every accepted player order: one that changes what the wing does ends the tasking as Recalled (no FormUp).</summary>
        internal static void OnPlayerOrder(WingOrder order)
        {
            WingSortieOrders s = Current;
            if (s == null || s.own || !s.tally.Active || s.tally.Ended || !PlanRules.Holds(order.Kind)) return;
            s.tally.End(WingSortieState.Recalled);
        }

        public string Refusal(in WingSortieOrder order)
        {
            if (WingNet.ClientOnly) return WingSortieRules.HostOnly;
            if (order.Intent == WingSortieIntent.None) return string.IsNullOrEmpty(order.Why) ? WingSortieRules.NoTask : order.Why;
            WingService w = WingService.Instance;
            if (w?.Selection == null) return WingSortieRules.NotReady;
            if (w.Player == null) return WingSortieRules.NotFlying;
            int sendable = 0;
            foreach (WingMember m in w.Members) if (m.Sendable) sendable++;
            if (sendable == 0) return w.Members.Count == 0 ? WingSortieRules.NoWing : WingSortieRules.NoneFlying;
            if (!order.Accepted) return WingSortieRules.NotAccepted;
            if (!float.IsFinite(order.X) || !float.IsFinite(order.Z)) return WingSortieRules.NoPosition;
            if (order.Intent != WingSortieIntent.Destroy) return null;
            if (!new PersistentID { Id = order.TargetId }.TryGetUnit(out Unit u) || u == null || u.disabled) return WingSortieRules.TargetDown;
            FactionHQ hq = w.Player.NetworkHQ;
            if (u.NetworkHQ == null || u.NetworkHQ == hq) return WingSortieRules.NotHostile;
            if (!Tracked(hq, u)) return null;
            if (!w.CanAttackAny(u)) return WingSortieRules.NoWeapon;
            float reach = WingSortieRules.AttackReach(CombatSupervisor.LeashMetres);
            GlobalPosition lead = (w.LeaderUnit ?? w.Player).GlobalPosition(), at = u.GlobalPosition();
            float distance = (float)System.Math.Sqrt((lead.x - at.x) * (lead.x - at.x) + (lead.z - at.z) * (lead.z - at.z));
            return distance > reach ? WingSortieRules.TooFar(distance, reach) : null;
        }

        private static bool Tracked(FactionHQ hq, Unit u) =>
            hq != null && hq.GetTrackingData(u.persistentID) != null && hq.IsTargetPositionAccurate(u, WingService.TargetAccuracyMetres);

        public bool Send(in WingSortieOrder order, out string answer)
        {
            string why = Refusal(in order);
            if (why != null) { answer = why; return false; }
            WingService w = WingService.Instance;
            Unit unit = null;
            bool tracked = false;
            if (order.Intent == WingSortieIntent.Destroy)
            {
                new PersistentID { Id = order.TargetId }.TryGetUnit(out unit);
                tracked = Tracked(w.Player.NetworkHQ, unit);
            }
            bool attack = WingSortieRules.ActionFor(order.Intent, tracked) == SortieAction.Attack;
            WingOrder wingOrder;
            if (attack) wingOrder = new WingOrder { Kind = OrderKind.Attack, Units = new[] { order.TargetId }, Scope = WingScope.Wing };
            else if (order.Intent == WingSortieIntent.Cover)
                wingOrder = WingOrder.Tasked(WingTask.Cap(Waypoint.At(order.X, order.Z), WingSortieRules.CoverRadius), WingScope.Wing);
            else
            {
                WingTask scout = WingTask.Move(Waypoint.At(order.X, order.Z));
                scout.Scout = true;
                wingOrder = WingOrder.Tasked(scout, WingScope.Wing);
            }
            tally.End(WingSortieState.Recalled);                       // the new order replaces the old tasking; no FormUp
            own = true;
            OrderResult r;
            try { r = WingOrders.Run(wingOrder); }
            finally { own = false; }
            if (!r.Accepted) { answer = string.IsNullOrEmpty(r.Reason) ? WingSortieRules.NoTask : r.Reason; return false; }
            tally.Begin(order.SortieId, order.Intent, order.SecondsRemaining > 0f ? w.MissionTime + order.SecondsRemaining : 0f);
            target = unit; x = order.X; z = order.Z; attacking = attack; OrbitMax = 0f; accumulated = 0f;
            answer = order.Intent == WingSortieIntent.Destroy && !tracked ? WingSortieRules.NotTracked : (r.Ack ?? "");
            return true;
        }

        public bool Recall(int sortieId, out string answer)
        {
            if (tally.Active && tally.SortieId == sortieId && tally.End(WingSortieState.Recalled))
            {
                FormUp();
                answer = "Wing recalled: forming up.";
                return true;
            }
            answer = "The wing is not on this sortie.";
            return false;
        }

        public void Release(int sortieId)
        {
            if (tally.Active && tally.SortieId == sortieId && tally.End(WingSortieState.Done)) FormUp();
        }

        public bool TryGetStatus(int sortieId, out WingSortieStatus status)
        {
            status = default;
            if (WingNet.ClientOnly || !tally.Active || tally.SortieId != sortieId) return false;
            status = new WingSortieStatus(sortieId, tally.State, tally.Wingmen, tally.TargetDown, WingSortieRules.Words(tally.State, tally.Intent, tally.Wingmen));
            return true;
        }

        public void Tick(float dt)
        {
            accumulated += dt;
            if (accumulated < 1f) return;
            accumulated = 0f;
            WingService w = WingService.Instance;
            if (w == null || !tally.Active || tally.Ended) return;
            int flying = 0;
            bool onStation = false;
            foreach (WingMember m in w.Members)
            {
                if (!m.Sendable) continue;
                flying++;
                float dx = m.Last.Pos.X - x, dz = m.Last.Pos.Z - z;
                float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (d > OrbitMax) OrbitMax = d;
                onStation |= attacking ? m.Engaged && ReferenceEquals(m.AssignedTarget, target) : d <= WingSortieRules.CoverRadius;
            }
            bool gone = tally.Intent == WingSortieIntent.Destroy && (target == null || target.disabled);
            tally.Step(w.MissionTime, flying, gone, onStation);
            if (tally.Ended && tally.State == WingSortieState.Done) FormUp();
        }

        /// <summary>The single automatic wing order: undoes the pilot's own press once. A refusal (no anchor) is logged by the executor and never retried.</summary>
        private void FormUp()
        {
            AutoFormUps++;
            own = true;
            try { WingOrders.Run(WingOrder.Of(OrderKind.FormUp)); }
            finally { own = false; }
        }
    }
}

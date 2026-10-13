using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// SKYWELL server (v5): the tender state machine of every kit. Stowed -> Deploying -> Tow -> Approach -> Station -> Service ->
    /// Return -> Tow, and Stowing back to Stowed. A friendly receiver that holds the service zone (SkywellZone: within 120 m of the
    /// tow position, under 15 m/s relative speed, under 25 deg of relative roll) for a second is served: the tender flies under
    /// its belly, holds station, and runs the pit stop (skywellPit.cs: fuel from the tanker's stock, one hand-off per round from the
    /// 6-round magazine) through vanilla Refuel / RpcRearm. The receiver outside the zone for more than 2 s breaks the service and the
    /// tender returns to the tow position. Nothing here, or anywhere in SKYWELL, ever moves a receiver: AI receivers that need service
    /// are called in and fly to the hold slot on their own flight loop (SkywellVisuals.SteerAfterPilot). AI tankers deploy on their own.
    /// </summary>
    internal sealed partial class SkywellService : MonoBehaviour, ISceneService
    {
        public const float FuelStockKg = 8000f;
        public const float AiEngageRange = 1500f;   // host: AI receivers astern within this of the zone are called in
        private const float TickInterval = 0.1f;
        private const float ScanInterval = 2f;
        private const float AiMinAltitude = 150f;    // an AI below this is on the ground or on approach: never called in, never steered
        private const float ReceiverCooldown = 30f;
        private const float CappedCooldown = 5f;     // a capped stop (jobs dropped) lets the pilot fly the zone again quickly
        private const float AbortCooldown = 5f;
        internal const float HotBreakCooldown = 8f;  // also the local re-entry lockout the HUD shows
        private const float EngageTimeout = 75f;     // a called-in AI that never makes the zone
        private const float StrikeCooldown = 30f;    // per failed call-in: the same AI is not hijacked again at once
        private const float MaxStrikeCooldown = 120f;
        private const float LostFlagSeconds = 3f;
        private const float RefusedFlagSeconds = 3f;
        private const float RestockRadius = 3000f;   // a landed tanker this close to a friendly field is reloaded

        private sealed class Kit
        {
            public Aircraft Tanker;
            public TenderState State;
            public float StateStart, StateEnd;
            public Aircraft Receiver;              // served (Approach .. Service)
            public Aircraft Caller;                // AI called in: it flies to the hold slot, the tender waits at the tow position
            public float CallEnd;
            public float FuelKg = FuelStockKg;
            public int Rounds = SkywellMagazine.Rounds;
            public float LastToggle = -10f;
            public bool StowRequested, OverSpeed, Reeling;
            public ZoneExit Exit;
            public readonly Dictionary<Aircraft, ZoneHold> InZone = new Dictionary<Aircraft, ZoneHold>(); // per kit: another kit must never reset these
            public Vector3 TenderAt = SkywellZone.Tow;  // where the tender was (deck axes) when it last let go of a receiver ...
            public float TenderSince = -100f;           // ... and when: it has been flying home since
            public int NextLane;                   // break lane of the next served AI
            public uint ReleasedId;                // last receiver let go (published with its reason and cooldown)
            public byte ReleaseKind;
            public float ReleasedUntil;
            public PitJob Preview;                 // the job the tender will serve first (planned when the receiver holds the zone)
            // Pit stop
            public readonly List<PitJob> Jobs = new List<PitJob>();
            public int JobIndex;                   // jobs committed so far
            public float JobStart, JobEnd, TransferStart, HotUntil, LostUntil, RefusedUntil, NextHotCheck;
            public bool Replan, JobCommitted;
            public ushort Flags;                   // persistent PitFlags of the last stop (the transient ones are added at publish)
            public ushort PitTenths;
        }

        /// <summary>A just-served AI receiver peels off to a clear lane for a few seconds before its own pilot takes over again.</summary>
        private struct BreakOrder
        {
            public Aircraft Tanker;
            public Vector3 Offset;
            public float Until;
        }

        private static SkywellService instance;
        private readonly Dictionary<Aircraft, Kit> kits = new Dictionary<Aircraft, Kit>();
        private readonly Dictionary<Aircraft, float> cooldown = new Dictionary<Aircraft, float>();
        private readonly Dictionary<Aircraft, float> lockout = new Dictionary<Aircraft, float>(); // hot-break: nobody re-enters at once
        private readonly Dictionary<Aircraft, int> strikes = new Dictionary<Aircraft, int>();   // called-in AIs that never made the zone
        private readonly Dictionary<Aircraft, BreakOrder> breaks = new Dictionary<Aircraft, BreakOrder>();
        private readonly List<Aircraft> drop = new List<Aircraft>();
        private float nextTick, nextScan;

        private void Awake() => instance = this;

        public void ResetForScene()
        {
            kits.Clear();
            cooldown.Clear();
            lockout.Clear();
            strikes.Clear();
            breaks.Clear();
            prefs.Clear();
            SkywellBoard.Views.Clear();
            SkywellBoard.HotLockUntil = 0f;
        }

        /// <summary>Server: flip the kit on the sender's own aircraft (ignored without a kit station).</summary>
        public static void Toggle(Aircraft tanker)
        {
            if (instance == null || tanker == null || tanker.disabled || SkywellBoard.KitStation(tanker) == null) return;
            Kit kit = instance.KitOf(tanker);
            float now = Time.timeSinceLevelLoad;
            if (now - kit.LastToggle < 0.75f) return; // held trigger
            kit.LastToggle = now;
            if (kit.State == TenderState.Stowed) instance.TryDeploy(kit, now, true);
            else if (!SkywellTender.Moving(kit.State)) instance.RequestStow(kit, now); // the deck is locked while it moves
        }

        public static float FuelKg(Aircraft tanker) =>
            instance != null && tanker != null && instance.kits.TryGetValue(tanker, out Kit k) ? k.FuelKg : -1f;

        public static int Rounds(Aircraft tanker) =>
            instance != null && tanker != null && instance.kits.TryGetValue(tanker, out Kit k) ? k.Rounds : -1;

        /// <summary>
        /// Host: the slots the steered AI receivers fly to right now, whichever kit serves them or called them in: a receiver being
        /// served (or called in) flies to the hold slot, a just-served one to its break lane.
        /// </summary>
        internal static void CollectSlots(Dictionary<Aircraft, SkywellSlot> into)
        {
            if (instance == null) return;
            float now = Time.timeSinceLevelLoad;
            foreach (Kit kit in instance.kits.Values)
            {
                if (kit.Tanker == null || kit.Tanker.disabled || !SkywellTender.Flying(kit.State)) continue;
                if (kit.Receiver != null) into[kit.Receiver] = new SkywellSlot(kit.Tanker, SkywellZone.HoldSlot);
                else if (kit.Caller != null) into[kit.Caller] = new SkywellSlot(kit.Tanker, SkywellZone.HoldSlot);
            }
            foreach (KeyValuePair<Aircraft, BreakOrder> pair in instance.breaks)
                if (pair.Key != null && !pair.Key.disabled && now < pair.Value.Until && pair.Value.Tanker != null && !pair.Value.Tanker.disabled && !into.ContainsKey(pair.Key))
                    into[pair.Key] = new SkywellSlot(pair.Value.Tanker, pair.Value.Offset);
        }

        private Kit KitOf(Aircraft tanker)
        {
            if (!kits.TryGetValue(tanker, out Kit kit)) kits[tanker] = kit = new Kit { Tanker = tanker };
            return kit;
        }

        private void FixedUpdate()
        {
            float now = Time.timeSinceLevelLoad;
            if (now < nextTick || !GameAccess.IsServer()) return;
            nextTick = now + TickInterval;
            bool scan = now >= nextScan;
            if (scan)
            {
                nextScan = now + ScanInterval;
                PruneState();
                DeployAiTankers(now);
            }

            drop.Clear();
            foreach (Kit kit in kits.Values)
            {
                if (kit.Tanker == null || kit.Tanker.disabled || SkywellBoard.KitStation(kit.Tanker) == null)
                {
                    drop.Add(kit.Tanker);
                    continue;
                }
                if ((kit.HotUntil > 0f && now >= kit.HotUntil) || (kit.LostUntil > 0f && now >= kit.LostUntil) || (kit.RefusedUntil > 0f && now >= kit.RefusedUntil))
                {
                    // The flags last 3 s: the red notices are the receiver's, and the refusal is the pilot's.
                    if (now >= kit.HotUntil) kit.HotUntil = 0f;
                    if (now >= kit.LostUntil) kit.LostUntil = 0f;
                    if (now >= kit.RefusedUntil) kit.RefusedUntil = 0f;
                    Publish(kit);
                }
                Limits(kit, now);
                Tick(kit, now, scan);
            }
            for (int i = 0; i < drop.Count; i++)
            {
                Aircraft t = drop[i];
                if (t != null) SkywellBoard.Set(new SkywellView { Tanker = t.persistentID.Id });
                kits.Remove(t);
            }
        }

        private void Tick(Kit kit, float now, bool scan)
        {
            switch (kit.State)
            {
                case TenderState.Stowed:
                    if (scan) Restock(kit, now);
                    break;
                case TenderState.Deploying:
                    if (now >= kit.StateEnd) Enter(kit, TenderState.Tow, 0f, now);
                    break;
                case TenderState.Tow:
                    if (kit.StowRequested) Enter(kit, TenderState.Stowing, now + SkywellTender.DeploySeconds, now);
                    else ScanForReceiver(kit, now, scan);
                    break;
                case TenderState.Return:
                    if (now >= kit.StateEnd)
                    {
                        kit.TenderAt = SkywellZone.Tow;
                        Enter(kit, TenderState.Tow, 0f, now);
                    }
                    else if (!kit.StowRequested) ScanForReceiver(kit, now, scan);
                    break;
                case TenderState.Approach:
                case TenderState.Station:
                case TenderState.Service:
                    if (!HotBreakCheck(kit, now)) AdvanceServing(kit, now);
                    break;
                case TenderState.Stowing:
                    if (now >= kit.StateEnd)
                    {
                        kit.StowRequested = kit.Reeling = false;
                        kit.TenderAt = SkywellZone.Tow;
                        kit.TenderSince = -100f;
                        Enter(kit, TenderState.Stowed, 0f, now);
                    }
                    break;
            }
        }

        // ---- deploy / stow

        private void TryDeploy(Kit kit, float now, bool announce)
        {
            Aircraft t = kit.Tanker;
            if (t.radarAlt < SkywellZone.DeployAltitude || SkywellBoard.Velocity(t).magnitude > SkywellZone.AutoReelSpeed)
            {
                if (announce)
                {
                    kit.RefusedUntil = now + RefusedFlagSeconds; // the tanker's HUD says why (too low or too fast)
                    Publish(kit);
                }
                return;
            }
            kit.StowRequested = kit.Reeling = false;
            Enter(kit, TenderState.Deploying, now + SkywellTender.DeploySeconds, now);
        }

        /// <summary>The tender goes home, then the deck stows (at once when it is already at the tow position).</summary>
        private void RequestStow(Kit kit, float now)
        {
            if (kit.State == TenderState.Stowed || kit.State == TenderState.Stowing) return;
            kit.StowRequested = true;
            if (kit.Receiver != null) Drop(kit, PitRelease.Aborted);
            else Publish(kit);
        }

        /// <summary>
        /// Tether limits while the kit is out: above 140 m/s the HUD warns, above 160 m/s (or below 150 m radar altitude, on the way
        /// down) the winch reels the tender in and the deck stows by itself.
        /// </summary>
        private void Limits(Kit kit, float now)
        {
            if (kit.State == TenderState.Stowed || kit.State == TenderState.Stowing)
            {
                if (kit.OverSpeed)
                {
                    kit.OverSpeed = false;
                    Publish(kit);
                }
                return;
            }
            float speed = SkywellBoard.Velocity(kit.Tanker).magnitude;
            bool over = speed > SkywellZone.MaxTankerSpeed;
            if (over != kit.OverSpeed)
            {
                kit.OverSpeed = over;
                Publish(kit);
            }
            if (!kit.StowRequested && (speed > SkywellZone.AutoReelSpeed || kit.Tanker.radarAlt < SkywellZone.StowAltitude))
            {
                kit.Reeling = true;
                VanguardStats.Bump("skywell.autoReel");
                RequestStow(kit, now);
            }
        }

        private void DeployAiTankers(float now)
        {
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled || a.Player != null || SkywellBoard.KitStation(a) == null) continue;
                Kit kit = KitOf(a);
                if (kit.State == TenderState.Stowed && a.radarAlt >= SkywellZone.DeployAltitude && SkywellBoard.Velocity(a).magnitude <= SkywellZone.MaxTankerSpeed)
                    TryDeploy(kit, now, false);
            }
        }

        /// <summary>A landed, parked tanker near a friendly field is reloaded: the fuel stock and the magazine.</summary>
        private void Restock(Kit kit, float now)
        {
            if (kit.FuelKg >= FuelStockKg - 1f && kit.Rounds >= SkywellMagazine.Rounds) return;
            Aircraft t = kit.Tanker;
            if (t.radarAlt > 5f || SkywellBoard.Velocity(t).sqrMagnitude > 25f || t.NetworkHQ == null) return;
            Airbase field = t.NetworkHQ.GetNearestAirbase(t.transform.position, new RunwayQuery { RunwayType = RunwayQueryType.Landing, MinSize = 0f, LandingSpeed = 0f, TailHook = false });
            if (field == null) return;
            Vector3 centre = field.center != null ? field.center.position : field.transform.position;
            if ((centre - t.transform.position).sqrMagnitude > RestockRadius * RestockRadius) return;
            kit.FuelKg = FuelStockKg;
            kit.Rounds = SkywellMagazine.Rounds;
            VanguardStats.Bump("skywell.restock");
            Publish(kit);
        }

        /// <summary>
        /// An AI that can be handed to the zone flight loop: well off the ground, gear up, and not in the taxi, take-off, landing or
        /// parked pilot state (those fly the runway, and the hold would aim a ground jet at the sky).
        /// </summary>
        internal static bool AiFlyable(Aircraft r)
        {
            if (r == null || r.disabled || r.Player != null || !(r.autopilot is AutopilotPlane)) return false;
            if (r.radarAlt < AiMinAltitude || r.gearState == LandingGear.GearState.LockedExtended || r.gearState == LandingGear.GearState.Extending) return false;
            Pilot pilot = r.pilots != null && r.pilots.Length > 0 ? r.pilots[0] : null;
            if (pilot == null) return true;
            PilotBaseState state = pilot.currentState;
            return !ReferenceEquals(state, pilot.AITaxiState) && !ReferenceEquals(state, pilot.AITakeoffState) &&
                   !ReferenceEquals(state, pilot.AILandingState) && !ReferenceEquals(state, pilot.parkedState);
        }

        /// <summary>Anyone (any side, player or AI, the receiver itself aside) within 35 m of this kit's hold slot: a call-in would fly into them.</summary>
        private static bool SlotBusy(Kit kit, Aircraft except)
        {
            Vector3 slot = new SkywellSlot(kit.Tanker, SkywellZone.HoldSlot).World;
            float clear = SkywellZone.SlotClearance * SkywellZone.SlotClearance;
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled || a == kit.Tanker || a == except) continue;
                if ((a.transform.position - slot).sqrMagnitude < clear) return true;
            }
            return false;
        }

        /// <summary>
        /// Tender at the tow position (or still flying home): the friendly aircraft that has held the zone for a second and needs
        /// service goes first (nearest the tow position). On the slow scan an AI astern that needs service but is not there yet is
        /// called in and flies itself to the hold slot, provided nobody is sitting in the way of the slot.
        /// </summary>
        private void ScanForReceiver(Kit kit, float now, bool scan)
        {
            Aircraft tanker = kit.Tanker;
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            Aircraft best = null, caller = null;
            float bestDist = float.MaxValue, callDist = AiEngageRange;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft r = all[i];
                if (r == null || r == tanker || r.disabled || r.NetworkHQ != tanker.NetworkHQ) continue;
                if (Busy(r, kit) || (cooldown.TryGetValue(r, out float coolUntil) && now < coolUntil) ||
                    (lockout.TryGetValue(r, out float lockUntil) && now < lockUntil))
                {
                    kit.InZone.Remove(r);
                    continue;
                }
                SkywellBoard.Rel(tanker, r, out Vector3 rel, out float relSpeed, out _, out float roll);
                bool inside = SkywellZone.Contains(rel, relSpeed, roll);
                kit.InZone.TryGetValue(r, out ZoneHold hold);
                bool held = hold.Update(inside, now);
                if (hold.Active) kit.InZone[r] = hold;
                else kit.InZone.Remove(r);
                if (held)
                {
                    float d = (rel - SkywellZone.Tow).sqrMagnitude;
                    if (d < bestDist && Needs(kit, r))
                    {
                        bestDist = d;
                        best = r;
                    }
                    continue;
                }
                if (inside || hold.Active) continue;
                float range = (rel - SkywellZone.HoldSlot).magnitude;
                if (scan && kit.Caller == null && rel.z < -10f && range < callDist && AiFlyable(r) && Needs(kit, r))
                {
                    callDist = range;
                    caller = r;
                }
            }
            if (best != null) BeginApproach(kit, best, now);
            else if (caller != null && !SlotBusy(kit, caller))
            {
                kit.Caller = caller;
                kit.CallEnd = now + EngageTimeout;
            }
            if (kit.Caller != null) CheckCaller(kit, now);
        }

        /// <summary>A called-in AI must keep flying the zone's slot: it leaves the sky, someone sits in its slot, or it never arrives.</summary>
        private void CheckCaller(Kit kit, float now)
        {
            Aircraft c = kit.Caller;
            if (c == null || c.disabled || !AiFlyable(c) || SlotBusy(kit, c) || !Needs(kit, c))
            {
                kit.Caller = null;
                return;
            }
            if (now < kit.CallEnd) return;
            strikes.TryGetValue(c, out int count);
            strikes[c] = ++count;
            VanguardStats.Bump("skywell.timeout");
            cooldown[c] = now + Mathf.Min(MaxStrikeCooldown, StrikeCooldown * count);
            kit.Caller = null;
        }

        /// <summary>The receiver holds the zone: the tender has this long to come from where it is (home, or still flying home from the last stop) to its belly.</summary>
        private void BeginApproach(Kit kit, Aircraft r, float now)
        {
            SkywellBoard.Rel(kit.Tanker, r, out Vector3 rel, out _, out _, out _);
            Bounds b = SkywellBoard.LocalBounds(r);
            var belly = new Vector3(rel.x, rel.y + b.min.y - SkywellTender.Under, rel.z);
            Vector3 from = SkywellTender.HomeEstimate(kit.TenderAt, now - kit.TenderSince);
            kit.Receiver = r;
            kit.Caller = null;
            kit.Exit.Reset();
            kit.HotUntil = kit.LostUntil = 0f; // the red flags are the visuals of the receiver they hit, not of whoever is served next
            kit.Flags = 0;
            kit.PitTenths = 0;
            kit.TenderAt = belly;
            kit.TenderSince = float.MaxValue; // attached to the receiver until it lets go
            PlanPreview(kit, r);
            Enter(kit, TenderState.Approach, now + SkywellTender.ArrivalSeconds(Vector3.Distance(belly, from)), now);
        }

        /// <summary>Worth service: a real fuel need the stock can meet, or a servable station the magazine can reload (as the receiver's order allows).</summary>
        private bool Needs(Kit kit, Aircraft r) => NeedsService(OrderFor(r), kit.FuelKg, kit.Rounds, r);

        /// <summary>Also read by the receiver's HUD (the published stock is all it needs).</summary>
        internal static bool NeedsService(PitOrder order, float fuelStockKg, int rounds, Aircraft r)
        {
            if (order != PitOrder.Munitions && FuelNeedKg(r) > Mathf.Max(50f, 0.15f * FuelCapacityKg(r)) && fuelStockKg > 1f) return true;
            if (order == PitOrder.Fuel || rounds <= 0) return false;
            SkywellStations.Collect(r, stationScratch);
            foreach (PitStation s in stationScratch)
                if (!s.Skip && s.Missing > 0 && s.MassPerRound > 0f) return true;
            return false;
        }

        /// <summary>Serving a stop, or about to: another kit's receiver or caller, or one this kit serves, is busy.</summary>
        private bool Busy(Aircraft r, Kit asking)
        {
            if (kits.ContainsKey(r)) return true; // tankers are not served
            foreach (Kit k in kits.Values)
                if (k.Receiver == r || (k.Caller == r && k != asking)) return true;
            return false;
        }

        private void AdvanceServing(Kit kit, float now)
        {
            Aircraft r = kit.Receiver;
            // A receiver Unity has destroyed (disconnect, despawn without a disabled phase) compares equal to null: let go of it here.
            if (r == null || r.disabled)
            {
                Drop(kit, PitRelease.Aborted);
                return;
            }
            SkywellBoard.Rel(kit.Tanker, r, out Vector3 rel, out float relSpeed, out _, out float roll);
            if (kit.Exit.Update(SkywellZone.Contains(rel, relSpeed, roll), now))
            {
                kit.LostUntil = now + LostFlagSeconds;
                VanguardStats.Bump("skywell.zoneLost");
                Drop(kit, PitRelease.ZoneLost);
                return;
            }
            switch (kit.State)
            {
                case TenderState.Approach:
                    if (now >= kit.StateEnd) Enter(kit, TenderState.Station, now + SkywellTender.StationSeconds, now);
                    break;
                case TenderState.Station:
                    if (now >= kit.StateEnd) BeginStop(kit, r, now);
                    break;
                case TenderState.Service:
                    RunJobs(kit, r, now);
                    break;
            }
        }

        private static void Enter(Kit kit, TenderState state, float end, float now)
        {
            SetState(kit, state, end, now);
            Publish(kit);
        }

        private static void SetState(Kit kit, TenderState state, float end, float now)
        {
            kit.State = state;
            kit.StateStart = now;
            kit.StateEnd = end;
        }

        /// <summary>Committed jobs stay committed: only the unfinished list, the order flags and the receiver are dropped, and the tender flies home.</summary>
        private void Drop(Kit kit, byte why, float cooldownSeconds = -1f)
        {
            Aircraft r = kit.Receiver;
            float now = Time.timeSinceLevelLoad;
            if (r != null)
            {
                bool capped = (kit.Flags & PitFlags.Capped) != 0;
                float wait = cooldownSeconds >= 0f ? cooldownSeconds
                    : why == PitRelease.HotBreak ? HotBreakCooldown
                    : why == PitRelease.Served ? (capped ? CappedCooldown : ReceiverCooldown)
                    : AbortCooldown;
                cooldown[r] = now + wait;
                if (why == PitRelease.HotBreak) lockout[r] = now + HotBreakCooldown;
                if (why == PitRelease.Served) strikes.Remove(r);
                kit.InZone.Remove(r);
                if (prefs.TryGetValue(r, out PitPrefs p)) p.Priority = -1; // one-shot
                kit.ReleasedId = r.persistentID.Id;
                kit.ReleaseKind = why;
                kit.ReleasedUntil = now + wait;
                // A served (or zone-lost) AI peels off to its own lane instead of idling in the slot the next receiver is called to.
                if (r.Player == null && (why == PitRelease.Served || why == PitRelease.ZoneLost) && !r.disabled)
                    breaks[r] = new BreakOrder { Tanker = kit.Tanker, Offset = SkywellZone.BreakSlot(kit.NextLane++ & 3), Until = now + SkywellZone.BreakSeconds };
            }
            kit.Receiver = null;
            kit.Jobs.Clear();
            kit.JobIndex = 0;
            kit.Replan = kit.JobCommitted = false;
            kit.Exit.Reset();
            if (SkywellTender.Serving(kit.State))
            {
                kit.TenderSince = now; // the tender flies home from its last pose from here on
                SetState(kit, TenderState.Return, now + SkywellTender.ReturnSeconds(kit.TenderAt), now);
            }
            Publish(kit);
        }

        internal static float FuelCapacityKg(Aircraft a)
        {
            float level = a.GetFuelLevel();
            return level > 0.001f ? a.GetFuelQuantity() / level : 0f;
        }

        internal static float FuelNeedKg(Aircraft a)
        {
            float level = a.GetFuelLevel();
            float quantity = a.GetFuelQuantity();
            if (level <= 0.001f) return 0f; // no tanks or bone dry: capacity unknown, skip
            return Mathf.Max(0f, a.fuelLevel * quantity / level - quantity);
        }

        internal static int MissingRounds(Aircraft a)
        {
            int missing = 0;
            if (a.weaponStations == null) return 0;
            foreach (WeaponStation s in a.weaponStations)
                if (s?.WeaponInfo != null && !s.WeaponInfo.cargo && !s.WeaponInfo.nuclear && s.WeaponInfo.massPerRound > 0f)
                    missing += Mathf.Max(0, s.FullAmmo - s.GetAmmoTotal());
            return missing;
        }

        private static void Publish(Kit kit)
        {
            bool serving = kit.Receiver != null && kit.State == TenderState.Service && kit.JobIndex < kit.Jobs.Count;
            // While the tender is still on its way, the first job is published as a preview: it flies straight to that pose.
            PitJob job = serving ? kit.Jobs[kit.JobIndex]
                : kit.Receiver != null && (kit.State == TenderState.Approach || kit.State == TenderState.Station) ? kit.Preview : default;
            float now = Time.timeSinceLevelLoad;
            int flags = kit.Flags;
            if (now < kit.HotUntil) flags |= PitFlags.HotBreak;
            if (now < kit.LostUntil) flags |= PitFlags.ZoneLost;
            if (now < kit.RefusedUntil) flags |= PitFlags.Refused;
            if (kit.OverSpeed) flags |= PitFlags.OverSpeed;
            if (kit.Reeling) flags |= PitFlags.AutoReel;
            SkywellBoard.Set(new SkywellView
            {
                Tanker = kit.Tanker.persistentID.Id,
                State = kit.State,
                Receiver = kit.Receiver != null ? kit.Receiver.persistentID.Id : 0u,
                FuelKg = (ushort)Mathf.Clamp(Mathf.RoundToInt(kit.FuelKg), 0, ushort.MaxValue),
                Rounds = (byte)Mathf.Clamp(kit.Rounds, 0, SkywellMagazine.Rounds),
                Order = kit.Receiver != null && instance != null ? instance.OrderFor(kit.Receiver) : PitOrder.Both,
                Job = job.Kind,
                JobStation = job.Kind == PitJobKind.Handoff || job.Kind == PitJobKind.Cassette ? (byte)Mathf.Clamp(job.Station, 0, 254) : (byte)255,
                JobIndex = (byte)Mathf.Clamp(kit.JobIndex, 0, 255),
                JobCount = (byte)Mathf.Clamp(kit.Jobs.Count, 0, 255),
                JobTenths = (byte)Mathf.Clamp(Mathf.RoundToInt(job.Seconds * 10f), 0, 255),
                StateTenths = (byte)Mathf.Clamp(Mathf.RoundToInt((now - kit.StateStart) * 10f), 0, 255),
                Flags = (ushort)flags,
                PitTenths = kit.PitTenths,
                Released = kit.ReleasedId,
                ReleaseKind = kit.ReleaseKind,
                Cooldown = (byte)Mathf.Clamp(Mathf.CeilToInt(kit.ReleasedUntil - now), 0, 255),
            });
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}

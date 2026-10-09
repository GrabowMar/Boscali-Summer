using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    internal enum SkywellPhase : byte { Idle, Engage, Transfer, Served }

    /// <summary>What every peer knows about one SKYWELL tanker (host: written by the service; clients: by SkywellNet).</summary>
    internal struct SkywellView
    {
        public uint Tanker;
        public bool Active;
        public uint Receiver;
        public SkywellPhase Phase;
        public float FuelKg;
        public float StockKg;
        // Pit stop (protocol 3): what the docked receiver is being served right now.
        public PitOrder Order;
        public PitJobKind Job;       // job in flight (None outside Transfer)
        public byte JobStation;      // index into the receiver's weaponStations, 255 = none (fuel)
        public byte JobIndex;        // jobs committed so far
        public byte JobCount;        // committed + remaining
        public byte JobTenths;       // seconds of the job in flight x10
        public byte Flags;           // PitFlags
        public ushort PitTenths;     // pit time x10 once Served
    }

    internal static class SkywellBoard
    {
        public static readonly Dictionary<uint, SkywellView> Views = new Dictionary<uint, SkywellView>();
        public static System.Action<SkywellView> Changed; // host -> SkywellNet broadcast

        public static void Set(SkywellView view)
        {
            Views[view.Tanker] = view;
            Changed?.Invoke(view);
        }

        /// <summary>The kit's station on this aircraft, or null.</summary>
        public static WeaponStation KitStation(Aircraft a)
        {
            if (a == null || a.weaponStations == null) return null;
            for (int i = 0; i < a.weaponStations.Count; i++)
            {
                WeaponStation s = a.weaponStations[i];
                if (s?.WeaponInfo != null && s.WeaponInfo.name.Contains(VanguardKeys.SkywellInfo)) return s;
            }
            return null;
        }

        /// <summary>Kit visual root (the mount the arms hang from), or the tanker itself.</summary>
        public static Transform Mount(Aircraft tanker)
        {
            WeaponStation s = KitStation(tanker);
            return s != null && s.Weapons.Count > 0 && s.Weapons[0] != null ? s.Weapons[0].transform : tanker.transform;
        }

        /// <summary>World point the receiver's probe is held at: mount-relative, in tanker axes.</summary>
        public static Vector3 ContactPoint(Aircraft tanker) =>
            Mount(tanker).position + tanker.transform.rotation * SkywellContact.ContactOffset;

        /// <summary>Refuel point: right side of the receiver's nose, from its real mesh bounds.</summary>
        public static Vector3 ProbePoint(Aircraft receiver)
        {
            Bounds b = LocalBounds(receiver);
            // Some airframes measure tiny bounds (mesh scale quirks): the cockpit is a safe stand-in for the nose.
            if (b.size.magnitude < 8f && receiver.cockpitViewPoint != null)
                return receiver.cockpitViewPoint.position + receiver.transform.forward * 1.2f + receiver.transform.up * 0.4f;
            return receiver.transform.TransformPoint(new Vector3(b.extents.x * 0.12f, b.center.y + b.extents.y * 0.25f, b.max.z - 2f));
        }

        /// <summary>Rearm point: under the left wing root, where the cargo arm offers the round.</summary>
        public static Vector3 PylonPoint(Aircraft receiver)
        {
            Bounds b = LocalBounds(receiver);
            return receiver.transform.TransformPoint(new Vector3(-b.extents.x * 0.3f, b.min.y + 0.3f, b.center.z + 0.5f));
        }

        private static readonly Dictionary<Aircraft, Bounds> boundsCache = new Dictionary<Aircraft, Bounds>();

        /// <summary>Airframe bounds in its own space (mesh bounds, so banking does not inflate them). Cached.</summary>
        public static Bounds LocalBounds(Aircraft a)
        {
            if (boundsCache.TryGetValue(a, out Bounds cached)) return cached;
            if (boundsCache.Count > 64) boundsCache.Clear();
            Transform root = a.transform;
            bool any = false;
            var b = new Bounds();
            foreach (MeshFilter mf in a.GetComponentsInChildren<MeshFilter>())
            {
                // Visible airframe meshes only: weapons, effect cones and helper meshes would stretch the bounds.
                if (mf.sharedMesh == null || mf.GetComponentInParent<Weapon>() != null) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                if (Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale).magnitude > 40f) continue;
                Bounds m = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (p.magnitude > 30f) continue;
                    if (!any) b = new Bounds(p, Vector3.zero);
                    else b.Encapsulate(p);
                    any = true;
                }
            }
            if (!any) b = new Bounds(Vector3.zero, new Vector3(10f, 3f, 15f));
            boundsCache[a] = b;
            return b;
        }
    }

    /// <summary>
    /// SKYWELL server (FS-41 CATOBAR style): a receiver's owner asks to dock while its pilot holds the brake near the
    /// arms (SkywellDock drives the airframe); the server confirms, waits for the docked pose, then runs the pit stop:
    /// a job list (fuel, one hot-swap handoff per round) built from the receiver's live state and its standing order
    /// (skywellPit.cs), through vanilla Refuel/RpcRearm, and keeps it held until the brake is released.
    /// AI tankers deploy on their own; AI receivers astern that need service are docked by the host.
    /// </summary>
    internal sealed partial class SkywellService : MonoBehaviour, ISceneService
    {
        public const float FuelStockKg = 8000f;
        public const float MunitionStockKg = 1500f;
        public const float EngageRadius = 60f;      // player: probe within this of the contact point
        public const float AiEngageRange = 1500f;   // host: AI receivers astern within this
        private const float TickInterval = 0.1f;
        private const float ScanInterval = 2f;
        private const float AiDeployAltitude = 300f;
        private const float AiStowAltitude = 150f;   // hysteresis: a landing never starts with the ramp held open
        private const float ReceiverCooldown = 30f;
        private const float AbortCooldown = 5f;
        private const float HotBreakCooldown = 8f;
        private const float DockedTolerance = 3f;
        private const float EngageTimeout = 75f;

        private sealed class Kit
        {
            public Aircraft Tanker;
            public bool Active;
            public Aircraft Receiver;
            public SkywellPhase Phase;
            public float PhaseEnd;
            public float FuelKg = FuelStockKg;
            public float StockKg = MunitionStockKg;
            public float LastToggle = -10f;
            // Pit stop
            public readonly List<PitJob> Jobs = new List<PitJob>();
            public int JobIndex;                   // jobs committed so far
            public float JobStart, JobEnd, TransferStart, HotUntil, NextHotCheck;
            public bool Replan, FuelCommitted;
            public byte Flags;
            public ushort PitTenths;
        }

        private static SkywellService instance;
        private readonly Dictionary<Aircraft, Kit> kits = new Dictionary<Aircraft, Kit>();
        private readonly Dictionary<Aircraft, float> cooldown = new Dictionary<Aircraft, float>();
        private readonly Dictionary<Aircraft, float> lockout = new Dictionary<Aircraft, float>(); // hot-break: nobody re-docks at once
        private readonly List<Aircraft> drop = new List<Aircraft>();
        private float nextTick, nextScan;

        private void Awake() => instance = this;

        public void ResetForScene()
        {
            kits.Clear();
            cooldown.Clear();
            lockout.Clear();
            prefs.Clear();
            SkywellBoard.Views.Clear();
        }

        /// <summary>Server: flip the kit on the sender's own aircraft (ignored without a kit station).</summary>
        public static void Toggle(Aircraft tanker)
        {
            if (instance == null || tanker == null || tanker.disabled || SkywellBoard.KitStation(tanker) == null) return;
            Kit kit = instance.KitOf(tanker);
            if (Time.timeSinceLevelLoad - kit.LastToggle < 0.75f) return; // held trigger
            kit.LastToggle = Time.timeSinceLevelLoad;
            kit.Active = !kit.Active;
            if (!kit.Active) instance.Drop(kit, false);
            Publish(kit);
        }

        /// <summary>Server: the receiver's owner engaged (brake held near the arms). False when refused.</summary>
        public static bool Dock(Aircraft receiver, Aircraft tanker)
        {
            if (instance == null || receiver == null || tanker == null || receiver == tanker || receiver.disabled ||
                !instance.kits.TryGetValue(tanker, out Kit kit) || !kit.Active || receiver.NetworkHQ != tanker.NetworkHQ) return false;
            if (kit.Receiver == receiver) return true;
            if (kit.Receiver != null || instance.Busy(receiver)) return false;
            if (instance.lockout.TryGetValue(receiver, out float until) && Time.timeSinceLevelLoad < until) return false;
            float range = receiver.Player == null ? AiEngageRange : EngageRadius + 20f;
            if (Vector3.Distance(SkywellBoard.ProbePoint(receiver), SkywellBoard.ContactPoint(tanker)) > range) return false;
            kit.Receiver = receiver;
            Enter(kit, SkywellPhase.Engage, Time.timeSinceLevelLoad + EngageTimeout);
            return true;
        }

        /// <summary>Server: brake released (or the owner gave up): the receiver is let go.</summary>
        public static void Undock(Aircraft receiver)
        {
            if (instance == null || receiver == null) return;
            foreach (Kit kit in instance.kits.Values)
                if (kit.Receiver == receiver) instance.Drop(kit, kit.Phase == SkywellPhase.Served);
        }

        public static float FuelKg(Aircraft tanker) =>
            instance != null && tanker != null && instance.kits.TryGetValue(tanker, out Kit k) ? k.FuelKg : -1f;

        public static float StockKg(Aircraft tanker) =>
            instance != null && tanker != null && instance.kits.TryGetValue(tanker, out Kit k) ? k.StockKg : -1f;

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
                PrunePrefs();
                DeployAiTankers();
            }

            drop.Clear();
            foreach (Kit kit in kits.Values)
            {
                if (kit.Tanker == null || kit.Tanker.disabled || SkywellBoard.KitStation(kit.Tanker) == null)
                {
                    drop.Add(kit.Tanker);
                    continue;
                }
                if (kit.HotUntil > 0f && now >= kit.HotUntil)
                {
                    kit.HotUntil = 0f; // the hot-break flag lasts 3 s
                    Publish(kit);
                }
                if (!kit.Active) continue;
                if (kit.Receiver == null)
                {
                    if (scan) CallInAi(kit, now);
                }
                else if (!HotBreakCheck(kit, now)) Advance(kit, now);
            }
            for (int i = 0; i < drop.Count; i++)
            {
                Aircraft t = drop[i];
                if (t != null) SkywellBoard.Set(new SkywellView { Tanker = t.persistentID.Id });
                kits.Remove(t);
            }
        }

        private void DeployAiTankers()
        {
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled || a.Player != null || SkywellBoard.KitStation(a) == null) continue;
                kits.TryGetValue(a, out Kit kit);
                if (kit != null && kit.Active)
                {
                    if (a.radarAlt >= AiStowAltitude) continue;
                    kit.Active = false; // low and slow: stow before the approach
                    Drop(kit, false);
                    continue;
                }
                if (a.radarAlt < AiDeployAltitude) continue;
                kit = kit ?? KitOf(a);
                kit.Active = true;
                Publish(kit);
            }
        }

        /// <summary>The AI "holds the brake": a friendly AI astern that needs service is docked by the host.</summary>
        private void CallInAi(Kit kit, float now)
        {
            Aircraft tanker = kit.Tanker;
            Vector3 contact = tanker.transform.InverseTransformPoint(SkywellBoard.ContactPoint(tanker));
            IReadOnlyList<Aircraft> all = UnitRegistry.allAircraft;
            Aircraft best = null;
            float bestDist = AiEngageRange;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft r = all[i];
                if (r == null || r == tanker || r.disabled || r.Player != null || r.NetworkHQ != tanker.NetworkHQ || Busy(r)) continue;
                if (cooldown.TryGetValue(r, out float until) && now < until) continue;
                if (!Needs(kit, r)) continue;
                Vector3 rel = tanker.transform.InverseTransformPoint(SkywellBoard.ProbePoint(r)) - contact;
                float d = rel.magnitude;
                if (rel.z < -10f && d < bestDist)
                {
                    bestDist = d;
                    best = r;
                }
            }
            if (best != null) Dock(best, tanker);
        }

        /// <summary>Worth a call-in: a real fuel need the stock can meet, or a servable station the stock can reload (as the receiver's order allows).</summary>
        private bool Needs(Kit kit, Aircraft r)
        {
            PitOrder order = OrderFor(r);
            if (order != PitOrder.Munitions && FuelNeedKg(r) > Mathf.Max(50f, 0.15f * FuelCapacityKg(r)) && kit.FuelKg > 1f) return true;
            if (order == PitOrder.Fuel) return false;
            SkywellStations.Collect(r, stationScratch);
            float lightest = float.MaxValue;
            foreach (PitStation s in stationScratch)
                if (!s.Skip && s.Missing > 0 && s.MassPerRound > 0f) lightest = Mathf.Min(lightest, s.MassPerRound);
            return lightest < float.MaxValue && kit.StockKg >= lightest;
        }

        private bool Busy(Aircraft r)
        {
            if (kits.ContainsKey(r)) return true; // tankers are not served
            foreach (Kit k in kits.Values)
                if (k.Receiver == r) return true;
            return false;
        }

        private void Advance(Kit kit, float now)
        {
            Aircraft r = kit.Receiver;
            if (r == null || r.disabled)
            {
                Drop(kit, false);
                return;
            }
            float dist = Vector3.Distance(SkywellBoard.ProbePoint(r), SkywellBoard.ContactPoint(kit.Tanker));
            switch (kit.Phase)
            {
                case SkywellPhase.Engage:
                    if (dist <= DockedTolerance) BeginStop(kit, r, now);
                    else if (now >= kit.PhaseEnd) Drop(kit, false);
                    break;
                case SkywellPhase.Transfer:
                    if (dist > SkywellContact.AbortDistance) Drop(kit, false);
                    else RunJobs(kit, r, now);
                    break;
                case SkywellPhase.Served:
                    // Players stay held until they release the brake; AI is let go after a beat.
                    if (dist > SkywellContact.AbortDistance || (r.Player == null && now >= kit.PhaseEnd)) Drop(kit, true);
                    break;
            }
        }

        private static void Enter(Kit kit, SkywellPhase phase, float end)
        {
            kit.Phase = phase;
            kit.PhaseEnd = end;
            Publish(kit);
        }

        /// <summary>Committed jobs stay committed: only the unfinished list, the order flags and the receiver are dropped.</summary>
        private void Drop(Kit kit, bool served, bool hot = false)
        {
            Aircraft r = kit.Receiver;
            if (r != null)
            {
                float now = Time.timeSinceLevelLoad;
                cooldown[r] = now + (hot ? HotBreakCooldown : served ? ReceiverCooldown : AbortCooldown);
                if (hot) lockout[r] = now + HotBreakCooldown;
                if (prefs.TryGetValue(r, out PitPrefs p)) p.Priority = -1; // one-shot
            }
            kit.Receiver = null;
            kit.Phase = SkywellPhase.Idle;
            kit.Jobs.Clear();
            kit.JobIndex = 0;
            kit.Replan = kit.FuelCommitted = false;
            kit.Flags = 0;
            kit.PitTenths = 0;
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
            bool serving = kit.Receiver != null && kit.Phase == SkywellPhase.Transfer && kit.JobIndex < kit.Jobs.Count;
            PitJob job = serving ? kit.Jobs[kit.JobIndex] : default;
            SkywellBoard.Set(new SkywellView
            {
                Tanker = kit.Tanker.persistentID.Id,
                Active = kit.Active,
                Receiver = kit.Receiver != null ? kit.Receiver.persistentID.Id : 0u,
                Phase = kit.Phase,
                FuelKg = kit.FuelKg,
                StockKg = kit.StockKg,
                Order = kit.Receiver != null && instance != null ? instance.OrderFor(kit.Receiver) : PitOrder.Both,
                Job = job.Kind,
                JobStation = job.Kind == PitJobKind.Handoff || job.Kind == PitJobKind.Cassette ? (byte)Mathf.Clamp(job.Station, 0, 254) : (byte)255,
                JobIndex = (byte)Mathf.Clamp(kit.JobIndex, 0, 255),
                JobCount = (byte)Mathf.Clamp(kit.Jobs.Count, 0, 255),
                JobTenths = (byte)Mathf.Clamp(Mathf.RoundToInt(job.Seconds * 10f), 0, 255),
                Flags = (byte)((kit.Flags & ~PitFlags.HotBreak) | (Time.timeSinceLevelLoad < kit.HotUntil ? PitFlags.HotBreak : 0)),
                PitTenths = kit.Phase == SkywellPhase.Served ? kit.PitTenths : (ushort)0,
            });
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}

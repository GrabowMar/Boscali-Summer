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

        /// <summary>Local aircraft: Time.timeSinceLevelLoad until which the server refuses a re-dock after a hot break (HUD sets, dock script reads).</summary>
        public static float HotLockUntil;

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

        /// <summary>The glider's anchor: the kit mount, the origin of the service box and of the tow position.</summary>
        public static Vector3 Anchor(Aircraft tanker) => Mount(tanker).position;

        /// <summary>Receiver in the tanker's box frame: position from the anchor in tanker axes, relative speed, relative roll.</summary>
        public static void Rel(Aircraft tanker, Aircraft receiver, out Vector3 rel, out float relSpeed, out float roll)
        {
            Transform tt = tanker.transform, rt = receiver.transform;
            rel = tt.InverseTransformDirection(rt.position - Anchor(tanker));
            relSpeed = (Velocity(receiver) - Velocity(tanker)).magnitude;
            roll = SkywellBox.Roll(tt.right, tt.up, tt.forward, rt.forward, rt.up);
        }

        private struct Track
        {
            public GlobalPosition Pos;
            public float Time;
            public Vector3 Vel;
        }

        private static readonly Dictionary<Aircraft, Track> tracks = new Dictionary<Aircraft, Track>();

        /// <summary>
        /// World velocity: the rigidbody's for an aircraft this peer simulates; for a remote one (the host's copy of a client's jet,
        /// the client's copy of the tanker) the difference of its replicated position, refreshed every 0.1 s, since a replica's body
        /// need not carry a velocity.
        /// </summary>
        public static Vector3 Velocity(Aircraft a)
        {
            if (a.rb != null && !a.remoteSim) return a.rb.velocity;
            float now = UnityEngine.Time.timeSinceLevelLoad;
            GlobalPosition p = a.GlobalPosition();
            if (tracks.TryGetValue(a, out Track t))
            {
                float dt = now - t.Time;
                if (dt < 0.1f && dt >= 0f) return t.Vel;
                if (dt < 1f && dt > 0f) t.Vel = Vector3.Lerp(t.Vel, (p - t.Pos) / dt, 0.6f);
                else t.Vel = a.rb != null ? a.rb.velocity : Vector3.zero;
            }
            else
            {
                if (tracks.Count > 64) tracks.Clear();
                t.Vel = a.rb != null ? a.rb.velocity : Vector3.zero;
            }
            t.Pos = p;
            t.Time = now;
            tracks[a] = t;
            return t.Vel;
        }

        /// <summary>Refuel point: a belly receptacle just ahead of the receiver's centre, from its real bounds (the glider's boom rises to it).</summary>
        public static Vector3 FuelPoint(Aircraft receiver)
        {
            Bounds b = LocalBounds(receiver);
            return receiver.transform.TransformPoint(new Vector3(b.center.x, b.min.y + 0.25f, b.center.z + b.extents.z * 0.2f));
        }

        /// <summary>Rearm point: under the left wing root, where the cargo arm offers the round.</summary>
        public static Vector3 PylonPoint(Aircraft receiver)
        {
            Bounds b = LocalBounds(receiver);
            return receiver.transform.TransformPoint(new Vector3(-b.extents.x * 0.3f, b.min.y + 0.3f, b.center.z + 0.5f));
        }

        private static readonly Dictionary<Aircraft, Bounds> boundsCache = new Dictionary<Aircraft, Bounds>();

        /// <summary>Size of an airframe whose meshes measure tiny (mesh scale quirks) and whose colliders say nothing: a generic fighter.</summary>
        private static readonly Bounds GenericAirframe = new Bounds(Vector3.zero, new Vector3(11f, 3.5f, 16f));

        /// <summary>
        /// Airframe bounds in its own space: the renderers' own bounds (so banking does not inflate them), then its colliders when
        /// those measure tiny, then a generic fighter. Cached.
        /// </summary>
        public static Bounds LocalBounds(Aircraft a)
        {
            if (boundsCache.TryGetValue(a, out Bounds cached)) return cached;
            if (boundsCache.Count > 64) boundsCache.Clear();
            Transform root = a.transform;
            bool any = false;
            var b = new Bounds();
            // Visible airframe meshes only: weapons, effect cones and helper meshes would stretch the bounds.
            foreach (Renderer rd in a.GetComponentsInChildren<Renderer>())
            {
                if (!(rd is MeshRenderer) && !(rd is SkinnedMeshRenderer)) continue;
                if (rd.GetComponentInParent<Weapon>() != null) continue;
                Add(root, rd.transform, rd.localBounds, 80f, ref b, ref any);
            }
            if (!any || b.size.magnitude < 8f)
            {
                any = false;
                foreach (Collider c in a.GetComponentsInChildren<Collider>())
                    if (c is BoxCollider box) Add(root, c.transform, new Bounds(box.center, box.size), 80f, ref b, ref any);
                    else if (c is MeshCollider mc && mc.sharedMesh != null && !c.isTrigger) Add(root, c.transform, mc.sharedMesh.bounds, 80f, ref b, ref any);
            }
            if (!any || b.size.magnitude < 8f) b = GenericAirframe;
            boundsCache[a] = b;
            return b;
        }

        private static void Add(Transform root, Transform from, Bounds local, float maxSize, ref Bounds into, ref bool any)
        {
            if (Vector3.Scale(local.size, from.lossyScale).magnitude > maxSize) return;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.InverseTransformPoint(from.TransformPoint(corner));
                if (p.magnitude > 60f) continue;
                if (!any) into = new Bounds(p, Vector3.zero);
                else into.Encapsulate(p);
                any = true;
            }
        }
    }

    /// <summary>
    /// SKYWELL server: a receiver that holds loose formation inside the service box behind and below the tanker (SkywellBox) is
    /// served - flying into the box is the whole request, and nothing here ever moves a receiver. The server confirms, gives the
    /// glider its few seconds to come under the belly, then runs the pit stop: a job list (fuel, one hot-swap handoff per round)
    /// built from the receiver's live state and its standing order (skywellPit.cs), through vanilla Refuel/RpcRearm. The receiver
    /// leaving the box ends the stop (BOX LOST). AI tankers deploy on their own; AI receivers astern that need service are called
    /// in and fly to the box centre on their own autopilot (SkywellVisuals).
    /// </summary>
    internal sealed partial class SkywellService : MonoBehaviour, ISceneService
    {
        public const float FuelStockKg = 8000f;
        public const float MunitionStockKg = 1500f;
        public const float AiEngageRange = 1500f;   // host: AI receivers astern within this are called in
        public const float ServedSeconds = 3f;      // SERVICE COMPLETE shows this long, then the glider lets go
        private const float TickInterval = 0.1f;
        private const float ScanInterval = 2f;
        private const float ReadySeconds = 11f;      // deploy 2.5 s + glider launch 8 s: no stop starts before the glider can fly it
        private const float AiDeployAltitude = 300f;
        private const float AiStowAltitude = 150f;   // hysteresis: a landing never starts with the ramp held open
        private const float ReceiverCooldown = 30f;
        private const float AbortCooldown = 5f;
        internal const float HotBreakCooldown = 8f; // also the local re-entry lockout the HUD shows
        private const float EngageTimeout = 75f;     // a called-in AI that never makes the box
        private const float LostFlagSeconds = 3f;

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
            public float ActiveSince;
            public bool Entered;                   // the receiver has held the box; Engage now counts the glider's trip
            public float OutSince;                 // time the receiver left the keep box (0 = inside)
            // Pit stop
            public readonly List<PitJob> Jobs = new List<PitJob>();
            public int JobIndex;                   // jobs committed so far
            public float JobStart, JobEnd, TransferStart, HotUntil, LostUntil, NextHotCheck;
            public bool Replan, FuelCommitted;
            public byte Flags;
            public ushort PitTenths;
        }

        private static SkywellService instance;
        private readonly Dictionary<Aircraft, Kit> kits = new Dictionary<Aircraft, Kit>();
        private readonly Dictionary<Aircraft, float> cooldown = new Dictionary<Aircraft, float>();
        private readonly Dictionary<Aircraft, float> lockout = new Dictionary<Aircraft, float>(); // hot-break: nobody re-enters at once
        private readonly Dictionary<Aircraft, float> inBoxSince = new Dictionary<Aircraft, float>();
        private readonly List<Aircraft> drop = new List<Aircraft>();
        private float nextTick, nextScan;

        private void Awake() => instance = this;

        public void ResetForScene()
        {
            kits.Clear();
            cooldown.Clear();
            lockout.Clear();
            inBoxSince.Clear();
            prefs.Clear();
            SkywellBoard.Views.Clear();
            SkywellBoard.HotLockUntil = 0f;
        }

        /// <summary>Server: flip the kit on the sender's own aircraft (ignored without a kit station).</summary>
        public static void Toggle(Aircraft tanker)
        {
            if (instance == null || tanker == null || tanker.disabled || SkywellBoard.KitStation(tanker) == null) return;
            Kit kit = instance.KitOf(tanker);
            if (Time.timeSinceLevelLoad - kit.LastToggle < 0.75f) return; // held trigger
            kit.LastToggle = Time.timeSinceLevelLoad;
            kit.Active = !kit.Active;
            if (kit.Active) kit.ActiveSince = Time.timeSinceLevelLoad;
            else instance.Drop(kit, false);
            Publish(kit);
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
                PruneState();
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
                if (kit.LostUntil > 0f && now >= kit.LostUntil)
                {
                    kit.LostUntil = 0f; // so does BOX LOST
                    Publish(kit);
                }
                if (!kit.Active) continue;
                if (kit.Receiver == null)
                {
                    if (now - kit.ActiveSince >= ReadySeconds) ScanForReceiver(kit, now, scan);
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
                kit.ActiveSince = Time.timeSinceLevelLoad;
                Publish(kit);
            }
        }

        /// <summary>
        /// Idle kit: the friendly aircraft that has held the box for a second and needs service goes first (nearest the centre).
        /// On the slow scan an AI astern that needs service but is not there yet is called in and flies itself to the centre.
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
                if (r == null || r == tanker || r.disabled || r.NetworkHQ != tanker.NetworkHQ || Busy(r)) continue;
                if (cooldown.TryGetValue(r, out float coolUntil) && now < coolUntil) continue;
                if (lockout.TryGetValue(r, out float lockUntil) && now < lockUntil) continue;
                SkywellBoard.Rel(tanker, r, out Vector3 rel, out float relSpeed, out float roll);
                if (SkywellBox.Inside(rel, relSpeed, roll, false))
                {
                    if (!inBoxSince.TryGetValue(r, out float since)) inBoxSince[r] = since = now;
                    float d = (rel - SkywellBox.Centre).sqrMagnitude;
                    if (now - since >= SkywellBox.HoldSeconds && d < bestDist && Needs(kit, r))
                    {
                        bestDist = d;
                        best = r;
                    }
                    continue;
                }
                inBoxSince.Remove(r);
                float range = (rel - SkywellBox.Centre).magnitude;
                if (scan && r.Player == null && rel.z < -10f && range < callDist && r.autopilot is AutopilotPlane && Needs(kit, r))
                {
                    callDist = range;
                    caller = r;
                }
            }
            if (best != null) Begin(kit, best, true, now);
            else if (caller != null) Begin(kit, caller, false, now);
        }

        private void Begin(Kit kit, Aircraft r, bool entered, float now)
        {
            kit.Receiver = r;
            kit.HotUntil = kit.LostUntil = 0f; // the red flags belong to the receiver they hit, not to whoever is served next
            kit.Entered = false;
            kit.OutSince = 0f;
            Enter(kit, SkywellPhase.Engage, now + EngageTimeout);
            if (entered) MarkEntered(kit, r, now);
        }

        /// <summary>The receiver holds the box: the glider has this long to come from its tow position to the belly.</summary>
        private static void MarkEntered(Kit kit, Aircraft r, float now)
        {
            SkywellBoard.Rel(kit.Tanker, r, out Vector3 rel, out _, out _);
            Bounds b = SkywellBoard.LocalBounds(r);
            var belly = new Vector3(rel.x, rel.y + b.min.y - SkywellGlider.Under, rel.z);
            kit.Entered = true;
            kit.OutSince = 0f;
            kit.PhaseEnd = now + SkywellGlider.ArrivalSeconds(Vector3.Distance(belly, SkywellGlider.Tow));
        }

        /// <summary>Worth service: a real fuel need the stock can meet, or a servable station the stock can reload (as the receiver's order allows).</summary>
        private bool Needs(Kit kit, Aircraft r) => NeedsService(OrderFor(r), kit.FuelKg, kit.StockKg, r);

        /// <summary>Also read by the receiver's HUD (the published stock is all it needs).</summary>
        internal static bool NeedsService(PitOrder order, float fuelStockKg, float munitionStockKg, Aircraft r)
        {
            if (order != PitOrder.Munitions && FuelNeedKg(r) > Mathf.Max(50f, 0.15f * FuelCapacityKg(r)) && fuelStockKg > 1f) return true;
            if (order == PitOrder.Fuel) return false;
            SkywellStations.Collect(r, stationScratch);
            float lightest = float.MaxValue;
            foreach (PitStation s in stationScratch)
                if (!s.Skip && s.Missing > 0 && s.MassPerRound > 0f) lightest = Mathf.Min(lightest, s.MassPerRound);
            return lightest < float.MaxValue && munitionStockKg >= lightest;
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
            SkywellBoard.Rel(kit.Tanker, r, out Vector3 rel, out float relSpeed, out float roll);
            if (SkywellBox.Inside(rel, relSpeed, roll, kit.Entered)) kit.OutSince = 0f;
            else if (kit.OutSince == 0f) kit.OutSince = now;
            bool lost = kit.Entered && kit.OutSince > 0f && now - kit.OutSince >= SkywellBox.LostSeconds;
            switch (kit.Phase)
            {
                case SkywellPhase.Engage:
                    if (!kit.Entered)
                    {
                        if (SkywellBox.Inside(rel, relSpeed, roll, false)) MarkEntered(kit, r, now);
                        else if (now >= kit.PhaseEnd) Drop(kit, false); // a called-in AI that never made the box
                    }
                    else if (lost) Lose(kit, now);
                    else if (now >= kit.PhaseEnd) BeginStop(kit, r, now);
                    break;
                case SkywellPhase.Transfer:
                    if (lost) Lose(kit, now);
                    else RunJobs(kit, r, now);
                    break;
                case SkywellPhase.Served:
                    if (lost || now >= kit.PhaseEnd) Drop(kit, true);
                    break;
            }
        }

        /// <summary>The receiver left the box: what was committed stays committed, the glider drops back, the HUD says so.</summary>
        private void Lose(Kit kit, float now)
        {
            kit.LostUntil = now + LostFlagSeconds;
            VanguardStats.Bump("skywell.boxLost");
            Drop(kit, false);
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
                inBoxSince.Remove(r);
                if (prefs.TryGetValue(r, out PitPrefs p)) p.Priority = -1; // one-shot
            }
            kit.Receiver = null;
            kit.Phase = SkywellPhase.Idle;
            kit.Jobs.Clear();
            kit.JobIndex = 0;
            kit.Entered = kit.Replan = kit.FuelCommitted = false;
            kit.OutSince = 0f;
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
            float now = Time.timeSinceLevelLoad;
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
                Flags = (byte)((kit.Flags & ~(PitFlags.HotBreak | PitFlags.BoxLost)) |
                               (now < kit.HotUntil ? PitFlags.HotBreak : 0) | (now < kit.LostUntil ? PitFlags.BoxLost : 0)),
                PitTenths = kit.Phase == SkywellPhase.Served ? kit.PitTenths : (ushort)0,
            });
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}

using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>What every peer knows about one SKYWELL tanker (host: written by the service; clients: by SkywellNet).</summary>
    internal struct SkywellView
    {
        public uint Tanker;
        public TenderState State;
        public uint Receiver;        // the receiver being served (Approach .. Service), else 0
        public ushort FuelKg;        // tanker fuel stock for the receivers
        public byte Rounds;          // rounds left in the tender's magazine (0..6)
        // Pit stop: what the receiver is being served right now.
        public PitOrder Order;
        public PitJobKind Job;       // job in flight (the first job as a preview during Approach / Station)
        public byte JobStation;      // index into the receiver's weaponStations, 255 = none (fuel)
        public byte JobIndex;        // jobs committed so far
        public byte JobCount;        // committed + remaining
        public byte JobTenths;       // seconds of the job in flight x10
        public byte StateTenths;     // seconds in this state x10 at publish time (capped at 25.5 s): late joiners animate from it
        public ushort Flags;         // PitFlags
        public ushort PitTenths;     // pit time x10 of the last completed stop
        // Last release: who was let go, why, and how long the server keeps them out. Receiver-specific, so a quick next stop never
        // overwrites a pilot's ZONE LOST / SERVICE IN notice.
        public uint Released;
        public byte ReleaseKind;     // PitRelease
        public byte Cooldown;        // seconds the server refuses Released, at publish time
        public float At;             // local Time.timeSinceLevelLoad when this view was stored (not on the wire)

        /// <summary>The kit is out (deploying, flying, or stowing).</summary>
        public bool Active => State != TenderState.Stowed;

        /// <summary>Seconds this view's state has lasted, as of now.</summary>
        public float StateAge => StateTenths / 10f + Mathf.Max(0f, Time.timeSinceLevelLoad - At);

        /// <summary>Seconds still to wait before the server takes <paramref name="id"/> again (0 when it is not the receiver this kit let go).</summary>
        public float CooldownLeft(uint id) =>
            Released != 0u && Released == id ? Mathf.Max(0f, Cooldown - (Time.timeSinceLevelLoad - At)) : 0f;
    }

    internal static class SkywellBoard
    {
        public static readonly Dictionary<uint, SkywellView> Views = new Dictionary<uint, SkywellView>();
        public static System.Action<SkywellView> Changed; // host -> SkywellNet broadcast

        /// <summary>Local aircraft: Time.timeSinceLevelLoad until which the server refuses a re-entry after a hot break (HUD sets, HUD reads).</summary>
        public static float HotLockUntil;

        /// <summary>The aircraft a published unit id names (0 = none), or null once it is gone.</summary>
        public static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;

        public static void Set(SkywellView view)
        {
            view.At = Time.timeSinceLevelLoad;
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
                if (IsKit(s?.WeaponInfo)) return s;
            }
            return null;
        }

        private static readonly Dictionary<WeaponInfo, bool> KitInfos = new Dictionary<WeaponInfo, bool>();

        /// <summary>The info is the SKYWELL kit. The name test runs once per WeaponInfo: Object.name allocates.</summary>
        public static bool IsKit(WeaponInfo info)
        {
            if (info == null) return false;
            if (!KitInfos.TryGetValue(info, out bool kit)) KitInfos[info] = kit = info.name.Contains(VanguardKeys.SkywellInfo);
            return kit;
        }

        /// <summary>Kit visual root (the mount the deck and tender hang from), or the tanker itself.</summary>
        public static Transform Mount(Aircraft tanker)
        {
            WeaponStation s = KitStation(tanker);
            return s != null && s.Weapons.Count > 0 && s.Weapons[0] != null ? s.Weapons[0].transform : tanker.transform;
        }

        /// <summary>The deck: the kit mount, the origin of the service zone and of the tow position.</summary>
        public static Vector3 Anchor(Aircraft tanker) => Mount(tanker).position;

        /// <summary>Receiver in the tanker's deck frame: position from the anchor in tanker axes, relative speed, speed along the tanker's heading, relative roll.</summary>
        public static void Rel(Aircraft tanker, Aircraft receiver, out Vector3 rel, out float relSpeed, out float relForward, out float roll)
        {
            Transform tt = tanker.transform, rt = receiver.transform;
            rel = tt.InverseTransformDirection(rt.position - Anchor(tanker));
            Vector3 dv = Velocity(receiver) - Velocity(tanker);
            relSpeed = dv.magnitude;
            relForward = Vector3.Dot(dv, tt.forward);
            roll = SkywellZone.Roll(tt.right, tt.up, tt.forward, rt.forward, rt.up);
        }

        private struct Track
        {
            public GlobalPosition Pos;
            public float Time;
            public Vector3 Vel;
        }

        private static readonly Dictionary<Aircraft, Track> tracks = new Dictionary<Aircraft, Track>();
        private const float VelocityBaseline = 0.4f;

        /// <summary>
        /// World velocity: the rigidbody's for an aircraft this peer simulates; for a remote one (the host's copy of a client's jet,
        /// the client's copy of the tanker) the difference of its replicated position over a 0.4 s baseline, since a replica's body
        /// need not carry a velocity. Position noise of a metre is 2.5 m/s over that baseline.
        /// </summary>
        public static Vector3 Velocity(Aircraft a)
        {
            if (a.rb != null && !a.remoteSim) return a.rb.velocity;
            float now = UnityEngine.Time.timeSinceLevelLoad;
            GlobalPosition p = a.GlobalPosition();
            if (tracks.TryGetValue(a, out Track t))
            {
                float dt = now - t.Time;
                if (dt < VelocityBaseline && dt >= 0f) return t.Vel;
                if (dt < 2f && dt > 0f) t.Vel = Vector3.Lerp(t.Vel, (p - t.Pos) / dt, 0.7f);
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

        /// <summary>Refuel point: a belly receptacle just ahead of the receiver's centre, from its real bounds (the boom rises to it).</summary>
        public static Vector3 FuelPoint(Aircraft receiver)
        {
            Bounds b = LocalBounds(receiver);
            return receiver.transform.TransformPoint(new Vector3(b.center.x, b.min.y + 0.25f, b.center.z + b.extents.z * 0.2f));
        }

        /// <summary>Rearm point: under the left wing root, where the loader offers the round when a rail cannot be found.</summary>
        public static Vector3 PylonPoint(Aircraft receiver)
        {
            Bounds b = LocalBounds(receiver);
            return receiver.transform.TransformPoint(new Vector3(-b.extents.x * 0.3f, b.min.y + 0.3f, b.center.z + 0.5f));
        }

        private struct CachedBounds
        {
            public Bounds Bounds;
            public float Until;
        }

        private static readonly Dictionary<Aircraft, CachedBounds> boundsCache = new Dictionary<Aircraft, CachedBounds>();

        /// <summary>Size of an airframe whose meshes measure tiny (mesh scale quirks) and whose colliders say nothing: a generic fighter.</summary>
        private static readonly Bounds GenericAirframe = new Bounds(Vector3.zero, new Vector3(11f, 3.5f, 16f));

        /// <summary>Gear down (or moving) hangs wheels under the belly: the bounds would put the tender and the boom too low, so they are re-measured a little later.</summary>
        private static bool GearHangs(Aircraft a) =>
            a.gearState == LandingGear.GearState.LockedExtended || a.gearState == LandingGear.GearState.Extending || a.gearState == LandingGear.GearState.Retracting;

        /// <summary>
        /// Airframe bounds in its own space, from the renderers' own bounds (so banking does not inflate them): a point probe built
        /// from collider extents or a single mesh's bounds measures tiny on some airframes. Falls back to its colliders when the
        /// renderers measure tiny, then to a generic fighter. Cached for good while the gear is up, for half a second while it hangs.
        /// </summary>
        public static Bounds LocalBounds(Aircraft a)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (boundsCache.TryGetValue(a, out CachedBounds cached) && now < cached.Until) return cached.Bounds;
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
            boundsCache[a] = new CachedBounds { Bounds = b, Until = GearHangs(a) ? now + 0.5f : float.PositiveInfinity };
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

    /// <summary>One slot a steered AI receiver flies to: a deck-frame offset of a tanker (the zone's hold slot or a break lane).</summary>
    internal readonly struct SkywellSlot
    {
        public readonly Aircraft Tanker;
        public readonly Vector3 Offset;

        public SkywellSlot(Aircraft tanker, Vector3 offset)
        {
            Tanker = tanker;
            Offset = offset;
        }

        /// <summary>World position of the slot.</summary>
        public Vector3 World => SkywellBoard.Anchor(Tanker) + Tanker.transform.TransformDirection(Offset);
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>RG-240 TRIDENT belly railgun (art: details/detail_railgun.py), carried instead of the bay fighters when
    /// Megaplane.BayPayload is Railgun. Stowed in the bay; when the pilot selects it, or its turret is given a target, the
    /// bay doors open, the elevator lowers the mount below the belly and the native turret is unstowed. Back off: the
    /// turret stows forward, the mount rises once the barrel is home, the doors close. The art yaw ring and cradle copy
    /// the hidden native turret's traverse and elevation, so what fires is what you see.</summary>
    internal sealed class MegaplaneRailgun : MonoBehaviour
    {
        private const float DoorOpen = 100f, DoorRate = 35f, LowerSeconds = 6f, HomeSeconds = 4f, Drop = 10.8f;
        [SerializeField] private Transform elevator, yaw, pitch, elevation;
        [SerializeField] private Turret turret;
        [SerializeField] private Vector3 restLocal, deployedLocal;
        private Quaternion yawRest, pitchRest;
        private float home;   // 0 following the native turret .. 1 art back at rest (stowing)
        private bool remoteWant, lastWant;   // peers follow the host's mount state (selection and targets only resolve host-side)
        private readonly List<(Transform t, Quaternion rest, float sign)> doors = new List<(Transform, Quaternion, float)>();
        private Aircraft aircraft;

        internal float Door { get; private set; }
        internal float Lower { get; private set; }   // 0 stowed in the bay .. 1 down and firing
#if !PUBLIC_RELEASE
        internal bool? Command;                       // sandbox override, null follows the native station
#endif
        internal bool Live => turret != null && Lower >= 1f;
        internal Turret Turret => turret;
        internal Vector3 ArtAim => pitch != null ? transform.InverseTransformDirection(pitch.forward) : Vector3.zero;        // carrier frame
        internal Vector3 NativeAim => elevation != null ? transform.InverseTransformDirection(elevation.forward) : Vector3.zero;
        internal Vector3 MuzzleLocal => pitch != null ? transform.InverseTransformPoint(pitch.position) : Vector3.zero;

        internal void Bind(Dictionary<string, Transform> nodes, Turret t, Transform elev)
        {
            turret = t;
            elevation = elev;
            elevator = nodes["Railgun_Elevator"];
            yaw = nodes["Railgun_Yaw"];
            pitch = nodes["Railgun_Pitch"];
            restLocal = elevator.localPosition;
            // the art's Railgun_Deployed empty sits exactly one authored drop below the elevator rest, span/scale pass
            // included; read it, so the mount always lands where the art says it should
            Vector3 drop = nodes.TryGetValue("Railgun_Deployed", out Transform deployed) && deployed != null
                ? elevator.parent.InverseTransformPoint(deployed.position) - elevator.parent.InverseTransformPoint(elevator.position)
                : elevator.parent.InverseTransformDirection(GetComponent<Aircraft>().transform.up) * -Drop;
            deployedLocal = restLocal + drop;
        }

        private void OnDestroy() => MegaplaneNet.UnregisterRailgun(this);

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            if (yaw != null) { yawRest = yaw.localRotation; pitchRest = pitch.localRotation; }
            foreach (Transform t in GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("FighterDoor_")))
                doors.Add((t, t.localRotation, t.name.EndsWith("_L") ? -1f : 1f));
            MegaplaneNet.RegisterRailgun(this);   // per spawned carrier, not the dormant template copy
        }

        /// <summary>Peer: the host's mount state.</summary>
        internal void RemoteWant(bool value) { if (aircraft != null && !aircraft.IsServer) remoteWant = value; }

        internal bool Wanted => lastWant;   // the net layer's late-joiner refresh
        internal uint UnitId => aircraft != null ? aircraft.persistentID.Id : 0;

        private void Update()
        {
            if (turret == null || aircraft == null) return;
            WeaponStation station = turret.GetWeaponStation();
            bool selected = station != null && aircraft.weaponManager != null && aircraft.weaponManager.currentWeaponStation == station;
            bool want;
            if (aircraft.IsServer)
            {
                // host: the station selection and the turret's target only resolve here; tell every peer the verdict
                bool deploy = selected || turret.GetTarget() != null;
#if !PUBLIC_RELEASE
                deploy = Command ?? deploy;
#endif
                want = !aircraft.disabled && deploy && (station == null || station.Ammo > 0 || selected);
                if (want != lastWant)
                {
                    lastWant = want;
                    MegaplaneNet.Broadcast(aircraft.persistentID.Id, MegaplaneWire.RailgunIndex, (byte)(want ? 1 : 0));
                }
            }
            else want = remoteWant;
            float dt = Time.deltaTime;
            // Stowing: the art swings the barrel home over HomeSeconds (the native turret is already stowed), then the mount rises.
            home = Mathf.MoveTowards(home, want ? 0f : 1f, dt / HomeSeconds);
            if (want) Lower = Door >= DoorOpen - .5f ? Mathf.MoveTowards(Lower, 1f, dt / LowerSeconds) : Lower;
            else if (home >= 1f) Lower = Mathf.MoveTowards(Lower, 0f, dt / LowerSeconds);
            turret.SetStowed(!(want && Lower >= 1f));
            Door = Mathf.MoveTowards(Door, want || Lower > 0f ? DoorOpen : 0f, DoorRate * dt);
            foreach ((Transform t, Quaternion rest, float sign) in doors) if (t != null) t.localRotation = rest * Quaternion.AngleAxis(sign * Door, Vector3.forward);
            elevator.localPosition = Vector3.Lerp(restLocal, deployedLocal, Mathf.SmoothStep(0f, 1f, Lower));
        }

        private void LateUpdate()
        {
            if (turret == null) return;
            // The art takes the native barrel's direction, not its rotation (the donor's transforms carry their own roll).
            Vector3 aim = transform.InverseTransformDirection(elevation.forward);
            float k = 1f - Mathf.SmoothStep(0f, 1f, home);
            float bearing = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg * k, depression = -Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg * k;
            yaw.localRotation = yawRest * Quaternion.AngleAxis(bearing, Vector3.up);
            pitch.localRotation = pitchRest * Quaternion.AngleAxis(depression, Vector3.right);
        }
    }
}

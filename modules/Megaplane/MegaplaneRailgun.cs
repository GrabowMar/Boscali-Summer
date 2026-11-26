using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    internal enum RailgunPhase { Stowed, Opening, Lowering, Deployed, Homing, Raising, Closing }

    /// <summary>The Trident's doors and elevator interlock before the gun can traverse. Stowing waits for the cradle and
    /// recoil to return to their clearance positions. The native gun supplies the shot event on the owner and observers.</summary>
    internal sealed class MegaplaneRailgun : MonoBehaviour
    {
        private const float DoorPercent = 100f, DoorSwing = 154f, DoorRate = 40f, ElevatorRate = 1f / 5f, HomeRate = 30f;
        private const float Drop = 9.756f, RecoilTravel = .88f, RecoilIn = .09f, RecoilOut = .62f, FlashSeconds = .23f;
        private const float SelectionRenewal = 1f, SelectionLease = 3f;
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly AccessTools.FieldRef<Gun, float> RecoilSignal = AccessTools.FieldRefAccess<Gun, float>("recoilEnergy");
        private static readonly Color ArcTint = new Color(.13f, .54f, 1f);

        [SerializeField] private Transform elevator, yaw, pitch, artMuzzle, elevation, nativeMuzzle;
        [SerializeField] private Turret turret;
        [SerializeField] private Gun gun;
        [SerializeField] private Vector3 restLocal, deployedLocal;

        private Quaternion yawRest, pitchRest;
        private Vector3 pitchOrigin, muzzleOrigin, nativeRest;
        private bool remoteWant, lastWant;
        private bool sentSelection;
        private float nextSelection, selectionExpires;
        private Player remotePilot;
        private readonly List<(Transform Door, Quaternion BaseRotation, float Sign, bool Front)> doors = new();
        private Aircraft aircraft;
        private AeroPart carrierPart;
        private MeshRenderer coil;
        private MaterialPropertyBlock glow;
        private Light dischargeLight;
        private int glowSlot = -1;
        private float glowStrength = -1f;
        private float shotAt = float.NegativeInfinity;

        internal RailgunPhase Phase { get; private set; }
        internal float Door { get; private set; }
        internal float Lower { get; private set; }
        internal float Recoil { get; private set; }
        internal int Shots { get; private set; }
        internal float Charge => Mathf.Clamp01((Time.time - shotAt) / MegaplaneGuns.RailInterval);
        internal float CycleProgress => Phase switch
        {
            RailgunPhase.Opening => Door / DoorPercent,
            RailgunPhase.Lowering => Lower,
            RailgunPhase.Homing => 1f - Mathf.Clamp01(HomeError / 150f),
            RailgunPhase.Raising => 1f - Lower,
            RailgunPhase.Closing => 1f - Door / DoorPercent,
            _ => 1f,
        };
#if !PUBLIC_RELEASE
        internal bool? Command;
#endif
        internal bool Live => Healthy && Phase == RailgunPhase.Deployed;
        internal bool Selected => turret != null && aircraft.weaponManager != null && turret.GetWeaponStation() != null &&
            aircraft.weaponManager.currentWeaponStation == turret.GetWeaponStation();
        internal bool RemoteSelected => aircraft != null && remotePilot != null && remotePilot == aircraft.Player && remotePilot.Aircraft == aircraft &&
            remotePilot.HQ != null && remotePilot.HQ == aircraft.NetworkHQ && aircraft.Owner != null &&
            aircraft.Owner == remotePilot.Owner && aircraft.Owner.IsAuthenticated && Time.unscaledTime < selectionExpires;
        internal Turret Turret => turret;
        internal Vector3 ArtAim => pitch != null ? transform.InverseTransformDirection(pitch.forward) : Vector3.zero;
        internal Vector3 NativeAim => elevation != null ? transform.InverseTransformDirection(elevation.forward) : Vector3.zero;
        internal Vector3 MuzzleLocal => pitch != null ? transform.InverseTransformPoint(pitch.position) : Vector3.zero;
        internal float MuzzleError => artMuzzle != null && nativeMuzzle != null ? Vector3.Distance(artMuzzle.position, nativeMuzzle.position) : -1f;
        internal float MuzzleAngle => artMuzzle != null && nativeMuzzle != null ? Vector3.Angle(artMuzzle.forward, nativeMuzzle.forward) : -1f;
        internal float Flash => dischargeLight != null ? dischargeLight.intensity : 0f;
        private bool Healthy => aircraft != null && !aircraft.disabled && turret != null && carrierPart != null &&
            carrierPart.hitPoints > 0f && !carrierPart.IsDetached();
        private float HomeError => Mathf.Max(Quaternion.Angle(yaw.localRotation, yawRest), Quaternion.Angle(pitch.localRotation, pitchRest));

        internal void Bind(Dictionary<string, Transform> nodes, Turret t, Gun weapon, Transform elev, Transform muzzle)
        {
            turret = t;
            gun = weapon;
            elevation = elev;
            nativeMuzzle = muzzle;
            elevator = nodes["Railgun_Elevator"];
            yaw = nodes["Railgun_Yaw"];
            pitch = nodes["Railgun_Pitch"];
            nodes.TryGetValue("Railgun_Muzzle", out artMuzzle);
            restLocal = elevator.localPosition;
            deployedLocal = nodes.TryGetValue("Railgun_Deployed", out Transform deployed) && deployed != null
                ? elevator.parent.InverseTransformPoint(deployed.position)
                : restLocal + elevator.parent.InverseTransformDirection(transform.up) * -Drop;
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            carrierPart = elevator.GetComponentInParent<AeroPart>(true);
            yawRest = yaw.localRotation;
            pitchRest = pitch.localRotation;
            pitchOrigin = pitch.localPosition;
            nativeRest = transform.InverseTransformDirection(elevation.forward);
            if (nativeMuzzle != null) muzzleOrigin = nativeMuzzle.localPosition;
            foreach (Transform node in GetComponentsInChildren<Transform>(true))
            {
                if (!node.name.StartsWith("FighterDoor_", StringComparison.Ordinal)) continue;
                doors.Add((node, node.localRotation, node.name.EndsWith("_L", StringComparison.Ordinal) ? -1f : 1f,
                    node.name.StartsWith("FighterDoor_F_", StringComparison.Ordinal)));
            }
            FitDischargeFeedback();
            MegaplaneNet.RegisterRailgun(this);
        }

        private void FitDischargeFeedback()
        {
            coil = pitch.GetComponent<MeshRenderer>();
            if (coil != null)
                glowSlot = Array.FindIndex(coil.sharedMaterials, material => material != null &&
                    material.name.StartsWith("BS_Megaplane_RailgunArc_Emit", StringComparison.Ordinal));
            if (glowSlot >= 0) glow = new MaterialPropertyBlock();
            if (artMuzzle != null)
            {
                dischargeLight = artMuzzle.gameObject.AddComponent<Light>();
                dischargeLight.color = ArcTint;
                dischargeLight.range = 42f;
                dischargeLight.shadows = LightShadows.None;
                dischargeLight.enabled = false;
            }
            SetGlow(0f);
        }

        private void OnDestroy() => MegaplaneNet.UnregisterRailgun(this);

        /// <summary>The host replicates the requested position. Each peer follows the same mechanical interlocks.</summary>
        internal void RemoteWant(bool value) { if (aircraft != null && !aircraft.IsServer) remoteWant = value; }

        internal void SetRemoteSelection(Player pilot, bool selected)
        {
            remotePilot = selected ? pilot : null;
            selectionExpires = Time.unscaledTime + SelectionLease;
        }

        private void ReportSelection()
        {
            Player pilot = aircraft.Player;
            if (!aircraft.LocalSim || !aircraft.HasAuthority || !aircraft.persistentID.IsValid || pilot == null ||
                !pilot.IsLocalPlayer || pilot.Aircraft != aircraft || pilot.HQ == null || pilot.HQ != aircraft.NetworkHQ)
            {
                sentSelection = false;
                return;
            }
            bool selected = Healthy && Selected;
            if (selected == sentSelection && (!selected || Time.unscaledTime < nextSelection)) return;
            if (MegaplaneNet.Ask(aircraft, selected ? MegaplaneWire.SelectRailVerb : MegaplaneWire.DeselectRailVerb))
            {
                sentSelection = selected;
                nextSelection = Time.unscaledTime + SelectionRenewal;
            }
        }

        internal bool Wanted => aircraft != null && aircraft.IsServer ? lastWant : remoteWant;
        internal uint UnitId => aircraft != null ? aircraft.persistentID.Id : 0;

        private void Update()
        {
            if (aircraft == null || elevator == null) return;
            bool want = remoteWant;
            if (aircraft.IsServer)
            {
                WeaponStation station = turret != null ? turret.GetWeaponStation() : null;
                Unit target = turret != null ? turret.GetTarget() : null;
                // The native station RPC only updates client observers. Dedicated hosts receive the authenticated
                // selection separately and retain the native local station as the pilot's own UI state.
                bool remoteSelected = RemoteSelected;
                if (!remoteSelected) remotePilot = null;
                bool pilotSelected = remoteSelected || (aircraft.Player != null && aircraft.Player.IsLocalPlayer && Selected);
                bool deploy = pilotSelected || (target != null && !target.disabled);
#if !PUBLIC_RELEASE
                deploy = Command ?? deploy;
#endif
                want = deploy && (station == null || station.Ammo > 0 || pilotSelected);
            }
            else ReportSelection();
            want &= Healthy;
            if (aircraft.IsServer && want != lastWant)
            {
                lastWant = want;
                MegaplaneNet.Broadcast(UnitId, MegaplaneWire.RailgunIndex, (byte)(want ? 1 : 0));
            }
            float previousDoor = Door, previousLower = Lower;
            Step(want, Time.deltaTime);
            if (turret != null) turret.SetStowed(!Live);
            if (previousLower != Lower)
                elevator.localPosition = Vector3.Lerp(restLocal, deployedLocal, Mathf.SmoothStep(0f, 1f, Lower));
            if (previousDoor == Door) return;
            foreach ((Transform door, Quaternion rest, float sign, bool front) in doors)
                if (door != null && door.IsChildOf(transform))
                {
                    float t = front ? Mathf.InverseLerp(0f, 87f, Door) : Mathf.InverseLerp(13f, DoorPercent, Door);
                    door.localRotation = rest * Quaternion.AngleAxis(sign * Mathf.SmoothStep(0f, DoorSwing, t), Vector3.forward);
                }
        }

        private void Step(bool want, float dt)
        {
            if (want)
            {
                if (Door < DoorPercent)
                {
                    Phase = RailgunPhase.Opening;
                    Door = Mathf.MoveTowards(Door, DoorPercent, DoorRate * dt);
                }
                else if (Lower < 1f)
                {
                    Phase = RailgunPhase.Lowering;
                    Lower = Mathf.MoveTowards(Lower, 1f, ElevatorRate * dt);
                }
                else Phase = RailgunPhase.Deployed;
                return;
            }
            if (Lower > 0f)
            {
                if (HomeError > .35f || Recoil > .015f || (turret != null && Vector3.Angle(NativeAim, nativeRest) > 1.5f))
                    Phase = RailgunPhase.Homing;
                else
                {
                    Phase = RailgunPhase.Raising;
                    Lower = Mathf.MoveTowards(Lower, 0f, ElevatorRate * dt);
                }
            }
            else if (Door > 0f)
            {
                Phase = RailgunPhase.Closing;
                Door = Mathf.MoveTowards(Door, 0f, DoorRate * dt);
            }
            else Phase = RailgunPhase.Stowed;
        }

        private void LateUpdate()
        {
            if (gun != null && RecoilSignal(gun) > 0f)
            {
                RecoilSignal(gun) = 0f;
                shotAt = Time.time;
                Shots++;
            }
            float shotAge = Time.time - shotAt;
            float stroke = shotAge < RecoilIn ? Mathf.InverseLerp(0f, RecoilIn, shotAge) : 1f - Mathf.InverseLerp(RecoilIn, RecoilOut, shotAge);
            Recoil = Mathf.SmoothStep(0f, RecoilTravel, stroke);
            if (Live)
            {
                Vector3 aim = NativeAim;
                float bearing = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
                float depression = -Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
                yaw.localRotation = yawRest * Quaternion.AngleAxis(bearing, Vector3.up);
                pitch.localRotation = pitchRest * Quaternion.AngleAxis(depression, Vector3.right);
            }
            else
            {
                float step = HomeRate * Time.deltaTime;
                yaw.localRotation = Quaternion.RotateTowards(yaw.localRotation, yawRest, step);
                pitch.localRotation = Quaternion.RotateTowards(pitch.localRotation, pitchRest, step);
            }
            pitch.localPosition = pitchOrigin + pitch.localRotation * (Vector3.back * Recoil);
            if (nativeMuzzle != null) nativeMuzzle.localPosition = muzzleOrigin + Vector3.back * Recoil;

            float flash = 1f - Mathf.InverseLerp(0f, FlashSeconds, shotAge);
            SetGlow(Healthy ? (Live ? .15f + 1.7f * Charge : .14f * Lower) + 7.5f * flash : 0f);
            if (dischargeLight != null)
            {
                dischargeLight.enabled = Healthy && flash > 0f;
                dischargeLight.intensity = 12f * flash;
            }
        }

        private void SetGlow(float strength)
        {
            if (coil == null || glowSlot < 0 || Mathf.Approximately(glowStrength, strength)) return;
            glowStrength = strength;
            glow.SetColor(Emission, ArcTint * strength);
            coil.SetPropertyBlock(glow, glowSlot);
        }
    }
}

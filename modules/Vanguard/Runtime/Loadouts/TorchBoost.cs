using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// SRB-2 TORCH strap-on boost rockets, a runtime store (<see cref="RuntimeStores"/>) built from the AGM heavy rack: the missile
    /// goes, a vanilla VLS booster casing (with its plume, smoke trail, lights and roar) takes its place. Firing the station
    /// lights every bottle on the aircraft at once; <see cref="TorchPod"/> pushes along the nose for TorchProfile.BurnSeconds, then
    /// the casings fall away. The game replays the firing on the other peers through the station (Aircraft.RpcLaunchMissile, the
    /// way MountedMissile does), so no message of our own is needed. The AI never picks it: it has no effectiveness or range.
    /// </summary>
    internal static class TorchBoost
    {
        public static readonly Type[] PatchTypes = Type.EmptyTypes;
        private const string Name = "SRB-2 TORCH";
        private const float AxisBelowSaddle = 0.273f; // models/Torch.py: the case axis under the saddle top (the model's origin)
        private static VLSBooster booster;

        public static void Register(ModuleContext context)
        {
            RuntimeStores.Add(new StoreRecipe
            {
                Key = VanguardKeys.TorchMount,
                Donor = "AGM_heavy_single",
                Carriers = new[] { "AGM_heavy_single", "AGM_heavy_triple", "ARM1_single" },
                Ready = () => Booster() != null,
                Build = Build
            });
            context.AddSceneService<TorchHud>(60);
        }

        /// <summary>The first vanilla VLS booster found on a mount's missile prefab (the anti-ship missiles carry one).</summary>
        private static VLSBooster Booster()
        {
            if (booster != null) return booster;
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (e == null) return null;
            foreach (WeaponMount mount in e.weaponMounts)
            {
                GameObject prefab = mount != null && mount.info != null ? mount.info.weaponPrefab : null;
                VLSBooster found = prefab != null ? prefab.GetComponentInChildren<VLSBooster>(true) : null;
                if (found != null) return booster = found;
            }
            return null;
        }

        private static void Build(WeaponMount mount)
        {
            GameObject root = mount.prefab;
            Vector3 at = Vector3.zero;
            Quaternion turn = Quaternion.identity;
            Weapon[] rails = root.GetComponentsInChildren<Weapon>(true);
            if (rails.Length > 0)
            {
                at = root.transform.InverseTransformPoint(rails[0].transform.position);
                turn = Quaternion.Inverse(root.transform.rotation) * rails[0].transform.rotation;
            }
            foreach (Weapon rail in rails)
                if (rail != null) UnityEngine.Object.DestroyImmediate(rail.transform == root.transform ? (UnityEngine.Object)rail : rail.gameObject);

            // The bottle is the booster cloned whole, under the inactive template so nothing wakes: only its VLSBooster brain goes
            // (it would reach for a missile that is not there), plus the collider and the other scripts; the trail emitters stay.
            VLSBooster source = Booster();
            GameObject bottle = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
            bottle.name = "TorchBottle";
            foreach (MonoBehaviour part in bottle.GetComponentsInChildren<MonoBehaviour>(true))
                if (part != null && !(part is TrailEmitter)) UnityEngine.Object.DestroyImmediate(part);
            foreach (Collider collider in bottle.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);

            // Long axis of the casing onto the pylon's forward, scaled to the bottle's length.
            MeshFilter shape = bottle.GetComponent<MeshFilter>();
            Vector3 size = Vector3.Scale(shape != null && shape.sharedMesh != null ? shape.sharedMesh.bounds.size : Vector3.forward, source.transform.lossyScale);
            int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
            Vector3 along = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            bottle.transform.localPosition = at;
            bottle.transform.localRotation = turn * Quaternion.FromToRotation(along, Vector3.forward);
            bottle.transform.localScale = source.transform.localScale * (TorchProfile.BottleLength / Mathf.Max(.01f, size[axis]));
            if (StoreArt.Prefab(StoreArt.Torch) != null)
            {
                // The baked bottle: its case axis where the rack's store axis was (Torch.py: origin = saddle top, axis below it).
                // The booster stays only as the plume, smoke, lamps and roar, its aft end moved onto the nozzle exit.
                GameObject plume = bottle;
                plume.name = "TorchPlume";
                bottle = new GameObject("TorchBottle");
                bottle.transform.SetParent(root.transform, false);
                bottle.transform.localPosition = at;
                bottle.transform.localRotation = turn;
                GameObject model = StoreArt.Spawn(StoreArt.Torch, bottle.transform);
                model.transform.localPosition = Vector3.up * AxisBelowSaddle;
                StoreArt.Bounds(bottle.transform, model.transform, out Bounds body);
                StoreArt.Bounds(bottle.transform, plume.transform, out Bounds casing);
                plume.transform.SetParent(bottle.transform, true);
                plume.transform.localPosition += Vector3.forward * (body.min.z - casing.min.z);
                if (plume.TryGetComponent(out MeshRenderer drum)) UnityEngine.Object.DestroyImmediate(drum);
                if (plume.TryGetComponent(out MeshFilter drumShape)) UnityEngine.Object.DestroyImmediate(drumShape);
            }
            TorchFx.Quiet(bottle);
            bottle.AddComponent<TorchFx>();

            WeaponInfo info = UnityEngine.Object.Instantiate(mount.info);
            info.name = "WI_VG_Torch";
            info.weaponPrefab = null;
            info.weaponName = Name;
            info.shortName = "TORCH";
            info.description = "Strap-on boost rockets, burned like a throttle: hold fire and every TORCH bottle pushes along the nose at a full g, let go and they wait. Fifteen seconds of full thrust per load; dry casings drop away, and the burn shows up hot to heat seekers.";
            info.effectiveness = default;      // no role: the AI never scores it (AnalyzeTarget returns 0 before any range test)
            info.targetRequirements = new TargetRequirements { maxRange = 1000f }; // the missile HUD's range ladder divides by maxRange
            info.pK = 0f;
            info.fireInterval = 0f; // a gun-style station: the game calls Fire every frame the trigger is held
            info.muzzleVelocity = 0f;
            info.maxSpeed = 0f;
            info.pierceDamage = 0f;
            info.blastDamage = 0f;
            info.visibilityWhenFired = 0f;
            info.costPerRound = TorchProfile.Cost;
            info.massPerRound = TorchProfile.FuelKg;
            info.missile = info.bomb = info.glideBomb = info.boresight = info.laserGuided = info.overHorizon = info.nuclear = info.strategic = false;
            info.useWeaponDoors = false;

            TorchPod pod = bottle.AddComponent<TorchPod>();
            pod.info = info;
            pod.Rearmable = true;

            mount.info = info;
            mount.mountName = Name;
            mount.ammo = 1;
            mount.emptyMass = TorchProfile.EmptyKg;
            mount.mass = TorchProfile.EmptyKg + TorchProfile.FuelKg;
        }
    }

    /// <summary>
    /// One TORCH bottle, burned like a throttle while the pilot holds fire (the station has no fire interval, so the game calls
    /// Fire every frame the trigger is down). Runs on every peer: the flying peer burns while the trigger is held and, every
    /// half second of the burn, replays it to the others through the station's launch RPC, which keeps their plume lit a moment
    /// longer than the gap; every peer counts its fuel off the pylon and draws the plume (<see cref="TorchFx"/>). Only the flying
    /// peer pushes, along the nose, with the aircraft's mass split over the lit bottles, on the aircraft body (never through the
    /// wing joint). A dry bottle drops away; rearming refills it.
    /// </summary>
    internal sealed class TorchPod : Weapon
    {
        private static readonly Dictionary<Aircraft, List<TorchPod>> Fleet = new Dictionary<Aircraft, List<TorchPod>>();
        private static readonly Dictionary<Aircraft, float> HeldUntil = new Dictionary<Aircraft, float>();
        private static readonly Dictionary<Aircraft, float> NextReplay = new Dictionary<Aircraft, float>();
        private static readonly FieldInfo NextRound = AccessTools.Field(typeof(WeaponStation), "weaponIndex");
        private Aircraft craft;
        private TorchFx fx;
        private bool burning, spent;
        private float throttle, fuel;

        /// <summary>Lit bottles on this aircraft, full-thrust seconds left in the fullest one, and the throttle they share.</summary>
        public static bool Burn(Aircraft aircraft, out int lit, out float left, out float thrust)
        {
            lit = 0;
            left = thrust = 0f;
            if (aircraft == null || !Fleet.TryGetValue(aircraft, out List<TorchPod> pods)) return false;
            foreach (TorchPod pod in pods)
            {
                left = Mathf.Max(left, TorchProfile.SecondsLeft(pod.fuel));
                if (!pod.burning) continue;
                lit++;
                thrust = Mathf.Max(thrust, pod.throttle);
            }
            return lit > 0;
        }

        public override void AttachToHardpoint(Aircraft aircraft, Hardpoint point, WeaponMount weaponMount)
        {
            base.AttachToHardpoint(aircraft, point, weaponMount);
            craft = aircraft;
            fx = GetComponent<TorchFx>();
            ammo = 1;
            fuel = info.massPerRound;
            point.ModifyMass(fuel);
            if (!Fleet.TryGetValue(aircraft, out List<TorchPod> pods)) Fleet[aircraft] = pods = new List<TorchPod>();
            pods.Add(this);
        }

        public override void Fire(Unit owner, Unit target, Vector3 inheritedVelocity, WeaponStation station, GlobalPosition aimpoint)
        {
            if (craft == null) return;
            float now = Time.timeSinceLevelLoad;
            // The trigger (WeaponStation.Fire, every frame while held) passes no aim point; a replay (LaunchMount) carries one.
            bool replay = !aimpoint.Equals(default(GlobalPosition));
            if (replay)
            {
                if (craft.LocalSim) return; // our own replay coming back: the trigger already drives this peer
                HeldUntil[craft] = now + TorchProfile.ReplayHold;
                if (station == null) return;
                NextRound?.SetValue(station, -1); // LaunchMount steps the round index after this call: keep it on the first bottle
                station.Ammo += 1;                // and it counts a round fired: give it back, the fuel is the real count
                return;
            }
            HeldUntil[craft] = now + TorchProfile.HoldGrace;
            if (station == null || !craft.LocalSim || !Lit(craft)) return;
            if (NextReplay.TryGetValue(craft, out float due) && now < due) return;
            NextReplay[craft] = now + TorchProfile.ReplayInterval;
            GlobalPosition ahead = craft.GlobalPosition() + craft.transform.forward * 50000f;
            if (craft.IsServer) craft.RpcLaunchMissile(station.Number, target, ahead);
            else if (craft.HasAuthority) craft.CmdLaunchMissile(station.Number, target, ahead);
        }

        public override void Rearm(int ammoToRearm, WeaponStation station)
        {
            if (burning) Quench();
            if (fuel >= info.massPerRound && !spent) return;
            hardpoint.ModifyMass(info.massPerRound - fuel);
            fuel = info.massPerRound;
            spent = false;
            throttle = 0f;
            ammo = 1;
            fx?.Restore();
            if (station != null) NextRound?.SetValue(station, 0);
            ReportReloading(false);
        }

        public override int GetAmmoLoaded() => ammo;

        public override int GetAmmoTotal() => ammo;

        public override int GetFullAmmo() => 1;

        private static bool Lit(Aircraft aircraft)
        {
            if (!Fleet.TryGetValue(aircraft, out List<TorchPod> pods)) return false;
            foreach (TorchPod pod in pods) if (pod.burning) return true;
            return false;
        }

        private void FixedUpdate()
        {
            if (craft == null || spent) return;
            float dt = Time.fixedDeltaTime;
            bool held = fuel > 0f && IsAttached() && HeldUntil.TryGetValue(craft, out float until) && Time.timeSinceLevelLoad < until;
            throttle = TorchProfile.Throttle(throttle, held, dt);
            if (throttle > 0f && !burning)
            {
                burning = true;
                fx?.Ignite(craft);
            }
            if (!burning) return;
            float used = TorchProfile.Burn(fuel, throttle, dt);
            fuel -= used;
            hardpoint.ModifyMass(-used);
            if (craft.LocalSim && craft.rb != null && IsAttached())
            {
                int lit = 0;
                foreach (TorchPod pod in Fleet[craft]) if (pod.burning) lit++;
                craft.rb.AddForce(craft.transform.forward * TorchProfile.Force(craft.GetMass(), lit, throttle));
            }
            if (fuel <= 0f || !IsAttached()) Burnout();
            else if (throttle <= 0f) Quench();
        }

        private void Update()
        {
            if (burning) fx?.Tick(throttle);
        }

        /// <summary>Released: the plume goes out, the bottle stays on the pylon with what fuel it has.</summary>
        private void Quench()
        {
            burning = false;
            throttle = 0f;
            fx?.Out();
        }

        /// <summary>Dry (or its wing is gone): out, and the casing drops away.</summary>
        private void Burnout()
        {
            Quench();
            spent = true;
            ammo = 0;
            if (fuel > 0f) hardpoint.ModifyMass(-fuel);
            fuel = 0f;
            fx?.Separate();
            if (weaponStation != null) weaponStation.Ammo = Mathf.Max(0, weaponStation.Ammo - 1);
        }

        private void OnDestroy()
        {
            if (hardpoint != null && fuel > 0f) hardpoint.ModifyMass(-fuel);
            if (craft is null || !Fleet.TryGetValue(craft, out List<TorchPod> pods)) return;
            pods.Remove(this);
            if (pods.Count == 0) Fleet.Remove(craft);
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// PCU-9 STORMCELL: a hidden capacitor pod cloned from the ECM pod. While it hangs on an aircraft the power supply holds more
    /// charge and refills faster, so lasers and jammers fire longer. Every peer runs it; contributions are per pod instance
    /// (one per hardpoint) and reversed exactly on removal.
    /// </summary>
    internal static class StormcellPod
    {
        /// <summary>Pylons that carry both recon and capacitor pods: every station offering one of these offers the pod too.</summary>
        internal static readonly string[] Carriers = { "SpecialFlarePod", "ECMPod1", "ARM1_single" };
        public static readonly Type[] PatchTypes = new Type[0];

        private static readonly AccessTools.FieldRef<PowerSupply, float> MaxCharge = Ref("maxCharge"), ChargeRate = Ref("chargePerRPM");
        private static readonly Dictionary<PowerSupply, RateBoost> Boosts = new Dictionary<PowerSupply, RateBoost>();
        private static bool logged;

        private sealed class RateBoost
        {
            public float Original, OriginalMax;
            public int Pods;
        }

        public static void Register(ModuleContext context) =>
            RuntimeStores.Add(new StoreRecipe
            {
                Key = VanguardKeys.StormcellMount, Donor = "ECMPod1", Carriers = Carriers,
                Build = mount => HidePod<StormcellUnit>(mount, "PCU-9 Stormcell", 180f, 0.4f, StoreArt.Stormcell)
            });

        /// <summary>Turns a cloned pod mount into a hidden one: its own Weapon/Countermeasure components go (the ECM pod's RadarJammer already
        /// edits the capacitance in Awake and would double-count), <typeparamref name="T"/> takes their place.</summary>
        internal static void HidePod<T>(WeaponMount mount, string name, float massKg, float cost, string art) where T : Weapon, IHiddenPod
        {
            Debug.Log("[Vanguard] " + name + " donor " + mount.jsonKey + ": emptyMass " + mount.emptyMass + ", emptyCost " + mount.emptyCost);
            foreach (Weapon weapon in mount.prefab.GetComponentsInChildren<Weapon>(true)) UnityEngine.Object.DestroyImmediate(weapon);
            foreach (Countermeasure cm in mount.prefab.GetComponentsInChildren<Countermeasure>(true)) UnityEngine.Object.DestroyImmediate(cm);
            StoreArt.Replace(mount.prefab.transform, art); // the baked pod in the donor pod's place; the donor's look stays without the bundle
            mount.prefab.AddComponent<T>();
            mount.countermeasure = mount.radar = mount.turret = false; // SpawnMount would look for a Countermeasure, Radar or Turret in the prefab
            mount.info = null; // every cost / rearm / selector path in the game tolerates a mount without one
            mount.mountName = name;
            mount.emptyMass = massKg;
            mount.emptyCost = cost;
        }

        /// <summary>Adds this pod's capacity and the charge-rate boost; returns the capacity to hand back to <see cref="Unfit"/>.</summary>
        internal static float Fit(PowerSupply supply)
        {
            // Sized on the capacity before any pod: a second pod, or a respawned loadout whose old pod is destroyed only at the end
            // of the frame, must not read an already boosted maxCharge.
            if (!Boosts.TryGetValue(supply, out RateBoost boost))
            {
                boost = new RateBoost { Original = ChargeRate != null ? ChargeRate(supply) : 0f, OriginalMax = MaxCharge != null ? MaxCharge(supply) : 0f };
                Boosts[supply] = boost;
                if (ChargeRate != null) ChargeRate(supply) = boost.Original * PodRules.ChargeRateScale;
            }
            boost.Pods++;
            float baseMax = boost.OriginalMax;
            float extra = PodRules.ExtraCapacity(baseMax);
            supply.ModifyCapacitance(extra);
            supply.enabled = true; // ModifyCapacitance does not wake a full, idle supply
            if (!logged)
            {
                logged = true;
                Debug.Log("[Vanguard] STORMCELL: vanilla maxCharge " + baseMax + " -> +" + extra + ", chargePerRPM " + (ChargeRate != null ? ChargeRate(supply) : 0f));
            }
            return extra;
        }

        internal static void Unfit(PowerSupply supply, float extra)
        {
            supply.ModifyCapacitance(-extra);
            if (Boosts.TryGetValue(supply, out RateBoost boost) && --boost.Pods <= 0)
            {
                if (ChargeRate != null) ChargeRate(supply) = boost.Original;
                Boosts.Remove(supply);
            }
            supply.enabled = true; // let FixedUpdate clamp the charge to the smaller capacity
        }

        private static AccessTools.FieldRef<PowerSupply, float> Ref(string field)
        {
            try { return AccessTools.Field(typeof(PowerSupply), field) != null ? AccessTools.FieldRefAccess<PowerSupply, float>(field) : null; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>The STORMCELL on one hardpoint. A Weapon only so the game spawns and attaches it with the mount; it never gets a station.</summary>
    internal sealed class StormcellUnit : Weapon, IHiddenPod
    {
        private PowerSupply supply;
        private float extra;
        private Aircraft carrier;
        private Transform rotor; // the baked model's ram-air turbine (models/Stormcell.py joint "Rotor"), null without the bundle

        public override void AttachToHardpoint(Aircraft aircraft, Hardpoint hardpoint, WeaponMount weaponMount)
        {
            base.AttachToHardpoint(aircraft, hardpoint, weaponMount);
            carrier = aircraft;
            if (rotor == null) rotor = StoreArt.Joint(transform, "Rotor");
            if (supply != null) return;
            supply = aircraft.GetPowerSupply();
            if (supply != null) extra = StormcellPod.Fit(supply);
        }

        public override float GetMass() => 0f; // info is null on this mount

        // Visual only: the turbine windmills with airspeed, up to about 12 turns a second.
        private void Update()
        {
            if (rotor != null && carrier != null)
                rotor.Rotate(transform.forward, Mathf.Min(carrier.speed * 30f, 4300f) * Time.deltaTime, Space.World);
        }

        private void OnDestroy()
        {
            if (supply != null) StormcellPod.Unfit(supply, extra);
            supply = null;
        }
    }
}

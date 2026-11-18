using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>A single strategic gravity bomb occupying a transport's entire pair of cargo slots.</summary>
    internal static class Sa74Store
    {
        public const string Key = "VG_Sa74N";
        public const string MountKey = "VG_Sa74N_Cargo";
        public const float MassKg = 18000f;
        public const float Price = 74000f;
        private const int PrefabHash = 0x53413734;
        private static readonly Dictionary<string, CargoPair> Pairs = new Dictionary<string, CargoPair>();
        private static readonly FieldInfo MountField = AccessTools.Field(typeof(Weapon), "mount");
        private static MissileDefinition definition;
        private static WeaponInfo info;
        private static MountedMissile donorRail;
        private static ClientObjectManager registeredWith;
        private static StoreRecipe recipe;

        internal readonly struct CargoPair
        {
            public readonly int Lead, Partner;
            public CargoPair(int lead, int partner) { Lead = lead; Partner = partner; }
        }

        public static readonly Type[] PatchTypes =
        {
            typeof(Sa74BlockedPatch), typeof(Sa74PrefabsPatch), typeof(Sa74DetonatePatch), typeof(Sa74ReleasedPatch),
            typeof(Sa74DetonationReplayPatch), typeof(Sa74SavedLoadoutPatch), typeof(Sa74BlastTargetsPatch), typeof(Sa74WinterPatch),
            typeof(Sa74PressureTimingPatch), typeof(Presentation.Sa74NativeBlastAudioPatch),
            typeof(Sa74BuildingRegistrationPatch), typeof(Sa74MapBuildingRegistrationPatch)
        };

        public static void Register(ModuleContext context)
        {
            recipe = new StoreRecipe
            {
                Key = MountKey, Donor = VanguardKeys.SkywellMount,
                Carriers = new[] { VanguardKeys.SkywellMount },
                Ready = BuildMissile, Build = BuildMount
            };
            RuntimeStores.Add(recipe);
            context.AddSceneService<Sa74CargoService>(61);
            context.AddSceneService<Sa74Fallout>(62);
            context.AddService<NativeWeatherOverride>(Sa74Fallout.Winter);
            context.AddSceneService<Networking.Sa74Net>(62);
            context.AddSceneService<Sa74CloudClearance>(63);
            context.AddSceneService<Presentation.Sa74Mushroom>(63);
            context.AddSceneService<Presentation.Sa74Particles>(63);
            context.AddSceneService<Presentation.Sa74BlastAudio>(64);
            context.AddSceneService<Sa74Destruction>(64);
        }

        public static bool Is(Missile missile) => missile != null && missile.definition != null && missile.definition.jsonKey == Key;
        public static bool Is(Weapon weapon) => weapon != null && MountField.GetValue(weapon) is WeaponMount mount && mount.jsonKey == MountKey;
        public static bool Is(WeaponMount mount) => mount != null && mount.jsonKey == MountKey;
        public static int PairCount => Pairs.Count;
        public static IEnumerable<string> EligibleAircraft => Pairs.Keys;
        public static bool Pair(string aircraft, out CargoPair pair) { Resolve(); return Pairs.TryGetValue(aircraft ?? "", out pair); }

        private static bool BuildMissile()
        {
            if (definition != null) return true;
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (e == null) return false;
            // Fixed vanilla identity: an addon loading earlier on one peer cannot change the bomb's physics.
            WeaponMount donor = RuntimeStores.FindMount("bomb_125_double");
            if (donor?.info?.weaponPrefab == null || donor.prefab == null ||
                donor.info.weaponPrefab.GetComponent<Missile>() == null || donor.prefab.GetComponentInChildren<MountedMissile>(true) == null) return false;
            donorRail = donor.prefab.GetComponentInChildren<MountedMissile>(true);
            Missile donorMissile = donor.info.weaponPrefab.GetComponent<Missile>();
            GameObject prefab = RuntimeStores.Template(donor.info.weaponPrefab, Key);
            Missile missile = prefab.GetComponent<Missile>();
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Sa74Model.Attach(prefab.transform);
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            CapsuleCollider body = prefab.AddComponent<CapsuleCollider>();
            body.direction = 2; body.radius = 1.5f; body.height = 8.4f;
            prefab.GetComponent<NetworkIdentity>().PrefabHash = PrefabHash;
            prefab.AddComponent<Sa74Flight>();

            info = UnityEngine.Object.Instantiate(donor.info);
            info.name = "WI_" + Key;
            info.weaponName = "SA-74N";
            info.shortName = "SA-74N";
            info.description = "74-megaton strategic gravity bomb. One 18-tonne weapon consumes both cargo slots. Impact detonation permanently devastates the mission map and starts fallout, radioactive ash and nuclear winter until the mission ends. Requires three seconds and 400 metres of separation to arm. ESCAPE BEFORE IMPACT.";
            info.weaponPrefab = prefab;
            info.massPerRound = MassKg;
            info.costPerRound = Price;
            info.blastDamage = 74000000000f;
            info.pierceDamage = 0f;
            info.airburstHeight = 0f;
            info.nuclear = info.strategic = info.bomb = true;
            info.cargo = info.troops = info.sling = info.missile = info.glideBomb = info.laserGuided = false;

            definition = UnityEngine.Object.Instantiate((MissileDefinition)donorMissile.definition);
            definition.name = definition.jsonKey = Key;
            definition.unitName = "SA-74N";
            definition.description = info.description;
            definition.mass = MassKg;
            definition.value = Price;
            definition.length = 8.4f; definition.width = 3.4f; definition.height = 3f;
            definition.radarSize = 10f;
            definition.unitPrefab = prefab;
            missile.definition = definition;
            Set(missile, "info", info);
            Set(missile, "mass", MassKg);
            Set(missile, "blastYield", 0f); // Sa74Fallout owns the sole authoritative blast.
            Set(missile, "pierceDamage", 0f);
            Set(missile, "impactFuse", true);
            Set(missile, "impactFuseDelay", 0f);
            Set(missile, "finArea", 5f);
            Set(missile, "torque", 0f); // Native bomb fuse/fin deployment stays; the weapon itself is unguided.
            Set(missile, "foldingFins", Array.CreateInstance(AccessTools.Field(typeof(Missile), "foldingFins").FieldType.GetElementType(), 0));
            Set(missile, "proxyFuse", null);
            foreach (object motor in (Array)AccessTools.Field(typeof(Missile), "motors").GetValue(missile))
            {
                Set(motor, "thrust", 0f); Set(motor, "fuelMass", 0f);
            }
            RuntimeStores.AppendMissile(definition);
            EnsureRegistered(false);
            return true;
        }

        private static void BuildMount(WeaponMount mount)
        {
            // SKYWELL supplies a MountedCargo in the cargo frame. Keep its authored anchor and ramp handling;
            // the native bomb's MountedMissile supplies the missile-specific network spawn and CCIP release.
            GameObject source = mount.prefab;
            MountedCargo sourceCargo = source.GetComponentInChildren<MountedCargo>(true);
            if (sourceCargo == null) throw new InvalidOperationException("SKYWELL cargo anchor is missing");
            Vector3 anchor = source.transform.InverseTransformPoint(sourceCargo.transform.position);
            Quaternion rotation = Quaternion.Inverse(source.transform.rotation) * sourceCargo.transform.rotation;
            var root = new GameObject(MountKey);
            root.transform.SetParent(RuntimeStores.Holder, false);
            MountedMissile rail = UnityEngine.Object.Instantiate(donorRail, root.transform);
            mount.prefab = root;
            UnityEngine.Object.DestroyImmediate(source);
            rail.name = "SA74N_CargoRelease";
            rail.transform.localPosition = anchor;
            rail.transform.localRotation = rotation;
            rail.transform.localScale = Vector3.one;
            rail.gameObject.SetActive(true);
            rail.info = info;
            foreach (Renderer renderer in rail.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in rail.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            Sa74Model.Attach(rail.transform);
            Set(rail, "railDirection", MountedMissile.RailDirection.Down);
            Set(rail, "railSpeed", 6f);
            Set(rail, "railLength", 3f);
            Set(rail, "railDelay", 1.5f);
            mount.info = info;
            mount.mountName = "SA-74N (both cargo slots)";
            mount.Cargo = true;
            mount.Troops = mount.missileBay = mount.turret = mount.radar = mount.countermeasure = false;
            mount.ammo = 1;
            mount.mass = MassKg;
            mount.emptyMass = 150f;
            mount.drag = 8f;
            mount.RCS = 12f;
            mount.prefab.AddComponent<Sa74Seat>();
        }

        private static bool IsNativeCargo(WeaponMount mount) => mount != null && !Is(mount) && mount.prefab != null &&
            mount.prefab.GetComponentInChildren<MountedCargo>(true) != null;

        /// <summary>Two physical native cargo positions, represented either by separate sets or by a two-slot cargo rack.</summary>
        public static void Resolve()
        {
            WeaponMount store = RuntimeStores.Mount(MountKey);
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (store == null || e == null) return;
            foreach (AircraftDefinition aircraft in e.aircraft)
            {
                if (aircraft == null || aircraft.unitPrefab == null || Pairs.ContainsKey(aircraft.jsonKey)) continue;
                HardpointSet[] sets = aircraft.unitPrefab.GetComponentInChildren<WeaponManager>(true)?.hardpointSets;
                if (sets == null || !sets.Any(s => s.weaponOptions.Contains(store))) continue;
                var cargo = new List<int>();
                for (int i = 0; i < sets.Length; i++)
                    if (sets[i].hardpoints.Count == 1 && sets[i].weaponOptions.Any(IsNativeCargo)) cargo.Add(i);
                CargoPair pair;
                if (cargo.Count >= 2) pair = new CargoPair(cargo[0], cargo[1]);
                else if (cargo.Count == 1 && sets[cargo[0]].weaponOptions.Any(m => IsNativeCargo(m) && m.prefab.GetComponentsInChildren<MountedCargo>(true).Length >= 2))
                    pair = new CargoPair(cargo[0], cargo[0]);
                else { foreach (HardpointSet set in sets) set.weaponOptions.Remove(store); continue; }
                Pairs[aircraft.jsonKey] = pair;
                for (int i = 0; i < sets.Length; i++) if (i != pair.Lead) sets[i].weaponOptions.Remove(store);
                foreach (Aircraft live in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                    if (live.definition == aircraft && live.weaponManager != null)
                        for (int i = 0; i < live.weaponManager.hardpointSets.Length; i++)
                            if (i != pair.Lead) live.weaponManager.hardpointSets[i].weaponOptions.Remove(store);
                Debug.Log("[Vanguard] SA-74N consumes both cargo slots on " + aircraft.jsonKey + " (" + pair.Lead + "," + pair.Partner + ")");
            }
        }

        public static bool Blocks(HardpointSet set, Loadout loadout)
        {
            if (set == null || loadout?.weapons == null) return false;
            Resolve();
            foreach (var entry in Pairs)
            {
                CargoPair pair = entry.Value;
                if (pair.Lead == pair.Partner || pair.Lead >= loadout.weapons.Count || !Is(loadout.weapons[pair.Lead])) continue;
                if (Encyclopedia.Lookup.TryGetValue(entry.Key, out UnitDefinition d) && d?.unitPrefab != null)
                {
                    HardpointSet[] sets = d.unitPrefab.GetComponentInChildren<WeaponManager>(true)?.hardpointSets;
                    if (sets != null && pair.Partner < sets.Length && ReferenceEquals(sets[pair.Partner], set)) return true;
                }
                foreach (Aircraft live in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                    if (live.definition?.jsonKey == entry.Key && live.weaponManager != null && pair.Partner < live.weaponManager.hardpointSets.Length &&
                        ReferenceEquals(live.weaponManager.hardpointSets[pair.Partner], set)) return true;
            }
            return false;
        }

        // Saved/handwritten missions bypass the hangar's vetting. Apply the same capacity rule before spawning their mounts.
        public static void Sanitize(Aircraft aircraft)
        {
            if (aircraft?.loadout?.weapons == null || aircraft.weaponManager == null) return;
            Resolve();
            List<WeaponMount> weapons = aircraft.loadout.weapons;
            bool hasPair = Pair(aircraft.definition?.jsonKey, out CargoPair pair);
            for (int i = 0; i < weapons.Count; i++)
                if (Is(weapons[i]) && (!hasPair || i != pair.Lead)) weapons[i] = null;
            if (hasPair && pair.Lead != pair.Partner && pair.Lead < weapons.Count && pair.Partner < weapons.Count && Is(weapons[pair.Lead])) weapons[pair.Partner] = null;
        }

        internal static void EnsureRegistered(bool byGame)
        {
            NetworkManagerNuclearOption manager = GameAccess.NetworkManagerOrNull;
            ClientObjectManager objects = manager?.ClientObjectManager;
            if (objects == null || definition?.unitPrefab == null) return;
            if (byGame) { registeredWith = objects; return; }
            if (objects == registeredWith || manager.Client == null || !manager.Client.Active) return;
            objects.RegisterPrefab(definition.unitPrefab.GetComponent<NetworkIdentity>());
            registeredWith = objects;
        }
        private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    }

    internal sealed class Sa74CargoService : MonoBehaviour, ISceneService
    {
        public void ResetForScene() => Sa74Store.Resolve();
        private void Update() { Sa74Store.Resolve(); Sa74Store.EnsureRegistered(false); }
    }

    internal sealed class Sa74Seat : MonoBehaviour
    {
        private Aircraft aircraft;
        private MountedMissile rail;
        private float nextDoor;
        private void Awake()
        {
            aircraft = GetComponentInParent<Aircraft>();
            rail = GetComponentInChildren<MountedMissile>(true);
            if (aircraft?.weaponManager == null || !Sa74Store.Pair(aircraft.definition?.jsonKey, out Sa74Store.CargoPair pair)) return;
            HardpointSet[] sets = aircraft.weaponManager.hardpointSets;
            if (pair.Partner == pair.Lead || pair.Partner >= sets.Length || sets[pair.Partner].hardpoints.Count == 0) return;
            Transform other = sets[pair.Partner].hardpoints[0].transform;
            Vector3 shift = (other.position - transform.parent.position) * .5f;
            foreach (MountedMissile rail in GetComponentsInChildren<MountedMissile>(true)) rail.transform.position += shift;
        }

        private void Update()
        {
            if (aircraft == null || aircraft.disabled || rail == null || !rail.gameObject.activeInHierarchy || rail.GetAmmoTotal() <= 0 || Time.time < nextDoor) return;
            nextDoor = Time.time + .5f;
            Presentation.SkywellVisuals.HoldCargoDoors(aircraft, null);
            Presentation.SkywellVisuals.ReinforceCargoRamps(aircraft);
        }
    }

    internal sealed class Sa74Flight : MonoBehaviour
    {
        private float releasedAt;
        private GlobalPosition releasedFrom;
        private bool detonated;
        private Missile missile;
        private void Awake() => missile = GetComponent<Missile>();
        private void OnEnable() { releasedAt = Time.timeSinceLevelLoad; releasedFrom = transform.GlobalPosition(); detonated = false; }
        public bool Armed => Time.timeSinceLevelLoad - releasedAt >= 3f && !FastMath.InRange(transform.GlobalPosition(),
            missile != null && missile.owner != null ? missile.owner.GlobalPosition() : releasedFrom, 400f);
        public void Impact(Missile missile)
        {
            if (detonated || !GameAccess.IsServer() || missile.disabled) return;
            detonated = true;
            if (Armed && missile.IsArmed()) Sa74Fallout.Detonate(transform.position, missile.owner);
        }
    }

    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.BlockedByOtherHardpoint))]
    internal static class Sa74BlockedPatch
    {
        private static void Postfix(HardpointSet __instance, Loadout loadout, ref bool __result)
        {
            if (!__result) __result = Sa74Store.Blocks(__instance, loadout);
        }
    }
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "RegisterPrefabs")]
    internal static class Sa74PrefabsPatch { private static void Postfix() => Sa74Store.EnsureRegistered(true); }
    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class Sa74SavedLoadoutPatch { private static void Prefix(Aircraft ___aircraft) => Sa74Store.Sanitize(___aircraft); }
    [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate))]
    internal static class Sa74DetonatePatch
    {
        private static void Prefix(Missile __instance)
        {
            if (Sa74Store.Is(__instance)) __instance.GetComponent<Sa74Flight>()?.Impact(__instance);
        }
    }
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnMissile), new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    internal static class Sa74ReleasedPatch
    {
        private static void Postfix(Missile __result)
        {
            if (Sa74Store.Is(__result) && GameAccess.IsServer()) Networking.Sa74Net.Released();
        }
    }
    [HarmonyPatch(typeof(Missile), "UserCode_RpcDetonate_897349600")]
    internal static class Sa74DetonationReplayPatch
    {
        private static bool Prefix(Missile __instance)
        {
            if (!Sa74Store.Is(__instance)) return true;
            foreach (Renderer renderer in __instance.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            foreach (Collider collider in __instance.GetComponentsInChildren<Collider>()) collider.enabled = false;
            if (__instance.rb != null) __instance.rb.isKinematic = true;
            return false;
        }
    }
}

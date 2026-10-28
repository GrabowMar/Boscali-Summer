using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// <summary>
    /// XQ-90 MANTA, built at runtime from the bundle's REMORA (no bundle rebuild): its own MissileDefinition and flight prefab at
    /// 2.3x (<see cref="MantaRules"/>), a WeaponInfo, and a wing mount that hangs on the ARM pylon and also claims the neighbouring
    /// station (<see cref="MantaPartners"/>). The flight is VanguardFlight's REMORA pipeline with MantaFlight's brain.
    /// </summary>
    internal static class MantaStore
    {
        private const string DonorMount = "VG_Remora_ARM1_single";
        private static readonly FieldInfo Motors = AccessTools.Field(typeof(Missile), "motors");

        public static readonly Type[] PatchTypes = { typeof(MantaBlockedPatch), typeof(MantaPrefabsPatch) };

        private static MissileDefinition definition;
        private static WeaponInfo info;
        private static GameObject prefab;
        private static bool failed;
        private static ClientObjectManager registeredWith;

        public static void Register(ModuleContext context)
        {
            RuntimeStores.Add(new StoreRecipe
            {
                Key = VanguardKeys.MantaMount,
                Donor = DonorMount,
                Carriers = new[] { "ARM1_single" },
                Ready = BuildMissile,
                Build = BuildMount,
            });
            context.AddSceneService<MantaPartners>(60);
        }

        // The flight prefab and its definition, once the bundle's REMORA is in the Encyclopedia. Runs once; a failure stops the recipe.
        private static bool BuildMissile()
        {
            if (definition != null) return true;
            if (failed || Encyclopedia.Lookup == null ||
                !Encyclopedia.Lookup.TryGetValue(VanguardKeys.Remora, out UnitDefinition donor) || !(donor is MissileDefinition remora) || remora.unitPrefab == null)
                return false;
            try
            {
                prefab = RuntimeStores.Template(remora.unitPrefab, VanguardKeys.Manta);
                if (!UseArt(prefab.transform, folded: false)) prefab.transform.localScale *= MantaRules.Scale; // no bundle: the big REMORA
                Missile missile = prefab.GetComponent<Missile>();
                float area = MantaRules.Scale * MantaRules.Scale;

                info = UnityEngine.Object.Instantiate(missile.GetWeaponInfo());
                info.name = "WI_" + VanguardKeys.Manta;
                info.weaponName = "XQ-90 Manta";
                info.shortName = "XQ-90";
                info.description = "Heavy escort drone, a bigger REMORA on two wing stations: four radar missiles with shoot-look-shoot, twelve flare bursts, a body-guard slot toward whatever locks you, a 20 km passive sensor that reveals enemy aircraft, fifteen minutes of endurance. DESIGNATE an aircraft and it hunts inside 16 km; a ground unit and it dives in as a guided bomb. Out of rounds it stays on your wing as a decoy.";
                info.massPerRound = MantaRules.Mass;
                info.costPerRound = VanguardPrices.Munition(VanguardKeys.Manta);
                info.blastDamage = MantaRules.BlastYield;
                info.pierceDamage = MantaRules.PierceDamage;
                info.weaponPrefab = prefab;

                definition = UnityEngine.Object.Instantiate(remora);
                definition.name = definition.jsonKey = VanguardKeys.Manta;
                definition.unitName = "XQ-90 Manta";
                definition.description = info.description;
                definition.value = VanguardPrices.Munition(VanguardKeys.Manta);
                definition.mass = MantaRules.Mass;
                definition.radarSize = MantaRules.RadarSize;
                definition.length *= MantaRules.Scale;
                definition.width *= MantaRules.Scale;
                definition.height *= MantaRules.Scale;
                definition.unitPrefab = prefab;

                missile.definition = definition;
                SetField(missile, "info", info);
                SetField(missile, "mass", MantaRules.Mass);
                SetField(missile, "finArea", (float)GetField(missile, "finArea") * area); // lift and drag follow the frontal area
                SetField(missile, "gLimit", MantaRules.GLimit);
                SetField(missile, "maxTurnRate", MantaRules.TurnRate);
                SetField(missile, "blastYield", MantaRules.BlastYield);
                SetField(missile, "pierceDamage", MantaRules.PierceDamage);
                foreach (object motor in (Array)Motors.GetValue(missile))
                {
                    SetField(motor, "thrust", MantaRules.Thrust); // the throttle above 1 (RemoraArcher.Boost) still multiplies it
                    SetField(motor, "burnTime", MantaRules.BurnTime);
                    SetField(motor, "fuelMass", MantaRules.Mass * MantaRules.FuelFraction);
                }
                prefab.GetComponent<NetworkIdentity>().PrefabHash = MantaRules.PrefabHash;

                RuntimeStores.AppendMissile(definition);
                EnsureRegistered(false);
                return true;
            }
            catch (Exception error)
            {
                failed = true;
                PatchGuard.Report("Vanguard.MantaStore.Missile", error);
                return false;
            }
        }

        // The wing mount: the REMORA's rack firing the MANTA, its stowed model at the same scale, the seat between two stations.
        private static void BuildMount(WeaponMount mount)
        {
            mount.info = info;
            mount.mountName = "XQ-90 Manta";
            mount.drag *= 3f;
            mount.RCS *= 4f;
            foreach (MountedMissile station in mount.prefab.GetComponentsInChildren<MountedMissile>(true))
            {
                station.info = info;
                if (UseArt(station.transform, folded: true)) continue;
                Transform visual = station.transform.Find("VanguardVisual");
                if (visual != null) visual.localScale *= MantaRules.Scale;
            }
            mount.prefab.AddComponent<MantaSeat>();
        }

        /// <summary>Registers the flight prefab with this client when the game's own pass (<c>RegisterPrefabs</c>, at client start) already ran
        /// without it. <paramref name="byGame"/>: the game's pass just ran and covers it if the definition was in the Encyclopedia.</summary>
        internal static void EnsureRegistered(bool byGame)
        {
            NetworkManagerNuclearOption manager = NetworkManagerNuclearOption.i;
            ClientObjectManager objects = manager != null ? manager.ClientObjectManager : null;
            if (objects == null || prefab == null) return;
            if (byGame)
            {
                if (definition != null && Encyclopedia.i != null && Encyclopedia.i.missiles.Contains(definition)) registeredWith = objects;
                return;
            }
            if (objects == registeredWith || manager.Client == null || !manager.Client.Active) return;
            registeredWith = objects;
            objects.RegisterPrefab(prefab.GetComponent<NetworkIdentity>());
        }

        /// <summary>
        /// Puts the baked MANTA in place of the REMORA model ("VanguardVisual", the name RemoraVisuals and WeaponArticulation bind
        /// to): in flight at the missile's origin with the colliders grown to its size, on the rack seated top to top where the
        /// stowed REMORA hung, outer panels folded 180 deg down under the belly. False (nothing touched) without the bundle.
        /// </summary>
        private static bool UseArt(Transform parent, bool folded)
        {
            if (StoreArt.Prefab(StoreArt.Manta) == null) return false;
            Transform old = parent.Find("VanguardVisual");
            Bounds before = default;
            bool had = old != null && StoreArt.Bounds(parent, old, out before);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            GameObject art = StoreArt.Spawn(StoreArt.Manta, parent);
            art.name = "VanguardVisual";
            if (folded) Presentation.WeaponArticulation.FoldManta(art.transform, parent, 180f);
            if (folded && had && StoreArt.Bounds(parent, art.transform, out Bounds now))
                art.transform.localPosition = new Vector3(before.center.x - now.center.x, before.max.y - now.max.y, before.center.z - now.center.z);
            if (!folded)
                foreach (Collider c in parent.GetComponentsInChildren<Collider>(true))
                    switch (c)
                    {
                        case BoxCollider box: box.size *= MantaRules.Scale; box.center *= MantaRules.Scale; break;
                        case CapsuleCollider cap: cap.radius *= MantaRules.Scale; cap.height *= MantaRules.Scale; cap.center *= MantaRules.Scale; break;
                        case SphereCollider ball: ball.radius *= MantaRules.Scale; ball.center *= MantaRules.Scale; break;
                    }
            return true;
        }

        private static void SetField(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);

        private static object GetField(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
    }

    /// <summary>The game registers every Encyclopedia missile's prefab with Mirage at client start; this notes that it covered the MANTA.</summary>
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "RegisterPrefabs")]
    internal static class MantaPrefabsPatch
    {
        private static void Postfix()
        {
            try { MantaStore.EnsureRegistered(true); }
            catch (Exception error) { PatchGuard.Report("Vanguard.MantaPrefabs", error); }
        }
    }

    /// <summary>The loadout rule: a MANTA on its station blocks the partner station, in the loadout screen and in the server's vetting alike.</summary>
    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.BlockedByOtherHardpoint))]
    internal static class MantaBlockedPatch
    {
        private static void Postfix(HardpointSet __instance, Loadout loadout, ref bool __result)
        {
            if (__result) return;
            try { __result = MantaPartners.Blocks(__instance, loadout); }
            catch (Exception error) { PatchGuard.Report("Vanguard.MantaBlocked", error); }
        }
    }

    /// <summary>
    /// Which station a MANTA's lead station drags in. Resolved once, when the store has been placed: each aircraft's lead sets pair
    /// with the nearest free wing station (<see cref="MantaRules.Partner"/>), the partner loses the MANTA option, and a lead with
    /// no partner loses it too, so the loadout can never hold a MANTA that did not claim a second station.
    /// </summary>
    internal sealed class MantaPartners : MonoBehaviour, ISceneService
    {
        private static readonly Dictionary<string, Dictionary<int, int>> Pairs = new Dictionary<string, Dictionary<int, int>>();
        private static readonly Dictionary<HardpointSet, KeyValuePair<string, int>> Owners = new Dictionary<HardpointSet, KeyValuePair<string, int>>();
        private static bool resolved;

        public void ResetForScene()
        {
            Owners.Clear(); // live aircraft sets from the last scene would keep their whole hardpoint graphs alive
            Resolve();
        }

        private void Update() => Resolve();

        /// <summary>Partner set index of a lead set, -1 when the set is not a MANTA lead.</summary>
        public static int PartnerOf(string aircraft, int set)
        {
            Resolve();
            return Pairs.TryGetValue(aircraft, out Dictionary<int, int> map) && map.TryGetValue(set, out int partner) ? partner : -1;
        }

        public static bool Blocks(HardpointSet set, Loadout loadout)
        {
            Resolve();
            WeaponMount manta = RuntimeStores.Mount(VanguardKeys.MantaMount);
            if (manta == null || Pairs.Count == 0 || loadout == null || loadout.weapons == null || !loadout.weapons.Contains(manta)) return false;
            KeyValuePair<string, int> owner = OwnerOf(set);
            if (owner.Value < 0 || !Pairs.TryGetValue(owner.Key, out Dictionary<int, int> map)) return false;
            foreach (KeyValuePair<int, int> pair in map)
                if (pair.Value == owner.Value && pair.Key < loadout.weapons.Count && loadout.weapons[pair.Key] == manta) return true;
            return false;
        }

        public static void Resolve()
        {
            if (resolved) return;
            WeaponMount manta = RuntimeStores.Mount(VanguardKeys.MantaMount);
            if (manta == null || GameAccess.EncyclopediaOrNull == null) return;
            resolved = true;
            var leads = new Dictionary<string, List<int>>();
            foreach (RuntimeStores.Placement placed in RuntimeStores.Placed)
            {
                if (placed.Store != VanguardKeys.MantaMount) continue;
                if (!leads.TryGetValue(placed.Aircraft, out List<int> list)) leads[placed.Aircraft] = list = new List<int>();
                list.Add(placed.Set);
            }
            foreach (KeyValuePair<string, List<int>> entry in leads)
            {
                HardpointSet[] sets = PrefabSets(entry.Key);
                if (sets == null) continue;
                Vector3[][] points = Points(entry.Key, sets);
                var taken = new HashSet<int>();
                var map = new Dictionary<int, int>();
                entry.Value.Sort();
                foreach (int lead in entry.Value)
                {
                    int partner = taken.Contains(lead) ? -1 : MantaRules.Partner(lead, points, taken);
                    if (partner < 0) { Prune(entry.Key, lead, manta); continue; }
                    taken.Add(lead);
                    taken.Add(partner);
                    map[lead] = partner;
                    Prune(entry.Key, partner, manta);
                }
                Pairs[entry.Key] = map;
                Debug.Log("[Vanguard] MANTA on " + entry.Key + ": " + map.Count + " two-station pairs of " + entry.Value.Count + " ARM stations");
            }
        }

        private static HardpointSet[] PrefabSets(string aircraft)
        {
            if (Encyclopedia.Lookup == null || !Encyclopedia.Lookup.TryGetValue(aircraft, out UnitDefinition def) || !(def is AircraftDefinition ad) || ad.unitPrefab == null) return null;
            WeaponManager manager = ad.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            return manager != null ? manager.hardpointSets : null;
        }

        // Each set's hardpoints in aircraft-local metres (x = right).
        private static Vector3[][] Points(string aircraft, HardpointSet[] sets)
        {
            Transform root = ((AircraftDefinition)Encyclopedia.Lookup[aircraft]).unitPrefab.transform;
            var points = new Vector3[sets.Length][];
            for (int i = 0; i < sets.Length; i++)
            {
                var list = new List<Vector3>();
                foreach (Hardpoint hardpoint in sets[i].hardpoints)
                    if (hardpoint != null && hardpoint.transform != null) list.Add(root.InverseTransformPoint(hardpoint.transform.position));
                points[i] = list.ToArray();
            }
            return points;
        }

        // Takes the option off the prefab and off every aircraft already in the scene.
        private static void Prune(string aircraft, int set, WeaponMount manta)
        {
            PrefabSets(aircraft)[set].weaponOptions.Remove(manta);
            foreach (Aircraft live in UnityEngine.Object.FindObjectsOfType<Aircraft>())
            {
                HardpointSet[] sets = live.definition != null && live.definition.jsonKey == aircraft && live.weaponManager != null ? live.weaponManager.hardpointSets : null;
                if (sets != null && set < sets.Length) sets[set].weaponOptions.Remove(manta);
            }
        }

        // A set instance (prefab or live aircraft) back to its aircraft key and index; found once, remembered (index -1: not an aircraft set).
        private static KeyValuePair<string, int> OwnerOf(HardpointSet set)
        {
            if (Owners.TryGetValue(set, out KeyValuePair<string, int> known)) return known;
            var found = new KeyValuePair<string, int>(null, -1);
            foreach (string key in Pairs.Keys)
            {
                int index = Array.IndexOf(PrefabSets(key) ?? new HardpointSet[0], set);
                if (index >= 0) { found = new KeyValuePair<string, int>(key, index); break; }
            }
            if (found.Value < 0)
                foreach (Aircraft live in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                {
                    int index = live.weaponManager != null && live.definition != null && Pairs.ContainsKey(live.definition.jsonKey) ? Array.IndexOf(live.weaponManager.hardpointSets, set) : -1;
                    if (index >= 0) { found = new KeyValuePair<string, int>(live.definition.jsonKey, index); break; }
                }
            Owners[set] = found;
            return found;
        }
    }

    /// <summary>
    /// On the mount prefab: when the mount is spawned on a hardpoint, the stowed MANTA moves to the midpoint between that hardpoint
    /// and its partner station. Awake runs before the weapon registers, so the launch point and the rearm position are the seat.
    /// </summary>
    internal sealed class MantaSeat : MonoBehaviour
    {
        private void Awake()
        {
            try { Seat(); }
            catch (Exception error) { PatchGuard.Report("Vanguard.MantaSeat", error); }
        }

        private void Seat()
        {
            Aircraft aircraft = GetComponentInParent<Aircraft>();
            Transform hardpoint = transform.parent;
            HardpointSet[] sets = aircraft != null && aircraft.weaponManager != null ? aircraft.weaponManager.hardpointSets : null;
            if (sets == null || hardpoint == null || aircraft.definition == null) return;
            int lead = -1;
            for (int i = 0; i < sets.Length && lead < 0; i++)
                foreach (Hardpoint h in sets[i].hardpoints)
                    if (h != null && h.transform == hardpoint) { lead = i; break; }
            int partner = MantaPartners.PartnerOf(aircraft.definition.jsonKey, lead);
            if (partner < 0) return;
            Transform nearest = null;
            float best = float.MaxValue;
            foreach (Hardpoint h in sets[partner].hardpoints)
            {
                float gap = h != null && h.transform != null ? Vector3.Distance(h.transform.position, hardpoint.position) : float.MaxValue;
                if (gap < best) { best = gap; nearest = h.transform; }
            }
            if (nearest == null) return;
            Vector3 shift = (nearest.position - hardpoint.position) * 0.5f;
            foreach (MountedMissile station in GetComponentsInChildren<MountedMissile>(true)) station.transform.position += shift;
        }
    }
}

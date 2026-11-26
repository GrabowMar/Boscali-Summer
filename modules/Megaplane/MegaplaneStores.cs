using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Native hardpoint sets for strike stores, air cover, and three independently selectable hold loads.
    /// The game's loadout screen, rearming, release and multiplayer replay all use these sets.</summary>
    internal static class MegaplaneStores
    {
        internal const string HaloMount = "AB_Paratroopers_x16", TenderMount = "VG_Skywell_Kit";
        private const string Remora = "VG_Remora_ARM1_single", Manta = "VG_Manta_Wing", Scythe = "AAM2_double";
        private const int Outboard = 1;
        internal static string Summary { get; private set; } = "not built";
        private const float DoorSeconds = 8f, DoorHold = 25f;

        private static readonly string[] StrikeMounts =
        {
            Remora, "VG_Hamilcar_AGM_heavy_single", "AGM_heavy_single", "AGM1_double", "ARM1_double", "ARM2_single",
            "bomb_250_glide_double", "bomb_500_glide_single", "bomb_penetrator1_mount", "AAM4_single",
        };
        private static readonly string[] EscortMounts =
        {
            Manta, Remora, "VG_Kestrel_Sled", Scythe, "AAM1_double", "AAM3_double", "AAM4_double",
            "RocketPod1_single", "AGM1_single",
        };

        private static WeaponMount Mount(Encyclopedia e, string key) => e.weaponMounts.FirstOrDefault(m => m != null && (m.jsonKey == key || m.name == key));

        internal static void Build(Encyclopedia e, Aircraft aircraft, AircraftParameters parameters, Dictionary<string, Transform> nodes)
        {
            WeaponManager manager = aircraft.GetComponentInChildren<WeaponManager>(true);
            if (manager == null || !nodes.ContainsKey("HoldExit")) { Summary = "no weapon manager or no hold art"; return; }
            WeaponMount tender = Mount(e, TenderMount);
            // Cargo lanes carry vehicles or supplies. The tender and troop stick each have a dedicated release station.
            List<WeaponMount> cargo = e.weaponMounts.Where(m => m != null && m != tender && !m.Troops && m.prefab != null &&
                m.prefab.GetComponentInChildren<MountedCargo>(true) is MountedCargo c && c.cargo != null).ToList();

            // The hold door: invisible native BayDoor at the ramp tip. Cargo spawns once it has rolled past it, outside the hull.
            Transform exit = nodes["HoldExit"];
            UnitPart holdPart = exit.GetComponentInParent<UnitPart>(true);
            var doorGo = new GameObject("BS_HoldDoor");
            doorGo.transform.SetParent(holdPart.transform, false);
            doorGo.transform.SetPositionAndRotation(exit.position, aircraft.transform.rotation);
            BayDoor door = doorGo.AddComponent<BayDoor>();
            MegaplaneGuns.Set(door, "openSpeed", 1f / DoorSeconds);
            MegaplaneGuns.Set(door, "closeSpeed", 1f / DoorSeconds);
            MegaplaneGuns.Set(door, "hingeAngle", 0f);

            List<WeaponMount> Options(params string[] keys) => keys.Select(k => Mount(e, k)).Where(m => m != null && m.prefab != null).ToList();
            // Use the forward and aft racks of each wing beam, leaving 18 m between stores along the beam.
            // Keep the serialized set indices stable even when optional stores are missing.
            manager.hardpointSets = new[]
            {
                Set(nodes, "INBOARD STRIKE", "Hardpoint_I_", Options(StrikeMounts), null, "_0", "_3"),
                Set(nodes, "OUTBOARD ESCORT", "Hardpoint_O_", Options(EscortMounts), null, "_0", "_3"),
                Set(nodes, "CARGO HOLD", "Hardpoint_Hold_", cargo, door, "_0"),
                Set(nodes, "SKYWELL TENDER", "Hardpoint_Tender", Options(TenderMount), door),
                Set(nodes, "PARATROOP STICK", "Hardpoint_Hold_C_1", Options(HaloMount), door),
            };

            // The native player hangar and air starts use loadouts[1]. AI can choose a standard fit for its role.
            Loadout invasion = Fit(manager, new[] { Remora, "AGM_heavy_single" }, new[] { Manta, Scythe }, "6x6_1_AAx1", TenderMount, HaloMount);
            parameters.loadouts = new List<Loadout> { new Loadout { weapons = new List<WeaponMount>(invasion.weapons) }, invasion };
            parameters.StandardLoadouts = new[]
            {
                new StandardLoadout { Name = "INVASION", loadout = new Loadout { weapons = new List<WeaponMount>(invasion.weapons) }, FuelRatio = 1f },
                new StandardLoadout { Name = "AIR DEFENCE", loadout = Fit(manager, new[] { "AAM4_single" }, new[] { Scythe }), FuelRatio = 1f },
                new StandardLoadout { Name = "STANDOFF STRIKE", loadout = Fit(manager, new[] { "AGM_heavy_single", "bomb_500_glide_single" }, new[] { "AAM3_double" }), FuelRatio = 1f },
                new StandardLoadout { Name = "RADAR SUPPRESSION", loadout = Fit(manager, new[] { "ARM1_double", "ARM2_single" }, new[] { Scythe }), FuelRatio = 1f },
            };
            aircraft.gameObject.AddComponent<MegaplaneRamp>();
            Summary = string.Join(", ", manager.hardpointSets.Select(s => $"{s.name} x{s.hardpoints.Count} ({s.weaponOptions.Count(m => m != null)} options)"));
            Debug.Log("[Megaplane] stores: " + Summary + "; fit " + string.Join(" / ", invasion.weapons.Select(w => w != null ? w.jsonKey : "-")));
        }

        private static HardpointSet Set(Dictionary<string, Transform> nodes, string name, string prefix, List<WeaponMount> options, BayDoor door, params string[] suffixes)
        {
            options.Insert(0, null);  // SavedLoadout reserves the first option for an empty station.
            var set = new HardpointSet { name = name, weaponOptions = options, precludingHardpointSets = new List<byte>() };
            FieldInfo pylons = AccessTools.Field(typeof(Hardpoint), "pylonOptions");
            BayDoor[] doors = door == null ? Array.Empty<BayDoor>() : new[] { door };
            foreach (var node in nodes.OrderBy(n => n.Key, StringComparer.Ordinal))
            {
                if (!node.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                    (suffixes.Length > 0 && !suffixes.Any(s => node.Key.EndsWith(s, StringComparison.Ordinal)))) continue;
                Transform position = node.Value;
                var hardpoint = new Hardpoint
                {
                    transform = position, part = position.GetComponentInParent<UnitPart>(true), bayDoors = doors,
                    doorOpenDuration = DoorHold, BuiltInWeapons = Array.Empty<Weapon>(), BuiltInTurrets = Array.Empty<Turret>(),
                };
                pylons.SetValue(hardpoint, Array.CreateInstance(pylons.FieldType.GetElementType(), 0));
                set.hardpoints.Add(hardpoint);
            }
            if (set.hardpoints.Count == 0) set.weaponOptions.Clear();
            return set;
        }

        private static WeaponMount Find(HardpointSet set, params string[] keys) =>
            keys.Select(key => set.weaponOptions.Find(m => m != null && m.jsonKey == key)).FirstOrDefault(m => m != null);

        private static Loadout Fit(WeaponManager manager, string[] strike, string[] escort, string cargo = null, string tender = null, string troops = null)
        {
            HardpointSet[] sets = manager.hardpointSets;
            return new Loadout { weapons = new List<WeaponMount> { Find(sets[0], strike), Find(sets[Outboard], escort),
                Find(sets[2], cargo), Find(sets[3], tender), Find(sets[4], troops) } };
        }

        // The Manta can register after the airframe. Add it to the hangar catalogue as well as the default fit.
        internal static void CompletePreset(AircraftDefinition definition, WeaponManager live = null)
        {
            if (definition == null || definition.unitPrefab == null || Encyclopedia.WeaponLookup == null ||
                !Encyclopedia.WeaponLookup.TryGetValue(Manta, out WeaponMount manta) || manta == null) return;
            HardpointSet[] template = definition.unitPrefab.GetComponentInChildren<WeaponManager>(true).hardpointSets;
            if (template.Length <= Outboard || template[Outboard].hardpoints.Count == 0) return;
            List<WeaponMount> options = template[Outboard].weaponOptions;
            if (!options.Contains(manta)) options.Insert(1, manta);
            if (live != null && live.hardpointSets.Length > Outboard && !live.hardpointSets[Outboard].weaponOptions.Contains(manta))
                live.hardpointSets[Outboard].weaponOptions.Insert(1, manta);

            AircraftParameters parameters = definition.aircraftParameters;
            if (parameters.loadouts[1].weapons[Outboard] == manta) return;
            foreach (Loadout preset in parameters.loadouts) preset.weapons[Outboard] = manta;
            parameters.StandardLoadouts[0].loadout.weapons[Outboard] = manta;
        }

        internal static Loadout UpgradeLoadout(Loadout loadout, int slots)
        {
            if (loadout == null || loadout.weapons.Count != 4 || slots != 5) return loadout;
            // Retain all four original selections, including empty hardpoints. The new troop lane starts empty.
            var migrated = new Loadout { weapons = new List<WeaponMount>(loadout.weapons) };
            migrated.weapons.Add(null);
            return migrated;
        }
    }

    /// <summary>Ramp and petal doors follow the hold's native BayDoor (opened by every peer's cargo/troop fire replay).
    /// The ventral mission-bay doors open with them at a restrained angle: the aft belly opens as one service line.</summary>
    internal sealed class MegaplaneRamp : MonoBehaviour
    {
        // Rest-to-open rotations in the art's local axes: Blender ramp +22.6 deg about X, petals +-95 about Y (= Unity Z); Unity negates both.
        internal const float RampOpen = -22.6f, PetalOpen = 95f, BayDoorOpen = 60f;
        private static readonly AccessTools.FieldRef<BayDoor, float> Amount = AccessTools.FieldRefAccess<BayDoor, float>("openAmount");
        private BayDoor door;
        private readonly List<(Transform t, Quaternion rest, Vector3 axis, float angle, float start, float end)> parts =
            new List<(Transform, Quaternion, Vector3, float, float, float)>();
        internal float Open => door != null ? Amount(door) : 0f;

        private void Awake()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "BS_HoldDoor") door = t.GetComponent<BayDoor>();
                else if (t.name == "Ramp") parts.Add((t, t.localRotation, Vector3.right, RampOpen, .48f, 1f));
                else if (t.name == "AftDoor_L") parts.Add((t, t.localRotation, Vector3.forward, -PetalOpen, 0f, .55f));
                else if (t.name == "AftDoor_R") parts.Add((t, t.localRotation, Vector3.forward, PetalOpen, 0f, .55f));
                else if (t.name == "BayDoor_L") parts.Add((t, t.localRotation, Vector3.forward, -BayDoorOpen, 0f, 1f));
                else if (t.name == "BayDoor_R") parts.Add((t, t.localRotation, Vector3.forward, BayDoorOpen, 0f, 1f));
            }
        }

        private void Update()
        {
            float open = Open;
            // The petals clear the aft sill before the ramp drops. The same phase
            // windows sequence the ramp back inside before the petals shut.
            foreach ((Transform t, Quaternion rest, Vector3 axis, float angle, float start, float end) in parts)
                if (t != null && t.IsChildOf(transform))
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, open));
                    t.localRotation = rest * Quaternion.AngleAxis(angle * k, axis);
                }
        }
    }
}

namespace BoscaliSummer.Modules.Megaplane
{
    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.InitializeWeaponManager))]
    internal static class MegaplaneLoadoutPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            Aircraft aircraft = MegaplaneCargoStation.AircraftRef(__instance);
            if (!MegaplaneModule.Is(aircraft)) return;
            MegaplaneStores.CompletePreset(aircraft.definition, __instance);
            Loadout fit = MegaplaneStores.UpgradeLoadout(aircraft.loadout, __instance.hardpointSets.Length);
            if (fit != aircraft.loadout) aircraft.Networkloadout = fit;
        }
    }

    [HarmonyPatch(typeof(SavedLoadout), nameof(SavedLoadout.CreateLoadout), typeof(WeaponManager))]
    internal static class MegaplaneSavedLoadoutPatch
    {
        private static void Postfix(WeaponManager weaponManager, ref Loadout __result)
        {
            Aircraft aircraft = weaponManager.GetComponentInParent<Aircraft>(true);
            if (MegaplaneModule.Is(aircraft)) __result = MegaplaneStores.UpgradeLoadout(__result, weaponManager.hardpointSets.Length);
        }
    }

    internal static class MegaplaneCargoStation
    {
        internal static readonly AccessTools.FieldRef<WeaponManager, Aircraft> AircraftRef = AccessTools.FieldRefAccess<WeaponManager, Aircraft>("aircraft");
    }

    /// <summary>Vanilla files every cargo mount into one station (a Tarantula has one bay). The G-240's hold lanes and the SKYWELL
    /// kit are separate loads: on the carrier, cargo shares a station only with the same cargo, so each is its own selection.</summary>
    [HarmonyLib.HarmonyPatch(typeof(WeaponManager), "StationExistsForWeaponInfo")]
    internal static class MegaplaneCargoStationPatch
    {
        private static bool Prefix(WeaponManager __instance, WeaponInfo weaponInfo, ref WeaponStation existingStation, ref bool __result)
        {
            Aircraft aircraft = MegaplaneCargoStation.AircraftRef(__instance);
            if (!MegaplaneModule.Is(aircraft)) return true;
            existingStation = weaponInfo == null ? null : System.Linq.Enumerable.FirstOrDefault(aircraft.weaponStations, s => s.WeaponInfo == weaponInfo);
            __result = existingStation != null;
            return false;
        }
    }
}

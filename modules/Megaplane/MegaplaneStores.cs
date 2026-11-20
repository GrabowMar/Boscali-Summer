using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>The carrier's stores are native hardpoint sets, so the loadout screen, firing, ammo, multiplayer replay and the
    /// VANGUARD drone C-menu all work as on any aircraft. The Darkreach donor's sets are replaced by:
    /// two wing sets (stores beams under four pylons, three racks each: REMORA / MANTA / KESTREL / HAMILCAR or the donor's
    /// weapons), three cargo-hold lanes (the game's own vehicle and supply airdrops, HALO paratroopers) that roll aft out of the
    /// ramp, and one SKYWELL tender kit at the ramp. The hold's door is a native BayDoor at the ramp's tip ("HoldExit"):
    /// MountedCargo rolls its load aft until it passes it, and HALO sticks jump from it; <see cref="MegaplaneRamp"/> draws the
    /// ramp and petal doors from its opening.</summary>
    internal static class MegaplaneStores
    {
        internal static readonly string[] DroneMounts = { "VG_Remora_ARM1_single", "VG_Manta_Wing", "VG_Kestrel_Sled", "VG_Hamilcar_AGM_heavy_single" };
        internal const string HaloMount = "AB_Paratroopers_x16", TenderMount = "VG_Skywell_Kit";
        internal static string Summary { get; private set; } = "not built";
        private const float DoorSeconds = 8f, DoorHold = 25f;

        private struct SetSpec { public string Name; public string Prefix; public bool Hold; }
        private static readonly SetSpec[] Sets =
        {
            new SetSpec { Name = "WING INBOARD", Prefix = "Hardpoint_I_" }, new SetSpec { Name = "WING OUTBOARD", Prefix = "Hardpoint_O_" },
            new SetSpec { Name = "HOLD PORT", Prefix = "Hardpoint_Hold_L_", Hold = true }, new SetSpec { Name = "HOLD CENTRE", Prefix = "Hardpoint_Hold_C_", Hold = true },
            new SetSpec { Name = "HOLD STARBOARD", Prefix = "Hardpoint_Hold_R_", Hold = true }, new SetSpec { Name = "SKYWELL TENDER", Prefix = "Hardpoint_Tender", Hold = true },
        };

        private static WeaponMount Mount(Encyclopedia e, string key) => e.weaponMounts.FirstOrDefault(m => m != null && (m.jsonKey == key || m.name == key));

        internal static void Build(Encyclopedia e, Aircraft aircraft, AircraftParameters parameters, Dictionary<string, Transform> nodes)
        {
            WeaponManager manager = aircraft.GetComponentInChildren<WeaponManager>(true);
            if (manager == null || !nodes.ContainsKey("HoldExit")) { Summary = "no weapon manager or no hold art"; return; }
            List<WeaponMount> donor = manager.hardpointSets.SelectMany(s => s.weaponOptions).Where(m => m != null && !m.Cargo && !m.Troops).Distinct().ToList();
            List<WeaponMount> drones = DroneMounts.Select(k => Mount(e, k)).Where(m => m != null).ToList();
            // Cargo the game can airdrop by itself: every mount whose MountedCargo carries a unit (vehicles, supply containers).
            List<WeaponMount> cargo = e.weaponMounts.Where(m => m != null && m.prefab != null && m.prefab.GetComponentInChildren<MountedCargo>(true) is MountedCargo c && c.cargo != null).ToList();
            WeaponMount halo = Mount(e, HaloMount), tender = Mount(e, TenderMount);
            if (halo != null) cargo.Insert(0, halo);

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

            Type pylonType = AccessTools.Field(typeof(Hardpoint), "pylonOptions").FieldType;
            var sets = new List<HardpointSet>();
            var counts = new List<string>();
            foreach (SetSpec spec in Sets)
            {
                List<Transform> points = nodes.Where(n => n.Key.StartsWith(spec.Prefix)).OrderBy(n => n.Key).Select(n => n.Value).ToList();
                if (points.Count == 0) continue;
                var set = new HardpointSet
                {
                    name = spec.Name, hardpoints = new List<Hardpoint>(), precludingHardpointSets = new List<byte>(),
                    weaponOptions = spec.Prefix == "Hardpoint_Tender" ? (tender != null ? new List<WeaponMount> { tender } : new List<WeaponMount>())
                        : spec.Hold ? new List<WeaponMount>(cargo) : drones.Concat(donor).ToList(),
                };
                foreach (Transform t in points)
                {
                    var hp = new Hardpoint
                    {
                        transform = t, part = t.GetComponentInParent<UnitPart>(true), bayDoors = spec.Hold ? new[] { door } : new BayDoor[0],
                        doorOpenDuration = DoorHold, BuiltInWeapons = new Weapon[0], BuiltInTurrets = new Turret[0],
                    };
                    AccessTools.Field(typeof(Hardpoint), "pylonOptions").SetValue(hp, Array.CreateInstance(pylonType.GetElementType(), 0));
                    set.hardpoints.Add(hp);
                }
                sets.Add(set);
                counts.Add(spec.Name + " x" + points.Count + " (" + set.weaponOptions.Count + " options)");
            }
            manager.hardpointSets = sets.ToArray();

            // Air-started carriers take loadouts[1]; the hangar shows the standard loadout.
            WeaponMount First(List<WeaponMount> options, params string[] keys) => keys.Select(k => options.FirstOrDefault(m => m.jsonKey == k)).FirstOrDefault(m => m != null) ?? options.FirstOrDefault();
            List<WeaponMount> vehicles = cargo.Where(m => m != halo).ToList();
            var fit = new Loadout { weapons = sets.Select(s => s.name switch
            {
                "WING INBOARD" => First(s.weaponOptions, "VG_Remora_ARM1_single"),
                "WING OUTBOARD" => First(s.weaponOptions, "VG_Manta_Wing", "VG_Remora_ARM1_single"),
                "HOLD PORT" => halo ?? vehicles.FirstOrDefault(),
                "HOLD CENTRE" => vehicles.FirstOrDefault(),
                "HOLD STARBOARD" => vehicles.Skip(1).FirstOrDefault() ?? vehicles.FirstOrDefault(),
                _ => s.weaponOptions.FirstOrDefault(),
            }).ToList() };
            parameters.loadouts = new List<Loadout> { fit, fit };
            parameters.StandardLoadouts = new[] { new StandardLoadout { Name = "INVASION", loadout = fit, FuelRatio = 1f } };
            aircraft.gameObject.AddComponent<MegaplaneRamp>();
            Summary = string.Join(", ", counts) + "; fit " + string.Join(" / ", fit.weapons.Select(w => w != null ? w.jsonKey : "-"));
            Debug.Log("[Megaplane] stores: " + Summary + "; cargo options " + string.Join(" ", cargo.Select(c => c.jsonKey)));
        }
    }

    /// <summary>Ramp and petal doors follow the hold's native BayDoor (opened by every peer's cargo/troop fire replay).</summary>
    internal sealed class MegaplaneRamp : MonoBehaviour
    {
        // Rest-to-open rotations in the art's local axes (Unity rotations are the negated Blender ones about X and Y).
        internal const float RampOpen = 23f, PetalOpen = 70f;
        private static readonly AccessTools.FieldRef<BayDoor, float> Amount = AccessTools.FieldRefAccess<BayDoor, float>("openAmount");
        private BayDoor door;
        private readonly List<(Transform t, Quaternion rest, Vector3 axis, float angle)> parts = new List<(Transform, Quaternion, Vector3, float)>();
        internal float Open => door != null ? Amount(door) : 0f;

        private void Awake()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "BS_HoldDoor") door = t.GetComponent<BayDoor>();
                else if (t.name == "Ramp") parts.Add((t, t.localRotation, Vector3.right, RampOpen));
                else if (t.name == "AftDoor_L") parts.Add((t, t.localRotation, Vector3.up, PetalOpen));
                else if (t.name == "AftDoor_R") parts.Add((t, t.localRotation, Vector3.up, -PetalOpen));
            }
        }

        private void Update()
        {
            float k = Mathf.SmoothStep(0f, 1f, Open);
            foreach ((Transform t, Quaternion rest, Vector3 axis, float angle) in parts) if (t != null) t.localRotation = rest * Quaternion.AngleAxis(angle * k, axis);
        }
    }
}

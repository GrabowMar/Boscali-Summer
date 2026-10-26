using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Ares.Domain;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Ares.Runtime
{
    /// <summary>SPAWN: the game's own catalog, placed on map clicks for a chosen side, in batches UNDO and CLEAR can take back.</summary>
    internal sealed partial class AresHost
    {
        private const int MaxBatches = 256;

        public int Side { get; set; }
        public AresKind Kind { get; set; } = AresKind.Ground;
        public int Item { get; set; }
        public int Count { get => count; set => count = Mathf.Clamp(value, 1, 8); }
        public float Altitude { get => altitude; set => altitude = Mathf.Clamp(value, 300f, 9000f); }
        private int count = 1;
        private float altitude = 1500f;

        private readonly Dictionary<AresKind, List<UnitDefinition>> catalogs = new Dictionary<AresKind, List<UnitDefinition>>();
        private readonly List<List<Unit>> batches = new List<List<Unit>>();

        public int Spawned
        {
            get
            {
                int n = 0;
                foreach (List<Unit> batch in batches)
                    foreach (Unit unit in batch)
                        if (unit != null) n++;
                return n;
            }
        }

        /// <summary>The definitions of one kind, sorted by name, allowed in this mission and with a prefab. Built once per scene.</summary>
        public List<UnitDefinition> Catalog(AresKind kind)
        {
            if (catalogs.TryGetValue(kind, out List<UnitDefinition> list)) return list;
            list = new List<UnitDefinition>();
            Encyclopedia book = Encyclopedia.i;
            if (book != null)
            {
                bool events = MissionManager.AllowEventContent;
                void Take<T>(List<T> source) where T : UnitDefinition
                {
                    if (source == null) return;
                    foreach (T definition in source)
                        if (definition != null && definition.unitPrefab != null && definition.IsAllowed(events)) list.Add(definition);
                }
                switch (kind)
                {
                    case AresKind.Air: Take(book.aircraft); break;
                    case AresKind.Ground: Take(book.vehicles); break;
                    case AresKind.Naval: Take(book.ships); break;
                    case AresKind.Structure: Take(book.buildings); break;
                }
                list.Sort((a, b) => string.Compare(a.unitName, b.unitName, StringComparison.OrdinalIgnoreCase));
            }
            catalogs[kind] = list;
            return list;
        }

        public int ItemCount => Kind == AresKind.Kit ? AresMath.Kits.Length : Catalog(Kind).Count;

        public string ItemName(int index)
        {
            if (Kind == AresKind.Kit) return index >= 0 && index < AresMath.Kits.Length ? AresMath.Kits[index].Name : "";
            List<UnitDefinition> list = Catalog(Kind);
            return index >= 0 && index < list.Count ? (list[index].unitName ?? list[index].jsonKey ?? "?").ToUpperInvariant() : "";
        }

        public string ItemDetail(int index)
        {
            if (Kind == AresKind.Kit) return index >= 0 && index < AresMath.Kits.Length ? AresMath.Kits[index].Blurb : "";
            List<UnitDefinition> list = Catalog(Kind);
            return index >= 0 && index < list.Count ? list[index].jsonKey ?? "" : "";
        }

        public bool Placing => Armed && PickLabel == "PLACE";

        /// <summary>PLACE: arms the map until Esc or PLACE again. Each click spawns the selection; a drag sets the heading.</summary>
        public string TogglePlace()
        {
            if (Placing)
            {
                Disarm();
                return Say("Placement stopped.");
            }
            if (ItemCount == 0) return Say("Nothing to place in this list.");
            return Arm("PLACE", "PLACING " + ItemName(Item) + ": click the map, drag to face. Esc stops.", true, (at, heading) => PlaceAt(at, heading));
        }

        /// <summary>Spawns the current selection (COUNT times, or one kit) at a map point facing <paramref name="heading"/>.</summary>
        public string PlaceAt(GlobalPosition at, float heading)
        {
            if (!Host(out string refusal)) return Say(refusal);
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            FactionHQ hq = SideHq(Side);
            if (spawner == null) return Say("No spawner in this scene.");
            if (hq == null) return Say("This mission has no " + SideName(Side) + " side.");
            var batch = new List<Unit>();
            string problem = null;
            if (Kind == AresKind.Kit)
            {
                if (Item < 0 || Item >= AresMath.Kits.Length) return Say("Pick a kit first.");
                AresKit kit = AresMath.Kits[Item];
                int missing = 0;
                foreach (AresKitMember member in kit.Members)
                {
                    UnitDefinition definition = ResolveKitMember(member, kit.Naval);
                    if (definition == null) { missing++; continue; }
                    AresMath.Rotate(member.X, member.Z, heading, out float dx, out float dz);
                    problem = SpawnOne(spawner, definition, new GlobalPosition(at.x + dx, at.y, at.z + dz), heading, hq, batch) ?? problem;
                }
                if (missing > 0) problem = missing + " kit member(s) are not in this game build.";
                return Finish(batch, kit.Name, problem);
            }
            List<UnitDefinition> list = Catalog(Kind);
            if (Item < 0 || Item >= list.Count) return Say("Pick a unit first.");
            UnitDefinition chosen = list[Item];
            float spacing = Kind == AresKind.Air ? 140f : Kind == AresKind.Naval ? 360f : Kind == AresKind.Structure ? 45f : 22f;
            for (int i = 0; i < count; i++)
            {
                AresMath.Slot(i, count, spacing, out float right, out float forward);
                AresMath.Rotate(right, forward, heading, out float dx, out float dz);
                problem = SpawnOne(spawner, chosen, new GlobalPosition(at.x + dx, at.y, at.z + dz), heading, hq, batch) ?? problem;
            }
            return Finish(batch, (chosen.unitName ?? "unit").ToUpperInvariant() + (count > 1 ? " x" + count : ""), problem);
        }

        private string Finish(List<Unit> batch, string what, string problem)
        {
            if (batch.Count == 0) return Say("Nothing spawned" + (problem != null ? ": " + problem : "."));
            foreach (Unit unit in batch) TrackForLocal(unit);
            batches.Add(batch);
            if (batches.Count > MaxBatches) batches.RemoveAt(0); // the oldest batch stays in the world, only UNDO forgets it
            log?.LogInfo("[Ares] spawned " + what + " for " + SideName(Side) + " (" + batch.Count + " units).");
            return Say("SPAWNED " + what + " FOR " + SideName(Side) + (problem != null ? " · " + problem : ""));
        }

        private UnitDefinition ResolveKitMember(AresKitMember member, bool naval)
        {
            if (naval)
            {
                List<UnitDefinition> ships = Catalog(AresKind.Naval);
                int.TryParse(member.Key.TrimStart('#'), out int index);
                return ships.Count == 0 ? null : ships[Mathf.Clamp(index, 0, ships.Count - 1) % ships.Count];
            }
            foreach (AresKind kind in new[] { AresKind.Ground, AresKind.Structure })
                foreach (UnitDefinition definition in Catalog(kind))
                    if (string.Equals(definition.jsonKey, member.Key, StringComparison.OrdinalIgnoreCase)) return definition;
            foreach (AresKind kind in new[] { AresKind.Ground, AresKind.Structure })
                foreach (UnitDefinition definition in Catalog(kind))
                    if (Holds(definition.jsonKey, member.Keyword) || Holds(definition.unitName, member.Keyword)) return definition;
            return null;
        }

        private static bool Holds(string text, string keyword) =>
            !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(keyword) && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>One unit at a point; null on success, else why not.</summary>
        private string SpawnOne(Spawner spawner, UnitDefinition definition, GlobalPosition at, float heading, FactionHQ hq, List<Unit> into)
        {
            Vector3 ground = Ground(at, out bool water);
            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            string name = "ARES:" + (definition.jsonKey ?? "unit") + ":" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Unit unit = null;
            switch (definition)
            {
                case AircraftDefinition aircraft:
                    unit = SpawnAircraft(spawner, aircraft, ground, rotation, hq, name, null);
                    break;
                case ShipDefinition _:
                    if (!water) return "ships need open water";
                    unit = spawner.SpawnShip(definition.unitPrefab, new Vector3(ground.x, Datum.LocalSeaY, ground.z).ToGlobalPosition(), rotation, hq, name, 1f, true);
                    break;
                case BuildingDefinition _:
                    if (water) return "buildings need land";
                    unit = spawner.SpawnBuilding(definition.unitPrefab, ground.ToGlobalPosition(), rotation, hq, null, name, false, null);
                    break;
                case VehicleDefinition _:
                    if (water) return "ground units need land";
                    Vector3 placed = GroundPlacement.Usable(definition) && GroundPlacement.TryPlace(definition, ground, rotation, out Vector3 free) ? free : ground;
                    unit = spawner.SpawnVehicle(definition.unitPrefab, placed.ToGlobalPosition(), rotation, Vector3.zero, hq, name, 1f, true, null);
                    break;
                default:
                    return "that kind cannot be spawned";
            }
            if (unit == null) return "the game refused the spawn";
            into.Add(unit);
            return null;
        }

        /// <summary>An aircraft flying at ALT above the ground (helicopters slow), its first standard loadout, combat AI awake. With a
        /// <paramref name="player"/> it is that player's aircraft instead (TAKE CONTROL).</summary>
        private Aircraft SpawnAircraft(Spawner spawner, AircraftDefinition definition, Vector3 ground, Quaternion rotation, FactionHQ hq,
            string name, NuclearOption.Networking.Player player, Vector3? velocity = null, Loadout loadout = null, float fuel = -1f)
        {
            if (loadout == null)
            {
                StandardLoadout[] standard = definition.aircraftParameters != null ? definition.aircraftParameters.StandardLoadouts : null;
                if (standard != null)
                    foreach (StandardLoadout option in standard)
                        if (option != null && !option.disabled && option.loadout != null)
                        {
                            loadout = option.loadout;
                            if (fuel < 0f) fuel = option.FuelRatio > 0f ? option.FuelRatio : 1f;
                            break;
                        }
            }
            if (fuel < 0f) fuel = 1f;
            Vector3 position = velocity.HasValue ? ground : new Vector3(ground.x, Mathf.Max(ground.y + altitude, Datum.LocalSeaY + 150f), ground.z);
            Vector3 speed = velocity ?? rotation * Vector3.forward * CruiseSpeed(definition);
            Aircraft aircraft = spawner.SpawnAircraft(player, definition.unitPrefab, loadout, fuel, default, position.ToGlobalPosition(), rotation,
                speed, null, hq, name, 0.75f, 0.75f);
            if (aircraft != null && player == null) Wake(aircraft);
            return aircraft;
        }

        private static float CruiseSpeed(AircraftDefinition definition)
        {
            AircraftParameters p = definition.aircraftParameters;
            if (p == null) return 150f;
            if (p.takeoffSpeed < 5f) return 25f; // a rotorcraft starts slow
            return Mathf.Max(80f, Mathf.Clamp(p.cornerSpeed, p.takeoffSpeed * 1.5f, Mathf.Max(p.takeoffSpeed * 1.5f, p.maxSpeed * 0.75f)));
        }

        private static void Wake(Aircraft aircraft)
        {
            if (aircraft.pilots == null || aircraft.pilots.Length == 0 || aircraft.pilots[0] == null) return;
            Pilot pilot = aircraft.pilots[0];
            if (pilot.AICombatState != null) pilot.SwitchState(pilot.AICombatState);
            else if (pilot.AIHeloCombatState != null) pilot.SwitchState(pilot.AIHeloCombatState);
        }

        public string Undo()
        {
            if (!Host(out string refusal)) return Say(refusal);
            if (batches.Count == 0) return Say("Nothing to undo.");
            List<Unit> last = batches[batches.Count - 1];
            batches.RemoveAt(batches.Count - 1);
            int n = 0;
            foreach (Unit unit in last)
                if (unit != null) { Remove(unit); n++; }
            return Say("UNDONE: " + n + " unit(s) removed.");
        }

        public string ClearAll()
        {
            if (!Host(out string refusal)) return Say(refusal);
            int n = 0;
            foreach (List<Unit> batch in batches)
                foreach (Unit unit in batch)
                    if (unit != null) { Remove(unit); n++; }
            batches.Clear();
            return Say("CLEARED: " + n + " ARES unit(s) removed.");
        }

        /// <summary>The game master sees what it made: an ARES unit of another side is a live track of yours (its map icon appears and
        /// can be selected), refreshed every 2 s from the cheats tick so it never goes stale.</summary>
        private static void TrackForLocal(Unit unit)
        {
            if (unit == null || unit.disabled || !GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player) || player.HQ == null ||
                unit.NetworkHQ == player.HQ) return;
            TrackingInfo info = player.HQ.GetTrackingData(unit.persistentID);
            if (info != null) info.UpdateInfo(unit.GlobalPosition());
            else player.HQ.RpcUpdateTrackingInfo(unit.persistentID);
        }

        private void TrackSpawned()
        {
            foreach (List<Unit> batch in batches)
                foreach (Unit unit in batch) TrackForLocal(unit);
        }

        /// <summary>The newest batch still in the world (the sim hook selects it).</summary>
        internal List<Unit> LastBatch() => batches.Count > 0 ? batches[batches.Count - 1] : null;

        internal static readonly string[] SideLabels = { "BDF", "PALA", "PMC", "PIRATES" };
        internal static string SideName(int side) => side >= 0 && side < SideLabels.Length ? SideLabels[side] : "?";
    }
}

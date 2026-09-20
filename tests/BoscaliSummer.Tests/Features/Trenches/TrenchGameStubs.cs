#if UNITY_EDITOR
// Isolated Unity regression harness. Native AI and Mirage require an in-game test.
using System.Collections.Generic;
using UnityEngine;
public class FactionHQ : MonoBehaviour { }
public static class Datum
{
    public static Transform origin;
    // Floating origin: global coordinates shift by this much to reach local space.
    public static Vector3 originPosition;
}
public static class PhysicsLayers { public const int IgnoreRaycast = 2; }
public class UnitPart : MonoBehaviour { public float hitPoints = 100; }
// Vanilla Unit carries the disabled flag and the owning HQ the garrison polls; only the
// members the trenches module touches exist here.
public class Unit : MonoBehaviour
{
    public bool disabled;
    public string UniqueName;
    public FactionHQ NetworkHQ;
}
public class PilotDismounted : Unit { }
public class Building : MonoBehaviour { public bool disabled; public FactionHQ NetworkHQ; }
public class Scenery : MonoBehaviour { }
public enum BuildingType { DEF }
public class UnitDefinition
{
    public string jsonKey;
    public string unitName;
    public GameObject unitPrefab;
    public float width = 2, length = 2, height = 2;
}
public class BuildingDefinition : UnitDefinition
{
    public BuildingType buildingType;
    public Vector3 spawnOffset;
}
public class SceneryDefinition : UnitDefinition { }
public class Encyclopedia
{
    public static Encyclopedia i;
    // Deliberately no Lookup dictionary: the works and soldier catalogs must come from the
    // instance lists. The aircraft/vehicle/ship/missile lists exist because the soldier
    // resolver scans every instance list for a dismounted-pilot prefab.
    public List<BuildingDefinition> buildings = new List<BuildingDefinition>();
    public List<SceneryDefinition> scenery = new List<SceneryDefinition>();
    public List<UnitDefinition> otherUnits = new List<UnitDefinition>();
    public List<UnitDefinition> aircraft = new List<UnitDefinition>();
    public List<UnitDefinition> vehicles = new List<UnitDefinition>();
    public List<UnitDefinition> ships = new List<UnitDefinition>();
    public List<UnitDefinition> missiles = new List<UnitDefinition>();

    /// <summary>
    /// Adds a native-defense definition for one of the emplacement keys, optionally with a
    /// real root <see cref="BoxCollider"/> footprint so the garrison's measurement path is
    /// exercised instead of its 1.6m fallback. The prefab is parked inactive like a real
    /// encyclopedia asset; the spawner activates its instances.
    /// </summary>
    public GameObject AddDefensePrefab(string key, float footprint = 0f)
    {
        var prefab = new GameObject(key);
        prefab.AddComponent<Building>();
        prefab.AddComponent<UnitPart>();
        if (footprint > 0f)
        {
            var box = prefab.AddComponent<BoxCollider>();
            box.size = new Vector3(footprint, 1.2f, footprint);
        }
        prefab.SetActive(false);
        buildings.Add(new BuildingDefinition
        {
            jsonKey = key, unitPrefab = prefab, buildingType = BuildingType.DEF, height = 1.2f
        });
        return prefab;
    }

    /// <summary>
    /// Adds a dismounted-pilot prefab plus a decoy scenery piece whose prefab is not a
    /// <see cref="PilotDismounted"/>, so the resolver's discrimination is exercised. Nothing
    /// is added by default: an untouched encyclopedia stays soldier-free and the fail-closed
    /// path is the default, which keeps existing checks unaffected. Returns the pilot prefab.
    /// </summary>
    public GameObject AddSoldierPrefab()
    {
        var decoy = new GameObject("Trenches_Decoy_Truck");
        decoy.AddComponent<UnitPart>();
        scenery.Add(new SceneryDefinition
        {
            jsonKey = "Trenches_Decoy_Truck", unitName = "Truck", unitPrefab = decoy,
            width = 2.4f, length = 6f, height = 2.4f
        });
        var pilot = new GameObject("Trenches_DismountedPilot");
        pilot.AddComponent<PilotDismounted>();
        otherUnits.Add(new UnitDefinition
        {
            jsonKey = "Trenches_DismountedPilot", unitName = "Dismounted pilot", unitPrefab = pilot
        });
        return pilot;
    }
}
public struct GlobalPosition
{
    private Vector3 value;
    public GlobalPosition(Vector3 v) { value = v; }
    public GlobalPosition(float x, float y, float z) { value = new Vector3(x, y, z); }
    public Vector3 ToLocalPosition() => value + Datum.originPosition;
}
public static class NetworkSceneSingleton<T> where T : class { public static T i; }
public abstract class SceneSingleton<T> : MonoBehaviour where T : SceneSingleton<T> { public static T i; }
public class CameraStateManager : SceneSingleton<CameraStateManager> { public Camera mainCamera; }
public sealed class GameAssets
{
    public static GameAssets i;
    public PhysicMaterial terrainMaterial;
}
public class Spawner
{
    public bool IsServer = true;
    public readonly ObjectManager ServerObjectManager = new ObjectManager();
    public readonly List<Building> Spawned = new List<Building>();
    public readonly List<Scenery> ScenerySpawned = new List<Scenery>();
    public readonly List<GameObject> SceneryPrefabs = new List<GameObject>();
    public readonly List<PilotDismounted> PilotsSpawned = new List<PilotDismounted>();
    public readonly List<GameObject> PilotPrefabs = new List<GameObject>();
    public Building SpawnBuilding(GameObject prefab, GlobalPosition position, Quaternion rotation, FactionHQ owner,
        object airbase, string name, bool capturable, object factory)
    {
        var go = Object.Instantiate(prefab, position.ToLocalPosition(), rotation);
        go.SetActive(true); // harness prefabs are parked inactive so they neither render nor collide
        go.name = name;
        var building = go.GetComponent<Building>();
        building.NetworkHQ = owner;
        Spawned.Add(building);
        return building;
    }
    public Scenery SpawnScenery(GameObject prefab, GlobalPosition position, Quaternion rotation, string name)
    {
        var go = Object.Instantiate(prefab, position.ToLocalPosition(), rotation);
        go.name = name;
        var scenery = go.GetComponent<Scenery>();
        ScenerySpawned.Add(scenery);
        SceneryPrefabs.Add(prefab);
        return scenery;
    }
    public PilotDismounted SpawnPilot(GameObject prefab, GlobalPosition globalPosition, Quaternion rotation, FactionHQ hq,
        string uniqueName)
    {
        var go = Object.Instantiate(prefab, globalPosition.ToLocalPosition(), rotation);
        go.SetActive(true);
        go.name = uniqueName;
        var pilot = go.GetComponent<PilotDismounted>();
        pilot.UniqueName = uniqueName;
        pilot.NetworkHQ = hq;
        PilotsSpawned.Add(pilot);
        PilotPrefabs.Add(prefab);
        return pilot;
    }
    public class ObjectManager { public void Destroy(GameObject go) => Object.DestroyImmediate(go); }
}
namespace BoscaliSummer.Features.Trenches.Runtime
{
    // The real probe raycasts the game terrain; the regression harness digs on flat ground.
    internal static class TrenchTerrain
    {
        internal static bool TryGround(Vector3 position, out Vector3 ground)
        { ground = new Vector3(position.x, 0, position.z); return true; }
        internal static bool TryGround(float x, float z, out Vector3 ground)
        { ground = new Vector3(x, 0, z); return true; }
        internal static Vector3 SnapToGround(Vector3 position) => new Vector3(position.x, 0, position.z);
    }
}
#endif

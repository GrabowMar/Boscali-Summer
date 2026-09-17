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
public class Building : MonoBehaviour { public bool disabled; public FactionHQ NetworkHQ; }
public class Scenery : MonoBehaviour { }
public enum BuildingType { DEF }
public class BuildingDefinition
{
    public string jsonKey;
    public BuildingType buildingType;
    public GameObject unitPrefab;
    public float width = 2, length = 2, height = 2;
    public Vector3 spawnOffset;
}
public class UnitDefinition
{
    public string jsonKey;
    public string unitName;
    public GameObject unitPrefab;
    public float width = 2, length = 2, height = 2;
}
public class SceneryDefinition : UnitDefinition { }
public class Encyclopedia
{
    public static Encyclopedia i;
    // Deliberately no Lookup dictionary: the works catalog must come from the instance lists.
    public List<BuildingDefinition> buildings = new List<BuildingDefinition>();
    public List<SceneryDefinition> scenery = new List<SceneryDefinition>();
    public List<UnitDefinition> otherUnits = new List<UnitDefinition>();
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
    public Building SpawnBuilding(GameObject prefab, GlobalPosition position, Quaternion rotation, FactionHQ owner,
        object airbase, string name, bool capturable, object factory)
    {
        var go = Object.Instantiate(prefab, position.ToLocalPosition(), rotation);
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

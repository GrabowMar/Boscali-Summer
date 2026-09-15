#if UNITY_EDITOR
// Isolated Unity regression harness. Native AI and Mirage require an in-game test.
using System.Collections.Generic;
using UnityEngine;
public class FactionHQ : MonoBehaviour { }
public static class Datum { public static Transform origin; }
public static class PhysicsLayers { public const int IgnoreRaycast = 2; }
public class UnitPart : MonoBehaviour { public float hitPoints = 100; }
public class Building : MonoBehaviour { public bool disabled; public FactionHQ NetworkHQ; }
public enum BuildingType { DEF }
public class BuildingDefinition
{
    public string jsonKey;
    public BuildingType buildingType;
    public GameObject unitPrefab;
    public float width = 2, length = 2, height = 2;
    public Vector3 spawnOffset;
}
public class Encyclopedia { public static Encyclopedia i; public List<BuildingDefinition> buildings = new List<BuildingDefinition>(); }
public struct GlobalPosition
{
    private Vector3 value;
    public GlobalPosition(Vector3 v) { value = v; }
    public Vector3 ToLocalPosition() => value;
}
public static class NetworkSceneSingleton<T> where T : class { public static T i; }
public class Spawner
{
    public bool IsServer = true;
    public readonly ObjectManager ServerObjectManager = new ObjectManager();
    public readonly List<Building> Spawned = new List<Building>();
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

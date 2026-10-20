using System;
using System.Reflection;
using BoscaliSummer.Core.Util;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Kaiju.Runtime
{
    /// <summary>
    /// The art (embedded kaiju.bundle, built by tools/Compile-KaijuAssets.ps1) and the hardpoint unit. A hardpoint is the
    /// game's smallest single-part building cloned at runtime, stripped of its looks and given one sphere collider, so
    /// every weak spot is a real lockable, damageable, replicated Unit with no Blueprinter content. Every peer builds the
    /// same clone and registers it under a fixed prefab hash, so the host's spawn messages resolve on clients.
    /// </summary>
    internal static class KaijuAssets
    {
        public const string NodeKey = "BS_KaijuNode";
        private const string Resource = "BoscaliSummer.Kaiju.kaiju.bundle";
        private const int NodePrefabHash = 0x4B4A4E31; // "KJN1": identical on every peer
        private static readonly FieldInfo Wreckage = AccessTools.Field(typeof(Building), "wreckage");
        private static readonly FieldInfo RepairTime = AccessTools.Field(typeof(Building), "repairTime");
        private static readonly FieldInfo Disintegrate = AccessTools.Field(typeof(UnitPart), "disintegrateObjects");

        private static GameObject body, spike, blister, node;
        private static bool loaded, nodeTried;
        private static ClientObjectManager registeredWith;

        public static GameObject Body => Load() ? body : null;
        public static GameObject Spike => Load() ? spike : null;
        public static GameObject Blister => Load() ? blister : null;

        public static GameObject Node
        {
            get
            {
                if (node == null && !nodeTried && Encyclopedia.i != null) { nodeTried = true; node = BuildNode(); }
                return node;
            }
        }

        /// <summary>Register the hardpoint prefab with this peer's client (cheap to call often). <paramref name="force"/>
        /// re-registers after the game rebuilt its own prefab table.</summary>
        public static void EnsureRegistered(bool force = false)
        {
            ClientObjectManager objects = NetworkManagerNuclearOption.i != null ? NetworkManagerNuclearOption.i.ClientObjectManager : null;
            if (objects == null || (objects == registeredWith && !force) || Node == null) return;
            objects.RegisterPrefab(Node.GetComponent<NetworkIdentity>());
            registeredWith = objects;
        }

        private static bool Load()
        {
            if (loaded) return body != null;
            loaded = true;
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(KaijuAssets).Assembly, Resource, 256 * 1024 * 1024);
                AssetBundle bundle = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
                if (bundle == null)
                {
                    Debug.LogWarning("[Kaiju] kaiju.bundle is not embedded; run tools/Compile-KaijuAssets.ps1");
                    return false;
                }
                foreach (GameObject go in bundle.LoadAllAssets<GameObject>())
                {
                    // Point bundled materials at the game's own URP shaders; the bundle's copies may miss its variants.
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials)
                            if (m != null && m.shader != null && Shader.Find(m.shader.name) is Shader native) m.shader = native;
                    if (go.name == "KaijuBody") body = go;
                    else if (go.name == "KaijuSpike") spike = go;
                    else if (go.name == "KaijuBlister") blister = go;
                }
                Debug.Log("[Kaiju] kaiju.bundle loaded: body " + (body != null) + ", spike " + (spike != null) + ", blister " + (blister != null));
            }
            catch (Exception error) { Debug.LogError("[Kaiju] kaiju.bundle: " + error.Message); }
            return body != null;
        }

        private static GameObject BuildNode()
        {
            if (Encyclopedia.i == null) return null;
            BuildingDefinition donor = null;
            float best = float.MaxValue;
            foreach (BuildingDefinition def in Encyclopedia.i.buildings)
            {
                if (def == null || def.unitPrefab == null || def.unitPrefab.GetComponent<Building>() == null) continue;
                GameObject p = def.unitPrefab;
                if (p.GetComponentsInChildren<UnitPart>(true).Length != 1 || p.GetComponentInChildren<Factory>(true) != null ||
                    p.GetComponentInChildren<Turret>(true) != null || p.GetComponentInChildren<Radar>(true) != null) continue;
                float volume = def.length * def.width * def.height;
                if (volume > 0f && volume < best) { best = volume; donor = def; }
            }
            if (donor == null)
            {
                Debug.LogWarning("[Kaiju] no single-part building to clone as a hardpoint");
                return null;
            }

            // Child of an inactive holder: the clone never wakes; Spawner.SpawnBuilding instantiates live copies of it.
            var holder = new GameObject("KaijuTemplates");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            GameObject go = UnityEngine.Object.Instantiate(donor.unitPrefab, holder.transform);
            go.name = NodeKey;

            // The clone keeps the donor's own definition: the game syncs definitions by Encyclopedia index (mission stats,
            // supply), and a new one would need an index every peer agrees on. Names are set per hardpoint instead.
            Building building = go.GetComponent<Building>();
            building.capturable = false;
            Wreckage?.SetValue(building, null);
            RepairTime?.SetValue(building, 1e9f);
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            UnitPart part = go.GetComponentInChildren<UnitPart>(true);
            Disintegrate?.SetValue(part, new GameObject[0]);
            part.transform.localPosition = Vector3.zero;
            part.gameObject.AddComponent<SphereCollider>().radius = 30f;
            go.GetComponent<NetworkIdentity>().PrefabHash = NodePrefabHash;
            Debug.Log("[Kaiju] hardpoint template cloned from " + donor.jsonKey);
            return go;
        }
    }
}

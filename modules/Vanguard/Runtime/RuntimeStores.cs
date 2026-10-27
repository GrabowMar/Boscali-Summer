using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>One runtime-built store: the mount it copies, the stations that may carry it, and how the copy becomes the store.</summary>
    internal sealed class StoreRecipe
    {
        /// <summary>The clone's jsonKey and asset name: save / mission / MP identity, never rename.</summary>
        public string Key;
        /// <summary>jsonKey (or asset name) of the mount to copy.</summary>
        public string Donor;
        /// <summary>Every aircraft station that offers one of these mounts (jsonKey or asset name) offers the clone too.</summary>
        public string[] Carriers;
        /// <summary>Extra precondition besides the donor existing (a missile clone the mount fires, say); null for none.</summary>
        public Func<bool> Ready;
        /// <summary>Turns the copy into the store. <c>mount.prefab</c> is already a private template under an inactive holder.</summary>
        public Action<WeaponMount> Build;
        /// <summary>Vanilla AI loadouts may roll it (a weapon the AI can use). Support stores and two-station stores stay off them.</summary>
        public bool AiPicks;
    }

    /// <summary>
    /// VANGUARD stores built at runtime from vanilla or bundle mounts: the Blueprinter editor project is gone, so the bundle
    /// cannot grow. Recipes run strictly in registration order once their donors exist, and each clone is appended to the
    /// Encyclopedia lists and the network index, so every peer running the same mods hands out the same indices (loadouts are
    /// synced by index, missions by jsonKey). Templates are active children of an inactive DontDestroyOnLoad holder, so they
    /// never wake but their instances do. A store goes on every station that already offers one of its carriers, the
    /// runtime twin of the bundle builder's Carriers().
    /// </summary>
    internal static class RuntimeStores
    {
        private const float WarnSeconds = 120f;
        private static readonly List<StoreRecipe> Recipes = new List<StoreRecipe>();
        private static readonly HashSet<WeaponMount> AiSkipped = new HashSet<WeaponMount>();
        private static readonly Dictionary<string, WeaponMount> Built = new Dictionary<string, WeaponMount>();
        private static readonly List<Placement> PlacedList = new List<Placement>();
        private static GameObject holder;
        private static int next;
        private static float waitingSince = -1f;

        /// <summary>A station that offers a runtime store: aircraft jsonKey + index into its WeaponManager.hardpointSets.</summary>
        internal struct Placement
        {
            public string Store;
            public string Aircraft;
            public int Set;
        }

        public static IReadOnlyList<Placement> Placed => PlacedList;

        /// <summary>Queue a recipe. Call in a fixed order from module install, on every peer.</summary>
        public static void Add(StoreRecipe recipe)
        {
            if (!Recipes.Exists(r => r.Key == recipe.Key)) Recipes.Add(recipe);
        }

        /// <summary>The built store with this jsonKey, or null before its recipe ran.</summary>
        public static WeaponMount Mount(string key) => key != null && Built.TryGetValue(key, out WeaponMount mount) ? mount : null;

        /// <summary>A runtime store vanilla AI loadouts must not roll (WeaponManager.SelectAIAircraftWeapons picks options at random).</summary>
        public static bool AiSkips(WeaponMount mount) => mount != null && AiSkipped.Contains(mount);

        /// <summary>The inactive DontDestroyOnLoad parent every runtime template lives under.</summary>
        public static Transform Holder
        {
            get
            {
                if (holder == null)
                {
                    holder = new GameObject("VanguardRuntimeTemplates");
                    holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(holder);
                }
                return holder.transform;
            }
        }

        /// <summary>An inactive-parented copy of a prefab: it keeps its own active flag, so instances spawned from it are live.</summary>
        public static GameObject Template(GameObject source, string name)
        {
            GameObject copy = UnityEngine.Object.Instantiate(source, Holder);
            copy.name = name;
            return copy;
        }

        /// <summary>Mount by jsonKey, else by asset name (vanilla placeholders carry the asset name in loadout data).</summary>
        public static WeaponMount FindMount(string keyOrName)
        {
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (e == null || string.IsNullOrEmpty(keyOrName)) return null;
            if (Encyclopedia.WeaponLookup != null && Encyclopedia.WeaponLookup.TryGetValue(keyOrName, out WeaponMount byKey) && byKey != null)
                return byKey;
            foreach (WeaponMount mount in e.weaponMounts)
                if (mount != null && (mount.jsonKey == keyOrName || mount.name == keyOrName)) return mount;
            return null;
        }

        /// <summary>Appends a runtime MissileDefinition the way Encyclopedia.AfterLoad indexes its own (lists, lookup, network index).
        /// The game registers every Encyclopedia missile's unit prefab with Mirage when a client starts, so the template's
        /// NetworkIdentity only needs a fixed, non-zero, unique PrefabHash set by the caller.</summary>
        public static void AppendMissile(MissileDefinition definition)
        {
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (e == null || definition == null || e.missiles.Contains(definition)) return;
            definition.CacheMass();
            e.missiles.Add(definition);
            if (Encyclopedia.Lookup != null) Encyclopedia.Lookup[definition.jsonKey] = definition;
            ((INetworkDefinition)definition).LookupIndex = e.IndexLookup.Count;
            e.IndexLookup.Add(definition);
        }

        /// <summary>Runs every recipe whose turn it is and whose donor exists. Cheap once all have run.</summary>
        public static void Tick()
        {
            if (next >= Recipes.Count) return;
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (e == null || Encyclopedia.WeaponLookup == null) return;
            while (next < Recipes.Count)
            {
                StoreRecipe recipe = Recipes[next];
                WeaponMount existing = FindMount(recipe.Key);
                if (existing != null)
                {
                    Built[recipe.Key] = existing;
                    next++;
                    continue;
                }
                WeaponMount donor = FindMount(recipe.Donor);
                if (donor == null || (recipe.Ready != null && !recipe.Ready()))
                {
                    // Keep waiting: a timed give-up would depend on how fast each machine loads Blueprinter's bundle and could
                    // leave peers with different index lists. Peers with the same content wait on the same missing donor.
                    if (waitingSince < 0f) waitingSince = Time.unscaledTime;
                    else if (waitingSince > 0f && Time.unscaledTime - waitingSince > WarnSeconds)
                    {
                        Debug.LogWarning("[Vanguard] runtime store " + recipe.Key + " is still waiting for donor " + recipe.Donor + "; it and later stores are not built");
                        waitingSince = 0f; // warned once
                    }
                    return;
                }
                waitingSince = -1f;
                try { Build(e, recipe, donor); }
                catch (Exception error) { PatchGuard.Report("Vanguard.RuntimeStores." + recipe.Key, error); }
                next++;
            }
        }

        private static void Build(Encyclopedia e, StoreRecipe recipe, WeaponMount donor)
        {
            WeaponMount mount = UnityEngine.Object.Instantiate(donor);
            mount.name = recipe.Key;
            mount.jsonKey = recipe.Key; // the field, not IHasJsonKey.JsonKey: that setter throws outside the editor
            mount.prefab = Template(donor.prefab, recipe.Key);
            recipe.Build?.Invoke(mount);
            mount.Initialize();

            e.weaponMounts.Add(mount);
            Encyclopedia.WeaponLookup[recipe.Key] = mount;
            ((INetworkDefinition)mount).LookupIndex = e.IndexLookup.Count;
            e.IndexLookup.Add(mount);
            Built[recipe.Key] = mount;
            if (!recipe.AiPicks) AiSkipped.Add(mount);

            int sets = Place(e, recipe, mount);
            Adopt();
            Debug.Log("[Vanguard] runtime store " + recipe.Key + " (" + mount.mountName + ") from " + donor.jsonKey + ", index " +
                      ((INetworkDefinition)mount).LookupIndex + ", on " + sets + " stations");
        }

        private static int Place(Encyclopedia e, StoreRecipe recipe, WeaponMount mount)
        {
            var carriers = new HashSet<string>(recipe.Carriers ?? new string[0]);
            int count = 0;
            foreach (AircraftDefinition aircraft in e.aircraft)
            {
                WeaponManager manager = aircraft != null && aircraft.unitPrefab != null ? aircraft.unitPrefab.GetComponentInChildren<WeaponManager>(true) : null;
                if (manager == null) continue;
                for (int i = 0; i < manager.hardpointSets.Length; i++)
                {
                    List<WeaponMount> options = manager.hardpointSets[i].weaponOptions;
                    if (options.Contains(mount) || !options.Exists(w => w != null && (carriers.Contains(w.jsonKey) || carriers.Contains(w.name)))) continue;
                    options.Add(mount);
                    PlacedList.Add(new Placement { Store = recipe.Key, Aircraft = aircraft.jsonKey, Set = i });
                    count++;
                }
            }
            return count;
        }

        /// <summary>Aircraft spawned before a store existed (a sim under -autoHost) copied their prefab's sets earlier: give them the new options.</summary>
        public static void Adopt()
        {
            foreach (Aircraft aircraft in UnityEngine.Object.FindObjectsOfType<Aircraft>())
            {
                WeaponManager prefab = aircraft.definition != null && aircraft.definition.unitPrefab != null
                    ? aircraft.definition.unitPrefab.GetComponentInChildren<WeaponManager>(true) : null;
                HardpointSet[] sets = aircraft.weaponManager != null ? aircraft.weaponManager.hardpointSets : null;
                for (int i = 0; prefab != null && sets != null && i < sets.Length && i < prefab.hardpointSets.Length; i++)
                    foreach (WeaponMount option in prefab.hardpointSets[i].weaponOptions)
                        if (!sets[i].weaponOptions.Contains(option)) sets[i].weaponOptions.Add(option);
            }
        }
    }

    /// <summary>Drives <see cref="RuntimeStores.Tick"/>: recipes wait for Blueprinter's bundle (frames after the Encyclopedia loads).</summary>
    internal sealed class RuntimeStoreService : MonoBehaviour, ISceneService
    {
        public void ResetForScene() => RuntimeStores.Tick();

        private void Update() => RuntimeStores.Tick();
    }

    /// <summary>A pylon pod that works through the aircraft's own systems and must never become a selectable weapon station.</summary>
    internal interface IHiddenPod
    {
    }
}

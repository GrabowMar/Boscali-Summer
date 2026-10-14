// The sceneId HQ and the registry patches are ported from ThirdFaction by 9138noms (https://github.com/9138noms/ThirdFaction),
// MIT licence, Copyright (c) 9138noms. That mod credits MinecrackTyler's NOFactionMod for the sceneId idea.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Modules.Pmc.Configuration;
using HarmonyLib;
using Mirage;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Pmc.Runtime
{
    /// <summary>
    /// The third side. Its <see cref="Faction"/> lives for the session. Its <see cref="FactionHQ"/> is made per scene, only for a mission whose
    /// faction list names PMC (or the editor's), as a Mirage scene object with a fresh sceneId so its sync and server setup run like any other HQ.
    /// It is kept out of <c>FactionRegistry.HQLookup</c>: the join screen has two slots, and a script enlists into it by name.
    /// </summary>
    internal static class PmcSide
    {
        internal static Faction Faction;
        internal static FactionHQ Hq;
        internal static ServerObjectManager Som;
        internal static ManualLogSource Log;
        internal static readonly HashSet<int> UnitIds = new HashSet<int>();
        internal static readonly HashSet<PersistentID> UnitPids = new HashSet<PersistentID>();

        private static readonly FieldInfo SceneIdField = AccessTools.Field(typeof(NetworkIdentity), "_sceneId");

        internal static void Install(PmcSettings settings, ManualLogSource log)
        {
            Log = log;
            Faction = Build(settings);
        }

        internal static void Uninstall()
        {
            Forget();
            Faction = null;
        }

        /// <summary>True when the mission names a PMC faction.</summary>
        internal static bool Wanted(Mission mission) => Faction != null && mission != null && mission.TryGetFaction(SideNames.Pmc, out _);

        /// <summary>Takes PMC out of the game's registries; the next mission that names it brings it back.</summary>
        internal static void Forget()
        {
            if (Faction != null)
            {
                FactionRegistry.factions.Remove(Faction);
                if (FactionRegistry.factionLookup.TryGetValue(SideNames.Pmc, out Faction listed) && listed == Faction)
                    FactionRegistry.factionLookup.Remove(SideNames.Pmc);
                FactionRegistry.HQLookup.Remove(Faction);
            }
            Hq = null;
            UnitIds.Clear();
            UnitPids.Clear();
        }

        /// <summary>The PMC HQ of this scene, made (and spawned, once the scene objects are) on first ask.</summary>
        internal static FactionHQ Ensure()
        {
            if (Hq != null) return Hq;
            Hq = null;
            UnitIds.Clear();
            UnitPids.Clear();
            // The server makes and spawns the HQ. A client building its own would never be spawned, and would leave PMC in its registries
            // for the vanilla missions after (a client never runs SetMission, where Forget happens).
            if (Faction == null || !GameAccess.IsServer()) return null;
            try
            {
                GameObject parent = GameObject.Find("SceneEssentials/FactionHQ");
                var go = new GameObject("FactionHQ_" + SideNames.Pmc);
                if (parent != null) go.transform.SetParent(parent.transform);
                NetworkIdentity identity = go.AddComponent<NetworkIdentity>();
                SceneIdField?.SetValue(identity, FreshSceneId());
                FactionHQ hq;
                try { hq = go.AddComponent<FactionHQ>(); }
                catch (Exception e)
                {
                    Log?.LogWarning("[Pmc] FactionHQ complained while being added (expected): " + e.Message);
                    hq = go.GetComponent<FactionHQ>();
                }
                if (hq == null)
                {
                    UnityEngine.Object.Destroy(go);
                    Log?.LogError("[Pmc] no FactionHQ could be made.");
                    return null;
                }
                // Without a stats tracker Gun.SpawnBullet throws for PMC guns.
                MissionStatsTracker stats = go.AddComponent<MissionStatsTracker>();
                hq.faction = Faction;
                hq.missionStatsTracker = stats;
                stats.hq = hq;
                var sync = new SyncSettings
                {
                    From = SyncFrom.Server,
                    To = SyncTo.Owner | SyncTo.ObserversOnly | SyncTo.OwnerAndObservers,
                    Timing = SyncTiming.Variable,
                    Interval = 0.1f
                };
                FieldInfo syncField = AccessTools.Field(typeof(NetworkBehaviour), "_syncSettings") ?? AccessTools.Field(typeof(NetworkBehaviour), "syncSettings");
                syncField?.SetValue(hq, sync);
                syncField?.SetValue(stats, sync);
                if (!FactionRegistry.factions.Contains(Faction)) FactionRegistry.factions.Add(Faction);
                FactionRegistry.factionLookup[SideNames.Pmc] = Faction;
                MissionManager.CurrentMission?.EnsureFactionExists(Faction, out _);
                Hq = hq;
                Log?.LogInfo("[Pmc] HQ made.");
                TrySpawn();
                return hq;
            }
            catch (Exception e)
            {
                Log?.LogError("[Pmc] HQ could not be made: " + e);
                return null;
            }
        }

        /// <summary>Registers the HQ with the server once the scene objects are spawned; before that the scene spawn picks it up itself.</summary>
        internal static void TrySpawn()
        {
            if (Hq == null || Som == null) return;
            try
            {
                NetworkIdentity identity = Hq.GetComponent<NetworkIdentity>();
                if (identity == null || identity.NetId != 0) return;
                Som.Spawn(identity.gameObject);
                Log?.LogInfo("[Pmc] HQ spawned, net id " + identity.NetId + ".");
            }
            catch (Exception e) { Log?.LogWarning("[Pmc] HQ spawn failed: " + e.Message); }
        }

        private static ulong FreshSceneId()
        {
            var taken = new HashSet<ulong>();
            if (SceneIdField != null)
                foreach (NetworkIdentity existing in Resources.FindObjectsOfTypeAll<NetworkIdentity>())
                    taken.Add((ulong)SceneIdField.GetValue(existing));
            var random = new System.Random();
            var bytes = new byte[8];
            ulong id;
            do { random.NextBytes(bytes); id = BitConverter.ToUInt64(bytes, 0); } while (id == 0 || taken.Contains(id));
            return id;
        }

        private static Faction Build(PmcSettings settings)
        {
            Faction faction = ScriptableObject.CreateInstance<Faction>();
            // A scene change unloads assets nothing in a scene holds; the session needs this one, and its logos, to survive.
            faction.hideFlags = HideFlags.DontUnloadUnusedAsset;
            faction.name = SideNames.Pmc;
            faction.factionName = SideNames.Pmc;
            faction.factionTag = settings.Tag.Value;
            faction.factionExtendedName = settings.DisplayName.Value;
            faction.color = PmcColor.TryParse(settings.Color.Value, out float r, out float g, out float b, out float a) ? new Color(r, g, b, a) : new Color(0.2f, 0.8f, 0.2f, 1f);
            faction.selectedColor = Color.Lerp(faction.color, Color.white, 0.3f);
            // Different screens read different sprite slots; an empty one shows a grey placeholder.
            faction.factionColorLogo = Logo(faction.color);
            faction.factionHeaderSprite = faction.factionColorLogo;
            faction.factionGrayscaleLogo = Gray(faction.factionColorLogo);
            FieldInfo convoys = AccessTools.Field(typeof(Faction), "convoyGroups");
            convoys?.SetValue(faction, Activator.CreateInstance(convoys.FieldType));
            return faction;
        }

        /// <summary>A disc with a darker ring and an eight-pointed star.</summary>
        private static Sprite Logo(Color color)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            float centre = size / 2f, outer = centre - 2f, ring = outer - 6f, star = ring - 4f;
            Color ringColor = Color.Lerp(color, Color.black, 0.55f), starColor = Color.Lerp(color, Color.white, 0.45f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre, dy = y - centre, distance = Mathf.Sqrt(dx * dx + dy * dy);
                    Color pixel;
                    if (distance > outer + 1.5f) pixel = Color.clear;
                    else if (distance > outer) pixel = Color.Lerp(ringColor, Color.clear, (distance - outer) / 1.5f);
                    else if (distance > ring) pixel = ringColor;
                    else pixel = distance <= Mathf.Lerp(star * 0.45f, star, Mathf.Abs(Mathf.Cos(Mathf.Atan2(dy, dx) * 4f))) ? starColor : color;
                    texture.SetPixel(x, y, pixel);
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite Gray(Sprite source)
        {
            Texture2D from = source.texture;
            var texture = new Texture2D(from.width, from.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            Color[] pixels = from.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                float l = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                pixels[i] = new Color(l, l, l, pixels[i].a);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}

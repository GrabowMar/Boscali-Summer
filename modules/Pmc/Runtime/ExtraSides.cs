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
    /// A side the base game does not have. Its <see cref="Faction"/> lives for the session. Its <see cref="FactionHQ"/> is made per scene, only for a mission
    /// whose faction list names it (a mission script brings it), as a Mirage scene object with a fresh sceneId so its sync and server setup run like any other
    /// HQ. It is kept out of <c>FactionRegistry.HQLookup</c>: the join screen has two slots, and a script enlists into it by name.
    /// </summary>
    internal sealed class ExtraSide
    {
        private static readonly FieldInfo SceneIdField = AccessTools.Field(typeof(NetworkIdentity), "_sceneId");

        /// <summary>Its faction name in mission files.</summary>
        internal readonly string Key;
        internal Faction Faction;
        internal FactionHQ Hq;
        internal readonly HashSet<int> UnitIds = new HashSet<int>();
        internal readonly HashSet<PersistentID> UnitPids = new HashSet<PersistentID>();

        internal ExtraSide(string key) => Key = key;

        /// <summary>True when the mission names this side.</summary>
        internal bool Wanted(Mission mission) => Faction != null && mission != null && mission.TryGetFaction(Key, out _);

        internal bool Owns(FactionHQ hq) => Faction != null && hq != null && hq.faction == Faction;

        /// <summary>Takes the side out of the game's registries; the next mission that names it brings it back.</summary>
        internal void Forget()
        {
            if (Faction != null)
            {
                FactionRegistry.factions.Remove(Faction);
                if (FactionRegistry.factionLookup.TryGetValue(Key, out Faction listed) && listed == Faction)
                    FactionRegistry.factionLookup.Remove(Key);
                FactionRegistry.HQLookup.Remove(Faction);
            }
            Hq = null;
            UnitIds.Clear();
            UnitPids.Clear();
        }

        /// <summary>This scene's HQ of the side, made (and spawned, once the scene objects are) on first ask.</summary>
        internal FactionHQ Ensure()
        {
            if (Hq != null) return Hq;
            UnitIds.Clear();
            UnitPids.Clear();
            // The server makes and spawns the HQ. A client building its own would never be spawned, and would leave the side in its registries
            // for the vanilla missions after (a client never runs SetMission, where Forget happens).
            if (Faction == null || !GameAccess.IsServer()) return null;
            ManualLogSource log = ExtraSides.Log;
            try
            {
                GameObject parent = GameObject.Find("SceneEssentials/FactionHQ");
                var go = new GameObject("FactionHQ_" + Key);
                if (parent != null) go.transform.SetParent(parent.transform);
                NetworkIdentity identity = go.AddComponent<NetworkIdentity>();
                SceneIdField?.SetValue(identity, FreshSceneId());
                FactionHQ hq;
                try { hq = go.AddComponent<FactionHQ>(); }
                catch (Exception e)
                {
                    log?.LogWarning("[Pmc] " + Key + " FactionHQ complained while being added (expected): " + e.Message);
                    hq = go.GetComponent<FactionHQ>();
                }
                if (hq == null)
                {
                    UnityEngine.Object.Destroy(go);
                    log?.LogError("[Pmc] no FactionHQ could be made for " + Key + ".");
                    return null;
                }
                // Without a stats tracker Gun.SpawnBullet throws for the side's guns.
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
                FactionRegistry.factionLookup[Key] = Faction;
                MissionManager.CurrentMission?.EnsureFactionExists(Faction, out _);
                Hq = hq;
                log?.LogInfo(Key == SideNames.Pmc ? "[Pmc] HQ made." : "[Pmc] " + Key + " HQ made.");
                TrySpawn();
                return hq;
            }
            catch (Exception e)
            {
                log?.LogError("[Pmc] " + Key + " HQ could not be made: " + e);
                return null;
            }
        }

        /// <summary>Registers the HQ with the server once the scene objects are spawned; before that the scene spawn picks it up itself.</summary>
        internal void TrySpawn()
        {
            ServerObjectManager som = ExtraSides.Som;
            if (Hq == null || som == null) return;
            try
            {
                NetworkIdentity identity = Hq.GetComponent<NetworkIdentity>();
                if (identity == null || identity.NetId != 0) return;
                som.Spawn(identity.gameObject);
                ExtraSides.Log?.LogInfo((Key == SideNames.Pmc ? "[Pmc] HQ" : "[Pmc] " + Key + " HQ") + " spawned, net id " + identity.NetId + ".");
            }
            catch (Exception e) { ExtraSides.Log?.LogWarning("[Pmc] " + Key + " HQ spawn failed: " + e.Message); }
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
    }

    /// <summary>The sides this module adds: PMC (the player's employer in a contract mission) and Pirates (nobody's navy).</summary>
    internal static class ExtraSides
    {
        internal static ExtraSide Pmc, Pirates;
        internal static ExtraSide[] All = Array.Empty<ExtraSide>();
        internal static ServerObjectManager Som;
        internal static ManualLogSource Log;

        internal static void Install(PmcSettings settings, ManualLogSource log)
        {
            Log = log;
            Pmc = new ExtraSide(SideNames.Pmc)
            {
                Faction = Build(SideNames.Pmc, settings.Tag.Value, settings.DisplayName.Value,
                    PmcColor.TryParse(settings.Color.Value, out float r, out float g, out float b, out float a) ? new Color(r, g, b, a) : new Color(0.2f, 0.8f, 0.2f, 1f))
            };
            Pirates = new ExtraSide(SideNames.Pirates) { Faction = Build(SideNames.Pirates, "PIR", "Pirates", new Color(0.78f, 0.24f, 0.16f, 1f)) };
            All = new[] { Pmc, Pirates };
        }

        internal static void Uninstall()
        {
            foreach (ExtraSide side in All)
            {
                side.Forget();
                side.Faction = null;
            }
            All = Array.Empty<ExtraSide>();
        }

        internal static ExtraSide Of(FactionHQ hq)
        {
            if (hq == null) return null;
            foreach (ExtraSide side in All) if (side.Owns(hq)) return side;
            return null;
        }

        internal static ExtraSide Of(Faction faction)
        {
            if (faction == null) return null;
            foreach (ExtraSide side in All) if (side.Faction == faction) return side;
            return null;
        }

        internal static ExtraSide Named(string name)
        {
            foreach (ExtraSide side in All) if (side.Faction != null && side.Key == name) return side;
            return null;
        }

        /// <summary>True for a unit of any extra side.</summary>
        internal static bool Holds(int unitInstanceId)
        {
            foreach (ExtraSide side in All) if (side.UnitIds.Contains(unitInstanceId)) return true;
            return false;
        }

        private static Faction Build(string key, string tag, string displayName, Color color)
        {
            Faction faction = ScriptableObject.CreateInstance<Faction>();
            // A scene change unloads assets nothing in a scene holds; the session needs this one, and its logos, to survive.
            faction.hideFlags = HideFlags.DontUnloadUnusedAsset;
            faction.name = key;
            faction.factionName = key;
            faction.factionTag = tag;
            faction.factionExtendedName = displayName;
            faction.color = color;
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

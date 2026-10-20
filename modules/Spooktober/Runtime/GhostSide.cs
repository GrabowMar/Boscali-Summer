// The sceneId HQ recipe is ported from ThirdFaction by 9138noms (https://github.com/9138noms/ThirdFaction), MIT licence,
// Copyright (c) 9138noms, by way of modules/Pmc/Runtime/PmcSide.cs. That mod credits MinecrackTyler's NOFactionMod for the sceneId idea.
using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using Mirage;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// GHOST, the fallen ace's side. The game treats every other HQ as an enemy, so a GHOST unit fights BDF and PALA alike.
    /// The host makes the HQ mid-mission on first ask: a Mirage scene object with a fresh sceneId, a stats tracker (Gun.SpawnBullet
    /// costs every round against it) and the mission entry its server setup reads. It is kept out of <c>FactionRegistry.HQLookup</c>,
    /// so the join screen and the leaderboard never list it. Radars only scan the HQs in HQLookup, so the other sides see GHOST
    /// by eye only. Host and single player only: a client has no scene object for its sceneId.
    /// </summary>
    internal static class GhostSide
    {
        internal const string Key = "GHOST";

        private static readonly FieldInfo SceneIdField = AccessTools.Field(typeof(NetworkIdentity), "_sceneId");

        private static Faction faction;
        private static FactionHQ hq;
        private static bool hooked;

        internal static bool IsGhost(FactionHQ candidate) => candidate != null && faction != null && candidate.faction == faction;

        /// <summary>This scene's GHOST HQ exists.</summary>
        internal static bool Made => hq != null;

        private static bool refusedLogged;

#if !PUBLIC_RELEASE
        /// <summary>Sim seam (SpooktoberAutomation fake_join): a single-player sim cannot connect a real client.</summary>
        internal static bool FakeRemote;
#endif

        /// <summary>True while any client other than the host's own is connected (the server lists every connection, the host's
        /// own pipe included, from the moment it connects). GHOST is single player / host-without-clients only (ruling R15): a
        /// client has no scene object for the HQ's fresh sceneId, so Mirage would throw SpawnObjectException there. Allocation-free:
        /// the director asks every frame while GHOST exists.</summary>
        internal static bool RemoteClients()
        {
#if !PUBLIC_RELEASE
            if (FakeRemote) return true;
#endif
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            return server != null && server.Active && server.AllPlayers.Count > (server.LocalPlayer != null ? 1 : 0);
        }

        /// <summary>This scene's GHOST HQ, made and spawned on first ask. Server only; null on a client, with remote clients connected
        /// (logged once), or on failure.</summary>
        internal static FactionHQ Ensure()
        {
            if (hq != null) return hq;
            ServerObjectManager som = GameAccess.IsServer() ? GameAccess.NetworkManagerOrNull?.ServerObjectManager : null;
            if (som == null || SceneIdField == null) return null;
            if (RemoteClients())
            {
                if (!refusedLogged) Plugin.Logger.LogWarning("[Spooktober] GHOST refused: remote clients are connected (single player only for now).");
                refusedLogged = true;
                return null;
            }
            if (!hooked)
            {
                // A new scene took the HQ with it; the registries and the mission entry go here.
                SceneManager.sceneLoaded += (scene, mode) =>
                {
                    if (mode != LoadSceneMode.Single) return;
                    try { Forget(); }
                    catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] GHOST forget failed: " + e.Message); }
                };
                hooked = true;
            }
            GameObject go = null;
            try
            {
                if (faction == null) faction = Build();
                go = new GameObject("FactionHQ_" + Key);
                GameObject parent = GameObject.Find("SceneEssentials/FactionHQ");
                if (parent != null) go.transform.SetParent(parent.transform);
                NetworkIdentity identity = go.AddComponent<NetworkIdentity>();
                SceneIdField?.SetValue(identity, FreshSceneId());
                FactionHQ made;
                try { made = go.AddComponent<FactionHQ>(); }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("[Spooktober] FactionHQ complained while being added (expected): " + e.Message);
                    made = go.GetComponent<FactionHQ>();
                }
                if (made == null) throw new InvalidOperationException("no FactionHQ could be added");
                MissionStatsTracker stats = go.AddComponent<MissionStatsTracker>();
                made.faction = faction;
                made.missionStatsTracker = stats;
                stats.hq = made;
                // Sync settings stay Mirage's default (public NetworkBehaviour.SyncSettings); PmcSide's reflection on
                // "_syncSettings"/"syncSettings" finds no such field in this Mirage build and sets nothing.
                if (!FactionRegistry.factions.Contains(faction)) FactionRegistry.factions.Add(faction);
                FactionRegistry.factionLookup[Key] = faction;
                // The HQ's server setup reads (and would add) the mission's entry for it; nobody may join GHOST.
                Mission mission = MissionManager.CurrentMission;
                if (mission != null)
                {
                    mission.EnsureFactionExists(faction, out MissionFaction entry);
                    entry.preventJoin = true;
                }
                som.Spawn(identity);
                // Spawning registered it in HQLookup (FactionHQ.OnStartClient); the two-slot join screen must not see it.
                FactionRegistry.HQLookup.Remove(faction);
                hq = made;
                Plugin.Logger.LogInfo("[Spooktober] GHOST HQ made and spawned, net id " + identity.NetId + "; factions " +
                                      FactionRegistry.factions.Count + ", listed HQs " + FactionRegistry.HQLookup.Count + ".");
                return hq;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Spooktober] GHOST HQ could not be made: " + e);
                if (go != null) UnityEngine.Object.Destroy(go);
                try { Forget(); }
                catch (Exception again) { Plugin.Logger.LogWarning("[Spooktober] GHOST forget after a failed make: " + again.Message); }
                return null;
            }
        }

        /// <summary>Destroys the HQ (unless its scene already did) and takes GHOST out of the registries and the mission. Safe to repeat.</summary>
        internal static void Forget()
        {
            // Mirage's NetworkIdentity.OnDestroy despawns it on the server at the end of the frame.
            if (hq != null) UnityEngine.Object.Destroy(hq.gameObject);
            hq = null;
            if (faction == null) return;
            FactionRegistry.factions.Remove(faction);
            if (FactionRegistry.factionLookup.TryGetValue(Key, out Faction listed) && listed == faction) FactionRegistry.factionLookup.Remove(Key);
            FactionRegistry.HQLookup.Remove(faction);
            Mission mission = MissionManager.CurrentMission;
            if (mission != null && mission.TryGetFaction(Key, out MissionFaction entry)) mission.factions.Remove(entry);
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

        /// <summary>The session's GHOST faction; one flat pale sprite fills every logo slot (an empty slot shows a grey placeholder).</summary>
        private static Faction Build()
        {
            Faction made = ScriptableObject.CreateInstance<Faction>();
            // A scene change unloads assets no scene holds; the session needs this one, and its logo, to survive.
            made.hideFlags = HideFlags.DontUnloadUnusedAsset;
            made.name = Key;
            made.factionName = Key;
            made.factionTag = Key;
            made.factionExtendedName = Key;
            made.color = new Color(0.62f, 0.94f, 0.68f, 1f);
            made.selectedColor = Color.Lerp(made.color, Color.white, 0.3f);
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = made.color;
            texture.SetPixels(pixels);
            texture.Apply();
            made.factionColorLogo = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            made.factionHeaderSprite = made.factionColorLogo;
            made.factionGrayscaleLogo = made.factionColorLogo;
            FieldInfo convoys = AccessTools.Field(typeof(Faction), "convoyGroups");
            convoys?.SetValue(made, Activator.CreateInstance(convoys.FieldType));
            return made;
        }
    }
}

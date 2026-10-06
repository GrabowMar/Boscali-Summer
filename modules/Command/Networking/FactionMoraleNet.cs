using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Game;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Networking
{
    [NetworkMessage]
    internal struct FactionMoraleChanged
    {
        public byte Protocol;
        public int FactionHash;
        public float Morale;
    }

    /// <summary>Bounded host readout for the faction panels; gameplay never reads the mirror.</summary>
    internal sealed class FactionMoraleNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private const float BroadcastInterval = 2f;
        // Unchanged morale is only repeated this often, for clients that joined since.
        private const float HeartbeatInterval = 15f;
        private const float ChangeThreshold = 0.1f;
        private const int MaximumFactions = FactionMoraleState.MaximumFactions;

        private readonly Dictionary<int, float> remote = new Dictionary<int, float>(MaximumFactions);
        private readonly Dictionary<int, float> sent = new Dictionary<int, float>(MaximumFactions);
        private CommandManager owner;
        private HandlerSlot clientSlot;
        private float nextRegistration;
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<FactionMoraleChanged>(Receive);
        private float nextBroadcast;
        private float nextHeartbeat;

        internal void Configure(CommandManager manager)
        {
            owner = manager;
            InstallSerializers();
        }

        private static void InstallSerializers()
        {
            MirageSerializers.Strict.Install<FactionMoraleChanged>(
                (Action<NetworkWriter, FactionMoraleChanged>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WritePackedInt32(value.FactionHash);
                    writer.WriteSingle(value.Morale);
                }),
                (Func<NetworkReader, FactionMoraleChanged>)(reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new FactionMoraleChanged { Protocol = protocol };
                    return new FactionMoraleChanged
                    {
                        Protocol = protocol,
                        FactionHash = reader.ReadPackedInt32(),
                        Morale = reader.ReadSingle(),
                    };
                }));
        }

        internal void ResetScene()
        {
            remote.Clear();
            sent.Clear();
            nextBroadcast = nextHeartbeat = 0f;
        }

        internal bool TryGet(string factionName, out float morale)
        {
            morale = 0f;
            return !string.IsNullOrEmpty(factionName) &&
                remote.TryGetValue(Hash(factionName), out morale);
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextRegistration)
            {
                nextRegistration = Time.unscaledTime + 0.5f;
                if (ClientHandlers.Swap(NetworkManagerNuclearOption.i?.Client?.MessageHandler)) remote.Clear();
            }
            if (!GameAccess.IsServer() || !MissionManager.IsRunning ||
                Time.unscaledTime < nextBroadcast || owner == null) return;
            nextBroadcast = Time.unscaledTime + BroadcastInterval;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            var factions = FactionRegistry.GetAllHQs();
            if (factions == null) return;
            bool heartbeat = Time.unscaledTime >= nextHeartbeat;
            if (heartbeat) nextHeartbeat = Time.unscaledTime + HeartbeatInterval;
            int inspected = 0;
            foreach (FactionHQ hq in factions)
            {
                if (++inspected > MaximumFactions) break;
                string name = hq?.faction?.factionName;
                if (string.IsNullOrEmpty(name) ||
                    !owner.Morale.TryGet(hq.GetInstanceID(), out float morale)) continue;
                int hash = Hash(name);
                if (!heartbeat && sent.TryGetValue(hash, out float last) &&
                    Mathf.Abs(morale - last) < ChangeThreshold) continue;
                if (sent.Count >= MaximumFactions && !sent.ContainsKey(hash)) sent.Clear();
                sent[hash] = morale;
                server.SendToAll(new FactionMoraleChanged
                {
                    Protocol = ProtocolVersion,
                    FactionHash = hash,
                    Morale = morale,
                }, authenticatedOnly: true, excludeLocalPlayer: true);
            }
        }

        private void Receive(INetworkPlayer _, FactionMoraleChanged message)
        {
            if (GameAccess.IsServer() || !MissionManager.IsRunning ||
                message.Protocol != ProtocolVersion || message.FactionHash == 0 ||
                float.IsNaN(message.Morale) || float.IsInfinity(message.Morale) ||
                message.Morale < 0f || message.Morale > 100f) return;
            if (remote.Count >= MaximumFactions && !remote.ContainsKey(message.FactionHash)) return;
            remote[message.FactionHash] = message.Morale;
        }

        private void OnDestroy() => ClientHandlers.Release();

        private static int Hash(string name)
        {
            int hash = unchecked((int)Deterministic.HashString(name));
            return hash == 0 ? 1 : hash;
        }
    }
}

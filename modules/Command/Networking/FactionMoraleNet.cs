using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Networking
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
        private const int MaximumFactions = FactionMoraleState.MaximumFactions;

        private readonly Dictionary<int, float> remote = new Dictionary<int, float>(MaximumFactions);
        private CommandManager owner;
        private MessageHandler clientHandler;
        private float nextRegistration;
        private float nextBroadcast;

        internal void Configure(CommandManager manager)
        {
            owner = manager;
            InstallSerializers();
        }

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<FactionMoraleChanged>), "Write",
                (Action<NetworkWriter, FactionMoraleChanged>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WritePackedInt32(value.FactionHash);
                    writer.WriteSingle(value.Morale);
                }));
            Bind(typeof(Reader<FactionMoraleChanged>), "Read",
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
            MessagePacker.RegisterMessage<FactionMoraleChanged>();
        }

        internal void ResetScene()
        {
            remote.Clear();
            nextBroadcast = 0f;
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
                MessageHandler handler = NetworkManagerNuclearOption.i?.Client?.MessageHandler;
                if (handler != clientHandler)
                {
                    clientHandler?.UnregisterHandler<FactionMoraleChanged>();
                    remote.Clear();
                    clientHandler = handler;
                    clientHandler?.RegisterHandler<FactionMoraleChanged>(Receive, false);
                }
            }
            if (!GameAccess.IsServer() || !MissionManager.IsRunning ||
                Time.unscaledTime < nextBroadcast || owner == null) return;
            nextBroadcast = Time.unscaledTime + BroadcastInterval;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            int sent = 0;
            var factions = FactionRegistry.GetAllHQs();
            if (factions == null) return;
            foreach (FactionHQ hq in factions)
            {
                if (sent >= MaximumFactions) break;
                string name = hq?.faction?.factionName;
                if (string.IsNullOrEmpty(name) ||
                    !owner.Morale.TryGet(hq.GetInstanceID(), out float morale)) continue;
                server.SendToAll(new FactionMoraleChanged
                {
                    Protocol = ProtocolVersion,
                    FactionHash = Hash(name),
                    Morale = morale,
                }, authenticatedOnly: true, excludeLocalPlayer: true);
                sent++;
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

        private void OnDestroy() => clientHandler?.UnregisterHandler<FactionMoraleChanged>();

        private static int Hash(string name)
        {
            int hash = unchecked((int)Deterministic.HashString(name));
            return hash == 0 ? 1 : hash;
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(property,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null) throw new MissingMemberException(holder.Name, property);
            target.SetValue(null, value, null);
        }
    }
}

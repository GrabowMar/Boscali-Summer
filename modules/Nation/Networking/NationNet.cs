using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Nation.Domain;
using BoscaliSummer.Modules.Nation.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Nation.Networking
{
    [NetworkMessage]
    internal struct NationIntent
    {
        public byte Protocol;
        public byte Verb;
        public int A, B;
    }

    [NetworkMessage]
    internal struct NationReply
    {
        public byte Protocol;
        public byte Outcome;
    }

    [NetworkMessage]
    internal struct NationSnapshot
    {
        public byte Protocol;
        public int FactionHash;
        public byte[] Payload;
        public int Count;
    }

    /// <summary>
    /// The nation layer's wire: a member's intent up, the host's answer back to that member, and the faction snapshot down to
    /// every member when it changed (at most once a second) plus a heartbeat for late joiners. Message names and field order are
    /// protocol identity; a layout change bumps <see cref="ProtocolVersion"/>.
    /// </summary>
    internal sealed class NationNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private const float SendInterval = 1f, HeartbeatInterval = 15f, IntentInterval = 0.4f;

        private NationService owner;
        private HandlerSlot serverSlot, clientSlot;
        private readonly SenderThrottle intents = new SenderThrottle();
        private float nextRegistration, nextSend, nextHeartbeat;

        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<NationIntent>(ReceiveIntent);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<NationSnapshot, NationReply>(ReceiveSnapshot, ReceiveReply);

        internal void Configure(NationService service)
        {
            owner = service;
            MirageSerializers.Strict.Install<NationIntent>(
                (Action<NetworkWriter, NationIntent>)((w, m) => { w.WriteByte(m.Protocol); w.WriteByte(m.Verb); w.WritePackedInt32(m.A); w.WritePackedInt32(m.B); }),
                (Func<NetworkReader, NationIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new NationIntent { Protocol = protocol }
                        : new NationIntent { Protocol = protocol, Verb = r.ReadByte(), A = r.ReadPackedInt32(), B = r.ReadPackedInt32() };
                }));
            MirageSerializers.Strict.Install<NationReply>(
                (Action<NetworkWriter, NationReply>)((w, m) => { w.WriteByte(m.Protocol); w.WriteByte(m.Outcome); }),
                (Func<NetworkReader, NationReply>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new NationReply { Protocol = protocol } : new NationReply { Protocol = protocol, Outcome = r.ReadByte() };
                }));
            MirageSerializers.Strict.Install<NationSnapshot>(
                (Action<NetworkWriter, NationSnapshot>)((w, m) =>
                {
                    w.WriteByte(m.Protocol);
                    w.WritePackedInt32(m.FactionHash);
                    w.WriteBytesAndSize(m.Payload, 0, m.Count);
                }),
                (Func<NetworkReader, NationSnapshot>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new NationSnapshot { Protocol = protocol };
                    int hash = r.ReadPackedInt32();
                    byte[] payload = r.ReadBytesAndSize(NationWire.MaxBytes);
                    return new NationSnapshot { Protocol = protocol, FactionHash = hash, Payload = payload, Count = payload?.Length ?? 0 };
                }));
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextRegistration)
            {
                nextRegistration = now + 0.5f;
                NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
                if (ServerHandlers.Swap(network?.Server?.Active == true ? network.Server.MessageHandler : null)) intents.Clear();
                ClientHandlers.Swap(network?.Client?.Active == true ? network.Client.MessageHandler : null);
            }
            if (owner == null || !GameAccess.IsServer() || !MissionManager.IsRunning || now < nextSend) return;
            nextSend = now + SendInterval;
            bool heartbeat = now >= nextHeartbeat;
            if (heartbeat) nextHeartbeat = now + HeartbeatInterval;
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            var hqs = FactionRegistry.GetAllHQs();
            if (server == null || !server.Active || hqs == null) return;
            int inspected = 0;
            foreach (FactionHQ hq in hqs)
            {
                if (++inspected > NationRules.MaxFactions) break;
                string name = hq?.faction?.factionName;
                if (string.IsNullOrEmpty(name) || !owner.TryPack(hq, heartbeat, out byte[] bytes)) continue;
                var snapshot = new NationSnapshot { Protocol = ProtocolVersion, FactionHash = Hash(name), Payload = bytes, Count = bytes.Length };
                IReadOnlyList<INetworkPlayer> players = server.AuthenticatedPlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    INetworkPlayer connection = players[i];
                    // The host's own player reads the book directly; only remote members of this faction get the snapshot.
                    if (connection == null || connection == server.LocalPlayer || !connection.TryGetPlayer<Player>(out Player player) ||
                        player == null || !ReferenceEquals(player.HQ, hq)) continue;
                    connection.Send(snapshot);
                }
            }
        }

        /// <summary>Client: ask the host. False when there is no connection to ask.</summary>
        internal bool SendIntent(NationVerb verb, int a, int b)
        {
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            if (client == null || !client.Active) return false;
            client.Send(new NationIntent { Protocol = ProtocolVersion, Verb = (byte)verb, A = a, B = b });
            return true;
        }

        private void ReceiveIntent(INetworkPlayer sender, NationIntent intent)
        {
            if (owner == null || !GameAccess.IsServer() || intent.Protocol != ProtocolVersion || sender == null ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null || player.HQ == null) return;
            // The faction and the identity come from the transport, never from the message.
            NationOutcome outcome = !intents.Allow(PlayerIdentity.Of(player), Time.unscaledTime, IntentInterval) ? NationOutcome.Busy
                : intent.Verb > (byte)NationVerb.Build ? NationOutcome.Unknown
                : owner.Apply(player.HQ, player, (NationVerb)intent.Verb, intent.A, intent.B);
            sender.Send(new NationReply { Protocol = ProtocolVersion, Outcome = (byte)outcome });
        }

        private void ReceiveReply(INetworkPlayer _, NationReply reply)
        {
            if (owner == null || GameAccess.IsServer() || reply.Protocol != ProtocolVersion || reply.Outcome > (byte)NationOutcome.NotMember) return;
            owner.Answer((NationOutcome)reply.Outcome);
        }

        private void ReceiveSnapshot(INetworkPlayer _, NationSnapshot snapshot)
        {
            if (owner == null || GameAccess.IsServer() || snapshot.Protocol != ProtocolVersion || snapshot.Payload == null) return;
            // A snapshot of another faction (a member who just switched sides) is not this player's to read.
            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || Hash(hq.faction.factionName) != snapshot.FactionHash) return;
            owner.Receive(snapshot.Payload, snapshot.Count);
        }

        private static int Hash(string name)
        {
            int hash = unchecked((int)Deterministic.HashString(name));
            return hash == 0 ? 1 : hash;
        }
    }
}

using System;
using System.Reflection;
using BoscaliSummer.Features.Progression.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Networking
{
    [NetworkMessage]
    internal struct SkillUnlockRequest
    {
        public byte Protocol;
        public int RequestId;
        public byte Skill;
    }

    [NetworkMessage]
    internal struct ProgressionStateMessage
    {
        public byte Protocol;
        public int RequestId;
        public ulong PlayerId;
        public ushort SkillMask;
        public byte Rank;
        public byte Result;
    }

    internal sealed class ProgressionNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private ProgressionManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private NetworkServer subscribedServer;
        private float nextRegistration;

        public void Configure(ProgressionManager progression)
        {
            manager = progression;
            InstallSerializers();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            RegisterEndpoints();
        }

        private void OnDestroy()
        {
            if (serverHandler != null) serverHandler.UnregisterHandler<SkillUnlockRequest>();
            if (clientHandler != null) clientHandler.UnregisterHandler<ProgressionStateMessage>();
            if (subscribedServer != null) subscribedServer.Authenticated.RemoveListener(OnAuthenticated);
        }

        public void SendUnlock(int requestId, SkillId skill)
        {
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return;
            client.Send(new SkillUnlockRequest { Protocol = ProtocolVersion, RequestId = requestId, Skill = (byte)skill });
        }

        public void RequestState()
        {
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return;
            client.Send(new SkillUnlockRequest { Protocol = ProtocolVersion, RequestId = 0, Skill = byte.MaxValue });
        }

        public void SendState(INetworkPlayer destination, Player player, int requestId, byte result)
        {
            if (destination == null || player == null) return;
            destination.Send(manager.CreateStateMessage(player, requestId, result));
        }

        private void RegisterEndpoints()
        {
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            if (network == null) return;
            if (network.Server != null && network.Server.Active && network.Server.MessageHandler != serverHandler)
            {
                serverHandler?.UnregisterHandler<SkillUnlockRequest>();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<SkillUnlockRequest>(ReceiveUnlock, false);
            }
            if (network.Server != null && network.Server.Active && subscribedServer != network.Server)
            {
                if (subscribedServer != null) subscribedServer.Authenticated.RemoveListener(OnAuthenticated);
                subscribedServer = network.Server;
                subscribedServer.Authenticated.AddListener(OnAuthenticated);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<ProgressionStateMessage>();
                clientHandler = network.Client.MessageHandler;
                clientHandler.RegisterHandler<ProgressionStateMessage>(ReceiveState, false);
            }
        }

        private void ReceiveUnlock(INetworkPlayer sender, SkillUnlockRequest request)
        {
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            if (request.Protocol != ProtocolVersion) return;
            byte result = request.Skill == byte.MaxValue
                ? (byte)0
                : manager.TryUnlock(player, (SkillId)request.Skill) ? (byte)1 : (byte)2;
            SendState(sender, player, request.RequestId, result);
        }

        private void ReceiveState(INetworkPlayer _, ProgressionStateMessage message)
        {
            if (message.Protocol == ProtocolVersion) manager.ReceiveState(message);
        }

        private void OnAuthenticated(INetworkPlayer player)
        {
            if (player != null && player.TryGetPlayer<Player>(out Player gamePlayer))
                SendState(player, gamePlayer, 0, 0);
        }

        private static bool serializersInstalled;

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            SetWriter<SkillUnlockRequest>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId);
                writer.WriteByte(value.Skill);
            });
            SetReader<SkillUnlockRequest>(reader => new SkillUnlockRequest
            {
                Protocol = reader.ReadByte(), RequestId = reader.ReadPackedInt32(), Skill = reader.ReadByte()
            });
            SetWriter<ProgressionStateMessage>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId);
                writer.WritePackedUInt64(value.PlayerId);
                writer.WritePackedUInt32(value.SkillMask);
                writer.WriteByte(value.Rank);
                writer.WriteByte(value.Result);
            });
            SetReader<ProgressionStateMessage>(reader => new ProgressionStateMessage
            {
                Protocol = reader.ReadByte(), RequestId = reader.ReadPackedInt32(),
                PlayerId = reader.ReadPackedUInt64(),
                SkillMask = (ushort)reader.ReadPackedUInt32(),
                Rank = reader.ReadByte(),
                Result = reader.ReadByte()
            });
            MessagePacker.RegisterMessage<SkillUnlockRequest>();
            MessagePacker.RegisterMessage<ProgressionStateMessage>();
        }

        private static void SetWriter<T>(Action<NetworkWriter, T> writer) =>
            typeof(Writer<T>).GetProperty("Write", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(null, writer, null);

        private static void SetReader<T>(Func<NetworkReader, T> reader) =>
            typeof(Reader<T>).GetProperty("Read", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(null, reader, null);
    }
}

using System;
using System.Reflection;
using BoscaliSummer.Features.Support.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Networking
{
    [NetworkMessage]
    internal struct SupportRequestMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Action;
        public float X;
        public float Y;
        public float Z;
    }

    [NetworkMessage]
    internal struct SupportResultMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Action;
        public byte Result;
        public float CooldownSeconds;
    }

    internal sealed class SupportNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private SupportManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;

        public void Configure(SupportManager support)
        {
            manager = support;
            InstallSerializers();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            if (network == null) return;
            if (network.Server?.MessageHandler != null && network.Server.Active && network.Server.MessageHandler != serverHandler)
            {
                serverHandler?.UnregisterHandler<SupportRequestMessage>();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<SupportRequestMessage>(ReceiveRequest, false);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<SupportResultMessage>();
                clientHandler = network.Client.MessageHandler;
                clientHandler.RegisterHandler<SupportResultMessage>(ReceiveResult, false);
            }
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<SupportRequestMessage>();
            clientHandler?.UnregisterHandler<SupportResultMessage>();
        }

        public void Request(int requestId, SupportActionId action, GlobalPosition target)
        {
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return;
            client.Send(new SupportRequestMessage
            {
                Protocol = ProtocolVersion, RequestId = requestId, Action = (byte)action,
                X = target.x, Y = target.y, Z = target.z
            });
        }

        public void Reply(INetworkPlayer player, SupportRequestMessage request, SupportResult result, float cooldown)
        {
            player?.Send(new SupportResultMessage
            {
                Protocol = ProtocolVersion,
                RequestId = request.RequestId,
                Action = request.Action,
                Result = (byte)result,
                CooldownSeconds = cooldown
            });
        }

        private void ReceiveRequest(INetworkPlayer sender, SupportRequestMessage request)
        {
            if (request.Protocol == ProtocolVersion) manager.ReceiveRequest(sender, request);
        }

        private void ReceiveResult(INetworkPlayer _, SupportResultMessage result)
        {
            if (result.Protocol == ProtocolVersion) manager.ReceiveResult(result);
        }

        private static bool serializersInstalled;

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            SetWriter<SupportRequestMessage>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId); writer.WriteByte(value.Action);
                writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z);
            });
            SetReader<SupportRequestMessage>(reader => new SupportRequestMessage
            {
                Protocol = reader.ReadByte(), RequestId = reader.ReadPackedInt32(), Action = reader.ReadByte(),
                X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle()
            });
            SetWriter<SupportResultMessage>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId); writer.WriteByte(value.Action);
                writer.WriteByte(value.Result); writer.WriteSingle(value.CooldownSeconds);
            });
            SetReader<SupportResultMessage>(reader => new SupportResultMessage
            {
                Protocol = reader.ReadByte(), RequestId = reader.ReadPackedInt32(), Action = reader.ReadByte(),
                Result = reader.ReadByte(), CooldownSeconds = reader.ReadSingle()
            });
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
        }

        private static void SetWriter<T>(Action<NetworkWriter, T> writer) =>
            typeof(Writer<T>).GetProperty("Write", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(null, writer, null);

        private static void SetReader<T>(Func<NetworkReader, T> reader) =>
            typeof(Reader<T>).GetProperty("Read", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(null, reader, null);
    }
}

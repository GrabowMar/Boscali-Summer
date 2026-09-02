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
        /// <summary>Bumped from 1: the action set changed to capability-addressed ids.</summary>
        internal const byte ProtocolVersion = 2;

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
            if (network.Server != null && network.Server.Active &&
                network.Server.MessageHandler != null && network.Server.MessageHandler != serverHandler)
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

        /// <summary>
        /// Submits one request. When this process is the server the request is validated and
        /// executed in-process, so single-player and listen-host never depend on the
        /// custom-message pipe and a dropped send can no longer look like a pending one.
        /// </summary>
        public void Request(int requestId, SupportActionId action, GlobalPosition target)
        {
            var message = new SupportRequestMessage
            {
                Protocol = ProtocolVersion,
                RequestId = requestId,
                Action = (byte)action,
                X = target.x,
                Y = target.y,
                Z = target.z
            };

            if (IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                SupportResult result = manager.Evaluate(local, message);
                manager.ReceiveResult(Reply(message, result, manager.ServerCooldown));
                return;
            }

            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }
            client.Send(message);
        }

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }

        private static SupportResultMessage Reply(
            SupportRequestMessage request, SupportResult result, float cooldown) =>
            new SupportResultMessage
            {
                Protocol = ProtocolVersion,
                RequestId = request.RequestId,
                Action = request.Action,
                Result = (byte)result,
                CooldownSeconds = result == SupportResult.Accepted ? cooldown : 0f
            };

        private void ReceiveRequest(INetworkPlayer sender, SupportRequestMessage request)
        {
            if (request.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            SupportResult result = manager.Evaluate(player, request);
            sender.Send(Reply(request, result, manager.ServerCooldown));
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
                writer.WritePackedInt32(value.RequestId);
                writer.WriteByte(value.Action);
                writer.WriteSingle(value.X);
                writer.WriteSingle(value.Y);
                writer.WriteSingle(value.Z);
            });
            SetReader<SupportRequestMessage>(reader => new SupportRequestMessage
            {
                Protocol = reader.ReadByte(),
                RequestId = reader.ReadPackedInt32(),
                Action = reader.ReadByte(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle()
            });
            SetWriter<SupportResultMessage>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId);
                writer.WriteByte(value.Action);
                writer.WriteByte(value.Result);
                writer.WriteSingle(value.CooldownSeconds);
            });
            SetReader<SupportResultMessage>(reader => new SupportResultMessage
            {
                Protocol = reader.ReadByte(),
                RequestId = reader.ReadPackedInt32(),
                Action = reader.ReadByte(),
                Result = reader.ReadByte(),
                CooldownSeconds = reader.ReadSingle()
            });
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
        }

        private static void SetWriter<T>(Action<NetworkWriter, T> writer) =>
            Bind(typeof(Writer<T>), "Write", writer);

        private static void SetReader<T>(Func<NetworkReader, T> reader) =>
            Bind(typeof(Reader<T>), "Read", reader);

        // A missing seam used to be swallowed by a null-conditional, leaving every support
        // message silently unable to round-trip. Report it instead.
        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[Support] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; support requests cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}

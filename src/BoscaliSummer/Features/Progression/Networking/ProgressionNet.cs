using System;
using System.Reflection;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Networking
{
    /// <summary>Client intent: a perk id, or <see cref="ProgressionNet.QueryOnly"/> for a snapshot.</summary>
    [NetworkMessage]
    internal struct ProgressionSubmit
    {
        public byte Protocol;
        public byte Perk;
    }

    /// <summary>
    /// The server's authoritative view of one player. Idempotent state, so a replayed or
    /// out-of-order snapshot is harmless and no request-id correlation is needed.
    /// </summary>
    [NetworkMessage]
    internal struct ProgressionSnapshot
    {
        public const byte Snapshot = 0;
        public const byte Unlocked = 1;
        public const byte Denied = 2;

        public byte Protocol;
        public uint PerkMask;
        public ushort Score;
        public byte EarnedPoints;
        public byte Rank;
        public byte Result;
    }

    internal sealed class ProgressionNet : MonoBehaviour
    {
        /// <summary>Bumped from 1: the mask widened and the rank budget became a score budget.</summary>
        internal const byte ProtocolVersion = 2;

        /// <summary>Perk id meaning "send me a snapshot, change nothing".</summary>
        internal const byte QueryOnly = byte.MaxValue;

        private ProgressionManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
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
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            if (network == null) return;
            if (network.Server != null && network.Server.Active &&
                network.Server.MessageHandler != null && network.Server.MessageHandler != serverHandler)
            {
                serverHandler?.UnregisterHandler<ProgressionSubmit>();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<ProgressionSubmit>(ReceiveSubmit, false);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<ProgressionSnapshot>();
                clientHandler = network.Client.MessageHandler;
                clientHandler.RegisterHandler<ProgressionSnapshot>(ReceiveSnapshot, false);
            }
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<ProgressionSubmit>();
            clientHandler?.UnregisterHandler<ProgressionSnapshot>();
        }

        /// <summary>
        /// Sends one intent. When this process is the server the request is resolved in-process,
        /// so single-player and listen-host never depend on the custom-message pipe.
        /// </summary>
        public void Submit(byte perkId)
        {
            if (IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                manager.Apply(manager.Handle(local, perkId), PlayerIdentity.Of(local));
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }
            client.Send(new ProgressionSubmit { Protocol = ProtocolVersion, Perk = perkId });
        }

        private static bool IsServer()
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            return server != null && server.Active;
        }

        private void ReceiveSubmit(INetworkPlayer sender, ProgressionSubmit submit)
        {
            if (submit.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            if (submit.Perk != QueryOnly && !PerkCatalog.IsDefined(submit.Perk)) return;
            sender.Send(manager.Handle(player, submit.Perk));
        }

        private void ReceiveSnapshot(INetworkPlayer _, ProgressionSnapshot snapshot)
        {
            if (snapshot.Protocol != ProtocolVersion) return;
            ulong localId = GameManager.GetLocalPlayer<Player>(out Player local) && local != null
                ? PlayerIdentity.Of(local)
                : PlayerIdentity.None;
            manager.Apply(snapshot, localId);
        }

        private static bool serializersInstalled;

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            SetWriter<ProgressionSubmit>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WriteByte(value.Perk);
            });
            SetReader<ProgressionSubmit>(reader => new ProgressionSubmit
            {
                Protocol = reader.ReadByte(),
                Perk = reader.ReadByte()
            });
            SetWriter<ProgressionSnapshot>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedUInt32(value.PerkMask);
                writer.WritePackedUInt32(value.Score);
                writer.WriteByte(value.EarnedPoints);
                writer.WriteByte(value.Rank);
                writer.WriteByte(value.Result);
            });
            SetReader<ProgressionSnapshot>(reader => new ProgressionSnapshot
            {
                Protocol = reader.ReadByte(),
                PerkMask = reader.ReadPackedUInt32(),
                Score = (ushort)reader.ReadPackedUInt32(),
                EarnedPoints = reader.ReadByte(),
                Rank = reader.ReadByte(),
                Result = reader.ReadByte()
            });
            MessagePacker.RegisterMessage<ProgressionSubmit>();
            MessagePacker.RegisterMessage<ProgressionSnapshot>();
        }

        // A failed install used to be swallowed by a null-conditional, leaving every message
        // silently unable to round-trip. Report it instead.
        private static void SetWriter<T>(Action<NetworkWriter, T> writer) =>
            Bind(typeof(Writer<T>), "Write", writer);

        private static void SetReader<T>(Func<NetworkReader, T> reader) =>
            Bind(typeof(Reader<T>), "Read", reader);

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[Progression] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; perk state cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}

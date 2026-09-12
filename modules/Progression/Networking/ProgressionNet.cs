using System;
using System.Reflection;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
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
        public uint Scene, Token;
        public int Generation;
    }

    /// <summary>
    /// The server's authoritative view of one player, correlated to the requesting scene and career.
    /// </summary>
    [NetworkMessage]
    internal struct ProgressionSnapshot
    {
        public const byte Snapshot = 0;
        public const byte Unlocked = 1;
        public const byte Denied = 2;

        public byte Protocol;
        public uint PerkMask;
        public int Score;
        public byte EarnedPoints;
        public byte Rank;
        public byte Result;
        public int Generation;
        public uint Scene, Token;
        public int ScorePerPoint;
        public byte MaximumPoints;
    }

    internal sealed class ProgressionNet : MonoBehaviour
    {
        /// <summary>Version 3 includes pilot generation so a retired career cannot be restored by an old reply.</summary>
        internal const byte ProtocolVersion = 3;

        /// <summary>Perk id meaning "send me a snapshot, change nothing".</summary>
        internal const byte QueryOnly = byte.MaxValue;

        private ProgressionManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;
        private uint scene, token;
        private ulong requestedPlayer;
        private FactionHQ requestedHq;

        internal void ResetScene() { scene++; requestedPlayer = PlayerIdentity.None; requestedHq = null; }

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
            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null)
            { manager.ReportOffline(); return; }
            if (GameAccess.IsServer())
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
            requestedPlayer = PlayerIdentity.Of(local); requestedHq = local.HQ;
            client.Send(new ProgressionSubmit { Protocol = ProtocolVersion, Perk = perkId,
                Scene = scene, Token = ++token, Generation = manager.Generation(local) });
        }

        private void ReceiveSubmit(INetworkPlayer sender, ProgressionSubmit submit)
        {
            if (!GameAccess.IsServer() || submit.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            if (submit.Perk != QueryOnly && !PerkCatalog.IsDefined(submit.Perk)) return;
            bool oldCareer = submit.Perk != QueryOnly && submit.Generation != manager.Generation(player);
            ProgressionSnapshot snapshot = manager.Handle(player, oldCareer ? QueryOnly : submit.Perk);
            if (oldCareer) snapshot.Result = ProgressionSnapshot.Denied;
            snapshot.Scene = submit.Scene; snapshot.Token = submit.Token;
            sender.Send(snapshot);
        }

        private void ReceiveSnapshot(INetworkPlayer _, ProgressionSnapshot snapshot)
        {
            if (GameAccess.IsServer() || snapshot.Protocol != ProtocolVersion || snapshot.Scene != scene || snapshot.Token != token) return;
            ulong localId = GameManager.GetLocalPlayer<Player>(out Player local) && local != null
                ? PlayerIdentity.Of(local)
                : PlayerIdentity.None;
            if (localId == PlayerIdentity.None || localId != requestedPlayer || local.HQ != requestedHq) return;
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
                writer.WritePackedUInt32(value.Scene); writer.WritePackedUInt32(value.Token); writer.WritePackedInt32(value.Generation);
            });
            SetReader<ProgressionSubmit>(reader => new ProgressionSubmit
            {
                Protocol = reader.ReadByte(),
                Perk = reader.ReadByte(), Scene = reader.ReadPackedUInt32(), Token = reader.ReadPackedUInt32(), Generation = reader.ReadPackedInt32()
            });
            SetWriter<ProgressionSnapshot>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedUInt32(value.PerkMask);
                writer.WritePackedInt32(value.Score);
                writer.WriteByte(value.EarnedPoints);
                writer.WriteByte(value.Rank);
                writer.WriteByte(value.Result);
                writer.WritePackedInt32(value.Generation);
                writer.WritePackedUInt32(value.Scene); writer.WritePackedUInt32(value.Token);
                writer.WritePackedInt32(value.ScorePerPoint); writer.WriteByte(value.MaximumPoints);
            });
            SetReader<ProgressionSnapshot>(reader => new ProgressionSnapshot
            {
                Protocol = reader.ReadByte(),
                PerkMask = reader.ReadPackedUInt32(),
                Score = reader.ReadPackedInt32(),
                EarnedPoints = reader.ReadByte(),
                Rank = reader.ReadByte(),
                Result = reader.ReadByte(),
                Generation = reader.ReadPackedInt32(), Scene = reader.ReadPackedUInt32(), Token = reader.ReadPackedUInt32(),
                ScorePerPoint = reader.ReadPackedInt32(), MaximumPoints = reader.ReadByte()
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

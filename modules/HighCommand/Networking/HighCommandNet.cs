using System;
using BoscaliSummer.Modules.HighCommand.Domain;
using BoscaliSummer.Modules.HighCommand.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.HighCommand.Networking
{
    [NetworkMessage]
    internal struct HighCommandQuery
    {
        public byte Protocol;
        public uint Scene;
        public uint Token;
    }

    [NetworkMessage]
    internal struct HighCommandSnapshot
    {
        public byte Protocol;
        public uint Scene;
        public uint Token;
        public string Status;
        public string Signal;
        public float Cohesion;
        public int Active;
        public int Kia;
        public CommanderWire[] Nodes;
        public CommanderLogWire[] Log;
        public CommanderLogWire[] HostileLog;
    }

    /// <summary>Flat staff log row. Text is clamped before it is written, not after it is read.</summary>
    internal struct CommanderLogWire
    {
        public int TargetId;
        public byte Tone;
        public string Text;
        public float Age;
    }

    /// <summary>Flat staff-post row. Text is clamped before it is written, not after it is read.</summary>
    internal struct CommanderWire
    {
        public const byte Friendly = 1;
        public const byte Known = 2;
        public const byte Kia = 4;
        public const byte Transit = 8;
        public const byte Disrupted = 16;
        public const byte Alert = 32;

        public int Id;
        public int ParentId;
        public byte Tier;
        public byte Flags;
        public byte TraitMask;
        public int Seed;
        public float IntelAge;
        public float Weight;
        public float X;
        public float Z;
        public string Name;
        public string Rank;
        public string Role;
        public string Location;
    }

    internal sealed class HighCommandNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 4;
        internal const int MaximumNodes = 32;

        private HighCommandManager manager;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<HighCommandQuery>(ReceiveQuery);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<HighCommandSnapshot>(ReceiveSnapshot);
        private readonly SenderThrottle nextReply = new SenderThrottle();
        private float nextRegistration, lastQuery;
        private uint scene, token;
        private FactionHQ requestedHq;
        private bool pending;

        public void Configure(HighCommandManager owner)
        {
            manager = owner;
            InstallSerializers();
        }

        public void ResetScene()
        {
            ResetClient();
            nextReply.Clear();
        }

        // Client correlation only. A listen host changing faction must keep the server's reply limits.
        private void ResetClient()
        {
            scene++; pending = false; requestedHq = null; lastQuery = -10f;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (pending && now - lastQuery >= 6f)
            {
                pending = false;
                manager.SetLocalStatus("Host link unavailable; staff board cleared.");
            }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;
            ServerHandlers.Swap(server);
            ClientHandlers.Swap(client);
            nextReply.Prune(now);
        }

        /// <summary>
        /// Ask for the board. The module is read-only by design: the staff lives, moves and
        /// dies on the host, and a client can only look at it.
        /// </summary>
        public void Request()
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null)
            {
                manager.SetLocalStatus("Join a faction to see its chain of command.");
                return;
            }
            if (requestedHq != player.HQ)
            {
                ResetClient(); requestedHq = player.HQ;
                manager.SetLocalStatus("Waiting for the staff board.");
            }
            if (pending || Time.unscaledTime - lastQuery < 4f) return;
            lastQuery = Time.unscaledTime;
            if (GameAccess.IsServer())
            {
                manager.Apply(manager.Snapshot(player));
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.SetLocalStatus("Connect to a host with the chain-of-command layer enabled.");
                return;
            }
            pending = true;
            client.Send(new HighCommandQuery
            {
                Protocol = ProtocolVersion, Scene = scene, Token = ++token,
            });
        }

        private void ReceiveQuery(INetworkPlayer sender, HighCommandQuery query)
        {
            if (!GameAccess.IsServer() || query.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            ulong id = PlayerIdentity.Of(player);
            float now = Time.unscaledTime;
            if (!nextReply.Allow(id, now, 1.5f)) return;
            HighCommandSnapshot snapshot = manager.Snapshot(player);
            snapshot.Scene = query.Scene;
            snapshot.Token = query.Token;
            sender.Send(snapshot);
        }

        private void ReceiveSnapshot(INetworkPlayer sender, HighCommandSnapshot snapshot)
        {
            if (GameAccess.IsServer() || !pending || snapshot.Protocol != ProtocolVersion ||
                snapshot.Scene != scene || snapshot.Token != token ||
                !GameManager.GetLocalPlayer<Player>(out Player local) || local == null || local.HQ != requestedHq)
                return;
            pending = false;
            manager.Apply(snapshot);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            ResetScene();
        }

        /// <summary>
        /// Readers never throw: a throw inside a Mirage handler can drop the connection. A foreign
        /// protocol keeps only its header; a board that fails validation reads as protocol 0,
        /// which the handler ignores.
        /// </summary>
        private static void InstallSerializers()
        {
            MirageSerializers.Strict.Install<HighCommandQuery>((Action<NetworkWriter, HighCommandQuery>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
            }),
            (Func<NetworkReader, HighCommandQuery>)(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new HighCommandQuery { Protocol = protocol };
                return new HighCommandQuery
                {
                    Protocol = protocol, Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(),
                };
            }));

            MirageSerializers.Strict.Install<HighCommandSnapshot>((Action<NetworkWriter, HighCommandSnapshot>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
                w.WriteString(NetText.Clip(v.Status, 96)); w.WriteString(NetText.Clip(v.Signal, 96));
                w.WriteSingle(v.Cohesion);
                w.WritePackedInt32(v.Active); w.WritePackedInt32(v.Kia);
                int count = Math.Min(MaximumNodes, v.Nodes?.Length ?? 0);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++) WriteNode(w, v.Nodes[i]);
                WriteLog(w, v.Log);
                WriteLog(w, v.HostileLog);
            }),
            (Func<NetworkReader, HighCommandSnapshot>)(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new HighCommandSnapshot { Protocol = protocol };
                var snapshot = new HighCommandSnapshot
                {
                    Protocol = protocol, Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(),
                    Status = NetText.Clip(r.ReadString(), 96), Signal = NetText.Clip(r.ReadString(), 96),
                    Cohesion = r.ReadSingle(),
                    Active = r.ReadPackedInt32(), Kia = r.ReadPackedInt32(),
                };
                if (!CommandSnapshotRules.ValidHeader(snapshot.Cohesion, snapshot.Active, snapshot.Kia))
                    return default;
                int count = r.ReadByte();
                if (count > CommandSnapshotRules.MaximumNodes) return default;
                snapshot.Nodes = new CommanderWire[count];
                for (int i = 0; i < count; i++)
                    if (!ReadNode(r, out snapshot.Nodes[i])) return default;
                if (!ReadLog(r, out snapshot.Log) || !ReadLog(r, out snapshot.HostileLog)) return default;
                return snapshot;
            }));

        }

        private static void WriteNode(NetworkWriter w, CommanderWire node)
        {
            w.WritePackedInt32(node.Id); w.WritePackedInt32(node.ParentId);
            w.WriteByte(node.Tier); w.WriteByte(node.Flags);
            w.WriteByte(node.TraitMask);
            w.WritePackedInt32(node.Seed); w.WriteSingle(node.IntelAge); w.WriteSingle(node.Weight);
            w.WriteSingle(node.X); w.WriteSingle(node.Z);
            w.WriteString(NetText.Clip(node.Name, 24)); w.WriteString(NetText.Clip(node.Rank, 8));
            w.WriteString(NetText.Clip(node.Role, 24)); w.WriteString(NetText.Clip(node.Location, 32));
        }

        private static bool ReadNode(NetworkReader r, out CommanderWire node)
        {
            node = new CommanderWire
            {
                Id = r.ReadPackedInt32(), ParentId = r.ReadPackedInt32(),
                Tier = r.ReadByte(), Flags = r.ReadByte(), TraitMask = r.ReadByte(),
                Seed = r.ReadPackedInt32(), IntelAge = r.ReadSingle(), Weight = r.ReadSingle(),
                X = r.ReadSingle(), Z = r.ReadSingle(),
                Name = NetText.Clip(r.ReadString(), 24), Rank = NetText.Clip(r.ReadString(), 8),
                Role = NetText.Clip(r.ReadString(), 24), Location = NetText.Clip(r.ReadString(), 32),
            };
            return CommandSnapshotRules.ValidNode(node.Id, node.ParentId, node.Tier, node.Flags,
                    node.IntelAge, node.Weight, node.X, node.Z) &&
                (node.TraitMask & ~CommandTraits.All) == 0;
        }

        private static void WriteLog(NetworkWriter w, CommanderLogWire[] rows)
        {
            int count = Math.Min(CommandSnapshotRules.MaximumLogRows, rows?.Length ?? 0);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                CommanderLogWire row = rows[i];
                w.WritePackedInt32(row.TargetId);
                w.WriteByte(row.Tone);
                w.WriteString(NetText.Clip(row.Text, CommandSnapshotRules.MaximumLogText));
                w.WriteSingle(row.Age);
            }
        }

        private static bool ReadLog(NetworkReader r, out CommanderLogWire[] rows)
        {
            int count = r.ReadByte();
            rows = null;
            if (count > CommandSnapshotRules.MaximumLogRows) return false;
            rows = new CommanderLogWire[count];
            for (int i = 0; i < count; i++)
            {
                var row = new CommanderLogWire
                {
                    TargetId = r.ReadPackedInt32(),
                    Tone = r.ReadByte(),
                    Text = NetText.Clip(r.ReadString(), CommandSnapshotRules.MaximumLogText),
                    Age = r.ReadSingle(),
                };
                if (!CommandSnapshotRules.ValidLogRow(row.TargetId, row.Tone, row.Text, row.Age)) return false;
                rows[i] = row;
            }
            return true;
        }
    }
}

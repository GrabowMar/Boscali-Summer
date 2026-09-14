using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.HighCommand.Domain;
using BoscaliSummer.Features.HighCommand.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.HighCommand.Networking
{
    [NetworkMessage]
    internal struct HighCommandQuery
    {
        public byte Protocol;
        public uint Scene;
        public uint Token;
        public byte Action;
        public int TargetId;
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
        public int CommandPoints;
        public int Active;
        public int Kia;
        public CommanderWire[] Nodes;
    }

    /// <summary>Flat staff-post row. Text is clamped before it is written, not after it is read.</summary>
    internal struct CommanderWire
    {
        public const byte Friendly = 1;
        public const byte Known = 2;
        public const byte Kia = 4;
        public const byte Transit = 8;
        public const byte Disrupted = 16;
        public const byte Marked = 32;
        public const byte ActionCommend = 1;
        public const byte ActionRelocate = 2;
        public const byte ActionBounty = 4;

        public int Id;
        public int ParentId;
        public byte Tier;
        public byte Flags;
        public byte Actions;
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
        public string Decoration;
    }

    internal sealed class HighCommandNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        internal const int MaximumNodes = 32;
        internal const byte ActionRefresh = 0;
        internal const byte ActionCommend = 1;
        internal const byte ActionRelocate = 2;
        internal const byte ActionBounty = 3;

        private HighCommandManager manager;
        private MessageHandler serverHandler, clientHandler;
        private readonly Dictionary<ulong, float> nextReply = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> nextAction = new Dictionary<ulong, float>();
        private readonly List<ulong> expired = new List<ulong>(64);
        private float nextRegistration, lastQuery, nextPrune;
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
            scene++; pending = false; requestedHq = null;
            nextReply.Clear(); nextAction.Clear(); expired.Clear(); lastQuery = -10f;
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
            if (server != serverHandler)
            {
                serverHandler?.UnregisterHandler<HighCommandQuery>();
                serverHandler = server;
                serverHandler?.RegisterHandler<HighCommandQuery>(ReceiveQuery, false);
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<HighCommandSnapshot>();
                clientHandler = client;
                clientHandler?.RegisterHandler<HighCommandSnapshot>(ReceiveSnapshot, false);
            }
            if (now >= nextPrune)
            {
                nextPrune = now + 10f;
                expired.Clear();
                foreach (var pair in nextReply)
                    if (now - pair.Value > 30f) expired.Add(pair.Key);
                for (int i = 0; i < expired.Count; i++) { nextReply.Remove(expired[i]); nextAction.Remove(expired[i]); }
            }
        }

        public void Request(byte action, int targetId)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null)
            {
                manager.SetLocalStatus("Join a faction to see its chain of command.");
                return;
            }
            if (requestedHq != player.HQ)
            {
                ResetScene(); requestedHq = player.HQ;
                manager.SetLocalStatus("Waiting for the staff board.");
            }
            if (action == ActionRefresh && (pending || Time.unscaledTime - lastQuery < 4f)) return;
            lastQuery = Time.unscaledTime;
            if (GameAccess.IsServer())
            {
                string result = action == ActionRefresh ? null : manager.Act(player, action, targetId);
                HighCommandSnapshot snapshot = manager.Snapshot(player);
                if (result != null) snapshot.Status = result;
                manager.Apply(snapshot);
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.SetLocalStatus("Connect to a host with the chain-of-command layer enabled.");
                return;
            }
            pending = true;
            if (action != ActionRefresh) manager.ReportStatus("Sending staff order...");
            client.Send(new HighCommandQuery
            {
                Protocol = ProtocolVersion, Scene = scene, Token = ++token,
                Action = action, TargetId = targetId,
            });
        }

        private void ReceiveQuery(INetworkPlayer sender, HighCommandQuery query)
        {
            if (!GameAccess.IsServer() || query.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            ulong id = PlayerIdentity.Of(player);
            float now = Time.unscaledTime;
            if (nextReply.TryGetValue(id, out float next) ? query.Action == ActionRefresh && now < next : nextReply.Count >= 64) return;
            nextReply[id] = now + 1.5f;
            if (query.Action > ActionBounty || (query.Action != ActionRefresh && query.TargetId <= 0)) return;
            string result = null;
            if (query.Action != ActionRefresh)
            {
                if (nextAction.TryGetValue(id, out float actionTime) && now < actionTime)
                    result = "Please wait 2 seconds before another staff order.";
                else
                {
                    nextAction[id] = now + 2f;
                    result = manager.Act(player, query.Action, query.TargetId);
                }
            }
            HighCommandSnapshot snapshot = manager.Snapshot(player);
            if (result != null) snapshot.Status = result;
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
            serverHandler?.UnregisterHandler<HighCommandQuery>();
            clientHandler?.UnregisterHandler<HighCommandSnapshot>();
            ResetScene();
        }

        internal static string Text(string value, int max) => string.IsNullOrEmpty(value) ? "" :
            value.Length > max ? value.Substring(0, max) : value;

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<HighCommandQuery>), "Write", (Action<NetworkWriter, HighCommandQuery>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
                w.WriteByte(v.Action); w.WritePackedInt32(v.TargetId);
            }));
            Bind(typeof(Reader<HighCommandQuery>), "Read", (Func<NetworkReader, HighCommandQuery>)(r =>
                new HighCommandQuery
                {
                    Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(),
                    Action = r.ReadByte(), TargetId = r.ReadPackedInt32(),
                }));

            Bind(typeof(Writer<HighCommandSnapshot>), "Write", (Action<NetworkWriter, HighCommandSnapshot>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
                w.WriteString(Text(v.Status, 96)); w.WriteString(Text(v.Signal, 96));
                w.WriteSingle(v.Cohesion); w.WritePackedInt32(v.CommandPoints);
                w.WritePackedInt32(v.Active); w.WritePackedInt32(v.Kia);
                int count = Math.Min(MaximumNodes, v.Nodes?.Length ?? 0);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++) WriteNode(w, v.Nodes[i]);            }));
            Bind(typeof(Reader<HighCommandSnapshot>), "Read", (Func<NetworkReader, HighCommandSnapshot>)(r =>
            {
                var snapshot = new HighCommandSnapshot
                {
                    Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(),
                    Status = Text(r.ReadString(), 96), Signal = Text(r.ReadString(), 96),
                    Cohesion = r.ReadSingle(), CommandPoints = r.ReadPackedInt32(),
                    Active = r.ReadPackedInt32(), Kia = r.ReadPackedInt32(),
                };
                int count = r.ReadByte();
                if (count > CommandSnapshotRules.MaximumNodes) throw new InvalidOperationException("High command snapshot exceeds node limit.");
                snapshot.Nodes = new CommanderWire[count];
                for (int i = 0; i < count; i++) snapshot.Nodes[i] = ReadNode(r);
                if (!CommandSnapshotRules.ValidHeader(snapshot.Cohesion, snapshot.CommandPoints,
                        snapshot.Active, snapshot.Kia))
                    throw new InvalidOperationException("Invalid high command snapshot header.");
                return snapshot;
            }));

            MessagePacker.RegisterMessage<HighCommandQuery>();
            MessagePacker.RegisterMessage<HighCommandSnapshot>();
        }

        private static void WriteNode(NetworkWriter w, CommanderWire node)
        {
            w.WritePackedInt32(node.Id); w.WritePackedInt32(node.ParentId);
            w.WriteByte(node.Tier); w.WriteByte(node.Flags); w.WriteByte(node.Actions);
            w.WriteByte(node.TraitMask);
            w.WritePackedInt32(node.Seed); w.WriteSingle(node.IntelAge); w.WriteSingle(node.Weight);
            w.WriteSingle(node.X); w.WriteSingle(node.Z);
            w.WriteString(Text(node.Name, 24)); w.WriteString(Text(node.Rank, 8));
            w.WriteString(Text(node.Role, 24)); w.WriteString(Text(node.Location, 32));
            w.WriteString(Text(node.Decoration, 24));
        }

        private static CommanderWire ReadNode(NetworkReader r)
        {
            var node = new CommanderWire
            {
                Id = r.ReadPackedInt32(), ParentId = r.ReadPackedInt32(),
                Tier = r.ReadByte(), Flags = r.ReadByte(), Actions = r.ReadByte(), TraitMask = r.ReadByte(),
                Seed = r.ReadPackedInt32(), IntelAge = r.ReadSingle(), Weight = r.ReadSingle(),
                X = r.ReadSingle(), Z = r.ReadSingle(),
                Name = Text(r.ReadString(), 24), Rank = Text(r.ReadString(), 8),
                Role = Text(r.ReadString(), 24), Location = Text(r.ReadString(), 32),
                Decoration = Text(r.ReadString(), 24),
            };
            if (!CommandSnapshotRules.ValidNode(node.Id, node.ParentId, node.Tier, node.Flags,
                    node.Actions, node.IntelAge, node.Weight, node.X, node.Z) ||
                (node.TraitMask & ~CommandTraits.All) != 0)
                throw new InvalidOperationException("Invalid high command node.");
            return node;
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null) throw new MissingMemberException(holder.FullName, property);
            target.SetValue(null, value, null);
        }
    }
}

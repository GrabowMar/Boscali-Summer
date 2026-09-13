using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Features.DynamicOperations.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Networking
{
    [NetworkMessage]
    internal struct OperationsQuery
    {
        public byte Protocol;
        public uint Scene;
        public uint Token;
        public int OperationId;
        public byte Action;
    }

    [NetworkMessage]
    internal struct OperationsSnapshot
    {
        public byte Protocol;
        public uint Scene;
        public uint Token;
        public string Status;
        public SecondaryObjectiveView[] Cards;
    }

    internal sealed class OperationsNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 2;
        private OperationsManager manager;
        private MessageHandler serverHandler, clientHandler;
        private readonly Dictionary<ulong, float> nextReply = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> nextAction = new Dictionary<ulong, float>();
        private readonly List<ulong> expired = new List<ulong>(64);
        private float nextRegistration, lastQuery, nextPrune;
        private uint scene, token;
        private FactionHQ requestedHq;
        private bool pending;

        public void Configure(OperationsManager owner)
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
                manager.SetLocalStatus("Host link unavailable; secondary state cleared.");
            }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;
            if (server != serverHandler)
            {
                serverHandler?.UnregisterHandler<OperationsQuery>();
                serverHandler = server;
                serverHandler?.RegisterHandler<OperationsQuery>(ReceiveQuery, false);
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<OperationsSnapshot>();
                clientHandler = client;
                clientHandler?.RegisterHandler<OperationsSnapshot>(ReceiveSnapshot, false);
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

        public void Request(int operationId = 0, bool cancel = false)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null)
            {
                manager.SetLocalStatus("Join a faction to see its secondary objectives.");
                return;
            }
            if (requestedHq != player.HQ)
            {
                ResetScene(); requestedHq = player.HQ;
                manager.SetLocalStatus("Waiting for faction objectives.");
            }
            if (operationId <= 0 && (pending || Time.unscaledTime - lastQuery < 2f))
            {
                if (operationId > 0) manager.ReportStatus("Host refresh in progress. Try the contract action again in 2 seconds.");
                return;
            }
            lastQuery = Time.unscaledTime;
            if (GameAccess.IsServer())
            {
                string result = operationId > 0 ? manager.Act(player, operationId, cancel) : null;
                OperationsSnapshot snapshot = manager.Snapshot(player);
                if (result != null) snapshot.Status = result;
                manager.Apply(snapshot);
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.SetLocalStatus("Connect to a host with dynamic operations enabled.");
                return;
            }
            pending = true;
            if (operationId > 0) manager.ReportStatus(cancel ? "Dismissing contract..." : "Accepting contract...");
            client.Send(new OperationsQuery { Protocol = ProtocolVersion, Scene = scene, Token = ++token,
                OperationId = operationId, Action = operationId <= 0 ? (byte)0 : cancel ? (byte)2 : (byte)1 });
        }

        private void ReceiveQuery(INetworkPlayer sender, OperationsQuery query)
        {
            if (!GameAccess.IsServer() || query.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            ulong id = PlayerIdentity.Of(player);
            float now = Time.unscaledTime;
            if (nextReply.TryGetValue(id, out float next) ? query.Action == 0 && now < next : nextReply.Count >= 64) return;
            nextReply[id] = now + 1.5f;
            if (query.Action > 2 || query.OperationId < 0 || (query.Action != 0 && query.OperationId == 0)) return;
            string result = null;
            if (query.Action != 0)
            {
                if (nextAction.TryGetValue(id, out float actionTime) && now < actionTime) result = "Please wait 2 seconds before another contract action.";
                else { nextAction[id] = now + 2f; result = manager.Act(player, query.OperationId, query.Action == 2); }
            }
            OperationsSnapshot snapshot = manager.Snapshot(player);
            if (result != null) snapshot.Status = result;
            snapshot.Scene = query.Scene;
            snapshot.Token = query.Token;
            sender.Send(snapshot);
        }

        private void ReceiveSnapshot(INetworkPlayer sender, OperationsSnapshot snapshot)
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
            serverHandler?.UnregisterHandler<OperationsQuery>();
            clientHandler?.UnregisterHandler<OperationsSnapshot>();
            ResetScene();
        }

        // Fixed card and text ceilings keep both late-join replies and parsing bounded.
        internal static string Text(string value) => string.IsNullOrEmpty(value) ? "" :
            value.Length > 128 ? value.Substring(0, 128) : value;

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<OperationsQuery>), "Write", (Action<NetworkWriter, OperationsQuery>)((w, v) =>
            { w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token); w.WritePackedInt32(v.OperationId); w.WriteByte(v.Action); }));
            Bind(typeof(Reader<OperationsQuery>), "Read", (Func<NetworkReader, OperationsQuery>)(r =>
                new OperationsQuery { Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(), OperationId = r.ReadPackedInt32(), Action = r.ReadByte() }));
            Bind(typeof(Writer<OperationsSnapshot>), "Write", (Action<NetworkWriter, OperationsSnapshot>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
                w.WriteString(Text(v.Status));
                int count = Math.Min(OperationBoard.MaximumCards, v.Cards?.Length ?? 0);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    SecondaryObjectiveView card = v.Cards[i];
                    w.WritePackedInt32(card.Id);
                    w.WriteString(Text(card.Title)); w.WriteString(Text(card.Description)); w.WriteString(Text(card.Target));
                    w.WriteString(Text(card.Status)); w.WriteString(Text(card.Reward));
                    w.WriteSingle(card.Progress); w.WriteSingle(card.SecondsRemaining);
                    w.WritePackedInt32(card.Money); w.WritePackedInt32(card.Xp); w.WriteByte(card.IsComplete ? (byte)1 : (byte)0);
                    w.WriteByte(card.IsOffered ? (byte)1 : (byte)0); w.WriteByte(card.IsActive ? (byte)1 : (byte)0); w.WriteByte(card.HasMarker ? (byte)1 : (byte)0);
                    w.WriteSingle(card.X); w.WriteSingle(card.Z); w.WriteSingle(card.Radius);
                }
            }));
            Bind(typeof(Reader<OperationsSnapshot>), "Read", (Func<NetworkReader, OperationsSnapshot>)(r =>
            {
                var snapshot = new OperationsSnapshot
                { Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(), Status = Text(r.ReadString()) };
                int count = r.ReadByte();
                if (count > OperationBoard.MaximumCards) throw new InvalidOperationException("Operations snapshot exceeds card limit.");
                snapshot.Cards = new SecondaryObjectiveView[count];
                for (int i = 0; i < count; i++)
                {
                    int id = r.ReadPackedInt32();
                    string title = Text(r.ReadString()), description = Text(r.ReadString()), target = Text(r.ReadString());
                    string status = Text(r.ReadString()), reward = Text(r.ReadString());
                    float progress = r.ReadSingle(), remaining = r.ReadSingle();
                    int money = r.ReadPackedInt32(), xp = r.ReadPackedInt32();
                    bool complete = r.ReadByte() == 1;
                    bool offered = r.ReadByte() == 1, active = r.ReadByte() == 1, marker = r.ReadByte() == 1;
                    float x = r.ReadSingle(), z = r.ReadSingle(), radius = r.ReadSingle();
                    if (!Operation.Finite(progress) || !Operation.Finite(remaining) || progress < 0f || progress > 1f ||
                        remaining < 0f || remaining > 1200f || money < 0 || money > 100000 || xp < 0 || xp > 10000 ||
                        !Operation.Finite(x) || !Operation.Finite(z) || !Operation.Finite(radius) || radius < 0f || radius > 1500f ||
                        (offered && active) || (complete && (offered || active)) || (marker && !active))
                        throw new InvalidOperationException("Invalid operations snapshot values.");
                    snapshot.Cards[i] = new SecondaryObjectiveView(id, title, description, target, status, reward,
                        progress, remaining, money, xp, complete, offered, active, marker, x, z, radius);
                }
                return snapshot;
            }));
            MessagePacker.RegisterMessage<OperationsQuery>();
            MessagePacker.RegisterMessage<OperationsSnapshot>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null) throw new MissingMemberException(holder.FullName, property);
            target.SetValue(null, value, null);
        }
    }
}

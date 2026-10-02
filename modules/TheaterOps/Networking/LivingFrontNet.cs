using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Modules.TheaterOps.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Networking
{
    [NetworkMessage]
    internal struct LivingFrontQuery { public byte Protocol; }

    [NetworkMessage]
    internal struct LivingFrontIntent
    {
        public byte Protocol, Kind, Posture;
        public int Id, Revision;
    }

    [NetworkMessage]
    internal struct LivingFrontSnapshot
    {
        public byte Protocol, Posture;
        public string Faction;
        public TheaterFrontView[] Fronts;
        public TheaterProposalView[] Proposals;
        public TheaterLiveOperationView Operation;
        public string[] Log;
    }

    /// <summary>Faction-private, host-authored war picture and validated player intent.</summary>
    internal sealed class LivingFrontNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private const int MaximumRecipients = 64;
        private const int MaximumPlayers = 64;
        private const int MaximumFaction = 64;
        private const int MaximumKey = 96;
        private const int MaximumLabel = 64;
        private const int MaximumStatus = 32;
        private const int MaximumBrief = 160;
        private const int MaximumForces = 96;
        private const float QueryInterval = 1f;
        private const float IntentInterval = 0.5f;

        private readonly List<TheaterFrontView> fronts = new List<TheaterFrontView>(LivingWarRules.MaximumFronts);
        private readonly List<TheaterProposalView> proposals =
            new List<TheaterProposalView>(LivingWarRules.MaximumOffers);
        private readonly List<string> log = new List<string>(StaffLog.MaximumEntries);
        private readonly Dictionary<ulong, float> nextQuery = new Dictionary<ulong, float>(16);
        private readonly Dictionary<ulong, float> nextIntent = new Dictionary<ulong, float>(16);
        private readonly List<ulong> stale = new List<ulong>(16);
        private LivingFrontService owner;
        private MessageHandler serverHandler, clientHandler;
        private float nextRegistration, nextPrune, nextClientQuery, nextFactionCheck;
        private bool queried;
        private FactionHQ queriedHq;

        internal void Configure(LivingFrontService service)
        {
            owner = service;
            InstallSerializers();
        }

        internal void ResetScene()
        {
            serverHandler?.UnregisterHandler<LivingFrontQuery>();
            serverHandler?.UnregisterHandler<LivingFrontIntent>();
            clientHandler?.UnregisterHandler<LivingFrontSnapshot>();
            serverHandler = null;
            clientHandler = null;
            queried = false;
            queriedHq = null;
            nextRegistration = nextPrune = nextFactionCheck = 0f;
            nextClientQuery = -10f;
            nextQuery.Clear(); nextIntent.Clear(); stale.Clear();
            fronts.Clear(); proposals.Clear(); log.Clear();
        }

        private void OnDestroy() => ResetScene();

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;
            if (server != serverHandler)
            {
                serverHandler?.UnregisterHandler<LivingFrontQuery>();
                serverHandler?.UnregisterHandler<LivingFrontIntent>();
                serverHandler = server;
                serverHandler?.RegisterHandler<LivingFrontQuery>(ReceiveQuery, false);
                serverHandler?.RegisterHandler<LivingFrontIntent>(ReceiveIntent, false);
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<LivingFrontSnapshot>();
                clientHandler = client;
                queried = false;
                clientHandler?.RegisterHandler<LivingFrontSnapshot>(ReceiveSnapshot, false);
            }
            if (clientHandler != null && !GameAccess.IsServer() && now >= nextFactionCheck)
            {
                nextFactionCheck = now + 1f;
                GameManager.GetLocalHQ(out FactionHQ hq);
                if (!ReferenceEquals(hq, queriedHq)) queried = false;
                if (!queried && hq != null && hq.faction != null && now >= nextClientQuery)
                {
                    queriedHq = hq;
                    queried = true;
                    nextClientQuery = now + 4f;
                    network.Client.Send(new LivingFrontQuery { Protocol = ProtocolVersion });
                }
            }
            if (now < nextPrune) return;
            nextPrune = now + 10f;
            Prune(nextQuery, now);
            Prune(nextIntent, now);
        }

        private void Prune(Dictionary<ulong, float> table, float now)
        {
            stale.Clear();
            foreach (KeyValuePair<ulong, float> pair in table)
                if (now - pair.Value > 30f) stale.Add(pair.Key);
            foreach (ulong id in stale) table.Remove(id);
        }

        internal bool SendIntent(byte kind, int id, int revision, byte posture)
        {
            if (GameAccess.IsServer() || !ValidIntent(kind, id, revision, posture)) return false;
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return false;
            client.Send(new LivingFrontIntent
            {
                Protocol = ProtocolVersion, Kind = kind, Id = id,
                Revision = revision, Posture = posture,
            });
            return true;
        }

        internal void Broadcast(string faction)
        {
            if (owner?.Authoritative != true || string.IsNullOrEmpty(faction)) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            LivingFrontSnapshot snapshot = SnapshotOf(faction);
            IReadOnlyList<INetworkPlayer> players = server.AuthenticatedPlayers;
            for (int i = 0; i < players.Count && i < MaximumRecipients; i++)
            {
                INetworkPlayer connection = players[i];
                if (connection == null || ReferenceEquals(connection, server.LocalPlayer) ||
                    !connection.TryGetPlayer<Player>(out Player player) || player?.HQ?.faction == null ||
                    !string.Equals(player.HQ.faction.factionName, faction, StringComparison.Ordinal))
                    continue;
                connection.Send(snapshot);
            }
        }

        private LivingFrontSnapshot SnapshotOf(string faction)
        {
            owner.CopySnapshot(faction, fronts, proposals, out TheaterLiveOperationView operation,
                out TheaterWarPosture posture, log);
            return new LivingFrontSnapshot
            {
                Protocol = ProtocolVersion,
                Faction = Text(faction, MaximumFaction),
                Posture = (byte)posture,
                Fronts = fronts.ToArray(),
                Proposals = proposals.ToArray(),
                Operation = operation,
                Log = log.ToArray(),
            };
        }

        private void ReceiveQuery(INetworkPlayer sender, LivingFrontQuery query)
        {
            if (query.Protocol != ProtocolVersion || owner?.Authoritative != true || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) ||
                player?.HQ?.faction == null || !Allow(nextQuery, player, QueryInterval)) return;
            sender.Send(SnapshotOf(player.HQ.faction.factionName));
        }

        private void ReceiveIntent(INetworkPlayer sender, LivingFrontIntent intent)
        {
            if (intent.Protocol != ProtocolVersion || owner?.Authoritative != true || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) ||
                player?.HQ?.faction == null ||
                !ValidIntent(intent.Kind, intent.Id, intent.Revision, intent.Posture) ||
                !Allow(nextIntent, player, IntentInterval)) return;
            owner.ApplyIntent(player.HQ, intent.Kind, intent.Id, intent.Revision, intent.Posture);
        }

        private void ReceiveSnapshot(INetworkPlayer _, LivingFrontSnapshot snapshot)
        {
            if (snapshot.Protocol != ProtocolVersion || GameAccess.IsServer() || owner == null ||
                snapshot.Posture > (byte)TheaterWarPosture.Bold ||
                snapshot.Fronts == null || snapshot.Fronts.Length > LivingWarRules.MaximumFronts ||
                snapshot.Proposals == null || snapshot.Proposals.Length > LivingWarRules.MaximumOffers ||
                snapshot.Log == null || snapshot.Log.Length > StaffLog.MaximumEntries ||
                !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null ||
                !string.Equals(snapshot.Faction, hq.faction.factionName, StringComparison.Ordinal)) return;
            owner.ApplyRemote(snapshot.Faction, snapshot.Fronts, snapshot.Proposals,
                snapshot.Operation, (TheaterWarPosture)snapshot.Posture, snapshot.Log);
        }

        private bool Allow(Dictionary<ulong, float> table, Player player, float interval)
        {
            float now = Time.unscaledTime;
            ulong id = PlayerIdentity.Of(player);
            if (table.TryGetValue(id, out float next) && now < next) return false;
            if (!table.ContainsKey(id) && table.Count >= MaximumPlayers) return false;
            table[id] = now + interval;
            return true;
        }

        private static bool ValidIntent(byte kind, int id, int revision, byte posture) =>
            kind <= 2 && (kind == 2
                ? id == 0 && revision == 0 && posture <= (byte)TheaterWarPosture.Bold
                : id > 0 && revision > 0 && posture == 0);

        private static string Text(string value, int max) => string.IsNullOrEmpty(value) ? ""
            : value.Length > max ? value.Substring(0, max) : value;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<LivingFrontQuery>), "Write",
                (Action<NetworkWriter, LivingFrontQuery>)((w, v) => w.WriteByte(v.Protocol)));
            Bind(typeof(Reader<LivingFrontQuery>), "Read",
                (Func<NetworkReader, LivingFrontQuery>)(r => new LivingFrontQuery { Protocol = r.ReadByte() }));
            Bind(typeof(Writer<LivingFrontIntent>), "Write",
                (Action<NetworkWriter, LivingFrontIntent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol); w.WriteByte(v.Kind); w.WriteInt32(v.Id);
                    w.WriteInt32(v.Revision); w.WriteByte(v.Posture);
                }));
            Bind(typeof(Reader<LivingFrontIntent>), "Read",
                (Func<NetworkReader, LivingFrontIntent>)(r => new LivingFrontIntent
                {
                    Protocol = r.ReadByte(), Kind = r.ReadByte(), Id = r.ReadInt32(),
                    Revision = r.ReadInt32(), Posture = r.ReadByte(),
                }));
            Bind(typeof(Writer<LivingFrontSnapshot>), "Write",
                (Action<NetworkWriter, LivingFrontSnapshot>)WriteSnapshot);
            Bind(typeof(Reader<LivingFrontSnapshot>), "Read",
                (Func<NetworkReader, LivingFrontSnapshot>)ReadSnapshot);
            MessagePacker.RegisterMessage<LivingFrontQuery>();
            MessagePacker.RegisterMessage<LivingFrontIntent>();
            MessagePacker.RegisterMessage<LivingFrontSnapshot>();
        }

        private static void WriteSnapshot(NetworkWriter w, LivingFrontSnapshot value)
        {
            w.WriteByte(value.Protocol);
            w.WriteString(Text(value.Faction, MaximumFaction));
            w.WriteByte(value.Posture);
            int count = Math.Min(value.Fronts?.Length ?? 0, LivingWarRules.MaximumFronts);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                TheaterFrontView front = value.Fronts[i];
                w.WriteString(Text(front.Key, MaximumKey));
                w.WriteString(Text(front.Label, MaximumLabel));
                w.WriteSingle(front.X); w.WriteSingle(front.Z);
                w.WriteString(Text(front.Status, MaximumStatus));
                w.WriteSingle(front.Pressure); w.WriteSingle(front.Trend);
                w.WriteByte(front.Observed ? (byte)1 : (byte)0);
                w.WriteSingle(front.AgeSeconds);
            }
            count = Math.Min(value.Proposals?.Length ?? 0, LivingWarRules.MaximumOffers);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                TheaterProposalView offer = value.Proposals[i];
                w.WriteInt32(offer.Id); w.WriteInt32(offer.Revision);
                w.WriteString(Text(offer.Kind, MaximumStatus));
                w.WriteString(Text(offer.Label, MaximumLabel));
                w.WriteString(Text(offer.TargetKey, MaximumKey));
                w.WriteSingle(offer.X); w.WriteSingle(offer.Z);
                w.WriteString(Text(offer.Brief, MaximumBrief));
                w.WriteString(Text(offer.Risk, MaximumStatus));
                w.WriteString(Text(offer.Forces, MaximumForces));
                w.WriteSingle(offer.SecondsRemaining);
            }
            TheaterLiveOperationView op = value.Operation;
            w.WriteByte(op == null ? (byte)0 : (byte)1);
            if (op != null)
            {
                w.WriteInt32(op.Id); w.WriteInt32(op.Revision);
                w.WriteString(Text(op.Kind, MaximumStatus));
                w.WriteString(Text(op.TargetKey, MaximumKey));
                w.WriteString(Text(op.Label, MaximumLabel));
                w.WriteSingle(op.X); w.WriteSingle(op.Z);
                w.WriteString(Text(op.Phase, MaximumStatus));
                w.WriteString(Text(op.Summary, MaximumForces));
                w.WriteByte((byte)Math.Min(Math.Max(op.GroundGroups, 0), 32));
                w.WriteByte((byte)Math.Min(Math.Max(op.AirGroups, 0), 32));
                w.WriteByte((byte)Math.Min(Math.Max(op.NavalGroups, 0), 32));
            }
            count = Math.Min(value.Log?.Length ?? 0, StaffLog.MaximumEntries);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
                w.WriteString(Text(value.Log[i], StaffLog.MaximumTextLength));
        }

        private static LivingFrontSnapshot ReadSnapshot(NetworkReader r)
        {
            var value = new LivingFrontSnapshot { Protocol = r.ReadByte() };
            if (value.Protocol != ProtocolVersion) return value;
            value.Faction = Text(r.ReadString(), MaximumFaction);
            value.Posture = r.ReadByte();
            int count = r.ReadByte();
            if (count > LivingWarRules.MaximumFronts) return value;
            value.Fronts = new TheaterFrontView[count];
            for (int i = 0; i < count; i++)
            {
                string key = Text(r.ReadString(), MaximumKey);
                string label = Text(r.ReadString(), MaximumLabel);
                float x = r.ReadSingle(), z = r.ReadSingle();
                string status = Text(r.ReadString(), MaximumStatus);
                float pressure = r.ReadSingle(), trend = r.ReadSingle();
                bool observed = r.ReadByte() == 1;
                float age = r.ReadSingle();
                if (string.IsNullOrEmpty(key) || !Finite(x) || !Finite(z) ||
                    !Finite(pressure) || !Finite(trend) || !Finite(age)) return default;
                value.Fronts[i] = new TheaterFrontView(key, label, x, z, status,
                    pressure, trend, observed, age);
            }
            count = r.ReadByte();
            if (count > LivingWarRules.MaximumOffers) return default;
            value.Proposals = new TheaterProposalView[count];
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32(), revision = r.ReadInt32();
                string kind = Text(r.ReadString(), MaximumStatus);
                string label = Text(r.ReadString(), MaximumLabel);
                string key = Text(r.ReadString(), MaximumKey);
                float x = r.ReadSingle(), z = r.ReadSingle();
                string brief = Text(r.ReadString(), MaximumBrief);
                string risk = Text(r.ReadString(), MaximumStatus);
                string forces = Text(r.ReadString(), MaximumForces);
                float seconds = r.ReadSingle();
                if (id <= 0 || revision <= 0 || string.IsNullOrEmpty(key) ||
                    !Finite(x) || !Finite(z) || !Finite(seconds)) return default;
                value.Proposals[i] = new TheaterProposalView(id, revision, kind, label,
                    key, x, z, brief, risk, forces, seconds);
            }
            if (r.ReadByte() != 0)
            {
                int id = r.ReadInt32(), revision = r.ReadInt32();
                string kind = Text(r.ReadString(), MaximumStatus);
                string key = Text(r.ReadString(), MaximumKey);
                string label = Text(r.ReadString(), MaximumLabel);
                float x = r.ReadSingle(), z = r.ReadSingle();
                string phase = Text(r.ReadString(), MaximumStatus);
                string summary = Text(r.ReadString(), MaximumForces);
                int ground = r.ReadByte(), air = r.ReadByte(), naval = r.ReadByte();
                if (id <= 0 || revision <= 0 || string.IsNullOrEmpty(key) ||
                    !Finite(x) || !Finite(z)) return default;
                value.Operation = new TheaterLiveOperationView(id, revision, kind, key,
                    label, x, z, phase, summary, ground, air, naval);
            }
            count = r.ReadByte();
            if (count > StaffLog.MaximumEntries) return default;
            value.Log = new string[count];
            for (int i = 0; i < count; i++)
                value.Log[i] = Text(r.ReadString(), StaffLog.MaximumTextLength);
            return value;
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(property,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError("[TheaterOps] Mirage serializer seam " +
                    holder.Name + "." + property + " is missing; living front cannot replicate.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}

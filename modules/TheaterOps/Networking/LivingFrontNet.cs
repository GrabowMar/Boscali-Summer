using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Modules.TheaterOps.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Networking
{
    [NetworkMessage]
    internal struct LivingFrontQuery { public byte Protocol; public int Session; }

    [NetworkMessage]
    internal struct LivingFrontIntent
    {
        public byte Protocol, Kind, Posture;
        public int Id, Revision, RequestId, Session, HostEpoch;
    }

    [NetworkMessage]
    internal struct LivingFrontResult
    {
        public byte Protocol;
        public string Faction;
        public int RequestId;
        public bool Accepted;
        public string Status;
        public int Session, HostEpoch;
    }

    [NetworkMessage]
    internal struct LivingFrontSnapshot
    {
        public byte Protocol, Posture;
        public bool HasSnapshot;
        public int Session, HostEpoch;
        public string Faction;
        public TheaterFrontView[] Fronts;
        public TheaterProposalView[] Proposals;
        public TheaterLiveOperationView Operation;
        public string[] Log;
    }

    /// <summary>Faction-private, host-authored war picture and validated player intent.</summary>
    internal sealed class LivingFrontNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 2;
        private const int MaximumRecipients = 64;
        private const int MaximumPlayers = 64;
        private const int MaximumFaction = 64;
        private const int MaximumKey = 96;
        private const int MaximumLabel = 64;
        private const int MaximumStatus = 32;
        private const int MaximumBrief = 160;
        private const int MaximumForces = 256;
        private const float QueryInterval = 1f;
        private const float IntentInterval = 0.5f;
        private const float IntentTimeout = 8f;

        private readonly List<TheaterFrontView> fronts = new List<TheaterFrontView>(LivingWarRules.MaximumFronts);
        private readonly List<TheaterProposalView> proposals =
            new List<TheaterProposalView>(LivingWarRules.MaximumOffers);
        private readonly List<string> log = new List<string>(StaffLog.MaximumEntries);
        private readonly SenderThrottle nextQuery = new SenderThrottle(MaximumPlayers);
        private readonly SenderThrottle nextIntent = new SenderThrottle(MaximumPlayers);
        private readonly Dictionary<ulong, (int Session, FactionHQ HQ)> clientSessions =
            new Dictionary<ulong, (int, FactionHQ)>(16);
        private LivingFrontService owner;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??=
            HandlerSlot.Of<LivingFrontQuery, LivingFrontIntent>(ReceiveQuery, ReceiveIntent);
        private HandlerSlot ClientHandlers => clientSlot ??=
            HandlerSlot.Of<LivingFrontSnapshot, LivingFrontResult>(ReceiveSnapshot, ReceiveResult);
        private Action<ulong> forgetSession;
        private float nextRegistration, nextClientQuery, nextFactionCheck;
        private FactionHQ queriedHq;
        private int nextRequestId, pendingRequestId;
        private static int nextHostEpoch;
        private int hostEpoch = NewHostEpoch(), clientSession = 1, seenHostEpoch;
        private float pendingDeadline;
        private string pendingFaction, commandStatus = "";
        internal bool CommandPending => pendingRequestId > 0;
        internal string CommandStatus => commandStatus;

        internal void Configure(LivingFrontService service)
        {
            owner = service;
            InstallSerializers();
        }

        internal void ResetScene()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            ClearClientState();
            owner?.ClearRemoteState();
            hostEpoch = NewHostEpoch();
            queriedHq = null;
            nextRegistration = nextFactionCheck = 0f;
            nextClientQuery = -10f;
            nextQuery.Clear(); nextIntent.Clear();
            clientSessions.Clear();
            fronts.Clear(); proposals.Clear(); log.Clear();
        }

        private void OnDestroy() => ResetScene();

        internal void ClearClientState()
        {
            pendingRequestId = 0; pendingFaction = null; commandStatus = "";
            clientSession = clientSession >= int.MaxValue ? 1 : clientSession + 1;
            seenHostEpoch = 0;
            nextClientQuery = 0f;
        }

        private static int NewHostEpoch() => nextHostEpoch = nextHostEpoch >= int.MaxValue
            ? 1 : nextHostEpoch + 1;

        private void Update()
        {
            float now = Time.unscaledTime;
            if (CommandPending && now >= pendingDeadline)
            {
                pendingRequestId = 0;
                commandStatus = "UNCONFIRMED · host reply timed out; refreshing staff";
                owner?.ClearRemoteState();
                nextClientQuery = 0f;
            }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;
            if (ServerHandlers.Swap(server))
            {
                nextQuery.Clear(); nextIntent.Clear(); clientSessions.Clear();
                hostEpoch = NewHostEpoch();
            }
            if (ClientHandlers.Swap(client))
            {
                ClearClientState();
                owner?.ClearRemoteState();
            }
            if (ClientHandlers.Current != null && !GameAccess.IsServer() && now >= nextFactionCheck)
            {
                nextFactionCheck = now + 1f;
                GameManager.GetLocalHQ(out FactionHQ hq);
                if (!ReferenceEquals(hq, queriedHq))
                {
                    queriedHq = hq;
                    ClearClientState(); owner?.ClearRemoteState();
                }
                if (hq != null && hq.faction != null && now >= nextClientQuery)
                {
                    // Retry absent/stale pictures; heartbeat also keeps a paused
                    // mission's transport current while gameplay clocks stand still.
                    nextClientQuery = now + (owner?.HasSnapshot == true &&
                        owner.SnapshotAgeSeconds <= LivingWarRules.SnapshotFreshSeconds ? 10f : 4f);
                    network.Client.Send(new LivingFrontQuery { Protocol = ProtocolVersion, Session = clientSession });
                }
            }
            nextQuery.Prune(now, forgetSession ??= id => clientSessions.Remove(id));
            nextIntent.Prune(now);
        }

        internal bool SendIntent(byte kind, int id, int revision, byte posture)
        {
            if (GameAccess.IsServer() || CommandPending || !ValidIntent(kind, id, revision, posture) ||
                seenHostEpoch <= 0 || !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null)
                return false;
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return false;
            pendingRequestId = nextRequestId = nextRequestId >= int.MaxValue ? 1 : nextRequestId + 1;
            pendingFaction = hq.faction.factionName;
            pendingDeadline = Time.unscaledTime + IntentTimeout;
            commandStatus = "PENDING · awaiting host";
            client.Send(new LivingFrontIntent
            {
                Protocol = ProtocolVersion, Kind = kind, Id = id,
                Revision = revision, Posture = posture, RequestId = pendingRequestId,
                Session = clientSession, HostEpoch = seenHostEpoch,
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
                if (!clientSessions.TryGetValue(PlayerIdentity.Of(player), out var session) ||
                    !ReferenceEquals(session.HQ, player.HQ)) continue;
                snapshot.Session = session.Session;
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
                Faction = NetText.Clip(faction, MaximumFaction),
                Posture = (byte)posture,
                HasSnapshot = owner.HasSnapshotFor(faction),
                HostEpoch = hostEpoch,
                Fronts = fronts.ToArray(),
                Proposals = proposals.ToArray(),
                Operation = operation,
                Log = log.ToArray(),
            };
        }

        private void ReceiveQuery(INetworkPlayer sender, LivingFrontQuery query)
        {
            if (query.Protocol != ProtocolVersion || query.Session <= 0 || owner?.Authoritative != true || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) ||
                player?.HQ?.faction == null || !nextQuery.Allow(PlayerIdentity.Of(player), Time.unscaledTime, QueryInterval)) return;
            clientSessions[PlayerIdentity.Of(player)] = (query.Session, player.HQ);
            LivingFrontSnapshot snapshot = SnapshotOf(player.HQ.faction.factionName);
            snapshot.Session = query.Session;
            sender.Send(snapshot);
        }

        private void ReceiveIntent(INetworkPlayer sender, LivingFrontIntent intent)
        {
            if (intent.Protocol != ProtocolVersion || owner?.Authoritative != true || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) ||
                player?.HQ?.faction == null || intent.RequestId <= 0) return;
            bool valid = ValidIntent(intent.Kind, intent.Id, intent.Revision, intent.Posture);
            bool current = intent.HostEpoch == hostEpoch && intent.Session > 0 &&
                clientSessions.TryGetValue(PlayerIdentity.Of(player), out var session) &&
                session.Session == intent.Session && ReferenceEquals(session.HQ, player.HQ);
            bool allowed = valid && current && nextIntent.Allow(PlayerIdentity.Of(player), Time.unscaledTime, IntentInterval);
            bool accepted = allowed && owner.ApplyIntent(player.HQ,
                intent.Kind, intent.Id, intent.Revision, intent.Posture);
            sender.Send(new LivingFrontResult
            {
                Protocol = ProtocolVersion, Faction = player.HQ.faction.factionName,
                RequestId = intent.RequestId, Accepted = accepted,
                Session = intent.Session, HostEpoch = hostEpoch,
                Status = accepted ? "ACCEPTED · staff updated" : !valid
                    ? "REJECTED · invalid command" : !current
                    ? "REJECTED · mission or faction changed" : !allowed
                    ? "REJECTED · command rate limited" : "REJECTED · choice changed; review current options",
            });
            if (current)
            {
                LivingFrontSnapshot snapshot = SnapshotOf(player.HQ.faction.factionName);
                snapshot.Session = intent.Session;
                sender.Send(snapshot);
            }
        }

        private void ReceiveResult(INetworkPlayer _, LivingFrontResult result)
        {
            if (GameAccess.IsServer() || result.Protocol != ProtocolVersion ||
                result.Session != clientSession || result.HostEpoch != seenHostEpoch ||
                !CommandPending || result.RequestId != pendingRequestId ||
                !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null ||
                result.Faction != pendingFaction || result.Faction != hq.faction.factionName ||
                string.IsNullOrEmpty(result.Status) || result.Status.Length > MaximumBrief) return;
            pendingRequestId = 0;
            commandStatus = (result.Accepted ? "ACCEPTED" : "REJECTED") + " · " +
                (result.Accepted ? "staff updated" : result.Status.Replace("REJECTED · ", ""));
            nextClientQuery = 0f;
        }

        private void ReceiveSnapshot(INetworkPlayer _, LivingFrontSnapshot snapshot)
        {
            if (snapshot.Protocol != ProtocolVersion || GameAccess.IsServer() || owner == null ||
                snapshot.Session != clientSession || snapshot.HostEpoch <= 0 ||
                snapshot.HostEpoch < seenHostEpoch ||
                snapshot.Posture > (byte)TheaterWarPosture.Bold ||
                snapshot.Fronts == null || snapshot.Fronts.Length > LivingWarRules.MaximumFronts ||
                snapshot.Proposals == null || snapshot.Proposals.Length > LivingWarRules.MaximumOffers ||
                snapshot.Log == null || snapshot.Log.Length > StaffLog.MaximumEntries ||
                !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null ||
                !string.Equals(snapshot.Faction, hq.faction.factionName, StringComparison.Ordinal)) return;
            seenHostEpoch = snapshot.HostEpoch;
            if (!snapshot.HasSnapshot) { owner.ClearRemoteState(); nextClientQuery = 0f; return; }
            owner.ApplyRemote(snapshot.Faction, snapshot.Fronts, snapshot.Proposals,
                snapshot.Operation, (TheaterWarPosture)snapshot.Posture, snapshot.Log);
        }

        private static bool ValidIntent(byte kind, int id, int revision, byte posture) =>
            kind <= 2 && (kind == 2
                ? id == 0 && revision == 0 && posture <= (byte)TheaterWarPosture.Bold
                : id > 0 && revision > 0 && posture == 0);

        private static string ReadText(NetworkReader reader, int max)
        {
            string value = reader.ReadString();
            return value == null || value.Length > max ? null : value;
        }


        private static readonly MirageSerializers Seams = new MirageSerializers(
            "[TheaterOps]", " is missing; living front cannot replicate.");

        private static void InstallSerializers()
        {
            Seams.Install<LivingFrontQuery>(
                (Action<NetworkWriter, LivingFrontQuery>)((w, v) =>
                { w.WriteByte(v.Protocol); w.WriteInt32(v.Session); }),
                (Func<NetworkReader, LivingFrontQuery>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return default;
                    return new LivingFrontQuery { Protocol = protocol, Session = r.ReadInt32() };
                }));
            Seams.Install<LivingFrontIntent>(
                (Action<NetworkWriter, LivingFrontIntent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol); w.WriteByte(v.Kind); w.WriteInt32(v.Id);
                    w.WriteInt32(v.Revision); w.WriteByte(v.Posture);
                    w.WriteInt32(v.RequestId);
                    w.WriteInt32(v.Session); w.WriteInt32(v.HostEpoch);
                }),
                (Func<NetworkReader, LivingFrontIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return default;
                    return new LivingFrontIntent
                    {
                        Protocol = protocol, Kind = r.ReadByte(), Id = r.ReadInt32(),
                        Revision = r.ReadInt32(), Posture = r.ReadByte(), RequestId = r.ReadInt32(),
                        Session = r.ReadInt32(), HostEpoch = r.ReadInt32(),
                    };
                }));
            Seams.Install<LivingFrontResult>(
                (Action<NetworkWriter, LivingFrontResult>)((w, v) =>
                {
                    w.WriteByte(v.Protocol); w.WriteString(NetText.Clip(v.Faction, MaximumFaction));
                    w.WriteInt32(v.RequestId); w.WriteByte(v.Accepted ? (byte)1 : (byte)0);
                    w.WriteString(NetText.Clip(v.Status, MaximumBrief));
                    w.WriteInt32(v.Session); w.WriteInt32(v.HostEpoch);
                }),
                (Func<NetworkReader, LivingFrontResult>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return default;
                    var v = new LivingFrontResult { Protocol = protocol, Faction = r.ReadString(),
                        RequestId = r.ReadInt32() };
                    byte accepted = r.ReadByte(); v.Accepted = accepted == 1; v.Status = r.ReadString();
                    v.Session = r.ReadInt32(); v.HostEpoch = r.ReadInt32();
                    return accepted > 1 || v.RequestId <= 0 || v.Session <= 0 || v.HostEpoch <= 0 || string.IsNullOrEmpty(v.Faction) ||
                        v.Faction.Length > MaximumFaction || string.IsNullOrEmpty(v.Status) ||
                        v.Status.Length > MaximumBrief ? default : v;
                }));
            Seams.Install<LivingFrontSnapshot>(
                (Action<NetworkWriter, LivingFrontSnapshot>)WriteSnapshot,
                (Func<NetworkReader, LivingFrontSnapshot>)ReadSnapshot);
        }

        private static void WriteSnapshot(NetworkWriter w, LivingFrontSnapshot value)
        {
            w.WriteByte(value.Protocol);
            w.WriteString(NetText.Clip(value.Faction, MaximumFaction));
            w.WriteByte(value.Posture);
            w.WriteByte(value.HasSnapshot ? (byte)1 : (byte)0);
            w.WriteInt32(value.Session); w.WriteInt32(value.HostEpoch);
            int count = Math.Min(value.Fronts?.Length ?? 0, LivingWarRules.MaximumFronts);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                TheaterFrontView front = value.Fronts[i];
                w.WriteString(NetText.Clip(front.Key, MaximumKey));
                w.WriteString(NetText.Clip(front.Label, MaximumLabel));
                w.WriteSingle(front.X); w.WriteSingle(front.Z);
                w.WriteString(NetText.Clip(front.Status, MaximumStatus));
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
                w.WriteString(NetText.Clip(offer.Kind, MaximumStatus));
                w.WriteString(NetText.Clip(offer.Label, MaximumLabel));
                w.WriteString(NetText.Clip(offer.TargetKey, MaximumKey));
                w.WriteSingle(offer.X); w.WriteSingle(offer.Z);
                w.WriteString(NetText.Clip(offer.Brief, MaximumBrief));
                w.WriteString(NetText.Clip(offer.Risk, MaximumStatus));
                w.WriteString(NetText.Clip(offer.Forces, MaximumForces));
                w.WriteSingle(offer.SecondsRemaining);
            }
            TheaterLiveOperationView op = value.Operation;
            w.WriteByte(op == null ? (byte)0 : (byte)1);
            if (op != null)
            {
                w.WriteInt32(op.Id); w.WriteInt32(op.Revision);
                w.WriteString(NetText.Clip(op.Kind, MaximumStatus));
                w.WriteString(NetText.Clip(op.TargetKey, MaximumKey));
                w.WriteString(NetText.Clip(op.Label, MaximumLabel));
                w.WriteSingle(op.X); w.WriteSingle(op.Z);
                w.WriteString(NetText.Clip(op.Phase, MaximumStatus));
                w.WriteString(NetText.Clip(op.Summary, MaximumForces));
                w.WriteByte((byte)Math.Min(Math.Max(op.GroundGroups, 0), 32));
                w.WriteByte((byte)Math.Min(Math.Max(op.AirGroups, 0), 32));
                w.WriteByte((byte)Math.Min(Math.Max(op.NavalGroups, 0), 32));
            }
            count = Math.Min(value.Log?.Length ?? 0, StaffLog.MaximumEntries);
            w.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
                w.WriteString(NetText.Clip(value.Log[i], StaffLog.MaximumTextLength));
        }

        private static LivingFrontSnapshot ReadSnapshot(NetworkReader r)
        {
            var value = new LivingFrontSnapshot { Protocol = r.ReadByte() };
            if (value.Protocol != ProtocolVersion) return value;
            value.Faction = ReadText(r, MaximumFaction);
            value.Posture = r.ReadByte();
            byte hasSnapshot = r.ReadByte();
            if (hasSnapshot > 1 || value.Posture > (byte)TheaterWarPosture.Bold ||
                string.IsNullOrEmpty(value.Faction)) return default;
            value.HasSnapshot = hasSnapshot == 1;
            value.Session = r.ReadInt32(); value.HostEpoch = r.ReadInt32();
            if (value.Session <= 0 || value.HostEpoch <= 0) return default;
            int count = r.ReadByte();
            if (count > LivingWarRules.MaximumFronts) return value;
            value.Fronts = new TheaterFrontView[count];
            for (int i = 0; i < count; i++)
            {
                string key = ReadText(r, MaximumKey);
                string label = ReadText(r, MaximumLabel);
                float x = r.ReadSingle(), z = r.ReadSingle();
                string status = ReadText(r, MaximumStatus);
                float pressure = r.ReadSingle(), trend = r.ReadSingle();
                byte observedValue = r.ReadByte();
                bool observed = observedValue == 1;
                float age = r.ReadSingle();
                if (string.IsNullOrEmpty(key) || label == null || status == null || !float.IsFinite(x) || !float.IsFinite(z) ||
                    !float.IsFinite(pressure) || pressure < 0f || pressure > 1f ||
                    !float.IsFinite(trend) || trend < -1f || trend > 1f ||
                    !float.IsFinite(age) || age < -1f || age > 86400f || observedValue > 1) return default;
                value.Fronts[i] = new TheaterFrontView(key, label, x, z, status,
                    pressure, trend, observed, age);
            }
            count = r.ReadByte();
            if (count > LivingWarRules.MaximumOffers) return default;
            value.Proposals = new TheaterProposalView[count];
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32(), revision = r.ReadInt32();
                string kind = ReadText(r, MaximumStatus);
                string label = ReadText(r, MaximumLabel);
                string key = ReadText(r, MaximumKey);
                float x = r.ReadSingle(), z = r.ReadSingle();
                string brief = ReadText(r, MaximumBrief);
                string risk = ReadText(r, MaximumStatus);
                string forces = ReadText(r, MaximumForces);
                float seconds = r.ReadSingle();
                if (id <= 0 || revision <= 0 || string.IsNullOrEmpty(key) ||
                    label == null || brief == null || risk == null || forces == null ||
                    !float.IsFinite(x) || !float.IsFinite(z) || !ValidKind(kind) ||
                    !float.IsFinite(seconds) || seconds < 0f || seconds > LivingWarRules.OfferSeconds) return default;
                value.Proposals[i] = new TheaterProposalView(id, revision, kind, label,
                    key, x, z, brief, risk, forces, seconds);
            }
            byte hasOperation = r.ReadByte();
            if (hasOperation > 1) return default;
            if (hasOperation == 1)
            {
                int id = r.ReadInt32(), revision = r.ReadInt32();
                string kind = ReadText(r, MaximumStatus);
                string key = ReadText(r, MaximumKey);
                string label = ReadText(r, MaximumLabel);
                float x = r.ReadSingle(), z = r.ReadSingle();
                string phase = ReadText(r, MaximumStatus);
                string summary = ReadText(r, MaximumForces);
                int ground = r.ReadByte(), air = r.ReadByte(), naval = r.ReadByte();
                if (id <= 0 || revision <= 0 || string.IsNullOrEmpty(key) ||
                    label == null || phase == null || summary == null ||
                    !float.IsFinite(x) || !float.IsFinite(z) || !ValidKind(kind) ||
                    ground > 32 || air > 32 || naval > 32) return default;
                value.Operation = new TheaterLiveOperationView(id, revision, kind, key,
                    label, x, z, phase, summary, ground, air, naval);
            }
            count = r.ReadByte();
            if (count > StaffLog.MaximumEntries) return default;
            value.Log = new string[count];
            for (int i = 0; i < count; i++)
            {
                value.Log[i] = ReadText(r, StaffLog.MaximumTextLength);
                if (value.Log[i] == null) return default;
            }
            return value;
        }

        private static bool ValidKind(string kind) => kind == "ASSAULT" || kind == "DEFEND" || kind == "RECON";
    }
}

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
        public byte Code;
        public string Detail;
        public float LockSeconds;
        public int Session, HostEpoch;
    }

    [NetworkMessage]
    internal struct LivingFrontSnapshot
    {
        public byte Protocol;
        public bool HasSnapshot;
        public int Session, HostEpoch;
        public string Faction;
        public TheaterWarSnapshot Body;
    }

    /// <summary>Faction-private, host-authored war picture and validated player intent.</summary>
    internal sealed class LivingFrontNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 3;
        private const int MaximumRecipients = 64;
        private const int MaximumPlayers = 64;
        private const float QueryInterval = 1f;
        private const float IntentInterval = 0.5f;
        private const float IntentTimeout = 8f;
        private const float CommandSeconds = 6f;

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
        private TheaterCommandResult lastCommand;
        private float lastCommandAt, firstQueryAt;
        private static int nextHostEpoch;
        private int hostEpoch = NewHostEpoch(), clientSession = 1, seenHostEpoch;
        private float pendingDeadline;
        private string pendingFaction;
        internal bool CommandPending => pendingRequestId > 0;

        /// <summary>Queries sent since the last accepted snapshot (any snapshot, an empty one included).</summary>
        internal int UnansweredQueries { get; private set; }
        /// <summary>Unscaled seconds since the first of those queries; 0 while none is unanswered.</summary>
        internal float SecondsSinceFirstQuery => UnansweredQueries > 0 ? Time.unscaledTime - firstQueryAt : 0f;

        /// <summary>The host's last answer to this client's command; null once it is older than 6 s.</summary>
        internal TheaterCommandResult LastCommand
        {
            get
            {
                if (lastCommand == null) return null;
                float age = Time.unscaledTime - lastCommandAt;
                if (age > CommandSeconds) return lastCommand = null;
                lastCommand.SecondsAgo = age;
                return lastCommand;
            }
        }

        private void Remember(TheaterCommandCode code, string detail, float lockSeconds)
        {
            lastCommand = new TheaterCommandResult { Code = code, Detail = detail ?? "", LockSeconds = lockSeconds };
            lastCommandAt = Time.unscaledTime;
        }

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
        }

        private void OnDestroy() => ResetScene();

        internal void ClearClientState()
        {
            pendingRequestId = 0; pendingFaction = null; lastCommand = null;
            UnansweredQueries = 0;
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
                Remember(TheaterCommandCode.Unconfirmed, "", 0f);
                owner?.ClearRemoteState();
                nextClientQuery = 0f;
            }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
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
                    if (UnansweredQueries++ == 0) firstQueryAt = now;
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
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            if (client == null || !client.Active) return false;
            pendingRequestId = nextRequestId = nextRequestId >= int.MaxValue ? 1 : nextRequestId + 1;
            pendingFaction = hq.faction.factionName;
            pendingDeadline = Time.unscaledTime + IntentTimeout;
            lastCommand = null;
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
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
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

        // The published body is shared by every recipient; only the header Session differs.
        private LivingFrontSnapshot SnapshotOf(string faction)
        {
            TheaterWarSnapshot body = owner.PublishedFor(faction);
            return new LivingFrontSnapshot
            {
                Protocol = ProtocolVersion,
                Faction = NetText.Clip(faction, LivingFrontWire.MaximumFaction),
                HasSnapshot = body != null,
                HostEpoch = hostEpoch,
                Body = body,
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
                RequestId = intent.RequestId, Session = intent.Session, HostEpoch = hostEpoch,
                Code = (byte)(accepted ? TheaterCommandCode.Accepted : !valid ? TheaterCommandCode.Invalid
                    : !current ? TheaterCommandCode.SessionChanged : !allowed ? TheaterCommandCode.RateLimited
                    : TheaterCommandCode.StaleChoice),
                Detail = "", LockSeconds = 0f,
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
                result.Faction != pendingFaction || result.Faction != hq.faction.factionName) return;
            pendingRequestId = 0;
            Remember((TheaterCommandCode)result.Code, result.Detail, result.LockSeconds);
            nextClientQuery = 0f;
        }

        private void ReceiveSnapshot(INetworkPlayer _, LivingFrontSnapshot snapshot)
        {
            if (snapshot.Protocol != ProtocolVersion || GameAccess.IsServer() || owner == null ||
                snapshot.Session != clientSession || snapshot.HostEpoch <= 0 ||
                snapshot.HostEpoch < seenHostEpoch ||
                snapshot.HasSnapshot && snapshot.Body == null ||
                !GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null ||
                !string.Equals(snapshot.Faction, hq.faction.factionName, StringComparison.Ordinal)) return;
            seenHostEpoch = snapshot.HostEpoch;
            UnansweredQueries = 0;
            if (!snapshot.HasSnapshot) { owner.ClearRemoteState(); nextClientQuery = 0f; return; }
            owner.ApplyRemote(snapshot.Body);
        }

        private static bool ValidIntent(byte kind, int id, int revision, byte posture) =>
            kind <= 2 && (kind == 2
                ? id == 0 && revision == 0 && posture <= (byte)TheaterWarPosture.Bold
                : id > 0 && revision > 0 && posture == 0);


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
                    w.WriteByte(v.Protocol); w.WriteString(NetText.Clip(v.Faction, LivingFrontWire.MaximumFaction));
                    w.WriteInt32(v.RequestId); w.WriteByte(v.Code);
                    w.WriteString(NetText.Clip(v.Detail, LivingFrontWire.MaximumDetail)); w.WriteSingle(v.LockSeconds);
                    w.WriteInt32(v.Session); w.WriteInt32(v.HostEpoch);
                }),
                (Func<NetworkReader, LivingFrontResult>)ReadResult);
            Seams.Install<LivingFrontSnapshot>(
                (Action<NetworkWriter, LivingFrontSnapshot>)WriteSnapshot,
                (Func<NetworkReader, LivingFrontSnapshot>)ReadSnapshot);
        }

        // A reader never throws into Mirage's handler: a violation, a short packet or a foreign protocol reads as default
        // (Protocol 0), which every handler ignores.
        private static LivingFrontResult ReadResult(NetworkReader r)
        {
            try
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return default;
                var v = new LivingFrontResult { Protocol = protocol, Faction = LivingFrontWire.Text(r, LivingFrontWire.MaximumFaction),
                    RequestId = r.ReadInt32(), Code = r.ReadByte() };
                v.Detail = LivingFrontWire.Text(r, LivingFrontWire.MaximumDetail); v.LockSeconds = r.ReadSingle();
                v.Session = r.ReadInt32(); v.HostEpoch = r.ReadInt32();
                return string.IsNullOrEmpty(v.Faction) || v.Detail == null || v.RequestId <= 0 || v.Code > (byte)TheaterCommandCode.NotAllowed ||
                    !(v.LockSeconds >= 0f && v.LockSeconds <= 120f) || v.Session <= 0 || v.HostEpoch <= 0 ? default : v;
            }
            catch (Exception) { return default; }
        }

        private static void WriteSnapshot(NetworkWriter w, LivingFrontSnapshot value)
        {
            w.WriteByte(value.Protocol);
            w.WriteString(NetText.Clip(value.Faction, LivingFrontWire.MaximumFaction));
            bool body = value.HasSnapshot && value.Body != null;
            w.WriteByte(body ? (byte)1 : (byte)0);
            w.WriteInt32(value.Session); w.WriteInt32(value.HostEpoch);
            if (body) LivingFrontWire.WriteBody(w, value.Body);
        }

        private static LivingFrontSnapshot ReadSnapshot(NetworkReader r)
        {
            try
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return default;
                var value = new LivingFrontSnapshot { Protocol = protocol, Faction = LivingFrontWire.Text(r, LivingFrontWire.MaximumFaction) };
                byte hasSnapshot = r.ReadByte();
                value.Session = r.ReadInt32(); value.HostEpoch = r.ReadInt32();
                if (string.IsNullOrEmpty(value.Faction) || hasSnapshot > 1 || value.Session <= 0 || value.HostEpoch <= 0) return default;
                value.HasSnapshot = hasSnapshot == 1;
                if (value.HasSnapshot && !LivingFrontWire.ReadBody(r, value.Faction, out value.Body)) return default;
                return value;
            }
            catch (Exception) { return default; }
        }
    }
}

using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Networking
{
    internal struct GamemodeStatus
    {
        public GameModeKind Mode;
        public GamemodePhase Phase;
        public int Leg, TransportingTeam, Score0, Score1, Winner, Cargo;
        public float Remaining;
        public string Route, Instruction;
    }

    [NetworkMessage]
    internal struct GamemodeQuery
    {
        public byte Protocol;
        public uint Scene, Token, Mission;
        public GameModeKind Mode;
    }

    [NetworkMessage]
    internal struct GamemodeReply
    {
        public byte Protocol;
        public uint Scene, Token, Mission;
        public GamemodeStatus Status;
    }

    /// <summary>Read-only polling. Client request tokens and a confirmed host mission epoch fence late replies across warm restarts.</summary>
    internal sealed class GamemodeNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private const float PollSeconds = 1f, StaleSeconds = 5f;
        private static uint nextMission = unchecked((uint)Environment.TickCount);
        private GamemodeRuntime runtime;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<GamemodeQuery>(ReceiveQuery);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<GamemodeReply>(ReceiveReply);
        private readonly SenderThrottle replies = new SenderThrottle();
        private uint scene, token, hostMission, knownMission, requestedMission;
        private float lastQuery = -10f, lastApplied = -10f, nextRegistration;
        private bool pending, fresh;
        private object observedMission;
        private GameModeKind observedMode;
        private NetworkClient requestedClient;
        private INetworkPlayer requestedPeer;
        private FactionHQ requestedHq;

        public void Configure(GamemodeRuntime owner)
        {
            runtime = owner;
            InstallSerializers();
            ResetScene();
        }

        public void ResetScene()
        {
            scene++;
            if (++nextMission == 0) nextMission++;
            hostMission = nextMission;
            knownMission = requestedMission = 0;
            pending = fresh = false;
            requestedClient = null;
            requestedPeer = null;
            requestedHq = null;
            observedMission = MissionManager.CurrentMission;
            observedMode = GamemodeRuntime.Kind;
            lastQuery = lastApplied = -10f;
            replies.Clear();
            runtime?.Apply(default);
        }

        private void Update()
        {
            if (runtime == null) return;
            float now = Time.unscaledTime;
            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            if (now >= nextRegistration)
            {
                nextRegistration = now + 0.5f;
                ServerHandlers.Swap(network?.Server?.Active == true ? network.Server.MessageHandler : null);
                ClientHandlers.Swap(network?.Client?.Active == true ? network.Client.MessageHandler : null);
                replies.Prune(now);
            }
            GameModeKind mode = GamemodeRuntime.Kind;
            if (!ReferenceEquals(observedMission, MissionManager.CurrentMission) || observedMode != mode) ResetScene();
            if (mode == GameModeKind.None)
            {
                if (fresh) Clear();
                return;
            }
            NetworkClient client = network?.Client;
            GameManager.GetLocalPlayer<Player>(out Player player);
            bool changed = requestedClient != null && (!ReferenceEquals(client, requestedClient) || !requestedClient.Active ||
                !requestedClient.IsConnected || !ReferenceEquals(requestedClient.Player, requestedPeer) || player?.HQ != requestedHq);
            if (changed) ResetScene();
            if (fresh && now - lastApplied >= StaleSeconds) Clear();
            if (pending && now - lastQuery >= 2f) pending = false;
            if (!pending && now - lastQuery >= PollSeconds) Request(player);
        }

        private void Request(Player player)
        {
            lastQuery = Time.unscaledTime;
            if (GameAccess.IsServer())
            {
                runtime.Apply(runtime.Snapshot(player));
                fresh = true;
                lastApplied = lastQuery;
                return;
            }
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            if (client == null || !client.Active || !client.IsConnected || client.Player == null || !client.Player.IsAuthenticated) return;
            requestedClient = client;
            requestedPeer = client.Player;
            requestedHq = player?.HQ;
            requestedMission = knownMission;
            pending = true;
            client.Send(new GamemodeQuery
            {
                Protocol = ProtocolVersion, Scene = scene, Token = ++token, Mission = requestedMission, Mode = GamemodeRuntime.Kind
            });
        }

        private void ReceiveQuery(INetworkPlayer sender, GamemodeQuery query)
        {
            if (runtime == null || !GameAccess.IsServer() || query.Protocol != ProtocolVersion ||
                query.Mode == GameModeKind.None || query.Mode != GamemodeRuntime.Kind || sender == null ||
                !sender.IsAuthenticated || !sender.IsConnected || !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !replies.Allow(PlayerIdentity.Of(player), Time.unscaledTime, 0.75f)) return;
            // Always return the live epoch; an unfamiliar one is a handshake only and cannot be displayed.
            sender.Send(new GamemodeReply
            {
                Protocol = ProtocolVersion, Scene = query.Scene, Token = query.Token, Mission = hostMission,
                Status = runtime.Snapshot(player)
            });
        }

        private void ReceiveReply(INetworkPlayer sender, GamemodeReply reply)
        {
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            GameManager.GetLocalPlayer<Player>(out Player player);
            if (runtime == null || GameAccess.IsServer() || !pending || !ReferenceEquals(observedMission, MissionManager.CurrentMission) || reply.Protocol != ProtocolVersion ||
                reply.Scene != scene || reply.Token != token || reply.Mission == 0 ||
                reply.Status.Mode != GamemodeRuntime.Kind || client == null || !client.Active || !client.IsConnected ||
                !ReferenceEquals(client, requestedClient) || sender == null || !sender.IsAuthenticated || !sender.IsConnected ||
                !ReferenceEquals(sender, requestedPeer) || !ReferenceEquals(sender, client.Player) || player?.HQ != requestedHq) return;
            pending = false;
            if (reply.Mission != requestedMission)
            {
                knownMission = reply.Mission;
                Clear();
                return;
            }
            knownMission = reply.Mission;
            runtime.Apply(reply.Status);
            lastApplied = Time.unscaledTime;
            fresh = true;
        }

        private void Clear()
        {
            fresh = false;
            runtime?.Apply(default);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            ResetScene();
        }

        private static void InstallSerializers()
        {
            MirageSerializers.Strict.Install<GamemodeQuery>((Action<NetworkWriter, GamemodeQuery>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
                w.WritePackedUInt32(v.Mission); w.WriteByte((byte)v.Mode);
            }), (Func<NetworkReader, GamemodeQuery>)(r => new GamemodeQuery
            {
                Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(),
                Mission = r.ReadPackedUInt32(), Mode = (GameModeKind)r.ReadByte()
            }));
            MirageSerializers.Strict.Install<GamemodeReply>((Action<NetworkWriter, GamemodeReply>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token); w.WritePackedUInt32(v.Mission);
                GamemodeStatus s = v.Status;
                w.WriteByte((byte)s.Mode); w.WriteByte((byte)s.Phase); w.WritePackedInt32(s.Leg);
                w.WritePackedInt32(s.TransportingTeam); w.WritePackedInt32(s.Score0); w.WritePackedInt32(s.Score1);
                w.WritePackedInt32(s.Winner); w.WritePackedInt32(s.Cargo); w.WriteSingle(s.Remaining);
                WriteText(w, s.Route, 160); WriteText(w, s.Instruction, 256);
            }), (Func<NetworkReader, GamemodeReply>)(r =>
            {
                var v = new GamemodeReply
                {
                    Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(), Mission = r.ReadPackedUInt32()
                };
                if (v.Protocol != ProtocolVersion) throw new InvalidOperationException("Unsupported gamemode protocol.");
                v.Status = new GamemodeStatus
                {
                    Mode = (GameModeKind)r.ReadByte(), Phase = (GamemodePhase)r.ReadByte(), Leg = r.ReadPackedInt32(),
                    TransportingTeam = r.ReadPackedInt32(), Score0 = r.ReadPackedInt32(), Score1 = r.ReadPackedInt32(),
                    Winner = r.ReadPackedInt32(), Cargo = r.ReadPackedInt32(), Remaining = r.ReadSingle(),
                    Route = ReadText(r, 160), Instruction = ReadText(r, 256)
                };
                GamemodeStatus s = v.Status;
                if (s.Mode < GameModeKind.None || s.Mode > GameModeKind.Heavyweight ||
                    s.Phase < GamemodePhase.Running || s.Phase > GamemodePhase.Finished ||
                    s.Leg < 0 || s.Leg > 2 || s.TransportingTeam < -1 || s.TransportingTeam > 1 ||
                    s.Winner < -1 || s.Winner > 1 || s.Score0 < 0 || s.Score1 < 0 || s.Cargo < 0 ||
                    !float.IsFinite(s.Remaining) || s.Remaining < 0f || s.Remaining > GamemodeRules.FuzzballSeconds)
                    throw new InvalidOperationException("Invalid gamemode snapshot.");
                return v;
            }));
        }

        // Length checked before allocation; unlike ReadString followed by clipping, oversized host data never allocates a large string.
        private static void WriteText(NetworkWriter writer, string value, int limit)
        {
            string text = NetText.Clip(value, limit);
            writer.WritePackedUInt32((uint)text.Length);
            foreach (char c in text) writer.WritePackedUInt32(c);
        }

        private static string ReadText(NetworkReader reader, int limit)
        {
            uint count = reader.ReadPackedUInt32();
            if (count > limit) throw new InvalidOperationException("Gamemode text exceeds its wire limit.");
            var chars = new char[(int)count];
            for (int i = 0; i < chars.Length; i++)
            {
                uint code = reader.ReadPackedUInt32();
                if (code > char.MaxValue) throw new InvalidOperationException("Invalid gamemode text character.");
                chars[i] = (char)code;
            }
            return new string(chars);
        }
    }
}
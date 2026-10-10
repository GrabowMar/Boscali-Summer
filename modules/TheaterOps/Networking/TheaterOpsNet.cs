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
    /// <summary>Client intent: send me the host's theatre priorities. Answered privately.</summary>
    [NetworkMessage]
    internal struct TheaterPriorityQuery
    {
        public byte Protocol;
    }

    /// <summary>
    /// One faction's main effort, or its clear (Active 0). The host sends one per change and
    /// answers a query with one per set faction, so the wire shape carries no list to grow.
    /// </summary>
    [NetworkMessage]
    internal struct TheaterPriorityState
    {
        public byte Protocol;
        public byte Active;
        public string Faction;
        public string Key;
        public string Label;
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>
    /// Replicates the host's theater main effort read-only to the clients of its faction.
    /// </summary>
    internal sealed class TheaterOpsNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 3;

        private const int MaximumEntries = 8;
        private const int MaximumFactionLength = TheaterPriorityService.MaximumFactionLength;
        private const int MaximumKeyLength = PriorityDirective.MaximumKeyLength;
        private const int MaximumLabelLength = PriorityDirective.MaximumLabelLength;
        private const int MaximumQueries = 64;
        private const float QueryInterval = 1f;
        private const float ClientQueryInterval = 4f;

        private TheaterPriorityService service;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<TheaterPriorityQuery>(ReceiveQuery);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<TheaterPriorityState>(ReceiveState);
        private readonly SenderThrottle nextQuery = new SenderThrottle(MaximumQueries);
        private readonly List<KeyValuePair<string, PriorityDirective>> buffer =
            new List<KeyValuePair<string, PriorityDirective>>(MaximumEntries);

        private float nextRegistration, lastClientQuery, nextFactionCheck;
        private bool queried;
        private FactionHQ queriedHq;

        public void Configure(TheaterPriorityService owner)
        {
            service = owner;
            InstallSerializers();
        }

        /// <summary>Drops the transport and asks again on the next scene.</summary>
        public void ResetScene()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            queried = false;
            queriedHq = null;
            nextFactionCheck = 0f;
            nextRegistration = 0f;
            lastClientQuery = -10f;
            nextQuery.Clear();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;

            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;

            ServerHandlers.Swap(server);
            if (ClientHandlers.Swap(client)) queried = false;

            // The host answers only the asker's own faction, so a client that had no faction yet
            // (the usual state at connect) or switched sides asks again once it has one.
            if (ClientHandlers.Current != null && queried && !GameAccess.IsServer() && now >= nextFactionCheck)
            {
                nextFactionCheck = now + 1f;
                GameManager.GetLocalHQ(out FactionHQ localHq);
                if (!ReferenceEquals(localHq, queriedHq)) queried = false;
            }

            // One query per connection and faction. Silence is a valid answer (the host has
            // nothing set), so there is no retry storm; a reconnect re-registers the client
            // handler and asks again, and a change after that is pushed to the faction.
            if (ClientHandlers.Current != null && !queried && !GameAccess.IsServer() &&
                now - lastClientQuery >= ClientQueryInterval)
            {
                NetworkClient transport = GameAccess.NetworkManagerOrNull?.Client;
                if (transport != null && transport.Active)
                {
                    queried = true;
                    lastClientQuery = now;
                    GameManager.GetLocalHQ(out queriedHq);
                    transport.Send(new TheaterPriorityQuery { Protocol = ProtocolVersion });
                }
            }

            nextQuery.Prune(now);
        }

        private const int MaximumRecipients = 64;

        /// <summary>
        /// A faction's main effort goes only to that faction's own players: an opposing client
        /// never receives the other side's effort. Bounded and allocation-free.
        /// </summary>
        private static void SendToFaction<T>(NetworkServer server, string faction, T message)
        {
            IReadOnlyList<INetworkPlayer> players = server.AuthenticatedPlayers;
            int count = Math.Min(players.Count, MaximumRecipients);
            for (int i = 0; i < count; i++)
            {
                INetworkPlayer connection = players[i];
                if (connection == null || ReferenceEquals(connection, server.LocalPlayer) ||
                    !connection.TryGetPlayer<Player>(out Player player) || player == null ||
                    player.HQ == null || player.HQ.faction == null ||
                    !string.Equals(player.HQ.faction.factionName, faction, StringComparison.Ordinal))
                    continue;
                connection.Send(message);
            }
        }

        /// <summary>Host broadcast: one faction set, replaced or cleared.</summary>
        internal void BroadcastState(string faction, PriorityDirective? directive)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;
            SendToFaction(server, faction, StateOf(faction, directive));
        }

        private void ReceiveQuery(INetworkPlayer sender, TheaterPriorityQuery query)
        {
            if (!GameAccess.IsServer() || query.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !nextQuery.Allow(PlayerIdentity.Of(player), Time.unscaledTime, QueryInterval))
                return;

            // The effort is the querying player's own faction's business; another side's is
            // never answered.
            if (player.HQ != null && player.HQ.faction != null)
            {
                string own = player.HQ.faction.factionName;
                int count = service.CopyDirectives(buffer);
                for (int i = 0; i < count; i++)
                    if (string.Equals(buffer[i].Key, own, StringComparison.Ordinal))
                        sender.Send(StateOf(buffer[i].Key, buffer[i].Value));
            }
        }

        private void ReceiveState(INetworkPlayer _, TheaterPriorityState state)
        {
            if (GameAccess.IsServer() || state.Protocol != ProtocolVersion) return;

            string faction = NetText.Clip(state.Faction, MaximumFactionLength);
            if (string.IsNullOrEmpty(faction)) return;

            if (state.Active == 0)
            {
                service.ApplyRemoteClear(faction);
                return;
            }

            service.ApplyRemote(
                faction, NetText.Clip(state.Key, MaximumKeyLength), NetText.Clip(state.Label, MaximumLabelLength),
                state.X, state.Y, state.Z);
        }

        private static TheaterPriorityState StateOf(string faction, PriorityDirective? directive)
        {
            if (!directive.HasValue)
                return new TheaterPriorityState { Protocol = ProtocolVersion, Faction = NetText.Clip(faction, MaximumFactionLength) };

            PriorityDirective value = directive.Value;
            return new TheaterPriorityState
            {
                Protocol = ProtocolVersion,
                Active = 1,
                Faction = NetText.Clip(faction, MaximumFactionLength),
                Key = NetText.Clip(value.Key, MaximumKeyLength),
                Label = NetText.Clip(value.Label, MaximumLabelLength),
                X = value.X,
                Y = value.Y,
                Z = value.Z,
            };
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }

        private static readonly MirageSerializers Seams = new MirageSerializers(
            "[TheaterOps]", " is missing; theater priorities cannot replicate on this game build.");

        private static void InstallSerializers()
        {
            Seams.Install<TheaterPriorityQuery>((Action<NetworkWriter, TheaterPriorityQuery>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
            }),
            (Func<NetworkReader, TheaterPriorityQuery>)(r =>
            {
                byte protocol = r.ReadByte();
                return new TheaterPriorityQuery { Protocol = protocol };
            }));

            Seams.Install<TheaterPriorityState>((Action<NetworkWriter, TheaterPriorityState>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte(v.Active);
                w.WriteString(NetText.Clip(v.Faction, MaximumFactionLength));
                if (v.Active == 0) return;
                w.WriteString(NetText.Clip(v.Key, MaximumKeyLength));
                w.WriteString(NetText.Clip(v.Label, MaximumLabelLength));
                w.WriteSingle(v.X);
                w.WriteSingle(v.Y);
                w.WriteSingle(v.Z);
            }),
            (Func<NetworkReader, TheaterPriorityState>)(r =>
            {
                byte protocol = r.ReadByte();
                var state = new TheaterPriorityState { Protocol = protocol };
                if (protocol != ProtocolVersion) return state;

                state.Active = r.ReadByte();
                state.Faction = r.ReadString();
                if (state.Active == 0) return state;
                state.Key = r.ReadString();
                state.Label = r.ReadString();
                state.X = r.ReadSingle();
                state.Y = r.ReadSingle();
                state.Z = r.ReadSingle();
                return state;
            }));

        }
    }
}

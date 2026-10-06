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
        private MessageHandler serverHandler, clientHandler;
        private readonly Dictionary<ulong, float> nextQuery = new Dictionary<ulong, float>(16);
        private readonly List<ulong> expired = new List<ulong>(16);
        private readonly List<KeyValuePair<string, PriorityDirective>> buffer =
            new List<KeyValuePair<string, PriorityDirective>>(MaximumEntries);

        private float nextRegistration, nextPrune, lastClientQuery, nextFactionCheck;
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
            serverHandler?.UnregisterHandler<TheaterPriorityQuery>();
            clientHandler?.UnregisterHandler<TheaterPriorityState>();
            serverHandler = null;
            clientHandler = null;
            queried = false;
            queriedHq = null;
            nextFactionCheck = 0f;
            nextRegistration = 0f;
            lastClientQuery = -10f;
            nextQuery.Clear();
            expired.Clear();
        }

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
                serverHandler?.UnregisterHandler<TheaterPriorityQuery>();
                serverHandler = server;
                serverHandler?.RegisterHandler<TheaterPriorityQuery>(ReceiveQuery, false);
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<TheaterPriorityState>();
                clientHandler = client;
                queried = false;
                clientHandler?.RegisterHandler<TheaterPriorityState>(ReceiveState, false);
            }

            // The host answers only the asker's own faction, so a client that had no faction yet
            // (the usual state at connect) or switched sides asks again once it has one.
            if (clientHandler != null && queried && !GameAccess.IsServer() && now >= nextFactionCheck)
            {
                nextFactionCheck = now + 1f;
                GameManager.GetLocalHQ(out FactionHQ localHq);
                if (!ReferenceEquals(localHq, queriedHq)) queried = false;
            }

            // One query per connection and faction. Silence is a valid answer (the host has
            // nothing set), so there is no retry storm; a reconnect re-registers the client
            // handler and asks again, and a change after that is pushed to the faction.
            if (clientHandler != null && !queried && !GameAccess.IsServer() &&
                now - lastClientQuery >= ClientQueryInterval)
            {
                NetworkClient transport = NetworkManagerNuclearOption.i?.Client;
                if (transport != null && transport.Active)
                {
                    queried = true;
                    lastClientQuery = now;
                    GameManager.GetLocalHQ(out queriedHq);
                    transport.Send(new TheaterPriorityQuery { Protocol = ProtocolVersion });
                }
            }

            if (now < nextPrune) return;
            nextPrune = now + 10f;
            expired.Clear();
            foreach (KeyValuePair<ulong, float> pair in nextQuery)
                if (now - pair.Value > 30f) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) nextQuery.Remove(expired[i]);
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
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            SendToFaction(server, faction, StateOf(faction, directive));
        }

        private void ReceiveQuery(INetworkPlayer sender, TheaterPriorityQuery query)
        {
            if (!GameAccess.IsServer() || query.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !RateLimit(player))
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

            string faction = Text(state.Faction, MaximumFactionLength);
            if (string.IsNullOrEmpty(faction)) return;

            if (state.Active == 0)
            {
                service.ApplyRemoteClear(faction);
                return;
            }

            service.ApplyRemote(
                faction, Text(state.Key, MaximumKeyLength), Text(state.Label, MaximumLabelLength),
                state.X, state.Y, state.Z);
        }

        private static TheaterPriorityState StateOf(string faction, PriorityDirective? directive)
        {
            if (!directive.HasValue)
                return new TheaterPriorityState { Protocol = ProtocolVersion, Faction = Text(faction, MaximumFactionLength) };

            PriorityDirective value = directive.Value;
            return new TheaterPriorityState
            {
                Protocol = ProtocolVersion,
                Active = 1,
                Faction = Text(faction, MaximumFactionLength),
                Key = Text(value.Key, MaximumKeyLength),
                Label = Text(value.Label, MaximumLabelLength),
                X = value.X,
                Y = value.Y,
                Z = value.Z,
            };
        }

        private bool RateLimit(Player player)
        {
            float now = Time.unscaledTime;
            ulong id = PlayerIdentity.Of(player);
            if (nextQuery.TryGetValue(id, out float next) && now < next) return false;
            if (!nextQuery.ContainsKey(id) && nextQuery.Count >= MaximumQueries) return false;
            nextQuery[id] = now + QueryInterval;
            return true;
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<TheaterPriorityQuery>();
            clientHandler?.UnregisterHandler<TheaterPriorityState>();
        }

        internal static string Text(string value, int max) => string.IsNullOrEmpty(value) ? ""
            : value.Length > max ? value.Substring(0, max) : value;

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<TheaterPriorityQuery>), "Write", (Action<NetworkWriter, TheaterPriorityQuery>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
            }));
            Bind(typeof(Reader<TheaterPriorityQuery>), "Read", (Func<NetworkReader, TheaterPriorityQuery>)(r =>
            {
                byte protocol = r.ReadByte();
                return new TheaterPriorityQuery { Protocol = protocol };
            }));

            Bind(typeof(Writer<TheaterPriorityState>), "Write", (Action<NetworkWriter, TheaterPriorityState>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte(v.Active);
                w.WriteString(Text(v.Faction, MaximumFactionLength));
                if (v.Active == 0) return;
                w.WriteString(Text(v.Key, MaximumKeyLength));
                w.WriteString(Text(v.Label, MaximumLabelLength));
                w.WriteSingle(v.X);
                w.WriteSingle(v.Y);
                w.WriteSingle(v.Z);
            }));
            Bind(typeof(Reader<TheaterPriorityState>), "Read", (Func<NetworkReader, TheaterPriorityState>)(r =>
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

            MessagePacker.RegisterMessage<TheaterPriorityQuery>();
            MessagePacker.RegisterMessage<TheaterPriorityState>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[TheaterOps] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; theater priorities cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}

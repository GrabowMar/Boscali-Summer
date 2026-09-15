using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.Events.Domain;
using BoscaliSummer.Features.Events.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Events.Networking
{
    /// <summary>
    /// Host-to-client event state, plus the one client intent: a response. State carries
    /// only the catalog index and mission timestamps (title and flavor text are looked up
    /// locally, like the chain of command never ships generated text). Index -1 = calm.
    /// </summary>
    [NetworkMessage]
    internal struct ActiveEventChanged
    {
        public byte Protocol;
        public sbyte CatalogIndex;
        public float StartedAtMissionTime;
        public float EndsAtMissionTime;
    }

    /// <summary>Client intent: respond to the active event, or ask what this player already bought.</summary>
    [NetworkMessage]
    internal struct EventIntent
    {
        public byte Protocol;
        public uint Token;
        public byte Action;
        public sbyte CatalogIndex;
    }

    /// <summary>The host's answer to one intent, directed to the requester.</summary>
    [NetworkMessage]
    internal struct EventReply
    {
        public byte Protocol;
        public uint Token;
        public sbyte CatalogIndex;
        public byte Result;
        public byte Kind;
        public int Cost;
    }

    internal sealed class EventsNet : MonoBehaviour
    {
        /// <summary>Version 2 adds the response intent and reply.</summary>
        internal const byte ProtocolVersion = 2;

        internal const byte ActionRespond = 0;
        internal const byte ActionQuery = 1;

        private const float ReplyTimeout = 5f;
        private const float SendInterval = 0.5f;
        private const float IntentInterval = 1f;
        private const int MaximumIntents = 64;

        private EventsManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private readonly Dictionary<ulong, float> nextIntent = new Dictionary<ulong, float>(16);
        private readonly List<ulong> expired = new List<ulong>(16);
        private float nextRegistration;
        private float nextSend;
        private float nextPrune;
        private float pendingSince;
        private uint token;
        private bool pending;

        public void Configure(EventsManager owner)
        {
            manager = owner;
            InstallSerializers();
        }

        /// <summary>Nothing is scoped to a scene, but a reset still drops a stale transport.</summary>
        public void ResetScene()
        {
            serverHandler?.UnregisterHandler<EventIntent>();
            clientHandler?.UnregisterHandler<EventReply>();
            clientHandler?.UnregisterHandler<ActiveEventChanged>();
            serverHandler = null;
            clientHandler = null;
            nextRegistration = 0f;
            pending = false;
            nextIntent.Clear();
            expired.Clear();
        }

        private void Update()
        {
            if (pending && Time.unscaledTime - pendingSince > ReplyTimeout)
            {
                pending = false;
                manager.ReportOffline();
            }

            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.MessageHandler;
            if (server != serverHandler)
            {
                serverHandler?.UnregisterHandler<EventIntent>();
                serverHandler = server;
                serverHandler?.RegisterHandler<EventIntent>(ReceiveIntent, false);
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<EventReply>();
                clientHandler?.UnregisterHandler<ActiveEventChanged>();
                clientHandler = client;
                clientHandler?.RegisterHandler<EventReply>(ReceiveReply, false);
                clientHandler?.RegisterHandler<ActiveEventChanged>(ReceiveChanged, false);
            }
        }

        public void Broadcast(sbyte catalogIndex, float start, float end)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new ActiveEventChanged
            {
                Protocol = ProtocolVersion,
                CatalogIndex = catalogIndex,
                StartedAtMissionTime = start,
                EndsAtMissionTime = end,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        internal void RequestResponse(sbyte catalogIndex) => Send(ActionRespond, catalogIndex);

        internal void QueryState(sbyte catalogIndex) => Send(ActionQuery, catalogIndex);

        private void Send(byte action, sbyte catalogIndex)
        {
            if (GameAccess.IsServer())
            {
                // The host owns the state; resolve its own request in-process.
                if (action != ActionRespond) return;
                if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null) return;
                EventResponseKind kind;
                int cost;
                EventResponseResult result = manager.Act(local, action, catalogIndex, out kind, out cost);
                manager.ApplyResponseResult((byte)result, (byte)kind, catalogIndex, cost);
                return;
            }

            if (Time.unscaledTime < nextSend || pending) return;
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }

            nextSend = Time.unscaledTime + SendInterval;
            pending = true;
            pendingSince = Time.unscaledTime;
            client.Send(new EventIntent
            {
                Protocol = ProtocolVersion,
                Token = ++token,
                Action = action,
                CatalogIndex = catalogIndex,
            });
        }

        private void ReceiveIntent(INetworkPlayer sender, EventIntent intent)
        {
            if (!GameAccess.IsServer() || intent.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || intent.Action > ActionQuery ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !RateLimit(player))
                return;

            EventResponseKind kind;
            int cost;
            EventResponseResult result = manager.Act(player, intent.Action, intent.CatalogIndex, out kind, out cost);
            sender.Send(new EventReply
            {
                Protocol = ProtocolVersion,
                Token = intent.Token,
                CatalogIndex = intent.CatalogIndex,
                Result = (byte)result,
                Kind = (byte)kind,
                Cost = cost,
            });
        }

        private void ReceiveReply(INetworkPlayer _, EventReply reply)
        {
            if (GameAccess.IsServer() || reply.Protocol != ProtocolVersion ||
                !pending || reply.Token != token)
                return;
            pending = false;
            manager.ApplyResponseResult(reply.Result, reply.Kind, reply.CatalogIndex, reply.Cost);
        }

        private void ReceiveChanged(INetworkPlayer _, ActiveEventChanged message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion) return;
            manager.ApplyRemote(message.CatalogIndex, message.StartedAtMissionTime, message.EndsAtMissionTime);
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<EventIntent>();
            clientHandler?.UnregisterHandler<EventReply>();
            clientHandler?.UnregisterHandler<ActiveEventChanged>();
        }

        /// <summary>Bounded per-player throttle; a query answer is cheap but not free.</summary>
        private bool RateLimit(Player player)
        {
            float now = Time.unscaledTime;
            if (now >= nextPrune)
            {
                nextPrune = now + 10f;
                expired.Clear();
                foreach (var pair in nextIntent)
                    if (now - pair.Value > 30f) expired.Add(pair.Key);
                for (int i = 0; i < expired.Count; i++) nextIntent.Remove(expired[i]);
            }

            ulong id = PlayerIdentity.Of(player);
            if (nextIntent.TryGetValue(id, out float next) && now < next) return false;
            if (!nextIntent.ContainsKey(id) && nextIntent.Count >= MaximumIntents) return false;
            nextIntent[id] = now + IntentInterval;
            return true;
        }

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<ActiveEventChanged>), "Write", (Action<NetworkWriter, ActiveEventChanged>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte((byte)v.CatalogIndex);
                w.WriteSingle(v.StartedAtMissionTime);
                w.WriteSingle(v.EndsAtMissionTime);
            }));
            Bind(typeof(Reader<ActiveEventChanged>), "Read", (Func<NetworkReader, ActiveEventChanged>)(r =>
            {
                byte protocol = r.ReadByte();
                var message = new ActiveEventChanged { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;
                message.CatalogIndex = unchecked((sbyte)r.ReadByte());
                message.StartedAtMissionTime = r.ReadSingle();
                message.EndsAtMissionTime = r.ReadSingle();
                return message;
            }));

            Bind(typeof(Writer<EventIntent>), "Write", (Action<NetworkWriter, EventIntent>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedUInt32(v.Token);
                w.WriteByte(v.Action);
                w.WriteByte((byte)v.CatalogIndex);
            }));
            Bind(typeof(Reader<EventIntent>), "Read", (Func<NetworkReader, EventIntent>)(r =>
            {
                byte protocol = r.ReadByte();
                var message = new EventIntent { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;
                message.Token = r.ReadPackedUInt32();
                message.Action = r.ReadByte();
                message.CatalogIndex = unchecked((sbyte)r.ReadByte());
                return message;
            }));

            Bind(typeof(Writer<EventReply>), "Write", (Action<NetworkWriter, EventReply>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedUInt32(v.Token);
                w.WriteByte((byte)v.CatalogIndex);
                w.WriteByte(v.Result);
                w.WriteByte(v.Kind);
                w.WritePackedInt32(v.Cost);
            }));
            Bind(typeof(Reader<EventReply>), "Read", (Func<NetworkReader, EventReply>)(r =>
            {
                byte protocol = r.ReadByte();
                var message = new EventReply { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;
                message.Token = r.ReadPackedUInt32();
                message.CatalogIndex = unchecked((sbyte)r.ReadByte());
                message.Result = r.ReadByte();
                message.Kind = r.ReadByte();
                message.Cost = r.ReadPackedInt32();
                return message;
            }));

            MessagePacker.RegisterMessage<ActiveEventChanged>();
            MessagePacker.RegisterMessage<EventIntent>();
            MessagePacker.RegisterMessage<EventReply>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[Events] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; world events cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Comms.Networking
{
    /// <summary>A peer's request to the host. Mirrors <see cref="CommsIntent"/>.</summary>
    [NetworkMessage]
    internal struct CommsUpMessage
    {
        public byte Protocol;
        public byte Op;
        public byte Channel;
        public byte Kind;
        public byte Style;
        public byte Size;
        public uint Target;
        public int[] Points;
        public string Text;
        public string[] Items;
    }

    /// <summary>A host message to one peer. Mirrors <see cref="CommsEnvelope"/>.</summary>
    [NetworkMessage]
    internal struct CommsDownMessage
    {
        public byte Protocol;
        public byte Event;
        public uint Id;
        public ulong Author;
        public string AuthorName;
        public int Faction;
        public byte Channel;
        public byte Kind;
        public byte Style;
        public byte Size;
        public byte Flags;
        public float Ttl;
        public int[] Points;
        public string Text;
        public string[] Items;
        public int[] Values;
        public ulong[] Players;
        public uint[] Ids;
    }

    /// <summary>
    /// The COMMS transport: one message up, one message down, both over the game's own Mirage
    /// connection, so there is no second socket, relay or Steam channel to configure (unlike a
    /// voice mod, COMMS carries a few hundred bytes at a time).
    ///
    /// <para>The host only ever sends to peers that have spoken to it — every modded client
    /// asks for a snapshot as soon as it has a side — so a player without the mod is never sent
    /// a message it cannot read. Routing resolves each peer's side from the host's own
    /// <see cref="Player"/> object at send time, which is what keeps team traffic on its team
    /// even after someone switches sides.</para>
    /// </summary>
    internal sealed class CommsNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = CommsWire.ProtocolVersion;

        private const int MaxRoster = 64;

        private CommsManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;

        private readonly Dictionary<ulong, INetworkPlayer> roster = new Dictionary<ulong, INetworkPlayer>();
        private readonly List<ulong> dropped = new List<ulong>();

        public void Configure(CommsManager owner)
        {
            manager = owner;
            InstallSerializers();
        }

        /// <summary>Whether a client transport is up, so a request has somewhere to go.</summary>
        public bool ClientActive
        {
            get
            {
                try
                {
                    NetworkClient client = NetworkManagerNuclearOption.i?.Client;
                    return client != null && client.Active;
                }
                catch { return false; }
            }
        }

        public void ResetScene()
        {
            Unregister();
            roster.Clear();
            nextRegistration = 0f;
        }

        private void OnDestroy() => Unregister();

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;

            NetworkManagerNuclearOption network;
            try { network = NetworkManagerNuclearOption.i; }
            catch { return; }

            MessageHandler server = network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.MessageHandler;
            if (server != serverHandler)
            {
                serverHandler?.UnregisterHandler<CommsUpMessage>();
                serverHandler = server;
                serverHandler?.RegisterHandler<CommsUpMessage>(ReceiveUp, false);
                roster.Clear();
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<CommsDownMessage>();
                clientHandler = client;
                clientHandler?.RegisterHandler<CommsDownMessage>(ReceiveDown, false);
                if (clientHandler != null) manager?.OnTransportChanged();
            }
        }

        // ---- Send --------------------------------------------------------------------------

        /// <summary>Hand a request to the host. False when there is no client transport.</summary>
        public bool SendUp(CommsIntent intent)
        {
            NetworkClient client;
            try { client = NetworkManagerNuclearOption.i?.Client; }
            catch { return false; }
            if (client == null || !client.Active) return false;
            client.Send(new CommsUpMessage
            {
                Protocol = ProtocolVersion,
                Op = (byte)intent.Op,
                Channel = (byte)intent.Channel,
                Kind = intent.Kind,
                Style = intent.Style,
                Size = intent.Size,
                Target = intent.Target,
                Points = intent.Points,
                Text = intent.Text,
                Items = intent.Items,
            });
            return true;
        }

        /// <summary>
        /// Host: deliver one routed message to every remembered peer it reaches. The host's own
        /// player is not in the roster; the manager applies its copy in-process.
        /// </summary>
        public void Route(CommsOutbound outbound)
        {
            if (!GameAccess.IsServer() || roster.Count == 0) return;
            CommsDownMessage message = ToMessage(outbound.Envelope);
            dropped.Clear();
            foreach (KeyValuePair<ulong, INetworkPlayer> pair in roster)
            {
                INetworkPlayer peer = pair.Value;
                if (peer == null || !peer.IsAuthenticated)
                {
                    dropped.Add(pair.Key);
                    continue;
                }
                if (outbound.Route != CommsRoute.All)
                {
                    if (!peer.TryGetPlayer<Player>(out Player player) || player == null) continue;
                    if (!outbound.Reaches(CommsIdentity.Id(player), CommsIdentity.Faction(player))) continue;
                }
                try
                {
                    peer.Send(message);
                }
                catch (Exception)
                {
                    // A peer that left between frames: forget it rather than throw every send.
                    dropped.Add(pair.Key);
                }
            }
            for (int i = 0; i < dropped.Count; i++) roster.Remove(dropped[i]);
        }

        // ---- Receive -----------------------------------------------------------------------

        private void ReceiveUp(INetworkPlayer sender, CommsUpMessage message)
        {
            if (!GameAccess.IsServer() || message.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;

            ulong id = CommsIdentity.Id(player);
            if (!roster.ContainsKey(id) && roster.Count >= MaxRoster) return;
            roster[id] = sender;

            manager?.HandleRemote(player, new CommsIntent
            {
                Op = (CommsOp)message.Op,
                Channel = (CommsChannel)message.Channel,
                Kind = message.Kind,
                Style = message.Style,
                Size = message.Size,
                Target = message.Target,
                Points = message.Points,
                Text = message.Text,
                Items = message.Items,
            });
        }

        private void ReceiveDown(INetworkPlayer _, CommsDownMessage message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion) return;
            manager?.ApplyRemote(new CommsEnvelope
            {
                Event = (CommsEvent)message.Event,
                Id = message.Id,
                Author = message.Author,
                AuthorName = message.AuthorName,
                Faction = message.Faction,
                Channel = (CommsChannel)message.Channel,
                Kind = message.Kind,
                Style = message.Style,
                Size = message.Size,
                Flags = message.Flags,
                Ttl = message.Ttl,
                Points = message.Points,
                Text = message.Text,
                Items = message.Items,
                Values = message.Values,
                Players = message.Players,
                Ids = message.Ids,
            });
        }

        private void Unregister()
        {
            serverHandler?.UnregisterHandler<CommsUpMessage>();
            clientHandler?.UnregisterHandler<CommsDownMessage>();
            serverHandler = null;
            clientHandler = null;
        }

        private static CommsDownMessage ToMessage(CommsEnvelope e) => new CommsDownMessage
        {
            Protocol = ProtocolVersion,
            Event = (byte)e.Event,
            Id = e.Id,
            Author = e.Author,
            AuthorName = e.AuthorName,
            Faction = e.Faction,
            Channel = (byte)e.Channel,
            Kind = e.Kind,
            Style = e.Style,
            Size = e.Size,
            Flags = e.Flags,
            Ttl = e.Ttl,
            Points = e.Points,
            Text = e.Text,
            Items = e.Items,
            Values = e.Values,
            Players = e.Players,
            Ids = e.Ids,
        };

        // ---- Serializers -------------------------------------------------------------------

        private static bool serializersInstalled;

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            Bind(typeof(Writer<CommsUpMessage>), "Write", (Action<NetworkWriter, CommsUpMessage>)CommsWire.WriteUp);
            Bind(typeof(Reader<CommsUpMessage>), "Read", (Func<NetworkReader, CommsUpMessage>)CommsWire.ReadUp);
            Bind(typeof(Writer<CommsDownMessage>), "Write", (Action<NetworkWriter, CommsDownMessage>)CommsWire.WriteDown);
            Bind(typeof(Reader<CommsDownMessage>), "Read", (Func<NetworkReader, CommsDownMessage>)CommsWire.ReadDown);
            MessagePacker.RegisterMessage<CommsUpMessage>();
            MessagePacker.RegisterMessage<CommsDownMessage>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[COMMS] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; comms cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.Squad.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Squad.Networking
{
    [NetworkMessage]
    internal struct SquadQuery { public byte Protocol; public uint Scene, Token; }

    [NetworkMessage]
    internal struct SquadSnapshot
    {
        public byte Protocol;
        public uint Scene, Token, Event;
        public PilotView Pilot;
        public bool Hunt;
        public int Bonus, Origin, ActiveIndex, HuntId;
        public string Status, Speaker, Chatter;
        public EnemyWingView[] Wings;
    }

    internal sealed class SquadNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 2;
        private SquadManager manager;
        private MessageHandler serverHandler, clientHandler;
        private readonly Dictionary<ulong, float> nextReply = new Dictionary<ulong, float>();
        private readonly List<ulong> expired = new List<ulong>(64);
        private float nextRegistration, nextPrune, lastQuery = -10f;
        private uint scene, token;
        private bool pending;
        private FactionHQ requestedHq;

        internal void Configure(SquadManager owner) { manager = owner; InstallSerializers(); }
        internal void ResetScene()
        { scene++; pending = false; requestedHq = null; nextReply.Clear(); lastQuery = -10f; }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (pending && now - lastQuery > 5f)
            { pending = false; manager.ClearLocal("Squad host link unavailable."); }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;
            if (server != serverHandler)
            { serverHandler?.UnregisterHandler<SquadQuery>(); serverHandler = server; serverHandler?.RegisterHandler<SquadQuery>(ReceiveQuery, false); }
            if (client != clientHandler)
            { clientHandler?.UnregisterHandler<SquadSnapshot>(); clientHandler = client; clientHandler?.RegisterHandler<SquadSnapshot>(ReceiveSnapshot, false); }
            if (now < nextPrune) return;
            nextPrune = now + 10f; expired.Clear();
            foreach (var entry in nextReply) if (now - entry.Value > 30f) expired.Add(entry.Key);
            foreach (ulong id in expired) nextReply.Remove(id);
        }

        internal void Request()
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null)
            { manager.ClearLocal("Join a faction to receive squad intelligence."); return; }
            if (requestedHq != player.HQ) { ResetScene(); requestedHq = player.HQ; manager.ClearLocal("Receiving squad intelligence."); }
            if (pending || Time.unscaledTime - lastQuery < 1f) return;
            lastQuery = Time.unscaledTime;
            if (GameAccess.IsServer()) { manager.Apply(manager.Snapshot(player), PlayerIdentity.Of(player)); return; }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) { manager.ClearLocal("Squad host link unavailable."); return; }
            pending = true;
            client.Send(new SquadQuery { Protocol = ProtocolVersion, Scene = scene, Token = ++token });
        }

        private void ReceiveQuery(INetworkPlayer sender, SquadQuery query)
        {
            if (!GameAccess.IsServer() || !MissionManager.IsRunning || query.Protocol != ProtocolVersion ||
                sender == null || !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            ulong id = PlayerIdentity.Of(player); float now = Time.unscaledTime;
            if (nextReply.TryGetValue(id, out float next) ? now < next : nextReply.Count >= 64) return;
            nextReply[id] = now + 0.75f;
            SquadSnapshot snapshot = manager.Snapshot(player);
            snapshot.Scene = query.Scene; snapshot.Token = query.Token; sender.Send(snapshot);
        }

        private void ReceiveSnapshot(INetworkPlayer sender, SquadSnapshot snapshot)
        {
            if (GameAccess.IsServer() || !pending || snapshot.Protocol != ProtocolVersion || snapshot.Scene != scene ||
                snapshot.Token != token || !GameManager.GetLocalPlayer<Player>(out Player local) || local == null || local.HQ != requestedHq) return;
            pending = false; manager.Apply(snapshot, PlayerIdentity.Of(local));
        }

        private void OnDestroy()
        { serverHandler?.UnregisterHandler<SquadQuery>(); clientHandler?.UnregisterHandler<SquadSnapshot>(); ResetScene(); }

        private static string Text(string text) => string.IsNullOrEmpty(text) ? "" : text.Substring(0, Math.Min(192, text.Length));
        private static void WriteText(NetworkWriter w, string text) => w.WriteString(Text(text));
        private static string ReadText(NetworkReader r) => Text(r.ReadString());
        private static int ReadInt(NetworkReader r, int maximum)
        {
            int value = r.ReadPackedInt32();
            if (value < 0 || value > maximum) throw new InvalidOperationException("Squad snapshot outside bounds.");
            return value;
        }

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<SquadQuery>), "Write", (Action<NetworkWriter, SquadQuery>)((w, v) =>
            { w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token); }));
            Bind(typeof(Reader<SquadQuery>), "Read", (Func<NetworkReader, SquadQuery>)(r =>
                new SquadQuery { Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32() }));
            Bind(typeof(Writer<SquadSnapshot>), "Write", (Action<NetworkWriter, SquadSnapshot>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token); w.WritePackedUInt32(v.Event);
                WriteText(w, v.Pilot.Name); WriteText(w, v.Pilot.Callsign); WriteText(w, v.Pilot.Status); WriteText(w, v.Pilot.Background);
                w.WriteByte(v.Pilot.Respawns ? (byte)1 : (byte)0); w.WritePackedInt32(v.Pilot.Deaths); w.WritePackedInt32(v.Pilot.Generation);
                w.WriteByte(v.Hunt ? (byte)1 : (byte)0); w.WritePackedInt32(v.Bonus); w.WritePackedInt32(v.Origin);
                w.WritePackedInt32(v.ActiveIndex + 1); w.WritePackedInt32(v.HuntId);
                WriteText(w, v.Status); WriteText(w, v.Speaker); WriteText(w, v.Chatter);
                int count = Math.Min(8, v.Wings?.Length ?? 0); w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    EnemyWingView wing = v.Wings[i];
                    WriteText(w, wing.Symbol); WriteText(w, wing.WingName); WriteText(w, wing.AceName);
                    w.WritePackedInt32(wing.Tier); WriteText(w, wing.Skill); WriteText(w, wing.Status);
                    w.WritePackedInt32(wing.MembersAlive); w.WritePackedInt32(wing.MemberCount);
                    WriteText(w, wing.TargetName); w.WritePackedInt32(wing.Returns);
                    w.WritePackedInt32(wing.AbilityMask);
                }
            }));
            Bind(typeof(Reader<SquadSnapshot>), "Read", (Func<NetworkReader, SquadSnapshot>)(r =>
            {
                var v = new SquadSnapshot { Protocol = r.ReadByte(), Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(), Event = r.ReadPackedUInt32() };
                string name = ReadText(r), callsign = ReadText(r), status = ReadText(r), background = ReadText(r);
                bool respawns = r.ReadByte() == 1; int deaths = ReadInt(r, 10000), generation = ReadInt(r, 10001);
                v.Pilot = new PilotView(name, callsign, status, respawns, deaths, generation, background);
                v.Hunt = r.ReadByte() == 1; v.Bonus = ReadInt(r, 20); v.Origin = ReadInt(r, int.MaxValue);
                v.ActiveIndex = ReadInt(r, 8) - 1; v.HuntId = ReadInt(r, int.MaxValue);
                v.Status = ReadText(r); v.Speaker = ReadText(r); v.Chatter = ReadText(r);
                int count = r.ReadByte();
                if (count > 8 || v.ActiveIndex >= count) throw new InvalidOperationException("Squad wing snapshot exceeds bounds.");
                v.Wings = new EnemyWingView[count];
                for (int i = 0; i < count; i++)
                {
                    string symbol = ReadText(r), wing = ReadText(r), ace = ReadText(r); int tier = ReadInt(r, 5);
                    string skill = ReadText(r), state = ReadText(r); int alive = ReadInt(r, 4), members = ReadInt(r, 4);
                    string target = ReadText(r); int returns = ReadInt(r, 2);
                    if (alive > members) throw new InvalidOperationException("Invalid Squad strength.");
                    int abilities = ReadInt(r, 15);
                    v.Wings[i] = new EnemyWingView(symbol, wing, ace, tier, skill, state, alive, members, target, returns, abilities);
                }
                return v;
            }));
            MessagePacker.RegisterMessage<SquadQuery>(); MessagePacker.RegisterMessage<SquadSnapshot>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo seam = holder.GetProperty(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (seam == null) throw new MissingMemberException(holder.FullName, property);
            seam.SetValue(null, value, null);
        }
    }
}

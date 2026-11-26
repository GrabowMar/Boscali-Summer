using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Host-to-peer aircraft system state. Transitions are sent as they happen, with a slow refresh for late joiners.</summary>
    [NetworkMessage]
    internal struct MegaplaneStateMessage
    {
        public byte Protocol;
        public uint Unit;
        public byte Index;   // 0/1 slot animation, 2 railgun, 3 plant, 4/5 fighter operation phase
        public byte Value;
    }

    /// <summary>A client pilot's bay or rail selection command, or an observer's request for damage state.</summary>
    [NetworkMessage]
    internal struct MegaplaneAskMessage
    {
        public byte Protocol;
        public uint Unit;
        public byte Verb;
    }

    /// <summary>The wire format, hand-written like every other Boscalia message. A read that finds anything out of
    /// bounds sets Protocol 0, which both handlers ignore; a foreign protocol version stops at its first byte.</summary>
    internal static class MegaplaneWire
    {
        /// <summary>Bump when a message's layout or meaning changes.</summary>
        public const byte ProtocolVersion = 8;

        internal const byte DropVerb = 0, RecallVerb = 1, InitialDamageVerb = 2, WaveOffVerb = 3;
        internal const byte SelectRailVerb = 4, DeselectRailVerb = 5;
        internal const byte RailgunIndex = 2, ReactorIndex = 3, OperationsIndex = 4, OperationsLastIndex = 5;

        public static void WriteState(NetworkWriter w, MegaplaneStateMessage v)
        {
            w.WriteByte(v.Protocol);
            w.WritePackedUInt32(v.Unit);
            w.WriteByte(v.Index);
            w.WriteByte(v.Value);
        }

        public static MegaplaneStateMessage ReadState(NetworkReader r)
        {
            var m = new MegaplaneStateMessage { Protocol = r.ReadByte() };
            if (m.Protocol != ProtocolVersion) return m;
            m.Unit = r.ReadPackedUInt32();
            m.Index = r.ReadByte();
            m.Value = r.ReadByte();
            int max = m.Index switch
            {
                ReactorIndex => MegaplaneReactor.Spent,
                RailgunIndex => 1,
                OperationsIndex or OperationsLastIndex => (int)FighterBayPhase.Stowing,
                _ => 4,
            };
            if (m.Index > OperationsLastIndex || m.Value > max) m.Protocol = 0;
            return m;
        }

        public static void WriteAsk(NetworkWriter w, MegaplaneAskMessage v)
        {
            w.WriteByte(v.Protocol);
            w.WritePackedUInt32(v.Unit);
            w.WriteByte(v.Verb);
        }

        public static MegaplaneAskMessage ReadAsk(NetworkReader r)
        {
            var m = new MegaplaneAskMessage { Protocol = r.ReadByte() };
            if (m.Protocol != ProtocolVersion) return m;
            m.Unit = r.ReadPackedUInt32();
            m.Verb = r.ReadByte();
            if (m.Verb > DeselectRailVerb) m.Protocol = 0;
            return m;
        }
    }

    /// <summary>
    /// The belly bay's replication. The host is the only state machine (spawn, latch, rearm timings); one tiny state
    /// message per transition tells every peer which slot is where, and each peer runs the same door/trapeze/visual
    /// animation from it, so a client finally sees the doors open, the trapeze swing and the stowed fighter disappear
    /// exactly when the host releases it. A client pilot's drop/recall key travels up as an ask, which the host
    /// accepts only from the player actually flying that carrier. Follows the COMMS transport: the game's own Mirage
    /// connection, serializers installed through <see cref="MirageSerializers"/>, handler slots swapped on a slow poll.
    /// </summary>
    internal sealed partial class MegaplaneNet : MonoBehaviour
    {
        internal static MegaplaneNet Instance { get; private set; }
        private static readonly List<MegaplaneBay> bays = new List<MegaplaneBay>();
        private static readonly List<MegaplaneRailgun> railguns = new List<MegaplaneRailgun>();
        private static readonly List<MegaplaneReactor> reactors = new List<MegaplaneReactor>();

        private HandlerSlot serverSlot, clientSlot, damageSlot;
        private float nextPoll, nextRefresh;

        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<MegaplaneAskMessage>(ReceiveAsk);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<MegaplaneStateMessage>(ReceiveState);
        private HandlerSlot DamageHandler => damageSlot ??= HandlerSlot.Of<MegaplaneDamageSeed>(ReceiveDamage);

        internal static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("BoscaliSummer.MegaplaneNet") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<MegaplaneNet>();
        }

        internal static void Register(MegaplaneBay bay)
        {
            if (!bays.Contains(bay)) bays.Add(bay);
        }

        internal static void Unregister(MegaplaneBay bay) => bays.Remove(bay);

        internal static void RegisterRailgun(MegaplaneRailgun railgun)
        {
            if (!railguns.Contains(railgun)) railguns.Add(railgun);
        }

        internal static void UnregisterRailgun(MegaplaneRailgun railgun) => railguns.Remove(railgun);

        internal static void Register(MegaplaneReactor reactor) => reactors.Add(reactor);
        internal static void Unregister(MegaplaneReactor reactor) => reactors.Remove(reactor);

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            DamageHandler.Release();
            CrashHandler.Release();
            ResetCrashes();
            if (Instance == this) Instance = null;
        }

        // ---- Client side -------------------------------------------------------------------

        /// <summary>Send one request to the host. False when the client connection is not authenticated yet.</summary>
        internal static bool Ask(Aircraft carrier, byte verb)
        {
            NetworkClient client;
            try { client = GameAccess.NetworkManagerOrNull?.Client; }
            catch { return false; }
            if (client == null || !client.Active || !client.IsConnected || client.Player == null || !client.Player.IsAuthenticated) return false;
            client.Send(new MegaplaneAskMessage { Protocol = MegaplaneWire.ProtocolVersion, Unit = carrier.persistentID.Id, Verb = verb });
            return true;
        }

        private void ReceiveState(INetworkPlayer _, MegaplaneStateMessage message)
        {
            if (GameAccess.IsServer() || message.Protocol != MegaplaneWire.ProtocolVersion) return;
            var unit = new PersistentID { Id = message.Unit };
            if (!unit.TryGetUnit(out Unit u) || !(u is Aircraft carrier)) return;
            if (message.Index <= 1 && carrier.TryGetComponent(out MegaplaneBay bay)) bay.RemoteState(message.Index, message.Value);
            else if (message.Index == MegaplaneWire.RailgunIndex && carrier.TryGetComponent(out MegaplaneRailgun rail)) rail.RemoteWant(message.Value != 0);
            else if (message.Index == MegaplaneWire.ReactorIndex)
            {
                var reactor = carrier.GetComponent<MegaplaneReactor>();
                if (reactor != null) reactor.RemoteState(message.Value);
            }
            else if (message.Index >= MegaplaneWire.OperationsIndex && carrier.TryGetComponent(out MegaplaneBayOperations operations))
                operations.Remote(message.Index - MegaplaneWire.OperationsIndex, message.Value);
        }

        // ---- Host side ---------------------------------------------------------------------

        private static void ReceiveDamage(INetworkPlayer _, MegaplaneDamageSeed message)
        {
            if (GameAccess.IsServer() || message.Protocol != MegaplaneWire.ProtocolVersion) return;
            var id = new PersistentID { Id = message.Unit };
            if (!id.TryGetUnit(out Unit unit) || !(unit is Aircraft carrier) || carrier.NetId != message.Spawn) return;
            MegaplaneDamage damage = carrier.GetComponent<MegaplaneDamage>();
            if (damage != null) damage.Restore(message);
        }

        private void ReceiveAsk(INetworkPlayer sender, MegaplaneAskMessage message)
        {
            if (!GameAccess.IsServer() || message.Protocol != MegaplaneWire.ProtocolVersion || sender == null || !sender.IsAuthenticated) return;
            if (!sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            var unit = new PersistentID { Id = message.Unit };
            if (!unit.TryGetUnit(out Unit u) || !(u is Aircraft carrier)) return;
            if (message.Verb == MegaplaneWire.InitialDamageVerb)
            {
                MegaplaneDamage damage = carrier.GetComponent<MegaplaneDamage>();
                if (damage != null && carrier.Identity.observers.Contains(sender) && damage.TryMakeSeed(sender, out var seed))
                    sender.Send(seed);
                return;
            }
            // Only the pilot of this live carrier may command its launch systems.
            if (player.Aircraft != carrier || player.HQ != carrier.NetworkHQ || carrier.disabled) return;
            if (message.Verb is MegaplaneWire.SelectRailVerb or MegaplaneWire.DeselectRailVerb)
            {
                if (player.HQ != null && carrier.Owner == sender && carrier.Player == player && carrier.TryGetComponent(out MegaplaneRailgun rail))
                    rail.SetRemoteSelection(player, message.Verb == MegaplaneWire.SelectRailVerb);
                return;
            }
            MegaplaneBay bay = carrier.GetComponent<MegaplaneBay>();
            if (bay == null) return;
            switch (message.Verb)
            {
                case MegaplaneWire.DropVerb: MegaplaneBayControls.Execute(bay, CarrierBayAction.Launch); break;
                case MegaplaneWire.RecallVerb: MegaplaneBayControls.Execute(bay, CarrierBayAction.Recall); break;
                case MegaplaneWire.WaveOffVerb: MegaplaneBayControls.Execute(bay, CarrierBayAction.WaveOff); break;
            }
        }

        /// <summary>Host: push one art state to every authenticated peer (all sides: the doors are visible to all).</summary>
        internal static void Broadcast(uint unit, int index, byte value)
        {
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;
            var message = new MegaplaneStateMessage { Protocol = MegaplaneWire.ProtocolVersion, Unit = unit, Index = (byte)index, Value = value };
            foreach (INetworkPlayer peer in server.AuthenticatedPlayers)
                if (peer != null && peer.IsAuthenticated)
                    try { peer.Send(message); }
                    catch (Exception) { /* a peer that left between frames: its handlers go with it */ }
        }

        // ---- Registration ------------------------------------------------------------------

        private static bool serializersInstalled;
        private static readonly MirageSerializers Seams = new MirageSerializers(
            "[Megaplane]", " is missing; the bay cannot replicate on this game build.");

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            Seams.Install<MegaplaneStateMessage>((Action<NetworkWriter, MegaplaneStateMessage>)MegaplaneWire.WriteState,
                (Func<NetworkReader, MegaplaneStateMessage>)MegaplaneWire.ReadState);
            Seams.Install<MegaplaneAskMessage>((Action<NetworkWriter, MegaplaneAskMessage>)MegaplaneWire.WriteAsk,
                (Func<NetworkReader, MegaplaneAskMessage>)MegaplaneWire.ReadAsk);
            Seams.Install<MegaplaneDamageSeed>((Action<NetworkWriter, MegaplaneDamageSeed>)MegaplaneDamageSeed.Write,
                (Func<NetworkReader, MegaplaneDamageSeed>)MegaplaneDamageSeed.Read);
            Seams.Install<MegaplaneCrashRecord>((Action<NetworkWriter, MegaplaneCrashRecord>)MegaplaneCrashRecord.Write,
                (Func<NetworkReader, MegaplaneCrashRecord>)MegaplaneCrashRecord.Read);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .5f;
            NetworkManagerNuclearOption network;
            try { network = GameAccess.NetworkManagerOrNull; }
            catch { return; }
            InstallSerializers();
            ServerHandlers.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            ClientHandlers.Swap(network?.Client?.MessageHandler);
            DamageHandler.Swap(network?.Client?.MessageHandler);
            CrashHandler.Swap(network?.Client?.MessageHandler);
            UpdateCrashes(network);
            if (!GameAccess.IsServer()) return;
            bool refresh = Time.unscaledTime >= nextRefresh;
            if (refresh) nextRefresh = Time.unscaledTime + 5f;
            foreach (MegaplaneBay bay in bays)
            {
                if (bay == null || bay.Carrier == null || !bay.Carrier.persistentID.IsValid) continue;
                if (bay.TryGetComponent(out MegaplaneBayOperations operations)) operations.Publish(bay, refresh);
                if (!refresh || (bay.State[0] == 0 && bay.State[1] == 0)) continue;
                uint unit = bay.Carrier.persistentID.Id;
                Broadcast(unit, 0, (byte)bay.State[0]);
                Broadcast(unit, 1, (byte)bay.State[1]);
            }
            if (!refresh) return;
            foreach (MegaplaneRailgun rail in railguns)
                if (rail != null && rail.Wanted) Broadcast(rail.UnitId, MegaplaneWire.RailgunIndex, 1);
            foreach (MegaplaneReactor reactor in reactors)
                if (reactor != null && reactor.State != 0) Broadcast(reactor.UnitId, MegaplaneWire.ReactorIndex, reactor.State);
        }
    }
}

using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct AlexIntent
    {
        public byte Protocol;
        public byte Action; // AlexNet.Cut
    }

    [NetworkMessage]
    internal struct AlexState
    {
        public byte Protocol;
        public uint Decoy, Host;
        public byte State, Pips, Captured, Dragged, Reason;
        public float Line, Target, FreeLeft;
    }

    /// <summary>
    /// ALE-X wire: the owner's CUT intent (the host resolves the aircraft from the sender, never from the message)
    /// and the host's per-decoy view for every peer's cable, pod animation and HUD.
    /// </summary>
    internal sealed class AlexNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        internal const byte Cut = 0;
        private const float IntentInterval = 0.5f;
        private const float ResendInterval = 5f; // late joiners
        private const float PruneInterval = 2f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private readonly System.Collections.Generic.List<uint> stale = new System.Collections.Generic.List<uint>();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<AlexIntent>(ReceiveIntent);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<AlexState>(ReceiveState);
        private float nextRegistration, nextResend, nextPrune;

        private void Awake()
        {
            AlexBoard.Changed = Broadcast;
            MirageSerializers.Strict.Install<AlexIntent>(
                (Action<NetworkWriter, AlexIntent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WriteByte(v.Action);
                }),
                (Func<NetworkReader, AlexIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new AlexIntent { Protocol = protocol }
                        : new AlexIntent { Protocol = protocol, Action = r.ReadByte() };
                }));
            MirageSerializers.Strict.Install<AlexState>(
                (Action<NetworkWriter, AlexState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.Decoy);
                    w.WritePackedUInt32(v.Host);
                    w.WriteByte(v.State);
                    w.WriteByte(v.Pips);
                    w.WriteByte(v.Captured);
                    w.WriteByte(v.Dragged);
                    w.WriteByte(v.Reason);
                    w.WriteSingle(v.Line);
                    w.WriteSingle(v.Target);
                    w.WriteSingle(v.FreeLeft);
                }),
                (Func<NetworkReader, AlexState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new AlexState { Protocol = protocol };
                    return new AlexState
                    {
                        Protocol = protocol,
                        Decoy = r.ReadPackedUInt32(),
                        Host = r.ReadPackedUInt32(),
                        State = r.ReadByte(),
                        Pips = r.ReadByte(),
                        Captured = r.ReadByte(),
                        Dragged = r.ReadByte(),
                        Reason = r.ReadByte(),
                        Line = r.ReadSingle(),
                        Target = r.ReadSingle(),
                        FreeLeft = r.ReadSingle(),
                    };
                }));
        }

        public void ResetForScene() => throttle.Clear();

        /// <summary>Owner peer: the fire press became a cut of its own aircraft's decoy.</summary>
        public static void RequestCut() =>
            NetworkManagerNuclearOption.i?.Client?.Send(new AlexIntent { Protocol = ProtocolVersion, Action = Cut });

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextResend && GameAccess.IsServer())
            {
                nextResend = now + ResendInterval;
                foreach (AlexView view in AlexBoard.Views.Values) Broadcast(view);
            }
            if (now >= nextPrune)
            {
                nextPrune = now + PruneInterval;
                Prune();
            }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            ServerHandlers.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            ClientHandlers.Swap(network?.Client?.MessageHandler);
            throttle.Prune(now);
        }

        // A client misses a Gone message on a bad link; a view whose decoy no longer exists is dead.
        private void Prune()
        {
            stale.Clear();
            foreach (uint id in AlexBoard.Views.Keys)
                if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit.disabled) stale.Add(id);
            for (int i = 0; i < stale.Count; i++) AlexBoard.Views.Remove(stale[i]);
        }

        private static void Broadcast(AlexView view)
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new AlexState
            {
                Protocol = ProtocolVersion,
                Decoy = view.Decoy,
                Host = view.Host,
                State = (byte)view.State,
                Pips = view.Pips,
                Captured = view.Captured,
                Dragged = view.Dragged,
                Reason = (byte)view.Reason,
                Line = view.Line,
                Target = view.Target,
                FreeLeft = view.FreeLeft,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveIntent(INetworkPlayer sender, AlexIntent message)
        {
            if (message.Protocol != ProtocolVersion || message.Action != Cut || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            if (!throttle.Allow(aircraft.persistentID.Id, Time.unscaledTime, IntentInterval)) return;
            AlexService.Cut(aircraft, KiteCut.Pilot);
        }

        private void ReceiveState(INetworkPlayer sender, AlexState message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer() || message.State > (byte)KiteState.Gone) return;
            if ((KiteState)message.State == KiteState.Gone)
            {
                AlexBoard.Views.Remove(message.Decoy);
                return;
            }
            AlexBoard.Views[message.Decoy] = new AlexView
            {
                Decoy = message.Decoy,
                Host = message.Host,
                State = (KiteState)message.State,
                Pips = message.Pips,
                Captured = message.Captured,
                Dragged = message.Dragged,
                Reason = (KiteCut)message.Reason,
                Line = message.Line,
                Target = message.Target,
                FreeLeft = message.FreeLeft,
                Stamp = Time.unscaledTime,
            };
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            AlexBoard.Changed = null;
        }
    }
}

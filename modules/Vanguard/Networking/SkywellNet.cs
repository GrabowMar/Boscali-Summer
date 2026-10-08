using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct SkywellIntent
    {
        public byte Protocol;
        public byte Action; // SkywellNet.Toggle / Dock / Undock
        public uint Tanker; // Dock only
    }

    [NetworkMessage]
    internal struct SkywellState
    {
        public byte Protocol;
        public uint Tanker;
        public bool Active;
        public uint Receiver;
        public byte Phase;
        public float FuelKg;
        public float StockKg;
    }

    /// <summary>
    /// SKYWELL wire: client intents (toggle own kit, abort own contact; the host resolves the aircraft from the
    /// sender, never from the message) and the host's per-tanker state for every peer's visuals and assist.
    /// </summary>
    internal sealed class SkywellNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 2;
        internal const byte Toggle = 0;
        internal const byte DockAction = 1;
        internal const byte UndockAction = 2;
        private const float IntentInterval = 0.5f;
        private const float ResendInterval = 5f; // late joiners

        private static SkywellNet instance;
        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<SkywellIntent>(ReceiveIntent);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<SkywellState>(ReceiveState);
        private float nextRegistration, nextResend;

        private void Awake()
        {
            instance = this;
            SkywellBoard.Changed = Broadcast;
            MirageSerializers.Strict.Install<SkywellIntent>(
                (Action<NetworkWriter, SkywellIntent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WriteByte(v.Action);
                    w.WritePackedUInt32(v.Tanker);
                }),
                (Func<NetworkReader, SkywellIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new SkywellIntent { Protocol = protocol }
                        : new SkywellIntent { Protocol = protocol, Action = r.ReadByte(), Tanker = r.ReadPackedUInt32() };
                }));
            MirageSerializers.Strict.Install<SkywellState>(
                (Action<NetworkWriter, SkywellState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.Tanker);
                    w.WriteBoolean(v.Active);
                    w.WritePackedUInt32(v.Receiver);
                    w.WriteByte(v.Phase);
                    w.WriteSingle(v.FuelKg);
                    w.WriteSingle(v.StockKg);
                }),
                (Func<NetworkReader, SkywellState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new SkywellState { Protocol = protocol };
                    return new SkywellState
                    {
                        Protocol = protocol,
                        Tanker = r.ReadPackedUInt32(),
                        Active = r.ReadBoolean(),
                        Receiver = r.ReadPackedUInt32(),
                        Phase = r.ReadByte(),
                        FuelKg = r.ReadSingle(),
                        StockKg = r.ReadSingle(),
                    };
                }));
        }

        public void ResetForScene() => throttle.Clear();

        /// <summary>Owner peer: toggle the kit on its own aircraft.</summary>
        public static void RequestToggle(Aircraft tanker)
        {
            if (GameAccess.IsServer()) SkywellService.Toggle(tanker);
            else Send(Toggle);
        }

        /// <summary>Owner peer: brake held near a kit; the dock driver already flies the receiver in.</summary>
        public static void RequestDock(Aircraft receiver, Aircraft tanker)
        {
            if (GameAccess.IsServer()) SkywellService.Dock(receiver, tanker);
            else Send(DockAction, tanker.persistentID.Id);
        }

        /// <summary>Owner peer: brake released.</summary>
        public static void RequestUndock(Aircraft receiver)
        {
            if (GameAccess.IsServer()) SkywellService.Undock(receiver);
            else Send(UndockAction);
        }

        private static void Send(byte action, uint tanker = 0u) =>
            NetworkManagerNuclearOption.i?.Client?.Send(new SkywellIntent { Protocol = ProtocolVersion, Action = action, Tanker = tanker });

        private void Update()
        {
            if (Time.unscaledTime >= nextResend && GameAccess.IsServer())
            {
                nextResend = Time.unscaledTime + ResendInterval;
                foreach (SkywellView view in SkywellBoard.Views.Values) Broadcast(view);
            }
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            ServerHandlers.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            ClientHandlers.Swap(network?.Client?.MessageHandler);
            throttle.Prune(Time.unscaledTime);
        }

        private static void Broadcast(SkywellView view)
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new SkywellState
            {
                Protocol = ProtocolVersion,
                Tanker = view.Tanker,
                Active = view.Active,
                Receiver = view.Receiver,
                Phase = (byte)view.Phase,
                FuelKg = view.FuelKg,
                StockKg = view.StockKg,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveIntent(INetworkPlayer sender, SkywellIntent message)
        {
            if (message.Protocol != ProtocolVersion || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            if (!throttle.Allow(((ulong)aircraft.persistentID.Id << 1) | message.Action, Time.unscaledTime, IntentInterval)) return;
            if (message.Action == Toggle) SkywellService.Toggle(aircraft);
            else if (message.Action == UndockAction) SkywellService.Undock(aircraft);
            else if (message.Action == DockAction && UnitRegistry.TryGetUnit(new PersistentID { Id = message.Tanker }, out Unit unit))
                SkywellService.Dock(aircraft, unit as Aircraft);
        }

        private void ReceiveState(INetworkPlayer sender, SkywellState message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            SkywellBoard.Views[message.Tanker] = new SkywellView
            {
                Tanker = message.Tanker,
                Active = message.Active,
                Receiver = message.Receiver,
                Phase = (SkywellPhase)message.Phase,
                FuelKg = message.FuelKg,
                StockKg = message.StockKg,
            };
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            if (instance == this)
            {
                instance = null;
                SkywellBoard.Changed = null;
            }
        }
    }
}

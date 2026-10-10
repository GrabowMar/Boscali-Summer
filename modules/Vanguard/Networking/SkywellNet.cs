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
    internal struct SkywellIntent
    {
        public byte Protocol;
        public byte Action; // SkywellNet.Toggle / OrderAction
        public uint Tanker; // reserved (the host finds receivers by the service zone itself)
        public byte Value;   // Order: PitOrder
        public byte Station; // Order: priority station + 1 (0 = none / auto)
    }

    [NetworkMessage]
    internal struct SkywellState
    {
        public byte Protocol;
        public uint Tanker;
        public byte State;      // TenderState
        public uint Receiver;   // the receiver being served, else 0
        public ushort FuelKg;   // tanker fuel stock
        public byte Rounds;     // rounds left in the tender's magazine
        public byte Order;      // PitOrder of the current receiver
        public byte Job;        // PitJobKind in flight
        public byte JobStation; // receiver station index, 255 = none
        public byte JobIndex;   // jobs committed so far
        public byte JobCount;   // committed + remaining
        public byte JobTenths;  // seconds of the job in flight x10
        public byte StateTenths; // seconds in this state x10 (late joiners animate from it)
        public ushort Flags;    // PitFlags
        public ushort PitTenths;
        public uint Released;   // last receiver let go ...
        public byte ReleaseKind; // ... why (PitRelease) ...
        public byte Cooldown;   // ... and for how many more seconds the server refuses it
    }

    /// <summary>
    /// SKYWELL wire: client intents (toggle own kit, set the pit-stop order; the host resolves the aircraft from the
    /// sender, never from the message; a receiver asks for service by flying into the service zone, which the host sees itself) and the host's per-tanker state for every peer's visuals and assist.
    /// </summary>
    internal sealed class SkywellNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 6;
        internal const byte Toggle = 0;
        internal const byte OrderAction = 3;
        private const float IntentInterval = 0.5f;
        private const float OrderInterval = 0.25f;
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
                    w.WriteByte(v.Value);
                    w.WriteByte(v.Station);
                }),
                (Func<NetworkReader, SkywellIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new SkywellIntent { Protocol = protocol }
                        : new SkywellIntent { Protocol = protocol, Action = r.ReadByte(), Tanker = r.ReadPackedUInt32(), Value = r.ReadByte(), Station = r.ReadByte() };
                }));
            MirageSerializers.Strict.Install<SkywellState>(
                (Action<NetworkWriter, SkywellState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.Tanker);
                    w.WriteByte(v.State);
                    w.WritePackedUInt32(v.Receiver);
                    w.WritePackedUInt32(v.FuelKg);
                    w.WriteByte(v.Rounds);
                    w.WriteByte(v.Order);
                    w.WriteByte(v.Job);
                    w.WriteByte(v.JobStation);
                    w.WriteByte(v.JobIndex);
                    w.WriteByte(v.JobCount);
                    w.WriteByte(v.JobTenths);
                    w.WriteByte(v.StateTenths);
                    w.WritePackedUInt32(v.Flags);
                    w.WritePackedUInt32(v.PitTenths);
                    w.WritePackedUInt32(v.Released);
                    w.WriteByte(v.ReleaseKind);
                    w.WriteByte(v.Cooldown);
                }),
                (Func<NetworkReader, SkywellState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new SkywellState { Protocol = protocol };
                    return new SkywellState
                    {
                        Protocol = protocol,
                        Tanker = r.ReadPackedUInt32(),
                        State = r.ReadByte(),
                        Receiver = r.ReadPackedUInt32(),
                        FuelKg = (ushort)Math.Min(r.ReadPackedUInt32(), ushort.MaxValue),
                        Rounds = r.ReadByte(),
                        Order = r.ReadByte(),
                        Job = r.ReadByte(),
                        JobStation = r.ReadByte(),
                        JobIndex = r.ReadByte(),
                        JobCount = r.ReadByte(),
                        JobTenths = r.ReadByte(),
                        StateTenths = r.ReadByte(),
                        Flags = (ushort)Math.Min(r.ReadPackedUInt32(), ushort.MaxValue),
                        PitTenths = (ushort)Math.Min(r.ReadPackedUInt32(), ushort.MaxValue),
                        Released = r.ReadPackedUInt32(),
                        ReleaseKind = r.ReadByte(),
                        Cooldown = r.ReadByte(),
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

        /// <summary>Owner peer: standing pit-stop order and the station served first (-1 = auto).</summary>
        public static void RequestOrder(Aircraft receiver, byte order, int station)
        {
            if (order > (byte)PitOrder.Munitions || station < -1 || station > 32) return;
            if (GameAccess.IsServer()) SkywellService.SetPrefs(receiver, (PitOrder)order, station);
            else Send(OrderAction, 0u, order, (byte)(station + 1));
        }

        private static void Send(byte action, uint tanker = 0u, byte value = 0, byte station = 0) =>
            NetworkManagerNuclearOption.i?.Client?.Send(new SkywellIntent
            {
                Protocol = ProtocolVersion, Action = action, Tanker = tanker, Value = value, Station = station,
            });

        private void Update()
        {
            if (Time.unscaledTime >= nextResend && GameAccess.IsServer())
            {
                nextResend = Time.unscaledTime + ResendInterval;
                foreach (SkywellView view in SkywellBoard.Views.Values)
                {
                    SkywellView fresh = view;
                    fresh.StateTenths = (byte)Mathf.Clamp(Mathf.RoundToInt(view.StateAge * 10f), 0, 255); // a late joiner animates from the real age
                    Broadcast(fresh);
                }
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
                State = (byte)view.State,
                Receiver = view.Receiver,
                FuelKg = view.FuelKg,
                Rounds = view.Rounds,
                Order = (byte)view.Order,
                Job = (byte)view.Job,
                JobStation = view.JobStation,
                JobIndex = view.JobIndex,
                JobCount = view.JobCount,
                JobTenths = view.JobTenths,
                StateTenths = view.StateTenths,
                Flags = view.Flags,
                PitTenths = view.PitTenths,
                Released = view.Released,
                ReleaseKind = view.ReleaseKind,
                Cooldown = view.Cooldown,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveIntent(INetworkPlayer sender, SkywellIntent message)
        {
            if (message.Protocol != ProtocolVersion || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            // Action in the low byte: the old "<< 1" collided adjacent ids with action 2.
            if (!throttle.Allow(((ulong)aircraft.persistentID.Id << 8) | message.Action, Time.unscaledTime,
                    message.Action == OrderAction ? OrderInterval : IntentInterval)) return;
            if (message.Action == OrderAction)
            {
                if (message.Value <= (byte)PitOrder.Munitions && message.Station <= 33)
                    SkywellService.SetPrefs(aircraft, (PitOrder)message.Value, message.Station - 1);
            }
            else if (message.Action == Toggle) SkywellService.Toggle(aircraft);
        }

        private void ReceiveState(INetworkPlayer sender, SkywellState message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            SkywellBoard.Views[message.Tanker] = new SkywellView
            {
                Tanker = message.Tanker,
                State = message.State <= (byte)TenderState.Stowing ? (TenderState)message.State : TenderState.Stowed,
                Receiver = message.Receiver,
                FuelKg = message.FuelKg,
                Rounds = (byte)Math.Min((int)message.Rounds, SkywellMagazine.Rounds),
                Order = message.Order <= (byte)PitOrder.Munitions ? (PitOrder)message.Order : PitOrder.Both,
                Job = message.Job <= (byte)PitJobKind.Cassette ? (PitJobKind)message.Job : PitJobKind.None,
                JobStation = message.JobStation,
                JobIndex = message.JobIndex,
                JobCount = message.JobCount,
                JobTenths = message.JobTenths,
                StateTenths = message.StateTenths,
                Flags = message.Flags,
                PitTenths = message.PitTenths,
                Released = message.Released,
                ReleaseKind = message.ReleaseKind,
                Cooldown = message.Cooldown,
                At = Time.timeSinceLevelLoad,
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

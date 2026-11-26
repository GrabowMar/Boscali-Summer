using System.Collections.Generic;
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
    internal enum DroneAction : byte { Screen, Strike, RamOn, RamOff, Share, Private, Pilot, Release }

    [NetworkMessage]
    internal struct VanguardDroneOrder
    {
        public byte Protocol;
        public byte Action;
        public uint UnitId;
    }

    [NetworkMessage]
    internal struct VanguardDroneState
    {
        public byte Protocol;
        public uint DroneId, OwnerId, PilotId, Lease;
        public bool Shared;
    }

    [NetworkMessage]
    internal struct VanguardDroneControls
    {
        public byte Protocol;
        public uint DroneId, Lease, Sequence;
        public float Pitch, Roll, Yaw, Throttle;
    }

    /// <summary>The server resolves the authenticated player for each order and owns sharing and pilot leases.</summary>
    internal sealed class VanguardNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 6;
        private const float OrderInterval = 0.2f, ResendInterval = 2f, InputInterval = 0.025f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private readonly SenderThrottle inputThrottle = new SenderThrottle();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<VanguardDroneOrder, VanguardDroneControls>(Receive, ReceiveControls);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<VanguardDroneState>(ReceiveState);
        private float nextRegistration, nextResend;

        private Unit designated;
        private Aircraft designatedFrom;
        private bool ramMode;
        private Aircraft ramFrom;

        public bool Striking => designated != null && !designated.disabled && designatedFrom != null && !designatedFrom.disabled &&
            GameManager.GetLocalAircraft(out Aircraft me) && me == designatedFrom;

        public bool CanStrike => LocalTarget(out _, out _);

        public bool RamMode => ramMode && ramFrom != null && !ramFrom.disabled && GameManager.GetLocalAircraft(out Aircraft me) && me == ramFrom;

        public Unit Designated => Striking ? designated : null;

        private void Awake()
        {
            MirageSerializers.Strict.Install<VanguardDroneOrder>(
                (writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WriteByte(value.Action);
                    writer.WritePackedUInt32(value.UnitId);
                },
                reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new VanguardDroneOrder { Protocol = protocol };
                    return new VanguardDroneOrder { Protocol = protocol, Action = reader.ReadByte(), UnitId = reader.ReadPackedUInt32() };
                });

            MirageSerializers.Strict.Install<VanguardDroneState>(
                (writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WritePackedUInt32(value.DroneId);
                    writer.WritePackedUInt32(value.OwnerId);
                    writer.WritePackedUInt32(value.PilotId);
                    writer.WritePackedUInt32(value.Lease);
                    writer.WriteBoolean(value.Shared);
                },
                reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new VanguardDroneState { Protocol = protocol };
                    return new VanguardDroneState
                    {
                        Protocol = protocol, DroneId = reader.ReadPackedUInt32(), OwnerId = reader.ReadPackedUInt32(),
                        PilotId = reader.ReadPackedUInt32(), Lease = reader.ReadPackedUInt32(), Shared = reader.ReadBoolean(),
                    };
                });

            MirageSerializers.Strict.Install<VanguardDroneControls>(
                (writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WritePackedUInt32(value.DroneId);
                    writer.WritePackedUInt32(value.Lease);
                    writer.WritePackedUInt32(value.Sequence);
                    writer.WriteSingle(value.Pitch);
                    writer.WriteSingle(value.Roll);
                    writer.WriteSingle(value.Yaw);
                    writer.WriteSingle(value.Throttle);
                },
                reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new VanguardDroneControls { Protocol = protocol };
                    return new VanguardDroneControls
                    {
                        Protocol = protocol, DroneId = reader.ReadPackedUInt32(), Lease = reader.ReadPackedUInt32(), Sequence = reader.ReadPackedUInt32(),
                        Pitch = reader.ReadSingle(), Roll = reader.ReadSingle(), Yaw = reader.ReadSingle(), Throttle = reader.ReadSingle(),
                    };
                });
        }

        public void ResetForScene()
        {
            designated = null;
            designatedFrom = null;
            ramMode = false;
            ramFrom = null;
            throttle.Clear();
            inputThrottle.Clear();
            DroneRoster.Clear();
        }

        public void Strike()
        {
            if (!LocalTarget(out Aircraft aircraft, out Unit target) || !SendOrder(aircraft, DroneAction.Strike, target)) return;
            designated = target;
            designatedFrom = aircraft;
        }

        public void Screen()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || !SendOrder(aircraft, DroneAction.Screen)) return;
            designated = null;
            designatedFrom = null;
        }

        public void SetRamMode(bool on)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                !SendOrder(aircraft, on ? DroneAction.RamOn : DroneAction.RamOff)) return;
            ramMode = on;
            ramFrom = aircraft;
        }

        private static bool SendOrder(Aircraft aircraft, DroneAction action, Unit target = null)
        {
            if (GameAccess.IsServer())
            {
                if (action == DroneAction.RamOn || action == DroneAction.RamOff) DroneOrders.SetRamMode(aircraft, action == DroneAction.RamOn);
                else DroneOrders.Order(aircraft, target);
                return true;
            }
            return Send(new VanguardDroneOrder
            {
                Protocol = ProtocolVersion, Action = (byte)action, UnitId = target != null ? target.persistentID.Id : 0u,
            });
        }

        internal bool SendDroneAction(Missile drone, DroneAction action)
        {
            if (action < DroneAction.Share || action > DroneAction.Release || !DroneRoster.TryGet(drone, out DroneRoster.Entry entry) ||
                !GameManager.GetLocalPlayer(out Player player) || !entry.VisibleTo(player)) return false;
            if (GameAccess.IsServer()) return ApplyDroneAction(player, entry, action);
            return Send(new VanguardDroneOrder { Protocol = ProtocolVersion, Action = (byte)action, UnitId = drone.persistentID.Id });
        }

        internal static bool SendDroneControls(Missile drone, Vector3 axes, float throttle)
        {
            if (!DroneRoster.TryGet(drone, out DroneRoster.Entry entry) ||
                !GameManager.GetLocalPlayer(out Player player) || !entry.ControlledBy(player)) return false;
            var message = new VanguardDroneControls
            {
                Protocol = ProtocolVersion, DroneId = drone.persistentID.Id, Lease = entry.Lease, Sequence = ++entry.SentSequence,
                Pitch = axes.x, Yaw = axes.y, Roll = axes.z, Throttle = throttle,
            };
            if (GameAccess.IsServer()) return ApplyControls(player, entry, message);
            return Send(message, Channel.Unreliable);
        }

        private static bool Send<T>(T message, Channel channel = Channel.Reliable)
        {
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            if (client == null || !client.Active || !client.IsConnected || client.Player == null || !client.Player.IsAuthenticated) return false;
            client.Send(message, channel);
            return true;
        }

        private static bool LocalTarget(out Aircraft aircraft, out Unit target)
        {
            target = null;
            if (!GameManager.GetLocalAircraft(out aircraft) || aircraft == null || aircraft.weaponManager == null) return false;
            List<Unit> targets = aircraft.weaponManager.GetTargetList();
            target = targets != null && targets.Count > 0 ? targets[0] : null;
            return target != null && !target.disabled;
        }

        private void Update()
        {
            if (!NetFeed.Poll(ServerHandlers, ClientHandlers, ref nextRegistration)) return;
            DroneRoster.Prune();
            float now = Time.unscaledTime;
            throttle.Prune(now);
            inputThrottle.Prune(now);
            if (!GameAccess.IsServer()) return;
            bool resend = now >= nextResend;
            if (resend) nextResend = now + ResendInterval;
            foreach (DroneRoster.Entry entry in DroneRoster.LiveEntries)
            {
                bool released = entry.Occupied && (!entry.ValidPilot || entry.InputExpired(now));
                if (released) entry.SetPilot(null);
                if (released || resend && entry.OwnerId != 0) Publish(entry);
            }
        }

        private void Receive(INetworkPlayer sender, VanguardDroneOrder message)
        {
            if (!TrySender(sender, message.Protocol, out Player player) || message.Action > (byte)DroneAction.Release) return;
            DroneAction action = (DroneAction)message.Action;
            if (!throttle.Allow(DroneRoster.PlayerId(player), Time.unscaledTime, OrderInterval)) return;
            if (action >= DroneAction.Share)
            {
                if (FindDrone(message.UnitId, out DroneRoster.Entry entry)) ApplyDroneAction(player, entry, action);
                return;
            }

            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            if (action == DroneAction.RamOn || action == DroneAction.RamOff)
            {
                DroneOrders.SetRamMode(aircraft, action == DroneAction.RamOn);
                return;
            }
            Unit target = null;
            if (action == DroneAction.Strike &&
                (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.UnitId }, out target) || target == null ||
                    target.disabled || target.NetworkHQ == aircraft.NetworkHQ || aircraft.NetworkHQ == null ||
                    !aircraft.NetworkHQ.TryGetKnownPosition(target, out _))) return;
            DroneOrders.Order(aircraft, target);
        }

        private static bool ApplyDroneAction(Player player, DroneRoster.Entry entry, DroneAction action)
        {
            if (!entry.VisibleTo(player)) return false;
            switch (action)
            {
                case DroneAction.Share:
                case DroneAction.Private:
                    if (!entry.Owner(player)) return false;
                    entry.Shared = action == DroneAction.Share;
                    if (!entry.Shared && entry.Occupied && !entry.Owner(entry.Pilot)) entry.SetPilot(null);
                    break;
                case DroneAction.Pilot:
                    if (entry.ControlledBy(player)) return true;
                    if (entry.Occupied || !DroneRoster.InRange(player, entry) || DroneRoster.ControlledBy(player) != null) return false;
                    entry.SetPilot(player);
                    break;
                case DroneAction.Release:
                    if (!entry.ControlledBy(player)) return false;
                    entry.SetPilot(null);
                    break;
                default:
                    return false;
            }
            Publish(entry);
            return true;
        }

        private void ReceiveControls(INetworkPlayer sender, VanguardDroneControls message)
        {
            if (!TrySender(sender, message.Protocol, out Player player) || !FindDrone(message.DroneId, out DroneRoster.Entry entry) ||
                !entry.ControlledBy(player) || !inputThrottle.Allow(DroneRoster.PlayerId(player), Time.unscaledTime, InputInterval)) return;
            ApplyControls(player, entry, message);
        }

        private static bool ApplyControls(Player player, DroneRoster.Entry entry, VanguardDroneControls message)
        {
            if (!entry.ControlledBy(player) || !entry.ValidPilot || message.Lease != entry.Lease ||
                !Axis(message.Pitch) || !Axis(message.Roll) || !Axis(message.Yaw) ||
                !(message.Throttle >= 0f && message.Throttle <= 1f) || !entry.AcceptInput(message.Sequence, Time.unscaledTime)) return false;
            DronePilot.Receive(entry.Missile, player, new Vector3(message.Pitch, message.Yaw, message.Roll), message.Throttle);
            return true;
        }

        private static bool Axis(float value) => value >= -1f && value <= 1f;

        private static bool TrySender(INetworkPlayer sender, byte protocol, out Player player)
        {
            player = null;
            return protocol == ProtocolVersion && sender != null && sender.IsAuthenticated && sender.IsConnected &&
                sender.TryGetPlayer(out player) && player != null && DroneRoster.PlayerId(player) != 0;
        }

        private static bool FindDrone(uint id, out DroneRoster.Entry entry)
        {
            entry = null;
            return UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) &&
                unit is Missile drone && DroneRoster.TryGet(drone, out entry);
        }

        private static void Publish(DroneRoster.Entry entry)
        {
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;
            var state = new VanguardDroneState
            {
                Protocol = ProtocolVersion, DroneId = entry.Missile.persistentID.Id, Shared = entry.Shared,
                OwnerId = entry.OwnerId, PilotId = entry.PilotId, Lease = entry.Lease,
            };
            IReadOnlyList<INetworkPlayer> peers = server.AuthenticatedPlayers;
            for (int i = 0; i < peers.Count; i++)
            {
                INetworkPlayer peer = peers[i];
                if (peer != null && peer != server.LocalPlayer && peer.IsConnected &&
                    peer.TryGetPlayer<Player>(out Player player) && entry.Friendly(player)) peer.Send(state);
            }
        }

        private void ReceiveState(INetworkPlayer sender, VanguardDroneState message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer() || !FindDrone(message.DroneId, out DroneRoster.Entry entry)) return;
            entry.Mirror(message.OwnerId, message.PilotId, message.Lease, message.Shared);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
            DroneRoster.Clear();
        }
    }

    /// <summary>What every Vanguard net service repeats: the 2 Hz handler poll and the host-to-clients send.</summary>
    internal static class NetFeed
    {
        /// <summary>Client side: at most every half second, moves the registrations onto the live connection (or off a dead one).</summary>
        internal static void Poll(HandlerSlot slot, ref float next)
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            slot.Swap(GameAccess.NetworkManagerOrNull?.Client?.MessageHandler);
        }

        /// <summary>Both sides (a service that also takes client intents): same cadence; true on the frames it ran, so the caller can prune its throttle then.</summary>
        internal static bool Poll(HandlerSlot server, HandlerSlot client, ref float next)
        {
            if (Time.unscaledTime < next) return false;
            next = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            server.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            client.Swap(network?.Client?.MessageHandler);
            return true;
        }

        /// <summary>Host to every authenticated client but its own peer (the host shows its own side directly).</summary>
        internal static void Broadcast<T>(T message)
        {
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server != null && server.Active) server.SendToAll(message, authenticatedOnly: true, excludeLocalPlayer: true);
        }
    }
}

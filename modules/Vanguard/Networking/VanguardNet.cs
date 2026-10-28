using System;
using System.Collections.Generic;
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
    internal struct VanguardDroneOrder
    {
        public byte Protocol;
        public bool Strike;
        public uint TargetId;
        /// <summary>A drone link order: DroneMode + 1 for the drone <see cref="DroneId"/> (WPT at X/Y/Z); 0 = a designation order.</summary>
        public byte Link;
        public uint DroneId;
        public float X, Y, Z;
    }

    /// <summary>
    /// Client intent for the Vanguard designation bus (DroneOrders) and the drone link (SteerOrders). The host resolves the
    /// launcher from the sender's own aircraft (never from the message), the target and the drone from their persistent ids,
    /// and only takes a drone the sender launched.
    /// </summary>
    internal sealed class VanguardNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 5; // 4: a HAND order carries the course (X, degrees) and altitude (Y); 5: RAM MODE (Link = RamLink)
        internal const byte RamLink = 200;      // Link value of a RAM MODE switch: X > 0.5 = on
        private const float OrderInterval = 0.2f; // per aircraft; a re-designate right after DESIGNATE must not be eaten

        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<VanguardDroneOrder>(Receive);
        private float nextRegistration;

        private Unit designated; // local view of the last order; lapses when that target dies
        private Aircraft designatedFrom; // the local aircraft that sent it: a respawn starts clean

        public bool Striking => designated != null && !designated.disabled && designatedFrom != null && !designatedFrom.disabled &&
            GameManager.GetLocalAircraft(out Aircraft me) && me == designatedFrom;

        public bool CanStrike => LocalTarget(out _, out _);

        /// <summary>RAM MODE as this client last set it (local view; the host holds the rule in DroneOrders).</summary>
        public bool RamMode => ramMode && ramFrom != null && !ramFrom.disabled && GameManager.GetLocalAircraft(out Aircraft me) && me == ramFrom;
        private bool ramMode;
        private Aircraft ramFrom;

        /// <summary>The local pilot's first selected target, else null (DRONE LINK's SEND TO TARGET).</summary>
        public Unit Target => LocalTarget(out _, out Unit target) ? target : null;

        /// <summary>The local pilot's live designation (what the drones work on), else null: the UAV feed tracks and boxes it.</summary>
        public Unit Designated => Striking ? designated : null;

        private void Awake()
        {
            MirageSerializers.Strict.Install<VanguardDroneOrder>(
                (Action<NetworkWriter, VanguardDroneOrder>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WriteBoolean(value.Strike);
                    writer.WritePackedUInt32(value.TargetId);
                    writer.WriteByte(value.Link);
                    if (value.Link == 0) return;
                    writer.WritePackedUInt32(value.DroneId);
                    writer.WriteSingle(value.X);
                    writer.WriteSingle(value.Y);
                    writer.WriteSingle(value.Z);
                }),
                (Func<NetworkReader, VanguardDroneOrder>)(reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new VanguardDroneOrder { Protocol = protocol };
                    var order = new VanguardDroneOrder
                    {
                        Protocol = protocol,
                        Strike = reader.ReadBoolean(),
                        TargetId = reader.ReadPackedUInt32(),
                        Link = reader.ReadByte(),
                    };
                    if (order.Link == 0) return order;
                    order.DroneId = reader.ReadPackedUInt32();
                    order.X = reader.ReadSingle();
                    order.Y = reader.ReadSingle();
                    order.Z = reader.ReadSingle();
                    return order;
                }));
        }

        public void ResetForScene()
        {
            designated = null;
            designatedFrom = null;
            ramMode = false;
            ramFrom = null;
            throttle.Clear();
            pendingLinks.Clear();
        }

        public void Strike()
        {
            if (!LocalTarget(out Aircraft aircraft, out Unit target)) return;
            designated = target;
            designatedFrom = aircraft;
            Send(aircraft, true, target);
        }

        public void Screen()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return;
            designated = null;
            designatedFrom = null;
            Send(aircraft, false, null);
        }

        /// <summary>RAM MODE on or off for the local aircraft's REMORAs: they ram instead of firing.</summary>
        public void SetRamMode(bool on)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return;
            ramMode = on;
            ramFrom = aircraft;
            if (GameAccess.IsServer()) { DroneOrders.SetRamMode(aircraft, on); return; }
            NetworkManagerNuclearOption.i?.Client?.Send(new VanguardDroneOrder { Protocol = ProtocolVersion, Link = RamLink, X = on ? 1f : 0f });
        }

        /// <summary>A drone link order for one of the local aircraft's drones (AUTO releases it); the host applies it to SteerOrders.</summary>
        public void SendLink(Missile drone, DroneMode mode, GlobalPosition point)
        {
            if (drone == null || !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return;
            if (GameAccess.IsServer())
            {
                if (Linkable(aircraft, drone)) SteerOrders.Set(drone, mode, point);
                return;
            }
            // The host takes one order per aircraft per OrderInterval and drops the rest without a word, while this client's mirror
            // already shows them: queue per drone (the newest wins) and send at the host's pace (Flush).
            pendingLinks[drone] = new VanguardDroneOrder
            {
                Protocol = ProtocolVersion,
                Link = (byte)(mode + 1),
                DroneId = drone.persistentID.Id,
                X = point.x, Y = point.y, Z = point.z,
            };
            Flush();
        }

        private readonly Dictionary<Missile, VanguardDroneOrder> pendingLinks = new Dictionary<Missile, VanguardDroneOrder>();
        private float nextLinkSend;

        private void Flush()
        {
            if (pendingLinks.Count == 0 || Time.unscaledTime < nextLinkSend) return;
            Missile first = null;
            foreach (Missile m in pendingLinks.Keys) { first = m; break; }
            VanguardDroneOrder order = pendingLinks[first];
            pendingLinks.Remove(first);
            if (first == null || first.disabled) return;
            nextLinkSend = Time.unscaledTime + OrderInterval + 0.05f;
            NetworkManagerNuclearOption.i?.Client?.Send(order);
        }

        /// <summary>The sender's own live, steerable drone inside link range.</summary>
        private static bool Linkable(Aircraft aircraft, Missile drone) =>
            drone != null && !drone.disabled && drone.owner == aircraft && drone.definition != null &&
            VanguardKeys.IsSteerable(VanguardKeys.RoleOf(drone.definition.jsonKey)) &&
            DroneLink.InRange(FastMath.Distance(aircraft.GlobalPosition(), drone.GlobalPosition()), WatchtowerPod.LinkScale(aircraft));

        private static bool LocalTarget(out Aircraft aircraft, out Unit target)
        {
            target = null;
            if (!GameManager.GetLocalAircraft(out aircraft) || aircraft == null || aircraft.weaponManager == null) return false;
            List<Unit> targets = aircraft.weaponManager.GetTargetList();
            target = targets != null && targets.Count > 0 ? targets[0] : null;
            return target != null && !target.disabled;
        }

        private static void Send(Aircraft aircraft, bool strike, Unit target)
        {
            if (GameAccess.IsServer())
            {
                DroneOrders.Order(aircraft, strike ? target : null);
                return;
            }
            NetworkManagerNuclearOption.i?.Client?.Send(new VanguardDroneOrder
            {
                Protocol = ProtocolVersion,
                Strike = strike,
                TargetId = target != null ? target.persistentID.Id : 0u,
            });
        }

        private void Update()
        {
            Flush();
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            ServerHandlers.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            throttle.Prune(Time.unscaledTime);
        }

        private void Receive(INetworkPlayer sender, VanguardDroneOrder message)
        {
            if (message.Protocol != ProtocolVersion || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            if (!throttle.Allow((ulong)aircraft.persistentID.Id, Time.unscaledTime, OrderInterval)) return;
            if (message.Link == RamLink)
            {
                DroneOrders.SetRamMode(aircraft, message.X > 0.5f);
                return;
            }
            if (message.Link > 0)
            {
                if (message.Link > (byte)DroneMode.Waypoint + 1 || !float.IsFinite(message.X) || !float.IsFinite(message.Y) || !float.IsFinite(message.Z)) return;
                if (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.DroneId }, out Unit unit) || !(unit is Missile drone) ||
                    !Linkable(aircraft, drone)) return;
                SteerOrders.Set(drone, (DroneMode)(message.Link - 1), new GlobalPosition(message.X, message.Y, message.Z));
                return;
            }
            Unit target = null;
            if (message.Strike && !UnitRegistry.TryGetUnit(new PersistentID { Id = message.TargetId }, out target)) return;
            if (target != null && (target.disabled || target.NetworkHQ == aircraft.NetworkHQ)) return;
            // Only a unit the pilot's faction already tracks: the drones fly to the target's true position.
            if (target != null && (aircraft.NetworkHQ == null || !aircraft.NetworkHQ.TryGetKnownPosition(target, out _))) return;
            DroneOrders.Order(aircraft, target);
        }

        private void OnDestroy() => ServerHandlers.Release();
    }

    /// <summary>What every Vanguard net service repeats: the 2 Hz handler poll and the host-to-clients send.</summary>
    internal static class NetFeed
    {
        /// <summary>Client side: at most every half second, moves the registrations onto the live connection (or off a dead one).</summary>
        internal static void Poll(HandlerSlot slot, ref float next)
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            slot.Swap(NetworkManagerNuclearOption.i?.Client?.MessageHandler);
        }

        /// <summary>Both sides (a service that also takes client intents): same cadence; true on the frames it ran, so the caller can prune its throttle then.</summary>
        internal static bool Poll(HandlerSlot server, HandlerSlot client, ref float next)
        {
            if (Time.unscaledTime < next) return false;
            next = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            server.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            client.Swap(network?.Client?.MessageHandler);
            return true;
        }

        /// <summary>Host to every authenticated client but its own peer (the host shows its own side directly).</summary>
        internal static void Broadcast<T>(T message)
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server != null && server.Active) server.SendToAll(message, authenticatedOnly: true, excludeLocalPlayer: true);
        }
    }
}

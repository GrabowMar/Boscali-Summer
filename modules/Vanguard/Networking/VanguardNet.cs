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
        internal const byte ProtocolVersion = 3;
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
            throttle.Clear();
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

        /// <summary>A drone link order for one of the local aircraft's drones (AUTO releases it); the host applies it to SteerOrders.</summary>
        public void SendLink(Missile drone, DroneMode mode, GlobalPosition point)
        {
            if (drone == null || !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return;
            if (GameAccess.IsServer())
            {
                if (Linkable(aircraft, drone)) SteerOrders.Set(drone, mode, point);
                return;
            }
            NetworkManagerNuclearOption.i?.Client?.Send(new VanguardDroneOrder
            {
                Protocol = ProtocolVersion,
                Link = (byte)(mode + 1),
                DroneId = drone.persistentID.Id,
                X = point.x, Y = point.y, Z = point.z,
            });
        }

        /// <summary>The sender's own live, steerable drone inside link range.</summary>
        private static bool Linkable(Aircraft aircraft, Missile drone) =>
            drone != null && !drone.disabled && drone.owner == aircraft && drone.definition != null &&
            VanguardKeys.IsSteerable(VanguardKeys.RoleOf(drone.definition.jsonKey)) &&
            DroneLink.InRange(FastMath.Distance(aircraft.GlobalPosition(), drone.GlobalPosition()));

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
            if (message.Link > 0)
            {
                if (message.Link > (byte)DroneMode.Waypoint + 1 || !Finite(message.X) || !Finite(message.Y) || !Finite(message.Z)) return;
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

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private void OnDestroy() => ServerHandlers.Release();
    }
}

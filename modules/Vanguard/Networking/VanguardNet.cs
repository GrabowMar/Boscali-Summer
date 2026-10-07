using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
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
    internal struct VanguardDroneOrder
    {
        public byte Protocol;
        public bool Strike;
        public uint TargetId;
    }

    /// <summary>
    /// Client intent for REMORA orders. The host resolves the launcher from the sender's own
    /// aircraft (never from the message) and the target from its persistent id.
    /// </summary>
    internal sealed class VanguardNet : MonoBehaviour, ISceneService, IDroneCommand
    {
        internal const byte ProtocolVersion = 1;
        private const float OrderInterval = 0.5f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<VanguardDroneOrder>(Receive);
        private float nextRegistration;

        public bool Striking { get; private set; }

        public bool CanStrike => LocalTarget(out _, out _);

        private void Awake()
        {
            MirageSerializers.Strict.Install<VanguardDroneOrder>(
                (Action<NetworkWriter, VanguardDroneOrder>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WriteBoolean(value.Strike);
                    writer.WritePackedUInt32(value.TargetId);
                }),
                (Func<NetworkReader, VanguardDroneOrder>)(reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new VanguardDroneOrder { Protocol = protocol };
                    return new VanguardDroneOrder
                    {
                        Protocol = protocol,
                        Strike = reader.ReadBoolean(),
                        TargetId = reader.ReadPackedUInt32(),
                    };
                }));
        }

        public void ResetForScene()
        {
            Striking = false;
            throttle.Clear();
        }

        public void Strike()
        {
            if (!LocalTarget(out Aircraft aircraft, out Unit target)) return;
            Striking = true;
            Send(aircraft, true, target);
        }

        public void Screen()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return;
            Striking = false;
            Send(aircraft, false, null);
        }

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
            Unit target = null;
            if (message.Strike && !UnitRegistry.TryGetUnit(new PersistentID { Id = message.TargetId }, out target)) return;
            if (target != null && (target.disabled || target.NetworkHQ == aircraft.NetworkHQ)) return;
            DroneOrders.Order(aircraft, target);
        }

        private void OnDestroy() => ServerHandlers.Release();
    }
}

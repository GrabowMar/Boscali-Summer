using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>Client intent: this pilot's landing zone moved (HasZone) or cleared.</summary>
    [NetworkMessage]
    internal struct LandingZoneOrder
    {
        public byte Protocol;
        public bool HasZone;
        public float X, Z;
    }

    /// <summary>The host applied a pilot's zone (ground height resolved): every peer keeps the same aim for that launcher's drops.</summary>
    [NetworkMessage]
    internal struct LandingZoneEvent
    {
        public byte Protocol;
        public uint AircraftId;
        public bool HasZone;
        public float X, Y, Z;
    }

    /// <summary>
    /// Landing-zone transport (see <see cref="ILandingZone"/>). The host resolves the launcher
    /// from the sender's own aircraft, never from the message, resolves the ground height, and
    /// broadcasts the result so HALO sticks plan to the same point on every peer. Clients only
    /// ever steer their own zone; the marker stays local.
    /// </summary>
    internal sealed class LandingZoneNet : MonoBehaviour, ISceneService, ILandingZone
    {
        internal const byte ProtocolVersion = 1;
        private const float OrderInterval = 0.5f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<LandingZoneOrder>(ReceiveOrder);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<LandingZoneEvent>(ReceiveEvent);
        private float nextRegistration;

        private GlobalPosition? local; // local view of the last order; lapses when that aircraft does
        private Aircraft localFrom;

        public bool HasLocal => TryGetLocal(out _);

        public bool TryGetLocal(out GlobalPosition point)
        {
            point = default;
            if (!local.HasValue || localFrom == null || localFrom.disabled) return false;
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me != localFrom) return false;
            point = local.Value;
            return true;
        }

        private void Awake()
        {
            MirageSerializers.Strict.Install<LandingZoneOrder>(
                (Action<NetworkWriter, LandingZoneOrder>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WriteBoolean(value.HasZone);
                    writer.WriteSingle(value.X);
                    writer.WriteSingle(value.Z);
                }),
                (Func<NetworkReader, LandingZoneOrder>)(reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new LandingZoneOrder { Protocol = protocol };
                    return new LandingZoneOrder
                    {
                        Protocol = protocol, HasZone = reader.ReadBoolean(),
                        X = reader.ReadSingle(), Z = reader.ReadSingle(),
                    };
                }));
            MirageSerializers.Strict.Install<LandingZoneEvent>(
                (Action<NetworkWriter, LandingZoneEvent>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WritePackedUInt32(value.AircraftId);
                    writer.WriteBoolean(value.HasZone);
                    writer.WriteSingle(value.X);
                    writer.WriteSingle(value.Y);
                    writer.WriteSingle(value.Z);
                }),
                (Func<NetworkReader, LandingZoneEvent>)(reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new LandingZoneEvent { Protocol = protocol };
                    return new LandingZoneEvent
                    {
                        Protocol = protocol, AircraftId = reader.ReadPackedUInt32(), HasZone = reader.ReadBoolean(),
                        X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(),
                    };
                }));
        }

        public void ResetForScene()
        {
            local = null;
            localFrom = null;
            throttle.Clear();
            LandingZones.Clear();
        }

        public void SetLocal(GlobalPosition point)
        {
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled) return;
            local = point;
            localFrom = aircraft;
            LandingZones.Order(aircraft, point); // own drops plan at once; the host's broadcast re-orders the same point
            Send(aircraft, true, point.x, point.z);
        }

        public void ClearLocal()
        {
            local = null;
            localFrom = null;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled) return;
            LandingZones.Order(aircraft, null);
            Send(aircraft, false, 0f, 0f);
        }

        public float ReachM(bool halo, float heightAboveZone) => halo ? LandingZoneReach.Halo(heightAboveZone) : LandingZoneReach.Glider(heightAboveZone);

        private void Send(Aircraft aircraft, bool hasZone, float x, float z)
        {
            if (GameAccess.IsServer())
            {
                Apply(aircraft, hasZone, x, z);
                return;
            }
            NetworkManagerNuclearOption.i?.Client?.Send(new LandingZoneOrder
            {
                Protocol = ProtocolVersion, HasZone = hasZone, X = x, Z = z,
            });
        }

        /// <summary>Host: store the zone at ground height and tell every other peer.</summary>
        private void Apply(Aircraft aircraft, bool hasZone, float x, float z)
        {
            if (hasZone && (!float.IsFinite(x) || !float.IsFinite(z))) return;
            float y = hasZone ? AirAssaultController.HaloGroundAt(new Vector3(x, 0f, z)) : 0f;
            LandingZones.Order(aircraft, hasZone ? new GlobalPosition(x, y, z) : (GlobalPosition?)null);
            ResolveLocalHeight(aircraft, hasZone, x, y, z);
            Plugin.Logger.LogInfo(hasZone
                ? $"[Landing Zone] {aircraft.name} aims at ({x:0}, {z:0})."
                : $"[Landing Zone] {aircraft.name} cleared.");
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new LandingZoneEvent
            {
                Protocol = ProtocolVersion, AircraftId = aircraft.persistentID.Id,
                HasZone = hasZone, X = x, Y = y, Z = z,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            ServerHandlers.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            ClientHandlers.Swap(network?.Client != null && network.Client.Active ? network.Client.MessageHandler : null);
            throttle.Prune(Time.unscaledTime);
        }

        private void ReceiveOrder(INetworkPlayer sender, LandingZoneOrder message)
        {
            if (message.Protocol != ProtocolVersion || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            if (!throttle.Allow((ulong)aircraft.persistentID.Id, Time.unscaledTime, OrderInterval)) return;
            Apply(aircraft, message.HasZone, message.X, message.Z);
        }

        private void ReceiveEvent(INetworkPlayer sender, LandingZoneEvent message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.AircraftId }, out Unit unit)) return;
            LandingZones.Order(unit, message.HasZone
                ? new GlobalPosition(message.X, message.Y, message.Z)
                : (GlobalPosition?)null);
            ResolveLocalHeight(unit, message.HasZone, message.X, message.Y, message.Z);
        }

        /// <summary>Apply the host's ground height only to the local pilot's still-current order.</summary>
        private void ResolveLocalHeight(Unit launcher, bool hasZone, float x, float y, float z)
        {
            if (!hasZone || localFrom != launcher || !local.HasValue ||
                local.Value.x != x || local.Value.z != z || !float.IsFinite(y)) return;
            local = new GlobalPosition(x, y, z);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }
    }
}

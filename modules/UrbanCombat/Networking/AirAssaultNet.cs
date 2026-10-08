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
    [NetworkMessage]
    internal struct RopeExfilRequest
    {
        public byte Protocol;
    }

    /// <summary>The host extracted a squad: every peer refills that helicopter's bench and shows the ride.</summary>
    [NetworkMessage]
    internal struct RopeExfilEvent
    {
        public byte Protocol;
        public uint AircraftId;
        public float X, Y, Z;
        public byte Count;
        public float Height;
    }

    /// <summary>
    /// SPIES exfil transport. Vanilla never syncs weapon ammo (each peer replays fires), so a
    /// refill needs this one broadcast. The host resolves the helicopter from the sender's own
    /// aircraft, never from the message, and validates hover, bench room and the position below.
    /// </summary>
    internal sealed class AirAssaultNet : MonoBehaviour, ISceneService, IAirAssaultOrders
    {
        internal const byte ProtocolVersion = 1;
        private const float RequestInterval = 2f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<RopeExfilRequest>(ReceiveRequest);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<RopeExfilEvent>(ReceiveEvent);
        private float nextRegistration;

        public bool CanRequestExfil =>
            GameManager.GetLocalAircraft(out Aircraft aircraft) && AirAssaultController.Instance?.CanExfil(aircraft) == true;

        private void Awake()
        {
            MirageSerializers.Strict.Install<RopeExfilRequest>(
                (Action<NetworkWriter, RopeExfilRequest>)((writer, value) => writer.WriteByte(value.Protocol)),
                (Func<NetworkReader, RopeExfilRequest>)(reader => new RopeExfilRequest { Protocol = reader.ReadByte() }));
            MirageSerializers.Strict.Install<RopeExfilEvent>(
                (Action<NetworkWriter, RopeExfilEvent>)((writer, value) =>
                {
                    writer.WriteByte(value.Protocol);
                    writer.WritePackedUInt32(value.AircraftId);
                    writer.WriteSingle(value.X);
                    writer.WriteSingle(value.Y);
                    writer.WriteSingle(value.Z);
                    writer.WriteByte(value.Count);
                    writer.WriteSingle(value.Height);
                }),
                (Func<NetworkReader, RopeExfilEvent>)(reader =>
                {
                    byte protocol = reader.ReadByte();
                    if (protocol != ProtocolVersion) return new RopeExfilEvent { Protocol = protocol };
                    return new RopeExfilEvent
                    {
                        Protocol = protocol, AircraftId = reader.ReadPackedUInt32(),
                        X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(),
                        Count = reader.ReadByte(), Height = reader.ReadSingle(),
                    };
                }));
        }

        public void ResetForScene() => throttle.Clear();

        public void RequestExfil()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return;
            if (GameAccess.IsServer())
            {
                Exfil(aircraft);
                return;
            }
            NetworkManagerNuclearOption.i?.Client?.Send(new RopeExfilRequest { Protocol = ProtocolVersion });
        }

        private static void Exfil(Aircraft aircraft)
        {
            AirAssaultController controller = AirAssaultController.Instance;
            if (controller == null || !controller.TryExfil(aircraft, out Vector3 site, out int count, out float height)) return;
            controller.PlayExfil(aircraft, site, count, height);
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new RopeExfilEvent
            {
                Protocol = ProtocolVersion, AircraftId = aircraft.persistentID.Id,
                X = site.x, Y = site.y, Z = site.z, Count = (byte)count, Height = height,
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

        private void ReceiveRequest(INetworkPlayer sender, RopeExfilRequest message)
        {
            if (message.Protocol != ProtocolVersion || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled) return;
            if (!throttle.Allow((ulong)aircraft.persistentID.Id, Time.unscaledTime, RequestInterval)) return;
            Exfil(aircraft);
        }

        private void ReceiveEvent(INetworkPlayer sender, RopeExfilEvent message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.AircraftId }, out Unit unit) || !(unit is Aircraft aircraft)) return;
            AirAssaultController.Instance?.PlayExfil(aircraft, new Vector3(message.X, message.Y, message.Z), message.Count, message.Height);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }
    }
}

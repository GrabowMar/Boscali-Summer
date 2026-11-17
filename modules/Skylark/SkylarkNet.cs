using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    [NetworkMessage]
    internal struct SkylarkIntent
    {
        public byte Protocol;
        public byte Action; // SkylarkNet.DriveAction / MastAction
        public byte Value;
    }

    [NetworkMessage]
    internal struct SkylarkState
    {
        public byte Protocol;
        public uint Aircraft;
        public byte Drive; // DriveMode
        public byte Mast;  // 0 stowed, 1 out
        public byte Charge; // battery 0..255
    }

    /// <summary>
    /// SKYLARK wire, copied from SKYWELL's: the pilot's drive-mode and C2 mast choices go to the host as intents (it resolves the
    /// aircraft from the sender, never from the message), the host applies them and sends each Skylark's state to every client
    /// (again every few seconds, for late joiners), so the host's seekers and every peer's sound and visuals agree.
    /// Fan tilt needs no message: it travels in the aircraft's own replicated inputs.
    /// </summary>
    internal sealed class SkylarkNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 2, DriveAction = 0, MastAction = 1;
        private const float IntentInterval = .4f, ResendInterval = 5f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<SkylarkIntent>(ReceiveIntent);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<SkylarkState>(ReceiveState);
        private float nextRegistration, nextResend;

        private void Awake()
        {
            MirageSerializers.Strict.Install<SkylarkIntent>(
                (Action<NetworkWriter, SkylarkIntent>)((w, v) => { w.WriteByte(v.Protocol); w.WriteByte(v.Action); w.WriteByte(v.Value); }),
                (Func<NetworkReader, SkylarkIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new SkylarkIntent { Protocol = protocol }
                        : new SkylarkIntent { Protocol = protocol, Action = r.ReadByte(), Value = r.ReadByte() };
                }));
            MirageSerializers.Strict.Install<SkylarkState>(
                (Action<NetworkWriter, SkylarkState>)((w, v) => { w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Aircraft); w.WriteByte(v.Drive); w.WriteByte(v.Mast); w.WriteByte(v.Charge); }),
                (Func<NetworkReader, SkylarkState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new SkylarkState { Protocol = protocol }
                        : new SkylarkState { Protocol = protocol, Aircraft = r.ReadPackedUInt32(), Drive = r.ReadByte(), Mast = r.ReadByte(), Charge = r.ReadByte() };
                }));
        }

        public void ResetForScene() => throttle.Clear();

        /// <summary>Owner peer: a drive or mast choice for its own aircraft. The host applies it at once, a client asks the host.</summary>
        internal static void Request(Aircraft aircraft, byte action, byte value)
        {
            if (GameAccess.IsServer()) Apply(aircraft, action, value);
            else GameAccess.NetworkManagerOrNull?.Client?.Send(new SkylarkIntent { Protocol = ProtocolVersion, Action = action, Value = value });
        }

        private static void Apply(Aircraft aircraft, byte action, byte value)
        {
            if (aircraft == null) return;
            if (action == DriveAction && value <= (byte)DriveMode.Hybrid) aircraft.GetComponent<SkylarkDrive>()?.SetMode((DriveMode)value);
            else if (action == MastAction) aircraft.GetComponent<SkylarkMast>()?.Set(value != 0);
            else return;
            Broadcast(aircraft);
        }

        internal static void Broadcast(Aircraft aircraft)
        {
            SkylarkDrive drive = aircraft.GetComponent<SkylarkDrive>();
            SkylarkMast mast = aircraft.GetComponent<SkylarkMast>();
            if (drive == null) return;
            Mirage.NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server != null && server.Active)
                server.SendToAll(new SkylarkState { Protocol = ProtocolVersion, Aircraft = aircraft.persistentID.Id, Drive = (byte)drive.Mode, Mast = (byte)(mast != null && mast.Deployed ? 1 : 0), Charge = (byte)Mathf.RoundToInt(drive.Charge * 255f) },
                    authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + .5f;
            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            ServerHandlers.Swap(network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null);
            ClientHandlers.Swap(network?.Client?.MessageHandler);
            throttle.Prune(Time.unscaledTime);
            if (Time.unscaledTime < nextResend || !GameAccess.IsServer()) return;
            nextResend = Time.unscaledTime + ResendInterval;
            foreach (SkylarkDrive drive in SkylarkDrive.All) Broadcast(drive.GetComponent<Aircraft>());
        }

        private void ReceiveIntent(INetworkPlayer sender, SkylarkIntent message)
        {
            if (message.Protocol != ProtocolVersion || !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft;
            if (aircraft == null || aircraft.disabled || aircraft.definition == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return;
            if (!throttle.Allow(((ulong)aircraft.persistentID.Id << 8) | message.Action, Time.unscaledTime, IntentInterval)) return;
            Apply(aircraft, message.Action, message.Value);
        }

        private void ReceiveState(INetworkPlayer sender, SkylarkState message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.Aircraft }, out Unit unit) || !(unit is Aircraft aircraft)) return;
            SkylarkDrive drive = aircraft.GetComponent<SkylarkDrive>();
            drive?.SyncCharge(message.Charge / 255f);
            if (message.Drive <= (byte)DriveMode.Hybrid) drive?.SetMode((DriveMode)message.Drive);
            aircraft.GetComponent<SkylarkMast>()?.Set(message.Mast != 0);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }
    }
}

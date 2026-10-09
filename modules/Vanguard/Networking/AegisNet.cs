using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Presentation;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    /// <summary>Server-side AEGIS moments the carrier's pilot hears about on the HUD.</summary>
    internal enum AegisCue : byte { Intercept = 1, SnapHit = 2 }

    [NetworkMessage]
    internal struct AegisEvent
    {
        public byte Protocol;
        public uint LauncherId;
        public byte Cue;
    }

    /// <summary>Server-to-client HUD cues only; a client cannot fire or score anything through this.</summary>
    internal sealed class AegisNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<AegisEvent>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<AegisEvent>(
                (Action<NetworkWriter, AegisEvent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.LauncherId);
                    w.WriteByte(v.Cue);
                }),
                (Func<NetworkReader, AegisEvent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new AegisEvent { Protocol = protocol };
                    return new AegisEvent { Protocol = protocol, LauncherId = r.ReadPackedUInt32(), Cue = r.ReadByte() };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + .5f;
            ClientHandlers.Swap(NetworkManagerNuclearOption.i?.Client?.MessageHandler);
        }

        /// <summary>Server: show the cue to the launcher's pilot, wherever that pilot sits.</summary>
        internal static void Raise(Unit launcher, AegisCue cue)
        {
            if (launcher == null) return;
            AegisHud.Cue(launcher, cue); // host's own aircraft
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new AegisEvent { Protocol = ProtocolVersion, LauncherId = launcher.persistentID.Id, Cue = (byte)cue },
                authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void Receive(INetworkPlayer sender, AegisEvent message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            if (message.Cue != (byte)AegisCue.Intercept && message.Cue != (byte)AegisCue.SnapHit) return;
            if (UnitRegistry.TryGetUnit(new PersistentID { Id = message.LauncherId }, out Unit unit))
                AegisHud.Cue(unit, (AegisCue)message.Cue);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}

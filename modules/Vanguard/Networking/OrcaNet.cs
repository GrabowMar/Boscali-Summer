using BoscaliSummer.Modules.Vanguard.Presentation;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct OrcaNotice
    {
        public byte Protocol, Call;
        public uint OwnerId, SubjectId;
        public float Km;
    }

    /// <summary>
    /// ORCA HUD feed (channel vg.orca). The server reports torpedo events; the launcher's own peer shows them.
    /// Server-to-client only: no client message is ever handled.
    /// </summary>
    internal sealed class OrcaNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        private const string Channel = "vg.orca";
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<OrcaNotice>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<OrcaNotice>(
                (Action<NetworkWriter, OrcaNotice>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WriteByte(v.Call);
                    w.WritePackedUInt32(v.OwnerId);
                    w.WritePackedUInt32(v.SubjectId);
                    w.WriteSingle(v.Km);
                }),
                (Func<NetworkReader, OrcaNotice>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new OrcaNotice { Protocol = protocol };
                    return new OrcaNotice
                    {
                        Protocol = protocol, Call = r.ReadByte(), OwnerId = r.ReadPackedUInt32(),
                        SubjectId = r.ReadPackedUInt32(), Km = r.ReadSingle(),
                    };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + .5f;
            ClientHandlers.Swap(NetworkManagerNuclearOption.i?.Client?.MessageHandler);
        }

        /// <summary>Server: tell the launcher's pilot (local or remote) what its ORCA just did.</summary>
        internal static void Report(Unit launcher, OrcaCall call, Unit subject, float km)
        {
            if (!(launcher is Aircraft pilot) || pilot.Player == null) return; // AI launchers have no HUD
            var notice = new OrcaNotice
            {
                Protocol = ProtocolVersion, Call = (byte)call, OwnerId = launcher.persistentID.Id,
                SubjectId = subject != null ? subject.persistentID.Id : 0u, Km = km,
            };
            Show(notice);
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server != null && server.Active) server.SendToAll(notice, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void Receive(INetworkPlayer sender, OrcaNotice notice)
        {
            if (notice.Protocol != ProtocolVersion || notice.Call > (byte)OrcaCall.Expired || GameAccess.IsServer()) return;
            Show(notice);
        }

        private static void Show(OrcaNotice notice)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || me.persistentID.Id != notice.OwnerId) return;
            if (!ModuleServices.TryGet(out IHudBoard board)) return;
            VanguardHudFeeds.DeclareChannel(Channel, "ORCA");
            var call = (OrcaCall)notice.Call;
            string detail = notice.SubjectId != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = notice.SubjectId }, out Unit unit) && unit != null
                ? unit.unitName : null;
            if (call == OrcaCall.Set) detail = "HOSTILE HULL WAKES IT";
            HudTone tone = call == OrcaCall.Awake ? HudTone.Caution : HudTone.Info;
            board.Notice(Channel, tone, OrcaSleeper.Line(call, notice.Km), detail);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}

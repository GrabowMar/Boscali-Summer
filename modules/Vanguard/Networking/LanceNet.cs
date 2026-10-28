using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    /// <summary>
    /// Owner client to server. Level is the held charge (Charge) or the requested raw power (Release), 0..255. Count is the
    /// number of slugs a Release volley fired (one per pod of the station; the server clamps it to the pods it knows).
    /// </summary>
    [NetworkMessage]
    internal struct LanceIntent
    {
        public byte Protocol, Station, Kind, Level, Count;
    }

    /// <summary>Server to clients: another pod's charge / shot / cancel for visuals, and the through-shot beam.</summary>
    [NetworkMessage]
    internal struct LanceFx
    {
        public byte Protocol;
        public uint ShooterId;
        public byte Station, Kind, Level, Heat, Hits;
        public float Ax, Ay, Az, Bx, By, Bz; // Beam only: global positions
    }

    /// <summary>
    /// LANCE wire. The owner tells the server its held charge (4 Hz) and its release; the server bounds the claim by its
    /// own bank clock and heat (LanceServer), then tells everyone the confirmed power so every peer shows the same pod.
    /// Damage never rides the wire: it is decided where the vanilla hit claim lands (the server).
    /// </summary>
    internal sealed class LanceNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 2;
        internal const byte IntentCharge = 0, IntentRelease = 1, IntentCancel = 2;
        internal const byte FxCharge = 0, FxShot = 1, FxBeam = 2, FxCancel = 3;
        private const float ReleaseInterval = 0.8f, ChargeInterval = 0.2f, CueInterval = 5f, MaxBeamRange = 6000f;

        private readonly SenderThrottle throttle = new SenderThrottle();
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<LanceIntent>(ReceiveIntent);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<LanceFx>(ReceiveFx);
        private float nextRegistration;
        private static float nextCue;

        private void Awake()
        {
            MirageSerializers.Strict.Install<LanceIntent>(
                (Action<NetworkWriter, LanceIntent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WriteByte(v.Station);
                    w.WriteByte(v.Kind);
                    w.WriteByte(v.Level);
                    w.WriteByte(v.Count);
                }),
                (Func<NetworkReader, LanceIntent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new LanceIntent { Protocol = protocol }
                        : new LanceIntent { Protocol = protocol, Station = r.ReadByte(), Kind = r.ReadByte(), Level = r.ReadByte(), Count = r.ReadByte() };
                }));
            MirageSerializers.Strict.Install<LanceFx>(
                (Action<NetworkWriter, LanceFx>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.ShooterId);
                    w.WriteByte(v.Station);
                    w.WriteByte(v.Kind);
                    w.WriteByte(v.Level);
                    w.WriteByte(v.Heat);
                    w.WriteByte(v.Hits);
                    if (v.Kind != FxBeam) return;
                    w.WriteSingle(v.Ax); w.WriteSingle(v.Ay); w.WriteSingle(v.Az);
                    w.WriteSingle(v.Bx); w.WriteSingle(v.By); w.WriteSingle(v.Bz);
                }),
                (Func<NetworkReader, LanceFx>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new LanceFx { Protocol = protocol };
                    var fx = new LanceFx
                    {
                        Protocol = protocol, ShooterId = r.ReadPackedUInt32(), Station = r.ReadByte(), Kind = r.ReadByte(),
                        Level = r.ReadByte(), Heat = r.ReadByte(), Hits = r.ReadByte(),
                    };
                    if (fx.Kind != FxBeam) return fx;
                    fx.Ax = r.ReadSingle(); fx.Ay = r.ReadSingle(); fx.Az = r.ReadSingle();
                    fx.Bx = r.ReadSingle(); fx.By = r.ReadSingle(); fx.Bz = r.ReadSingle();
                    return fx;
                }));
        }

        public void ResetForScene() => throttle.Clear();

        private void Update()
        {
            if (NetFeed.Poll(ServerHandlers, ClientHandlers, ref nextRegistration)) throttle.Prune(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }

        // ---- owner peer

        private static byte Level(float value01) => (byte)Mathf.Clamp(Mathf.RoundToInt(value01 * 255f), 0, 255);
        private static byte HeatByte(float heat) => Level(heat / 1.5f);
        private static float FromLevel(byte level) => level / 255f;
        private static float FromHeat(byte heat) => heat / 255f * 1.5f;

        public static void SendCharge(LancePod pod, float charge01)
        {
            if (GameAccess.IsServer())
                Broadcast(new LanceFx { Kind = FxCharge, ShooterId = pod.Aircraft.persistentID.Id, Station = pod.Station.Number, Level = Level(charge01), Heat = HeatByte(pod.Heat) });
            else SendIntent(pod, IntentCharge, charge01);
        }

        /// <summary>Client owner: the raw (pre-derate) power it held and how many slugs the station fired; the server applies its own bound and heat.</summary>
        public static void SendRelease(LancePod pod, float requested01, int count) => SendIntent(pod, IntentRelease, requested01, count);

        public static void SendCancel(LancePod pod)
        {
            if (pod.Station == null) return;
            if (GameAccess.IsServer())
                Broadcast(new LanceFx { Kind = FxCancel, ShooterId = pod.Aircraft.persistentID.Id, Station = pod.Station.Number });
            else SendIntent(pod, IntentCancel, 0f);
        }

        private static void SendIntent(LancePod pod, byte kind, float level01, int count = 1) =>
            NetworkManagerNuclearOption.i?.Client?.Send(new LanceIntent
            {
                Protocol = ProtocolVersion, Station = pod.Station.Number, Kind = kind, Level = Level(level01), Count = (byte)Mathf.Clamp(count, 1, 255),
            });

        // ---- server

        /// <summary>Server-owned pod (host or AI) fired: tell everyone the shot power.</summary>
        public static void ServerShot(LancePod pod, float power01) =>
            Broadcast(new LanceFx { Kind = FxShot, ShooterId = pod.Aircraft.persistentID.Id, Station = pod.Station.Number, Level = Level(power01), Heat = HeatByte(pod.Heat) });

        public static void ServerBeam(PersistentID shooter, Vector3 from, Vector3 to, int hits)
        {
            GlobalPosition a = from.ToGlobalPosition(), b = to.ToGlobalPosition();
            Broadcast(new LanceFx
            {
                Kind = FxBeam, ShooterId = shooter.Id, Hits = (byte)Mathf.Min(hits, 255),
                Ax = a.x, Ay = a.y, Az = a.z, Bx = b.x, By = b.y, Bz = b.z,
            });
        }

        private static void Broadcast(LanceFx fx)
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            fx.Protocol = ProtocolVersion;
            server.SendToAll(fx, authenticatedOnly: true, excludeLocalPlayer: true);
            Apply(fx); // the host is excluded from SendToAll
        }

        private void ReceiveIntent(INetworkPlayer sender, LanceIntent intent)
        {
            if (intent.Protocol != ProtocolVersion || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            Aircraft aircraft = player.Aircraft; // never taken from the message
            if (aircraft == null || aircraft.disabled || intent.Station >= aircraft.weaponStations.Count) return;
            WeaponStation station = aircraft.weaponStations[intent.Station];
            int pods = LanceService.AttachedIn(station);
            if (pods == 0) return;
            float interval = intent.Kind == IntentRelease ? ReleaseInterval : ChargeInterval;
            if (!throttle.Allow(((ulong)aircraft.persistentID.Id << 10) | ((ulong)intent.Station << 2) | intent.Kind, Time.unscaledTime, interval)) return;
            uint id = aircraft.persistentID.Id;
            if (intent.Kind == IntentRelease)
            {
                // Only a loaded station can fire. The selected station is NOT checked: a dedicated server never receives the
                // owner's RpcSetActiveStation, so its weaponManager.currentWeaponStation would reject honest shots. The slugs
                // per volley claimed are bounded by the pods the server sees.
                if (station.Ammo <= 0)
                {
                    VanguardStats.Bump("lance.rejected");
                    return;
                }
                int count = Mathf.Clamp(intent.Count, 1, pods);
                if (LanceServer.Release(aircraft, intent.Station, station.Ammo, FromLevel(intent.Level), count, Time.time, out float power, out float heat))
                    Broadcast(new LanceFx { Kind = FxShot, ShooterId = id, Station = intent.Station, Level = Level(power), Heat = HeatByte(heat) });
            }
            else if (intent.Kind == IntentCharge)
                Broadcast(new LanceFx { Kind = FxCharge, ShooterId = id, Station = intent.Station, Level = intent.Level, Heat = HeatByte(LanceServer.HeatOf(aircraft, intent.Station)) });
            else if (intent.Kind == IntentCancel)
                Broadcast(new LanceFx { Kind = FxCancel, ShooterId = id, Station = intent.Station });
        }

        // ---- every peer

        private void ReceiveFx(INetworkPlayer sender, LanceFx fx)
        {
            if (fx.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            Apply(fx);
        }

        private static void Apply(LanceFx fx)
        {
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = fx.ShooterId }, out Unit unit) || !(unit is Aircraft aircraft) || aircraft.disabled) return;
            if (fx.Kind == FxBeam)
            {
                var a = new GlobalPosition(fx.Ax, fx.Ay, fx.Az);
                var b = new GlobalPosition(fx.Bx, fx.By, fx.Bz);
                if (!Finite(a) || !Finite(b) || FastMath.Distance(a, aircraft.GlobalPosition()) > MaxBeamRange) return;
                LanceFxPlayer.Beam(a, b);
                if (fx.Hits >= 2 && aircraft.Player != null && aircraft.LocalSim)
                    LanceService.Notice(HudTone.Info, "THROUGH-SHOT  x" + fx.Hits);
                return;
            }
            List<LancePod> pods = LanceService.PodsIn(aircraft, fx.Station); // every pod of the station shows the same charge and shot
            if (pods.Count == 0 || aircraft.LocalSim) return; // the owner already drew its own pods
            float now = Time.time;
            foreach (LancePod pod in pods)
            {
                pod.Heat = FromHeat(fx.Heat);
                if (fx.Kind == FxCharge)
                {
                    pod.RemoteCharge = FromLevel(fx.Level);
                    pod.RemoteAt = now;
                }
                else if (fx.Kind == FxShot)
                {
                    pod.FxPower = FromLevel(fx.Level);
                    pod.FxAt = now;
                    pod.RemoteCharge = 0f;
                }
                else if (fx.Kind == FxCancel) pod.RemoteCharge = 0f;
            }
            if (fx.Kind == FxCharge) Cue(pods[0], aircraft, FromLevel(fx.Level), now);
        }

        private static bool Finite(GlobalPosition p) => float.IsFinite(p.x + p.y + p.z);

        // Counterplay for the victim: a hostile pod charged past 70 % within 3.5 km and pointing within 6 deg of us.
        private static void Cue(LancePod pod, Aircraft shooter, float charge, float now)
        {
            if (charge < 0.7f || now < nextCue || GameManager.IsHeadless || !GameManager.GetLocalAircraft(out Aircraft me) || me == shooter ||
                me.NetworkHQ == null || me.NetworkHQ == shooter.NetworkHQ || pod.Gun == null) return;
            Vector3 to = me.transform.position - pod.Gun.transform.position;
            if (to.sqrMagnitude > 3500f * 3500f || Vector3.Angle(pod.Gun.transform.forward, to) > 6f) return;
            nextCue = now + CueInterval;
            LanceService.Notice(HudTone.Warning, "RAILGUN CHARGED - AIMED AT YOU");
        }
    }
}

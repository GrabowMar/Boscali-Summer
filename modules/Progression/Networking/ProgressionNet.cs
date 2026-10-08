using BoscaliSummer.Modules.Progression.Domain;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Networking
{
    /// <summary>Client intent: a perk id, or <see cref="ProgressionNet.QueryOnly"/> for a snapshot.</summary>
    [NetworkMessage]
    internal struct ProgressionSubmit
    {
        public byte Protocol;
        public byte Perk;
        public uint Scene, Token;
        public int Generation;
    }

    /// <summary>
    /// The server's authoritative view of one player, correlated to the requesting scene and career.
    /// </summary>
    [NetworkMessage]
    internal struct ProgressionSnapshot
    {
        public const byte Snapshot = 0;
        public const byte Unlocked = 1;
        public const byte Denied = 2;

        public byte Protocol;
        public uint PerkMask;
        public int Score;
        public byte EarnedPoints;
        public byte Rank;
        public byte Result;
        public int Generation;
        public uint Scene, Token;
        public int ScorePerPoint;
        public byte MaximumPoints;
        public uint PlaneId;
        public byte EngineMap;
    }

    [NetworkMessage]
    internal struct PlaneTuneRequest
    {
        public byte Protocol;
        public uint AircraftId;
        public byte Mode;
    }

    [NetworkMessage]
    internal struct PlaneTuneState
    {
        public byte Protocol;
        public uint AircraftId;
        public byte Mode;
        public byte Accepted;
    }

    internal sealed class ProgressionNet : MonoBehaviour
    {
        /// <summary>
        /// Version 5 adds host-approved per-aircraft engine maps.
        /// </summary>
        internal const byte ProtocolVersion = 5;

        /// <summary>Perk id meaning "send me a snapshot, change nothing".</summary>
        internal const byte QueryOnly = byte.MaxValue;

        // A client polls every 2 s while a view is open and holds each pick until its reply, so
        // these only bind a peer that floods; queries and picks are limited apart so a poll can
        // never swallow the pick clicked right after it.
        private const float QueryInterval = 1f;
        private const float IntentInterval = 0.25f;
        private const int MaximumSenders = 64;

        private ProgressionManager manager;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??=
            HandlerSlot.Of<ProgressionSubmit, PlaneTuneRequest>(ReceiveSubmit, ReceivePlaneTune);
        private HandlerSlot ClientHandlers => clientSlot ??=
            HandlerSlot.Of<ProgressionSnapshot, PlaneTuneState>(ReceiveSnapshot, ReceivePlaneTuneState);
        private float nextRegistration;
        private uint scene, token;
        private ulong requestedPlayer;
        private FactionHQ requestedHq;
        private readonly System.Collections.Generic.Dictionary<ulong, float> lastTuneChange =
            new System.Collections.Generic.Dictionary<ulong, float>();
        private readonly System.Collections.Generic.Dictionary<uint, byte> tunes =
            new System.Collections.Generic.Dictionary<uint, byte>();
        private readonly SenderThrottle nextQuery = new SenderThrottle(MaximumSenders);
        private readonly SenderThrottle nextIntent = new SenderThrottle(MaximumSenders);

        internal void ResetScene()
        {
            scene++; requestedPlayer = PlayerIdentity.None; requestedHq = null;
            lastTuneChange.Clear(); tunes.Clear(); nextQuery.Clear(); nextIntent.Clear();
        }

        internal byte TuneFor(Aircraft aircraft) => aircraft != null &&
            tunes.TryGetValue(aircraft.persistentID.Id, out byte mode) ? mode : (byte)0;

        internal void AcceptSnapshotTune(uint aircraftId, byte mode)
        {
            if (aircraftId == 0 || !PlaneEngineMap.IsDefined(mode)) return;
            if (tunes.Count >= 128 && !tunes.ContainsKey(aircraftId)) tunes.Clear();
            tunes[aircraftId] = mode;
        }

        public void Configure(ProgressionManager progression)
        {
            manager = progression;
            InstallSerializers();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            if (network == null) return;
            // Unlike the other modules, a missing handler keeps the current registrations.
            if (network.Server != null && network.Server.Active && network.Server.MessageHandler != null)
                ServerHandlers.Swap(network.Server.MessageHandler);
            if (network.Client?.MessageHandler != null) ClientHandlers.Swap(network.Client.MessageHandler);
        }

        private void OnDestroy()
        {
            ServerHandlers.Release();
            ClientHandlers.Release();
        }

        internal void RequestPlaneTune(Aircraft aircraft, int mode)
        {
            if (aircraft == null || mode < 0 || mode > byte.MaxValue ||
                !PlaneEngineMap.IsDefined((byte)mode) ||
                !GameManager.GetLocalPlayer<Player>(out Player local) || local?.Aircraft != aircraft) return;
            if (GameAccess.IsServer())
            {
                manager.ReportTune(ApplyPlaneTune(local, aircraft.persistentID.Id, (byte)mode),
                    aircraft.persistentID.Id);
                return;
            }
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            if (client != null && client.Active)
                client.Send(new PlaneTuneRequest { Protocol = ProtocolVersion,
                    AircraftId = aircraft.persistentID.Id, Mode = (byte)mode });
        }

        private void ReceivePlaneTune(INetworkPlayer sender, PlaneTuneRequest request)
        {
            if (!GameAccess.IsServer() || request.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !Allow(nextIntent, PlayerIdentity.Of(player), IntentInterval)) return;
            if (!ApplyPlaneTune(player, request.AircraftId, request.Mode))
                sender.Send(new PlaneTuneState { Protocol = ProtocolVersion,
                    AircraftId = request.AircraftId, Mode = request.Mode, Accepted = 0 });
        }

        private bool ApplyPlaneTune(Player player, uint aircraftId, byte mode)
        {
            Aircraft aircraft = player?.Aircraft;
            if (aircraft == null || aircraft.persistentID.Id != aircraftId || aircraft.disabled ||
                !aircraft.IsLanded() || !PlaneEngineMap.IsDefined(mode)) return false;
            ulong owner = PlayerIdentity.Of(player);
            if (lastTuneChange.TryGetValue(owner, out float last) && Time.unscaledTime - last < 2f) return false;
            if (lastTuneChange.Count >= 128) lastTuneChange.Clear();
            lastTuneChange[owner] = Time.unscaledTime;
            if (tunes.Count >= 128 && !tunes.ContainsKey(aircraftId)) tunes.Clear();
            tunes[aircraftId] = mode;
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server != null && server.Active)
                server.SendToAll(new PlaneTuneState { Protocol = ProtocolVersion,
                    AircraftId = aircraftId, Mode = mode, Accepted = 1 },
                    authenticatedOnly: true, excludeLocalPlayer: true);
            return true;
        }

        private void ReceivePlaneTuneState(INetworkPlayer _, PlaneTuneState applied)
        {
            if (GameAccess.IsServer() || applied.Protocol != ProtocolVersion) return;
            if (applied.Accepted == 0) { manager.ReportTune(false, applied.AircraftId); return; }
            if (!PlaneEngineMap.IsDefined(applied.Mode)) return;
            if (tunes.Count >= 128 && !tunes.ContainsKey(applied.AircraftId)) tunes.Clear();
            tunes[applied.AircraftId] = applied.Mode;
            var id = new PersistentID { Id = applied.AircraftId };
            UnitRegistry.TryGetUnit<Aircraft>(id, out Aircraft aircraft);
            if (aircraft != null && GameManager.GetLocalPlayer<Player>(out Player local) && local?.Aircraft == aircraft)
                manager.ReportTune(true, applied.AircraftId);
        }

        /// <summary>
        /// Sends one intent. When this process is the server the request is resolved in-process,
        /// so single-player and listen-host never depend on the custom-message pipe.
        /// </summary>
        public void Submit(byte perkId)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null)
            { manager.ReportOffline(); return; }
            if (GameAccess.IsServer())
            {
                manager.Apply(manager.Handle(local, perkId), PlayerIdentity.Of(local));
                return;
            }
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }
            requestedPlayer = PlayerIdentity.Of(local); requestedHq = local.HQ;
            client.Send(new ProgressionSubmit { Protocol = ProtocolVersion, Perk = perkId,
                Scene = scene, Token = ++token, Generation = manager.Generation(local) });
        }

        private void ReceiveSubmit(INetworkPlayer sender, ProgressionSubmit submit)
        {
            if (!GameAccess.IsServer() || submit.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            if (submit.Perk != QueryOnly && !PerkCatalog.IsDefined(submit.Perk)) return;
            if (!Allow(submit.Perk == QueryOnly ? nextQuery : nextIntent, PlayerIdentity.Of(player),
                    submit.Perk == QueryOnly ? QueryInterval : IntentInterval)) return;
            bool oldCareer = submit.Perk != QueryOnly && submit.Generation != manager.Generation(player);
            ProgressionSnapshot snapshot = manager.Handle(player, oldCareer ? QueryOnly : submit.Perk);
            if (oldCareer) snapshot.Result = ProgressionSnapshot.Denied;
            snapshot.Scene = submit.Scene; snapshot.Token = submit.Token;
            sender.Send(snapshot);
        }

        /// <summary>Bounded per-player throttle; stale entries are pruned so the cap never locks out a newcomer.</summary>
        private bool Allow(SenderThrottle limits, ulong id, float interval)
        {
            float now = Time.unscaledTime;
            nextQuery.Prune(now); nextIntent.Prune(now);
            return limits.Allow(id, now, interval);
        }

        private void ReceiveSnapshot(INetworkPlayer _, ProgressionSnapshot snapshot)
        {
            if (GameAccess.IsServer() || snapshot.Protocol != ProtocolVersion || snapshot.Scene != scene || snapshot.Token != token) return;
            ulong localId = GameManager.GetLocalPlayer<Player>(out Player local) && local != null
                ? PlayerIdentity.Of(local)
                : PlayerIdentity.None;
            if (localId == PlayerIdentity.None || localId != requestedPlayer || local.HQ != requestedHq) return;
            manager.Apply(snapshot, localId);
        }

        private static bool serializersInstalled;
        private static readonly MirageSerializers Seams = new MirageSerializers(
            "[Progression]", " is missing; perk state cannot replicate on this game build.");

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            Seams.Install<ProgressionSubmit>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WriteByte(value.Perk);
                writer.WritePackedUInt32(value.Scene); writer.WritePackedUInt32(value.Token); writer.WritePackedInt32(value.Generation);
            },
            // A foreign protocol keeps only its header: its layout may differ, and reading past the
            // end would throw inside a Mirage handler.
            reader =>
            {
                byte protocol = reader.ReadByte();
                if (protocol != ProtocolVersion) return new ProgressionSubmit { Protocol = protocol };
                return new ProgressionSubmit
                {
                    Protocol = protocol,
                    Perk = reader.ReadByte(), Scene = reader.ReadPackedUInt32(), Token = reader.ReadPackedUInt32(), Generation = reader.ReadPackedInt32()
                };
            });
            Seams.Install<ProgressionSnapshot>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedUInt32(value.PerkMask);
                writer.WritePackedInt32(value.Score);
                writer.WriteByte(value.EarnedPoints);
                writer.WriteByte(value.Rank);
                writer.WriteByte(value.Result);
                writer.WritePackedInt32(value.Generation);
                writer.WritePackedUInt32(value.Scene); writer.WritePackedUInt32(value.Token);
                writer.WritePackedInt32(value.ScorePerPoint); writer.WriteByte(value.MaximumPoints);
                writer.WritePackedUInt32(value.PlaneId); writer.WriteByte(value.EngineMap);
            },
            reader =>
            {
                byte protocol = reader.ReadByte();
                if (protocol != ProtocolVersion) return new ProgressionSnapshot { Protocol = protocol };
                return new ProgressionSnapshot
                {
                    Protocol = protocol,
                    PerkMask = reader.ReadPackedUInt32(),
                    Score = reader.ReadPackedInt32(),
                    EarnedPoints = reader.ReadByte(),
                    Rank = reader.ReadByte(),
                    Result = reader.ReadByte(),
                    Generation = reader.ReadPackedInt32(), Scene = reader.ReadPackedUInt32(), Token = reader.ReadPackedUInt32(),
                    ScorePerPoint = reader.ReadPackedInt32(), MaximumPoints = reader.ReadByte(),
                    PlaneId = reader.ReadPackedUInt32(), EngineMap = reader.ReadByte()
                };
            });
            Seams.Install<PlaneTuneRequest>((writer, value) =>
            {
                writer.WriteByte(value.Protocol); writer.WritePackedUInt32(value.AircraftId); writer.WriteByte(value.Mode);
            },
            reader =>
            {
                byte protocol = reader.ReadByte();
                if (protocol != ProtocolVersion) return new PlaneTuneRequest { Protocol = protocol };
                return new PlaneTuneRequest { Protocol = protocol, AircraftId = reader.ReadPackedUInt32(), Mode = reader.ReadByte() };
            });
            Seams.Install<PlaneTuneState>((writer, value) =>
            {
                writer.WriteByte(value.Protocol); writer.WritePackedUInt32(value.AircraftId); writer.WriteByte(value.Mode);
                writer.WriteByte(value.Accepted);
            },
            reader =>
            {
                byte protocol = reader.ReadByte();
                if (protocol != ProtocolVersion) return new PlaneTuneState { Protocol = protocol };
                return new PlaneTuneState
                {
                    Protocol = protocol, AircraftId = reader.ReadPackedUInt32(), Mode = reader.ReadByte(),
                    Accepted = reader.ReadByte()
                };
            });
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime.Actions;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// WATCH OFFICER OVERLORD, the host adapter. It runs only on the host (SpaceService only ticks there) and does nothing a human
    /// could not: it reads the contacts the faction has already revealed, MARKs them and SENDs them through the same host paths,
    /// and asks for a RADAR SCAN through the same bird reservation. All decisions live in the pure
    /// <see cref="WatchOfficerPolicy"/>; this class gathers the facts and carries the plan out. Fog of war holds: OVERLORD never
    /// reads an unrevealed unit, and with no contact it can only scan a public map objective, never invent a target.
    /// It has no wallet and earns nothing; its posts carry no effort shares and a fired post's fee goes to HQ FUND.
    /// </summary>
    internal sealed class SpaceWatchOfficer
    {
        private const int MaximumFactions = 8;
        private SpaceService service;
        private SupportManager manager;
        private readonly Dictionary<FactionHQ, FactionRun> runs = new Dictionary<FactionHQ, FactionRun>();
        private readonly List<FactionHQ> factions = new List<FactionHQ>(MaximumFactions);

        public void Configure(SpaceService spaceService, SupportManager support)
        {
            service = spaceService;
            manager = support;
        }

        /// <summary>A human of <paramref name="owner"/> did a SPACE domain verb; that faction's OVERLORD yields (60 s solo, 300 s with company).</summary>
        public void RecordHumanActivity(FactionHQ owner, ulong player, float missionNow)
        {
            if (owner == null || player == PlayerIdentity.None) return;
            FactionRun run = RunFor(owner);
            run?.Brain.RecordHuman(missionNow);
        }

        /// <summary>One host frame. Cheap while nothing is due: each faction's policy throttles itself to one think per two mission seconds.</summary>
        public void Tick(float missionNow)
        {
            if (service == null || manager == null || !SpaceRules.MissionTime(missionNow)) return;
            service.CopyFactions(factions);
            for (int i = 0; i < factions.Count; i++)
            {
                FactionRun run = RunFor(factions[i]);
                if (run == null || !run.Brain.Policy.Due(missionNow)) continue;
                if (run.Host.Humans == 0)
                {
                    // A faction with no human has no OVERLORD: its scan would stamp the AI faction's native tracking database
                    // (RpcUpdateTrackingInfo), handing the enemy AI free intel, and nothing consumes an AI TASKED board.
                    // The pure policy still staffs zero humans (the offline sim runs it).
                    run.Brain.Policy.Defer(missionNow, 2f); // nobody home: look again in two seconds, not every frame
                    continue;
                }
                try { run.Brain.Step(run.Host, missionNow); }
                catch (Exception e)
                {
                    // One faction's fault never stops another's; the throttle gives the same fault thirty seconds before it repeats.
                    run.Brain.Policy.NoteScanFailed(missionNow);
                    Plugin.Logger?.LogWarning("[Support.Overlord] " + run.Host.Name + " step failed: " + e.Message);
                }
            }
        }

        public void ResetForScene()
        {
            foreach (var pair in runs) pair.Value.Brain.Reset();
            runs.Clear();
            factions.Clear();
        }

        private FactionRun RunFor(FactionHQ owner)
        {
            if (runs.TryGetValue(owner, out FactionRun run)) return run;
            if (runs.Count >= MaximumFactions) return null;
            run = new FactionRun(service, manager, owner);
            runs.Add(owner, run);
            return run;
        }

        private sealed class FactionRun
        {
            public readonly WatchOfficerBrain Brain = new WatchOfficerBrain();
            public readonly Host Host;
            public FactionRun(SpaceService service, SupportManager manager, FactionHQ owner) { Host = new Host(service, manager, owner); }
        }

        /// <summary>The live-game side of one faction's OVERLORD.</summary>
        private sealed class Host : IWatchHost
        {
            private const float MapRefreshSeconds = 10f;
            private const int MaximumBases = 128, MaximumSites = 8;

            private readonly SpaceService service;
            private readonly SupportManager manager;
            private readonly FactionHQ owner;
            private readonly List<SpaceContact> reveals = new List<SpaceContact>(SpaceContacts.MaxReveals);
            private readonly List<Vector2> friendly = new List<Vector2>(16);
            private readonly List<WatchSite> sites = new List<WatchSite>(MaximumSites);
            private readonly List<float> siteRank = new List<float>(MaximumSites);
            private float nextMapRefresh;
            private int requestId;

            public Host(SpaceService service, SupportManager manager, FactionHQ owner)
            {
                this.service = service; this.manager = manager; this.owner = owner;
            }

            public string Name => owner != null ? owner.name : "?";
            public int Humans => manager.HumanCount(owner);

            public void Gather(float now, ref WatchInputs inputs, List<WatchTarget> targets, List<WatchSite> into)
            {
                inputs.Linked = false;
                if (!service.TryWatchParts(owner, out SpaceState state, out SpaceObservations observations, out TaskedDesk desk)) return;
                SpaceContacts contacts = observations.Contacts;
                contacts.Prune(now);
                inputs.Linked = state.LiveUplinkCount > 0;
                inputs.BoardCount = desk.Board.Count;
                inputs.BoardCapacity = desk.Capacity;
                inputs.OverlordPosts = desk.Board.CountWatchOfficer(now, TaskedDomain.Space);
                inputs.LiveMarks = contacts.MarkCount;
                inputs.RodReadyIn = Ready(state, SupportActionId.Artillery, BirdTask.Rod, now);
                inputs.RadarReadyIn = Ready(state, SupportActionId.Recon, BirdTask.Scan, now);
                inputs.OpticalReadyIn = Ready(state, SupportActionId.SatCamera, BirdTask.Camera, now);
                RefreshMap(now);

                contacts.CopyReveals(now, reveals);
                for (int i = 0; i < reveals.Count && targets.Count < SpaceContacts.MaxReveals; i++)
                {
                    SpaceContact c = reveals[i];
                    // Only what the faction could MARK itself: a live reveal, host-classified enemy ground, not already MARKed, and a
                    // unit the host still admits. Nothing else is ever read.
                    if (c.Classification != ContactClass.EnemyGround || contacts.TryMark(c.Id, now, out _)) continue;
                    if (!observations.TryApprovedUnit(c.Id, now, out Unit unit) || unit == null) continue;
                    UnitDefinition definition = unit.definition;
                    float value = definition != null ? definition.value : 0f;
                    WatchKind kind = definition != null
                        ? WatchOfficerPolicy.KindOf(definition.code, definition.roleIdentity.antiSurface, definition.roleIdentity.antiAir) : WatchKind.Other;
                    targets.Add(new WatchTarget(c.Id, c.X, c.Z, value, kind, c.Moving, IsStrategic(unit), c.ExpiresAt, FrontMeters(c.X, c.Z)));
                }
                for (int i = 0; i < sites.Count; i++) into.Add(sites[i]);
            }

            private float Ready(SpaceState state, SupportActionId action, BirdTask task, float now) =>
                manager.ActionEnabled(action) ? state.ReadyIn(task, now) : float.PositiveInfinity;

            private bool IsStrategic(Unit unit)
            {
                try
                {
                    return owner.trackingDatabase != null && owner.trackingDatabase.TryGetValue(unit.persistentID, out TrackingInfo track) &&
                        track != null && owner.IsStrategicTarget(track);
                }
                catch (Exception) { return false; }
            }

            private float FrontMeters(float x, float z)
            {
                if (friendly.Count == 0) return -1f;
                float best = float.PositiveInfinity;
                for (int i = 0; i < friendly.Count; i++)
                {
                    float dx = friendly[i].x - x, dz = friendly[i].y - z;
                    best = Mathf.Min(best, dx * dx + dz * dz);
                }
                return Mathf.Sqrt(best);
            }

            /// <summary>
            /// Public map facts only: the faction's own bases (how near a contact is to the front) and the enemy-held bases, nearest
            /// to ours first, as places a scan may legitimately look. No enemy unit position is read.
            /// </summary>
            private void RefreshMap(float now)
            {
                if (now < nextMapRefresh) return;
                nextMapRefresh = now + MapRefreshSeconds;
                friendly.Clear(); sites.Clear(); siteRank.Clear();
                if (FactionRegistry.airbaseLookup == null) return;
                int inspected = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > MaximumBases) break;
                    if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.center == null || airbase.CurrentHQ != owner) continue;
                    GlobalPosition p = airbase.center.position.ToGlobalPosition();
                    if (friendly.Count < friendly.Capacity) friendly.Add(new Vector2(p.x, p.z));
                }
                inspected = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > MaximumBases) break;
                    if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.center == null ||
                        airbase.CurrentHQ == null || airbase.CurrentHQ == owner) continue;
                    GlobalPosition p = airbase.center.position.ToGlobalPosition();
                    float rank = FrontMeters(p.x, p.z);
                    int at = siteRank.Count;
                    while (at > 0 && siteRank[at - 1] > rank) at--;
                    if (at >= MaximumSites) continue;
                    // The optical verdict is per site (its own sky), sampled with the map every ten seconds.
                    sites.Insert(at, new WatchSite(airbase.GetInstanceID(), p.x, p.z, OpticalDay(p.x, p.z))); siteRank.Insert(at, rank);
                    if (sites.Count > MaximumSites) { sites.RemoveAt(sites.Count - 1); siteRank.RemoveAt(siteRank.Count - 1); }
                }
            }

            private static bool OpticalDay(float x, float z)
            {
                if (!SpaceSky.TrySample(new GlobalPosition(x, 0f, z), out WeatherViewSample sky)) return false;
                return SpaceFeedRules.Optical(true, sky) == OpticalVerdict.Ok && SpaceFeedRules.RadiusFactor(sky, false) >= 0.75f;
            }

            public MarkVerdict Mark(int contactId)
            {
                MarkVerdict verdict = service.ObservationsFor(owner)?.Mark(SpaceContacts.WatchOfficerId, contactId, false) ?? MarkVerdict.NoContact;
                Plugin.Logger?.LogDebug("[Support.Overlord] " + Name + " MARK #" + contactId + " -> " + verdict);
                return verdict;
            }

            public TaskedOutcome Send(int[] markIds, int count)
            {
                if (!service.TryWatchParts(owner, out _, out _, out TaskedDesk desk)) return TaskedOutcome.Unavailable;
                var ids = new int[count];
                Array.Copy(markIds, ids, count);
                TaskedResult result = desk.SendWatchOfficer(++requestId, ids);
                Plugin.Logger?.LogInfo("[Support.Overlord] " + Name + " TASKED post " + (result.Outcome == TaskedOutcome.Posted ? "#" + result.CallId : "refused") +
                    " x" + count + " -> " + result.Outcome + " (source WATCH OFFICER).");
                return result.Outcome;
            }

            public bool Scan(WatchScan kind, in WatchSite site)
            {
                if (!service.TryWatchParts(owner, out SpaceState state, out _, out _)) return false;
                bool radar = kind == WatchScan.Radar;
                SupportActionId action = radar ? SupportActionId.Recon : SupportActionId.SatCamera;
                if (!manager.ActionEnabled(action)) return false;
                float now = SupportManager.MissionNow();
                if (!state.TryReserve(radar ? BirdTask.Scan : BirdTask.Camera, now, out SpaceTaskReservation receipt)) return false;
                var transaction = new SpaceActionTransaction(service, owner, state, receipt, manager.TaskSecondsOf(action));
                // No free reveal: if the task could not be committed after the window opens, it must not open at all.
                if (!transaction.CanLaunch) { transaction.Cancel(); return false; }
                int contacts;
                var point = new GlobalPosition(site.X, 0f, site.Z);
                try
                {
                    contacts = radar
                        ? service.OpenWindow(owner, point, manager.Settings.SarSceneRadius.Value, BirdKind.Radar, 0f, ReconAction.StationaryThreshold)
                        : service.OpenOptical(owner, point, manager.Settings.OpticalSceneRadius.Value, out _);
                }
                catch (Exception e)
                {
                    transaction.Cancel();
                    Plugin.Logger?.LogWarning("[Support.Overlord] " + Name + " scan failed: " + e.Message);
                    return false;
                }
                if (contacts < 0 || !transaction.Commit()) { transaction.Cancel(); return false; }
                Plugin.Logger?.LogInfo("[Support.Overlord] " + Name + " " + (radar ? "RADAR SCAN" : "SAT CAMERA") + " of a public objective -> " + contacts + " contact(s).");
                return true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Host-owned constellation grounded in actual, killable native sites.</summary>
    internal sealed class SpaceService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumBases = 16, MaximumScanUnits = 4096;
        private sealed class FactionSpace
        {
            public SpaceState State;
            public Unit[] Links;
            public float[] Baseline;
            public IReadOnlyList<Unit> View;
            public SpaceObservations Observations;
            public TaskedDesk Tasked;
            /// <summary>Callsigns of the humans whose posts this faction shows, so a poll does not look every one up again.</summary>
            public readonly Dictionary<ulong, string> Labels = new Dictionary<ulong, string>();
            /// <summary>Last mission second a dead bird's rebuild bar was funded (M6a: an ASAT strike kills a bird).</summary>
            public float BirdFundedAt;
        }
        private struct IntentEntry { public string Words; public float NextAt; }
        private readonly struct Candidate
        {
            public readonly GlobalPosition Anchor, Position;
            public readonly Airbase Parent;
            public readonly float RearScore;
            public Candidate(GlobalPosition anchor, GlobalPosition position, Airbase parent, float score)
            { Anchor = anchor; Position = position; Parent = parent; RearScore = score; }
        }

        private readonly Dictionary<FactionHQ, FactionSpace> factions = new Dictionary<FactionHQ, FactionSpace>();
        private readonly Dictionary<FactionHQ, float> nextAttempt = new Dictionary<FactionHQ, float>();
        private readonly UplinkSpawner spawner = new UplinkSpawner();
        private readonly SpaceWatchOfficer watch = new SpaceWatchOfficer();
        private readonly Dictionary<FactionHQ, IntentEntry> intents = new Dictionary<FactionHQ, IntentEntry>();
        private SupportManager manager;
        private float nextTick;
        private float nextCleanup;
        private float nextTaskedWarning;
        private bool coarse; // mirror gate reads use the 1 Hz world state instead of re-sampling natives per viewer
        public int Generation { get; private set; } = 1;
        internal UplinkSpawner Spawner => spawner;

        public void Configure(SupportManager support)
        {
            manager = support;
            watch.Configure(this, support);
        }

        public void ResetForScene()
        {
            foreach (var pair in factions)
            {
                pair.Value.Tasked?.Retire(); // returns in-flight escrow and makes every old launch callback inert
                pair.Value.State.Clear();
                pair.Value.Observations?.Dispose();
            }
            Generation = Generation == int.MaxValue ? 1 : Generation + 1;
            manager?.SpaceNet?.ResetForScene(); // old receipts and every faction subscription end with the scene
            watch.ResetForScene();
            intents.Clear();
            factions.Clear();
            nextAttempt.Clear();
            nextTick = 0f;
            scanCapLogged = false;
            spawner.ResetForScene();
        }
        private void OnDestroy() => ResetForScene();

        private readonly List<SpaceMark> snapMarks = new List<SpaceMark>(SpaceContacts.MaxMarks);

        /// <summary>Host: the nearest fresh own-faction MARK within <see cref="SpaceFireControl.MarkSnapRadius"/> of a perk's aim point.</summary>
        internal bool TryNearestMark(FactionHQ owner, float x, float z, float now, out SpaceMark mark)
        {
            mark = default;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction) || faction.Observations == null) return false;
            snapMarks.Clear();
            faction.Observations.Contacts.CopyMarks(now, snapMarks);
            return SpaceFireControl.TryNearestMark(snapMarks, x, z, now, out mark);
        }

        /// <summary>Authoritative read for host decisions: re-samples the natives first, so a gate sees native loss immediately.</summary>
        public bool TryGetState(FactionHQ owner, out SpaceState state)
        {
            state = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            if (!coarse) Refresh(owner, faction, SupportManager.MissionNow());
            state = faction.State;
            return true;
        }

        /// <summary>
        /// Display read (quotes, panels, the sky): the 1 Hz world state the host tick keeps current, never a re-sample. Safe to call
        /// every frame; a native loss shows within about a second.
        /// </summary>
        public bool TryGetStateCoarse(FactionHQ owner, out SpaceState state)
        {
            state = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            state = faction.State;
            return true;
        }

        internal IReadOnlyList<Unit> UplinksFor(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.View : Array.Empty<Unit>();

        // ---- OPS FRONTS programme effects (host) ----------------------------------------------------

        /// <summary>LAUNCH SATELLITE finished: the lowest dead bird is on station. False (nothing changed) when none is down.</summary>
        internal bool TryLaunchBird(FactionHQ owner, out BirdKind bird)
        {
            bird = BirdKind.Optical;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            int restored = faction.State.RestoreBird();
            if (restored < 0) return false;
            bird = (BirdKind)restored;
            Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " " + bird + " bird launched by its programme and on station.");
            return true;
        }

        /// <summary>UPLINK SITE finished: a real uplink site is raised on a rear spot of a held airbase and takes the place of the first dead one. False when none is dead or no legal spot exists.</summary>
        internal bool TryRebuildUplink(FactionHQ owner)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction) || FactionRegistry.airbaseLookup == null) return false;
            int slot = -1;
            for (int i = 0; i < faction.Links.Length && slot < 0; i++) if (UplinkSpawner.Down(faction.Links[i], owner)) slot = i;
            if (slot < 0) return false;
            float diagonal = TheaterFrame.Resolve().magnitude;
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 128) break;
                if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.CurrentHQ != owner || airbase.center == null) continue;
                if (!TryAround(owner, airbase.center.position, airbase, diagonal, out Candidate spot) ||
                    !spawner.TryCreate(owner, slot, spot.Anchor, spot.Parent, out Unit made)) continue;
                faction.Links[slot] = made;
                faction.Baseline[slot] = UplinkSpawner.Health(made);
                Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " uplink " + slot + " raised again by its programme.");
                return true;
            }
            return false;
        }

        /// <summary>The parts WATCH OFFICER OVERLORD works one faction through. False until the faction has a SPACE.</summary>
        internal bool TryWatchParts(FactionHQ owner, out SpaceState state, out SpaceObservations observations, out TaskedDesk desk)
        {
            state = null; observations = null; desk = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            state = faction.State; observations = faction.Observations; desk = faction.Tasked;
            return state != null && observations != null && desk != null;
        }

        /// <summary>Fills the (cleared) list with every faction that has a SPACE. Bounded by <see cref="MaximumFactions"/>.</summary>
        internal void CopyFactions(List<FactionHQ> into)
        {
            into.Clear();
            foreach (var pair in factions) if (into.Count < MaximumFactions) into.Add(pair.Key);
        }

        /// <summary>A human of this faction did a SPACE domain verb (MARK, SEND, CLAIM or a bird task): OVERLORD steps back. Host only.</summary>
        internal void NoteHumanSpaceVerb(Player player)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null) return;
            watch.RecordHumanActivity(player.HQ, PlayerIdentity.Of(player), SupportManager.MissionNow());
        }

        /// <summary>
        /// The faction's ENEMY INTENT words: the name of the main effort the enemy's director has chosen, read from the host through a
        /// Core contract, or UNKNOWN. Never a coordinate or a unit. Refreshed every five wall seconds, not per viewer poll.
        /// </summary>
        internal string IntentFor(FactionHQ owner)
        {
            if (owner == null || !GameAccess.IsServer()) return IntentWords.Unknown;
            float t = Time.unscaledTime;
            if (intents.TryGetValue(owner, out IntentEntry known) && t < known.NextAt) return known.Words;
            string words = ReadIntent(owner);
            if (intents.Count < MaximumFactions || intents.ContainsKey(owner)) intents[owner] = new IntentEntry { Words = words, NextAt = t + 5f };
            return words;
        }

        private string ReadIntent(FactionHQ owner)
        {
            if (!ModuleServices.TryGet(out IEnemyIntentSource source) || (source is UnityEngine.Object unity && unity == null)) return IntentWords.Unknown;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return IntentWords.Unknown;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || hq == owner || hq.faction == null) continue;
                // A human-led enemy has no director intent to show (its main effort is whatever its humans do): UNKNOWN.
                if (manager == null || manager.HumanCount(hq) > 0) continue;
                if (source.TryGetMainEffort(hq.faction.factionName, out string label)) return IntentWords.ForEnemy(0, label);
            }
            return IntentWords.Unknown;
        }

        private string LabelOf(FactionSpace faction, FactionHQ owner, ulong player)
        {
            if (faction.Labels.TryGetValue(player, out string known)) return known;
            string label = manager != null ? manager.PlayerLabel(owner, player) : "";
            // A name is cached once found; an absent player is looked up again next time (bounded by the posts on the board).
            if (label.Length > 0 && faction.Labels.Count < SpaceContacts.MaxPlayers)
                faction.Labels[player] = SpaceWire.Clean(label, SpaceWire.MaxMaker);
            return SpaceWire.Clean(label, SpaceWire.MaxMaker);
        }

        private int RadarDeadline(SpaceState state, float now)
        {
            // A scan the host has switched off, or with no live uplink, is "unavailable", never "ready".
            if (manager == null || !manager.ActionEnabled(SupportActionId.Recon)) return SpaceWire.RadarUnavailable;
            float wait = state.ReadyIn(BirdTask.Scan, now);
            if (float.IsInfinity(wait)) return SpaceWire.RadarUnavailable;
            return wait <= 0.05f ? 0 : SpaceMirror.GateDeadline(now, wait);
        }

        /// <summary>The faction's TASKED desk (the CYBER service posts its BURN packages on it). False until the faction has a SPACE.</summary>
        internal bool TryGetDesk(FactionHQ owner, out TaskedDesk desk)
        {
            desk = owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.Tasked : null;
            return desk != null;
        }

        internal SpaceObservations ObservationsFor(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.Observations : null;

        internal int OpenWindow(FactionHQ owner, GlobalPosition point, float radius, BirdKind source,
            float minimumSpeed, float maximumSpeed) =>
            ObservationsFor(owner)?.Open(point, radius, source, minimumSpeed, maximumSpeed) ?? -1;

        /// <summary>
        /// The host side of the optical camera task: read the sky at the aim, refuse with words at night or when the sky is unknown,
        /// otherwise open an OPTICAL reveal window whose radius the weather policy sized. Contacts it admits stay revealed for the
        /// window's 20 mission seconds, so <see cref="SpaceObservations.Tick"/> no longer forgets optical-sourced reveals.
        /// </summary>
        internal int OpenOptical(FactionHQ owner, GlobalPosition point, float baseRadius, out SupportResult refusal)
        {
            refusal = SupportResult.Accepted;
            bool known = SpaceSky.TrySample(point, out WeatherViewSample sky);
            OpticalVerdict verdict = SpaceFeedRules.Optical(known, sky);
            if (verdict != OpticalVerdict.Ok)
            {
                refusal = verdict == OpticalVerdict.NightUnavailable ? SupportResult.OpticalNight : SupportResult.SkyUnknown;
                return -1;
            }
            // CYBER BIRD JAM halves the footprint of a jammed faction's camera.
            float jam = CyberService.Active != null ? CyberService.Active.BirdOpticalFactor(owner) : 1f;
            int admitted = OpenWindow(owner, point, SpaceFeedRules.OpticalRadius(baseRadius, sky) * jam, BirdKind.Optical, 0f, float.MaxValue);
            if (admitted < 0) refusal = SupportResult.SpawnFailed;
            return admitted;
        }

        // ---- TASKED calls (host authority; Task 6 network handlers call these) ---------------------

        private bool TryDesk(Player player, out TaskedDesk desk)
        {
            desk = null;
            if (!GameAccess.IsServer() || player == null || player.HQ == null || PlayerIdentity.Of(player) == PlayerIdentity.None ||
                manager?.Settings == null || !manager.Settings.Enabled.Value || !factions.TryGetValue(player.HQ, out FactionSpace faction)) return false;
            desk = faction.Tasked;
            return desk != null;
        }

        /// <summary>Posts the player's confirmed MARKs as one TASKED call. Replays by (player, request id).</summary>
        internal TaskedResult SendTasked(Player player, int[] markIds, int requestId) =>
            TryDesk(player, out TaskedDesk desk) ? desk.Send(PlayerIdentity.Of(player), requestId, markIds)
                : new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);

        /// <summary>
        /// Queues a claim. The host arbitrates inside a 200 ms window (favorite first, then receipt order); the winner's
        /// escrow, launch and settlement follow on the host tick and the verdict arrives through <see cref="TryTaskedResult"/>.
        /// </summary>
        internal TaskedResult ClaimTasked(Player player, int postId, int requestId, bool favorite = false) =>
            TryDesk(player, out TaskedDesk desk) ? desk.Claim(PlayerIdentity.Of(player), requestId, postId, favorite)
                : new TaskedResult(TaskedOutcome.Unavailable, postId, requestId);

        internal bool TryTaskedResult(Player player, int requestId, out TaskedResult result)
        {
            result = default;
            return TryDesk(player, out TaskedDesk desk) && desk.TryResult(PlayerIdentity.Of(player), requestId, out result);
        }

        /// <summary>Wires the push for queued claims that resolve later (Task 6 answers the client from it).</summary>
        internal void SubscribeTasked(FactionHQ owner, Action<ulong, int, TaskedResult> handler)
        {
            if (owner != null && handler != null && factions.TryGetValue(owner, out FactionSpace faction) && faction.Tasked != null)
                faction.Tasked.Resolved += handler;
        }

        /// <summary>
        /// Fills one viewer's faction view: the headline always, the rows only for an open feed. Everything a client may learn is
        /// derived here from the host's own state; nothing is read from the client. False when the faction has no SPACE.
        /// </summary>
        internal bool FillFeed(Player viewer, bool rows, SpaceFeedState into, List<SpaceContact> reveals, List<SpaceMark> marks,
            List<TaskedPostInfo> posts)
        {
            coarse = true; // display reads use the 1 Hz world state; only host gates re-sample the natives
            try { return FillFeedCore(viewer, rows, into, reveals, marks, posts); }
            finally { coarse = false; }
        }

        private bool FillFeedCore(Player viewer, bool rows, SpaceFeedState into, List<SpaceContact> reveals, List<SpaceMark> marks,
            List<TaskedPostInfo> posts)
        {
            into.Active = false; into.Feed = false; into.Family = SpaceFamilyState.Dark;
            into.UplinksLive = 0; into.UplinksTotal = 0; into.LiveMarks = 0; into.Gate = TaskedOutcome.None; into.GateDetail = 0;
            into.NewestPost = 0; into.NewestAction = default; into.NewestTargets = 0; into.NewestWatchOfficer = false; into.NewestOwn = false;
            into.NewestMaker = ""; into.RadarReadyAt = 0; into.Intent = "";
            into.ClearRows();
            if (viewer == null || viewer.HQ == null || manager == null || !factions.TryGetValue(viewer.HQ, out FactionSpace faction) ||
                faction.Observations == null) return false;
            float now = SupportManager.MissionNow();
            if (!SpaceRules.MissionTime(now)) return false;
            SpaceContacts contacts = faction.Observations.Contacts;
            contacts.Prune(now);
            into.Active = true;
            into.Family = faction.State.Family(now);
            into.UplinksLive = (byte)faction.State.LiveUplinkCount;
            into.UplinksTotal = (byte)faction.State.UplinkCount;
            into.LiveMarks = (byte)Math.Min(SpaceContacts.MaxMarks, contacts.MarkCount);
            into.Gate = manager.TaskedGate(viewer, out int detail);
            detail = Math.Max(0, detail);
            // A cooldown or freeze counts down on the client: send its deadline (mission time), not seconds left, so a cooling
            // member is not sent a new value every second.
            // The deadline comes from the unrounded remaining time (the integer detail would flip it by a second every poll).
            into.GateDetail = into.Gate == TaskedOutcome.Cooldown ? SpaceMirror.GateDeadline(now, manager.ServerCooldownRemaining(viewer, SupportActionId.Artillery))
                : SpaceMirror.GateIsDeadline(into.Gate) ? SpaceMirror.GateDeadline(now, detail) : detail;
            ulong salt = manager.SpaceNet != null ? manager.SpaceNet.Salt : 0UL;
            ulong viewerId = PlayerIdentity.Of(viewer);
            into.RadarReadyAt = RadarDeadline(faction.State, now);
            string intent = IntentFor(viewer.HQ);
            into.Intent = IntentWords.IsKnown(intent) ? intent : ""; // UNKNOWN is the footer's default, not a payload
            if (faction.Tasked != null)
            {
                // The headline always carries the newest post, so a pilot with the feed closed still hears about a new call.
                faction.Tasked.Board.Snapshot(now, posts);
                int newest = -1;
                for (int i = 0; i < posts.Count; i++) if (newest < 0 || posts[i].Call.Id > posts[newest].Call.Id) newest = i;
                if (newest >= 0)
                {
                    TaskedCall call = posts[newest].Call;
                    into.NewestPost = call.Id; into.NewestAction = call.Action;
                    into.NewestTargets = (byte)Math.Max(1, Math.Min(TaskedBoard.MaxMarks, call.MarkCount));
                    into.NewestWatchOfficer = call.WatchOfficer;
                    into.NewestOwn = !call.WatchOfficer && call.Maker == viewerId;
                    into.NewestMaker = call.WatchOfficer || into.NewestOwn ? "" : LabelOf(faction, viewer.HQ, call.Maker);
                }
            }
            if (!rows) return true;
            into.Feed = true;
            contacts.CopyReveals(now, reveals);
            for (int i = 0; i < reveals.Count && into.Contacts.Count < SpaceWire.MaxContacts; i++)
            {
                SpaceContact c = reveals[i];
                // One row the wire cannot carry is dropped here, not allowed to make the whole message unreadable.
                if (!SpaceWire.Codable(c.X) || !SpaceWire.Codable(c.Z)) continue;
                // The verdict (truth) stays on the host: the client is told a deterministic, noisy probable class.
                ProbableClass probable = SpaceProbable.Of(c.Classification, c.Id, c.ObservationGeneration, salt, out byte percent);
                into.Contacts.Add(new FeedContact { Id = c.Id, UnitId = faction.Observations.UnitIdOf(c.Id), X = c.X, Z = c.Z,
                    Class = probable, Percent = percent, Moving = c.Moving, Source = c.Source, Expires = c.ExpiresAt });
            }
            contacts.CopyMarks(now, marks);
            for (int i = 0; i < marks.Count && into.Marks.Count < SpaceWire.MaxMarks; i++)
                if (SpaceWire.Codable(marks[i].X) && SpaceWire.Codable(marks[i].Z))
                    into.Marks.Add(new FeedMark { Id = marks[i].Id, X = marks[i].X, Z = marks[i].Z, Moving = marks[i].Moving,
                    Source = marks[i].Source, Expires = marks[i].ExpiresAt });
            if (faction.Tasked == null) return true;
            for (int i = 0; i < posts.Count && into.Posts.Count < SpaceWire.MaxPosts; i++)
            {
                TaskedCall call = posts[i].Call;
                bool own = !call.WatchOfficer && call.Maker == viewerId;
                const int price = 0, payoff = 0; // a TASKED claim is free; the wire still carries the two fields
                // The post is a host snapshot of fixed ground points taken at SEND; the client shows them for the post's whole life.
                var points = new FeedPoint[call.MarkCount];
                bool codable = points.Length > 0;
                for (int m = 0; m < points.Length && codable; m++)
                {
                    SpaceMark mark = call.MarkAt(m);
                    codable = SpaceWire.Codable(mark.X) && SpaceWire.Codable(mark.Z);
                    points[m] = new FeedPoint { X = mark.X, Z = mark.Z, Source = mark.Source };
                }
                if (!codable) continue; // a post the wire cannot describe is not offered rather than poisoning the message
                into.Posts.Add(new FeedPost { CallId = call.Id, Action = call.Action, WatchOfficer = call.WatchOfficer, Own = own,
                    Launching = posts[i].Launching, Points = points, Price = price, Payoff = payoff, Expires = call.ExpiresAt,
                    Claimant = posts[i].Held ? manager.PlayerLabel(viewer.HQ, posts[i].Holder) : "",
                    Maker = call.WatchOfficer || own ? "" : LabelOf(faction, viewer.HQ, call.Maker) });
            }
            return true;
        }

        /// <summary>The pilot currently holding a post of this faction (for the CLAIMED BY words).</summary>
        internal bool TryHolder(FactionHQ owner, int callId, out ulong pilot)
        {
            pilot = 0;
            return owner != null && factions.TryGetValue(owner, out FactionSpace faction) && faction.Tasked != null &&
                faction.Tasked.TryHolder(callId, out pilot);
        }

        private void Update()
        {
            if (manager?.Settings == null || !manager.Settings.Enabled.Value)
            {
                if (factions.Count > 0) ResetForScene();
                if (Time.unscaledTime >= nextCleanup)
                {
                    nextCleanup = Time.unscaledTime + 1f;
                    spawner.RetryCleanup();
                }
                return;
            }
            if (!GameAccess.IsServer() || (GameManager.gameState != GameState.SinglePlayer &&
                GameManager.gameState != GameState.Multiplayer)) return;
            float now = SupportManager.MissionNow();
            // Claim arbitration is 200 ms: the desks run every frame, the 1 s world refresh below does not.
            foreach (var pair in factions)
            {
                // One faction's desk fault must not stop the other desks or the world refresh.
                try { pair.Value.Tasked?.Tick(); }
                catch (Exception e)
                {
                    if (Time.unscaledTime >= nextTaskedWarning)
                    {
                        nextTaskedWarning = Time.unscaledTime + 10f;
                        Plugin.Logger?.LogWarning("[Support.Tasked] Desk tick failed: " + e.Message);
                    }
                }
            }
            if (manager.Settings.WatchOfficerEnabled.Value)
            {
                // OVERLORD (host only) staffs a SPACE no human is working. A fault in it never stops the desks or the mirror.
                try { watch.Tick(now); }
                catch (Exception e)
                {
                    if (Time.unscaledTime >= nextTaskedWarning)
                    {
                        nextTaskedWarning = Time.unscaledTime + 10f;
                        Plugin.Logger?.LogWarning("[Support.Overlord] Watch officer tick failed: " + e.Message);
                    }
                }
            }
            try { manager.SpaceNet?.Tick(); }
            catch (Exception e)
            {
                if (Time.unscaledTime >= nextTaskedWarning)
                {
                    nextTaskedWarning = Time.unscaledTime + 10f;
                    Plugin.Logger?.LogWarning("[Support.Space] Mirror poll failed: " + e.Message);
                }
            }
            if (now < nextTick) return;
            nextTick = now + 1f;
            spawner.RetryCleanup();
            // CYBER BIRD JAM: an enemy intrusion that holds our UPLINK node (or a fired BIRD JAM package) slows every bird task.
            // The enemy CYBER front's counter pressure (x1.5 when it leads by 40 or more) rides the same multiplier.
            foreach (var pair in factions)
                pair.Value.State.JamFactor = (CyberService.Active != null ? CyberService.Active.BirdCooldownFactor(pair.Key) : 1f) * (manager?.Fronts != null ? manager.Fronts.SpaceCooldownFactor(pair.Key) : 1f);
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                if (factions.TryGetValue(hq, out FactionSpace faction)) { Refresh(hq, faction, now); RebuildBirds(hq, faction, now); continue; }
                if (factions.Count >= MaximumFactions || nextAttempt.Count >= MaximumFactions && !nextAttempt.ContainsKey(hq)) continue;
                if (nextAttempt.TryGetValue(hq, out float retry) && now < retry) continue;
                nextAttempt[hq] = now + 30f;
                if (TryEstablish(hq, out faction))
                {
                    factions.Add(hq, faction);
                    FactionHQ resolvedFor = hq;
                    SubscribeTasked(resolvedFor, (player, request, result) => manager?.SpaceNet?.OnTaskedResolved(resolvedFor, player, request, result));
                    Plugin.Logger?.LogInfo("[Support.Space] " + hq.name + ": " + faction.Links.Length +
                        " native uplink(s), OPTICAL / RADAR / KINETIC ready.");
                }
                break; // Expensive initial world sampling is limited to one faction per mission second.
            }
        }

        /// <summary>
        /// Space spec 5.2: a bird an ASAT strike killed returns through a 400-unit restore bar (a plain timer in S0, funded by the front budget from S1)
        /// and then a 6 minute build. The pure <see cref="SpaceState"/> owns the clock; this only pays the bar and says when a bird is back.
        /// </summary>
        private void RebuildBirds(FactionHQ owner, FactionSpace faction, float now)
        {
            byte down = faction.State.DownMask;
            if (down == 0) { faction.BirdFundedAt = now; return; }
            float dt = Mathf.Clamp(now - faction.BirdFundedAt, 0f, 5f);
            faction.BirdFundedAt = now;
            for (int i = 0; i < SpaceRules.BirdCount; i++)
            {
                if ((down & (1 << i)) == 0) continue;
                var bar = faction.State.BirdBar((BirdKind)i);
                if (bar.Complete) continue;
                bar.Fund(manager.BirdRebuildFunding(owner, dt));
            }
            byte back = faction.State.TickBirds(now);
            for (int i = 0; i < SpaceRules.BirdCount; i++)
                if ((back & (1 << i)) != 0) Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " " + (BirdKind)i + " bird rebuilt and back on station.");
        }

        private static void Refresh(FactionHQ owner, FactionSpace faction, float now)
        {
            faction.Observations?.Tick(now);
            for (int i = 0; i < faction.Links.Length; i++)
            {
                Unit link = faction.Links[i];
                float health = faction.Baseline[i] > 0f ? Mathf.Clamp01(UplinkSpawner.Health(link) / faction.Baseline[i]) : 0f;
                faction.State.SetUplink(i, health, UplinkSpawner.Down(link, owner), now);
            }
        }

        private bool scanCapLogged;
        private void NoteScanCap(int count)
        {
            if (scanCapLogged) return;
            scanCapLogged = true; // reset with the scene's service state
            Plugin.Logger?.LogWarning("[Support.Space] Uplink site search saw " + count + " units; only the first " + MaximumScanUnits + " are considered.");
        }

        private bool TryEstablish(FactionHQ owner, out FactionSpace faction)
        {
            faction = null;
            if (UnitRegistry.allUnits != null && UnitRegistry.allUnits.Count > MaximumScanUnits) NoteScanCap(UnitRegistry.allUnits.Count);
            var legal = new List<Candidate>(UplinkPlacement.MaxCandidates);
            Vector2 span = TheaterFrame.Resolve();
            if (FactionRegistry.airbaseLookup != null)
            {
                int inspected = 0, heldBases = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > 128 || heldBases >= MaximumBases) break;
                    if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.CurrentHQ != owner || airbase.center == null) continue;
                    heldBases++;
                    CollectAround(owner, airbase.center.position, airbase, span.magnitude, legal, 8);
                }
            }
            var links = new List<Unit>(2);
            if (legal.Count > 0)
            {
                var choices = new UplinkCandidate[legal.Count];
                for (int i = 0; i < choices.Length; i++)
                    choices[i] = new UplinkCandidate(i, legal[i].Position.x, legal[i].Position.z, legal[i].RearScore);
                if (UplinkPlacement.TryPair(choices, span.magnitude, out int a, out int b))
                {
                    Create(owner, legal[a], links);
                    Create(owner, legal[b], links);
                }
                else
                {
                    int best = 0;
                    for (int i = 1; i < legal.Count; i++) if (legal[i].RearScore > legal[best].RearScore) best = i;
                    Create(owner, legal[best], links); // No sampled spaced pair: one real site, never a phantom second.
                }
            }
            if (links.Count == 0) BorrowAfloat(owner, span.magnitude, links);
            if (links.Count == 0 && UnitRegistry.allUnits != null)
            {
                // Native stored spawn origins are only candidates; terrain/mission/ownership rules still apply.
                int origins = 0;
                for (int i = 0; i < UnitRegistry.allUnits.Count && i < MaximumScanUnits && origins < 16; i++)
                {
                    Unit unit = UnitRegistry.allUnits[i];
                    if (unit == null || unit.disabled || unit.NetworkHQ != owner) continue;
                    origins++;
                    Vector3 origin = unit.startPosition.ToLocalPosition();
                    if (unit is Aircraft) origin.y = Datum.LocalSeaY + 100f;
                    if (!GroundPlacement.DryGround(origin, out _) || !TryAround(owner, origin, null, span.magnitude, out Candidate field)) continue;
                    Create(owner, field, links);
                    if (links.Count > 0) break;
                }
            }
            if (links.Count == 0) return false;
            var array = links.ToArray();
            var baseline = new float[array.Length];
            for (int i = 0; i < array.Length; i++) baseline[i] = UplinkSpawner.Health(array[i]);
            faction = new FactionSpace { State = new SpaceState(array.Length), Links = array, Baseline = baseline,
                View = Array.AsReadOnly(array), Observations = new SpaceObservations(owner) };
            faction.Tasked = manager?.CreateTaskedDesk(this, owner, faction.Observations);
            SeedGeo(owner, faction.State);
            return true;
        }

        /// <summary>Parks the faction's three birds over its side of the front: own airbase centroid shifted toward the enemy's.</summary>
        private static void SeedGeo(FactionHQ owner, SpaceState state)
        {
            float ownU = 0f, ownV = 0f, foeU = 0f, foeV = 0f; int own = 0, foe = 0;
            Vector2 span = TheaterFrame.Resolve();
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
                {
                    if (a == null || a.AttachedAirbase || a.UnitDestroyed() || a.CurrentHQ == null) continue;
                    GlobalPosition p = (a.center != null ? a.center : a.transform).GlobalPosition();
                    float u = GeoSpace.U(p.x, span.x), v = GeoSpace.V(p.z, span.y);
                    if (a.CurrentHQ == owner) { ownU += u; ownV += v; own++; } else { foeU += u; foeV += v; foe++; }
                }
            if (own == 0) { ownU = 0.35f; ownV = 0.5f; } else { ownU /= own; ownV /= own; }
            if (foe == 0) { foeU = 1f - ownU; foeV = 1f - ownV; } else { foeU /= foe; foeV /= foe; }
            var hu = new float[3]; var hv = new float[3];
            GeoSpace.Defaults(ownU, ownV, foeU, foeV, hu, hv);
            state.SetHomes(hu, hv);
        }

        /// <summary>Host: one faction's bird burns to a map point (the SPACE director and the RELOCATE command both come here).</summary>
        internal GeoSpace.Refusal TryRelocate(FactionHQ owner, int bird, float u, float v, float now, out GeoBird after)
        {
            after = default;
            if (owner == null || bird < 0 || bird >= SpaceRules.BirdCount || !factions.TryGetValue(owner, out FactionSpace faction)) return GeoSpace.Refusal.Dead;
            GeoSpace.Refusal why = faction.State.Relocate((BirdKind)bird, now, u, v);
            after = faction.State.Geo((BirdKind)bird);
            if (why == GeoSpace.Refusal.None) Plugin.Logger?.LogInfo("[Support.Space] " + owner.name + " " + GeoSpace.Names[bird] + " bird burns to " + after.ToU.ToString("0.00") + "," + after.ToV.ToString("0.00") + " (fuel " + Mathf.RoundToInt(after.Fuel) + " %).");
            return why;
        }

        private void Create(FactionHQ owner, in Candidate candidate, List<Unit> links)
        {
            if (spawner.TryCreate(owner, links.Count, candidate.Anchor, candidate.Parent, out Unit link)) links.Add(link);
        }

        private bool TryAround(FactionHQ owner, Vector3 origin, Airbase parent, float diagonal, out Candidate candidate)
        {
            var found = new List<Candidate>(1);
            CollectAround(owner, origin, parent, diagonal, found, 1);
            candidate = found.Count > 0 ? found[0] : default;
            return found.Count > 0;
        }

        private void CollectAround(FactionHQ owner, Vector3 origin, Airbase parent, float diagonal,
            List<Candidate> candidates, int limit)
        {
            int start = candidates.Count;
            float exclusion = Mathf.Clamp(500f * Mathf.Clamp(diagonal / 150000f, 0.4f, 2.5f), 250f, 1000f);
            float radius = (parent != null ? parent.GetRadius() : 500f) + exclusion + 120f;
            for (int ring = 0; ring < 3; ring++)
                for (int step = 0; step < 12; step++)
                {
                    if (candidates.Count - start >= limit || candidates.Count >= UplinkPlacement.MaxCandidates) return;
                    float angle = (step * 5 % 12) * Mathf.PI / 6f;
                    Vector3 desired = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (radius + ring * 240f);
                    if (!RearScore(owner, desired, diagonal, out float rear) ||
                        !spawner.TryPlan(desired.ToGlobalPosition(), parent, out GlobalPosition[] points, out _)) continue;
                    candidates.Add(new Candidate(desired.ToGlobalPosition(), points[0], parent, rear));
                }
        }

        private static bool RearScore(FactionHQ owner, Vector3 desired, float diagonal, out float score)
        {
            score = 0f;
            if (ModuleServices.TryGet(out ITerritoryIngress territory))
            {
                GlobalPosition point = desired.ToGlobalPosition();
                if (territory.TryNearestEdge(owner.GetInstanceID(), point.x, point.z, out float x, out float z))
                {
                    float dx = point.x - x, dz = point.z - z;
                    score = Mathf.Sqrt(dx * dx + dz * dz);
                    return territory.TryGetHoldStrength(owner.GetInstanceID(), point.x, point.z, out float hold) &&
                        hold >= 0f && score <= Mathf.Max(40000f, diagonal * 0.25f);
                }
            }
            float nearest = float.PositiveInfinity;
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbase == null || airbase.disabled || airbase.center == null || airbase.CurrentHQ == null || airbase.CurrentHQ == owner) continue;
                    Vector3 delta = desired - airbase.center.position; delta.y = 0f;
                    nearest = Mathf.Min(nearest, delta.magnitude);
                }
            if (float.IsInfinity(nearest)) return true; // No known objective front yet: ordinary legality remains.
            score = nearest;
            // Startup/no-territory fallback: bounded objective-distance proxy, not measured front distance.
            return nearest * 0.5f <= Mathf.Max(40000f, diagonal * 0.25f);
        }

        private static void BorrowAfloat(FactionHQ owner, float diagonal, List<Unit> links)
        {
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            // Carrier first, then escort. Borrow the actual armed unit, following native movement/death/ownership.
            for (int pass = 0; pass < 2 && links.Count < 2; pass++)
                for (int i = 0; i < units.Count && i < MaximumScanUnits && links.Count < 2; i++)
                {
                    if (!(units[i] is Ship ship) || ship.disabled || ship.NetworkHQ != owner || links.Contains(ship) ||
                        (ship.GetAirbase() != null) != (pass == 0) || UplinkSpawner.Down(ship, owner)) continue;
                    if (links.Count > 0 && Vector3.Distance(links[0].transform.position, ship.transform.position) < diagonal * 0.25f) continue;
                    links.Add(ship); // Not added to spawner.owned: teardown never destroys a native carrier/escort.
                }
        }
    }
}

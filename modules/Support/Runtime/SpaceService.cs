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
        private const int MaximumFactions = 8, MaximumBases = 16;
        private sealed class FactionSpace
        {
            public SpaceState State;
            public Unit[] Links;
            public float[] Baseline;
            public IReadOnlyList<Unit> View;
            public bool Field;
            public SpaceObservations Observations;
            public TaskedDesk Tasked;
        }
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
        private SupportManager manager;
        private float nextTick;
        private float nextCleanup;
        private float nextTaskedWarning;
        public int Generation { get; private set; } = 1;
        internal UplinkSpawner Spawner => spawner;

        public void Configure(SupportManager support) => manager = support;

        public void ResetForScene()
        {
            foreach (var pair in factions)
            {
                pair.Value.Tasked?.Retire(); // returns in-flight escrow and makes every old launch callback inert
                pair.Value.State.Clear();
                pair.Value.Observations?.Dispose();
            }
            Generation = Generation == int.MaxValue ? 1 : Generation + 1;
            factions.Clear();
            nextAttempt.Clear();
            nextTick = 0f;
            spawner.ResetForScene();
        }
        private void OnDestroy() => ResetForScene();

        public bool TryGetState(FactionHQ owner, out SpaceState state)
        {
            state = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSpace faction)) return false;
            Refresh(owner, faction, SupportManager.MissionNow()); // Final host gates observe native loss immediately.
            state = faction.State;
            return true;
        }

        internal IReadOnlyList<Unit> UplinksFor(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.View : Array.Empty<Unit>();

        internal SpaceObservations ObservationsFor(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSpace faction) ? faction.Observations : null;

        internal int OpenWindow(FactionHQ owner, GlobalPosition point, float radius, BirdKind source,
            float minimumSpeed, float maximumSpeed) =>
            ObservationsFor(owner)?.Open(point, radius, source, minimumSpeed, maximumSpeed) ?? -1;

        public SpaceFamilyState ReadFamily(FactionHQ owner, float now) =>
            TryGetState(owner, out SpaceState state) ? state.Family(now) : SpaceFamilyState.Dark;

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
            if (now < nextTick) return;
            nextTick = now + 1f;
            spawner.RetryCleanup();
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                if (factions.TryGetValue(hq, out FactionSpace faction)) { Refresh(hq, faction, now); continue; }
                if (factions.Count >= MaximumFactions || nextAttempt.Count >= MaximumFactions && !nextAttempt.ContainsKey(hq)) continue;
                if (nextAttempt.TryGetValue(hq, out float retry) && now < retry) continue;
                nextAttempt[hq] = now + 30f;
                if (TryEstablish(hq, out faction))
                {
                    factions.Add(hq, faction);
                    Plugin.Logger?.LogInfo("[Support.Space] " + hq.name + ": " + faction.Links.Length +
                        " native uplink(s), OPTICAL / RADAR / KINETIC ready" + (faction.Field ? " [FIELD site]" : "."));
                }
                break; // Expensive initial world sampling is limited to one faction per mission second.
            }
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

        private bool TryEstablish(FactionHQ owner, out FactionSpace faction)
        {
            faction = null;
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
            bool fromOrigin = false;
            if (links.Count == 0 && UnitRegistry.allUnits != null)
            {
                // Native stored spawn origins are only candidates; terrain/mission/ownership rules still apply.
                int origins = 0;
                for (int i = 0; i < UnitRegistry.allUnits.Count && i < 4096 && origins < 16; i++)
                {
                    Unit unit = UnitRegistry.allUnits[i];
                    if (unit == null || unit.disabled || unit.NetworkHQ != owner) continue;
                    origins++;
                    Vector3 origin = unit.startPosition.ToLocalPosition();
                    if (unit is Aircraft) origin.y = Datum.LocalSeaY + 100f;
                    if (!GroundPlacement.DryGround(origin, out _) || !TryAround(owner, origin, null, span.magnitude, out Candidate field)) continue;
                    Create(owner, field, links);
                    if (links.Count > 0) { fromOrigin = true; break; }
                }
            }
            if (links.Count == 0) return false;
            var array = links.ToArray();
            var baseline = new float[array.Length];
            for (int i = 0; i < array.Length; i++) baseline[i] = UplinkSpawner.Health(array[i]);
            faction = new FactionSpace { State = new SpaceState(array.Length), Links = array, Baseline = baseline,
                View = Array.AsReadOnly(array), Field = fromOrigin, Observations = new SpaceObservations(owner) };
            faction.Tasked = manager?.CreateTaskedDesk(this, owner, faction.Observations);
            return true;
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
                for (int i = 0; i < units.Count && i < 4096 && links.Count < 2; i++)
                {
                    if (!(units[i] is Ship ship) || ship.disabled || ship.NetworkHQ != owner || links.Contains(ship) ||
                        (ship.GetAirbase() != null) != (pass == 0) || UplinkSpawner.Down(ship, owner)) continue;
                    if (links.Count > 0 && Vector3.Distance(links[0].transform.position, ship.transform.position) < diagonal * 0.25f) continue;
                    links.Add(ship); // Not added to spawner.owned: teardown never destroys a native carrier/escort.
                }
        }
    }
}

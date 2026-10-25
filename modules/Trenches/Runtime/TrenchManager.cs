using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Trenches.Configuration;
using BoscaliSummer.Modules.Trenches.Domain;
using BoscaliSummer.Modules.Trenches.Networking;
using BoscaliSummer.Modules.Trenches.Visuals;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Trenches.Runtime
{
    /// <summary>
    /// Server-authoritative field positions. Command's front traces arrive as ordered
    /// contour polylines — a beachhead ring, a diagonal front, a whole frontier — and each
    /// becomes a natural Bezier line offset onto its owner's side, trimmed to the ground
    /// that accepts it and matured into a belt. Placement is host-side; ditch curves
    /// replicate to clients, who carve the same geometry locally, while native defenders
    /// and scenery replicate through vanilla Mirage spawning.
    /// </summary>
    internal sealed class TrenchManager : MonoBehaviour, ISceneService, IFieldworksReadiness
    {
        public const int MaximumActiveLines = 8;
        private const float TraceRefreshSeconds = 5f;
        private const float BuildAttemptSeconds = 2f;
        private const float SimulationSeconds = 0.75f;
        private const float RetireSeconds = 300f;
        // Position spacing lives in TrenchTraceMath (SpacingFor): same-side lines hold
        // 360m apart while opposing mirror pairs may close to 120m.
        // (see the note above; the old 250m cross-faction floor is gone on purpose)
        private const int MaximumFactions = 8;
        private const int RefusalReportAttempts = 15;
        // An exhausted front rescans from its hottest trace after this long: the front moves and
        // positions retire, so a one-pass scan left the theater empty for the rest of the session.
        private const float RescanSeconds = 90f;
        private const float RefusalReportSeconds = 60f;

        private TrenchesSettings settings;
        private ITerritoryIngress territory;

        private readonly List<TrenchLine> lines = new List<TrenchLine>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchVisualChunk> visualChunks = new Dictionary<int, TrenchVisualChunk>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchGarrison> garrisons = new Dictionary<int, TrenchGarrison>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchWorks> works = new Dictionary<int, TrenchWorks>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchLine> clientLines = new Dictionary<int, TrenchLine>(MaximumActiveLines);
        private readonly List<TrenchLine> clientLineList = new List<TrenchLine>(MaximumActiveLines);

        private readonly FrontlineTracePoint[] tracePoints = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] traceLengths = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] tracePressure = new float[FrontlineTraceLimits.MaximumTraces];
        private readonly int[] traceOrder = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly int[] traceOffset = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly FactionHQ[] factions = new FactionHQ[MaximumFactions];
        private int factionCount;
        private int factionIndex;
        private int traceCount;
        private int traceIndex;
        private int windowStart;
        // One scan cursor per faction slot: every build attempt rotates to the next live
        // front, so a long first front can never fill the theater quota before the other
        // side digs once. factionIds re-seeds a slot whose HQ changed under the index.
        private readonly int[] factionTrace = new int[MaximumFactions];
        private readonly int[] factionWindow = new int[MaximumFactions];
        private readonly int[] factionLastOrdered = new int[MaximumFactions];
        private readonly float[] factionExhaustedAt = new float[MaximumFactions];
        private float nextOverlapLog;
        private readonly int[] factionIds = new int[MaximumFactions];
        private readonly float[] airfieldX = new float[TrenchTraceMath.MaximumAirfields];
        private readonly float[] airfieldZ = new float[TrenchTraceMath.MaximumAirfields];
        private int airfieldCount;

        private int nextLineId = 1;
        private float nextTraceRefresh;
        private float nextBuildAttempt;
        // One planning attempt in flight, stepped inside PlanBudgetMs per frame.
        private const double PlanBudgetMs = 1.0; // halved from 2.0: the plan just spans twice the frames, nothing is drawn until it lands
        private TrenchPlanner.PlanJob planJob;
        private int planMaximum;
        // One belt growth in flight, stepped the same way; never beside planJob (shared planner buffers).
        private TrenchPlanner.PlanJob growJob;
        private TrenchStage growBefore;
        private TrenchLine reinforceLine;
        private bool reinforceBegun;
        // Nests raised per frame in a reinforcement (a belt can ask for six; each is a vanilla spawn plus ground rays),
        // and works placed per simulation tick (each a scenery spawn).
        private const int ReinforceSpawnsPerFrame = 1;
        private const int WorksSpawnsPerTick = 2;
        // A new position is raised over CommitStages frames (garrison, ditch chunk, works + registration); in one frame it was the 6 ms spike.
        private const int CommitStages = 3;
        private sealed class CommitJob
        {
            public TrenchLine Line;
            public int Maximum, Stage;
            public TrenchGarrison Garrison;
            public TrenchVisualChunk Chunk;
            public TrenchWorks Works;
        }
        private CommitJob commitJob;
        private float nextSimulationTick;
        private float nextSeedDelay;
        private float nextGrowthWarning;
        private float nextGarrisonWarning;
        private float nextRefusalWarning;
        private int planRefusals;
        private readonly int[] lastTraceReport = new int[MaximumFactions];

        public event Action OnLinesChanged;

        public IReadOnlyList<TrenchLine> Lines => lines;

        /// <summary>Server lines on the host, replicated curves on clients: what to draw.</summary>
        internal IReadOnlyList<TrenchLine> DisplayLines => GameAccess.IsServer() ? Lines : clientLineList;

        /// <summary>Client-side: a committed or grown curve arrives; carve the same ditch.</summary>
        internal void ReceiveGeometry(int id, int ownerHash, TrenchStage stage,
            Vector3[] curve, Vector3[] threat, Vector3[] support, Vector3[] redoubt,
            Vector3[][] links, Vector3[][] spurs)
        {
            if (GameAccess.IsServer() || !BoscaliMission.FortificationsAllowed) return;
            if (clientLines.TryGetValue(id, out TrenchLine held) && SameGeometry(held, stage, curve, support, redoubt, links, spurs))
                return;   // a late-join resend of a line already carved: no rebuild
            // Threat vectors stay unit XZ: only ditch traces snap height to local terrain.
            SnapHeights(curve);
            SnapHeights(support);
            SnapHeights(redoubt);
            SnapHeights(links);
            SnapHeights(spurs);
            var line = TrenchLine.FromNetwork(id, ownerHash, stage, curve, threat, support, redoubt, links, spurs);
            if (clientLines.ContainsKey(id))
            {
                clientLines[id] = line;
                for (int i = 0; i < clientLineList.Count; i++)
                    if (clientLineList[i].Id == id) { clientLineList[i] = line; break; }
            }
            else
            {
                if (clientLines.Count >= MaximumActiveLines) return;
                clientLines.Add(id, line);
                clientLineList.Add(line);
            }
            if (visualChunks.TryGetValue(id, out TrenchVisualChunk chunk) && chunk != null)
                chunk.Initialize(line);
            else
                visualChunks[id] = CreateVisualChunk(line);
            OnLinesChanged?.Invoke();
        }

        private static bool SameGeometry(TrenchLine held, TrenchStage stage, Vector3[] curve, Vector3[] support,
            Vector3[] redoubt, Vector3[][] links, Vector3[][] spurs)
        {
            static int Len<T>(T[] a) => a?.Length ?? 0;
            static bool SameXZ(Vector3[] a, Vector3[] b)
            {
                if (Len(a) != Len(b)) return false;
                if (a == null || a.Length == 0) return true;
                Vector3 a0 = a[0], b0 = b[0], a1 = a[a.Length - 1], b1 = b[b.Length - 1];
                return Mathf.Abs(a0.x - b0.x) < 0.01f && Mathf.Abs(a0.z - b0.z) < 0.01f &&
                    Mathf.Abs(a1.x - b1.x) < 0.01f && Mathf.Abs(a1.z - b1.z) < 0.01f;
            }
            return held.Stage == stage && SameXZ(held.Curve, curve) && SameXZ(held.Support, support) &&
                SameXZ(held.Redoubt, redoubt) && Len(held.Links) == Len(links) && Len(held.Spurs) == Len(spurs);
        }

        /// <summary>Client-side: a transition arrives; restate the line and its map marks.</summary>
        internal void ReceiveState(int id, TrenchStage stage, int defenders, bool suppressed, bool overrun)
        {
            if (GameAccess.IsServer()) return;
            if (!clientLines.TryGetValue(id, out TrenchLine line)) return;
            bool widthChanged = line.Stage != stage;
            line.Stage = stage;
            line.DefenderCount = defenders;
            line.Suppressed = suppressed;
            line.Overrun = overrun;
            if (widthChanged && visualChunks.TryGetValue(id, out TrenchVisualChunk chunk) && chunk != null)
                chunk.Rebuild();
            OnLinesChanged?.Invoke();
        }

        /// <summary>Client-side: a retired line leaves; its ditch fills back in.</summary>
        internal void ReceiveRemoved(int lineId)
        {
            if (GameAccess.IsServer()) return;
            if (!clientLines.Remove(lineId)) return;
            for (int i = 0; i < clientLineList.Count; i++)
                if (clientLineList[i].Id == lineId) { clientLineList.RemoveAt(i); break; }
            if (visualChunks.TryGetValue(lineId, out TrenchVisualChunk chunk) && chunk != null)
                Destroy(chunk.gameObject);
            visualChunks.Remove(lineId);
            OnLinesChanged?.Invoke();
        }

        /// <summary>
        /// Lays a plan-view trace from the wire on this peer's terrain, at any slope. A point whose probe
        /// misses the terrain takes the height between its snapped neighbours instead of the
        /// wire's y=0, which spiked the ditch down to sea level.
        /// </summary>
        private static void SnapHeights(Vector3[] trace)
        {
            if (trace == null) return;
            int last = -1;   // index of the last snapped point
            for (int i = 0; i < trace.Length; i++)
            {
                if (!TrenchTerrain.TryTerrain(trace[i].x, trace[i].z, out Vector3 ground)) continue;
                trace[i].y = ground.y;
                for (int gap = last + 1; gap < i; gap++)
                    trace[gap].y = last < 0 ? ground.y : Mathf.Lerp(trace[last].y, ground.y, (gap - last) / (float)(i - last));
                last = i;
            }
            if (last >= 0)
                for (int gap = last + 1; gap < trace.Length; gap++) trace[gap].y = trace[last].y;
        }

        private static void SnapHeights(Vector3[][] traces)
        {
            if (traces == null) return;
            for (int i = 0; i < traces.Length; i++) SnapHeights(traces[i]);
        }

        public void Configure(TrenchesSettings config, ITerritoryIngress control)
        {
            settings = config;
            territory = control;
        }

        public void ResetForScene()
        {
            planJob = null;
            growJob = null;
            reinforceLine = null;
            reinforceBegun = false;
            if (commitJob != null)
            {
                commitJob.Works?.Remove();
                if (commitJob.Chunk != null) Destroy(commitJob.Chunk.gameObject);
                commitJob.Garrison?.Remove();
                commitJob = null;
            }
            foreach (var work in works.Values) work.Remove();
            works.Clear();
            foreach (var garrison in garrisons.Values) garrison.Remove();
            garrisons.Clear();
            foreach (var chunk in visualChunks.Values)
            {
                if (chunk != null) Destroy(chunk.gameObject);
            }
            visualChunks.Clear();
            clientLines.Clear();
            clientLineList.Clear();
            lines.Clear();

            TrenchMaterialResolver.ResetForScene();
            TrenchRoadIndex.ResetForScene();
            airfieldCount = 0;

            nextLineId = 1;
            factionCount = 0;
            factionIndex = 0;
            traceCount = traceIndex = windowStart = 0;
            Array.Clear(factionTrace, 0, factionTrace.Length);
            Array.Clear(factionWindow, 0, factionWindow.Length);
            Array.Clear(factionIds, 0, factionIds.Length);
            Array.Fill(factionLastOrdered, -1);
            Array.Fill(factionExhaustedAt, -1f);
            nextTraceRefresh = 0f;
            nextBuildAttempt = 0f;
            nextSimulationTick = 0f;
            nextGrowthWarning = 0f;
            nextGarrisonWarning = 0f;
            nextRefusalWarning = 0f;
            planRefusals = 0;
            Array.Clear(lastTraceReport, 0, lastTraceReport.Length);
            nextSeedDelay = Time.unscaledTime + 2.5f;
        }

        private void OnDestroy()
        {
            ResetForScene();
        }

        private void Update()
        {
            if (settings == null || !GameAccess.IsServer()) return;
            if (!settings.Enabled.Value || !BoscaliMission.FortificationsAllowed)
            {
                if (lines.Count > 0 || commitJob != null || planJob != null || growJob != null)
                {
                    for (int i = 0; i < lines.Count; i++) TrenchNet.BroadcastRemoved(lines[i].Id);
                    ResetForScene();
                }
                return;
            }
            if (Datum.origin == null) return;
            if (Time.unscaledTime < nextSeedDelay) return;

            if (reinforceLine != null) FinishReinforce();
            if (commitJob != null)
            {
                StepCommit();
            }
            else if (planJob != null)
            {
                // The trace cursor stays frozen until the attempt in flight lands.
                if (TrenchPlanner.StepPlan(planJob, PlanBudgetMs)) FinishBuild();
            }
            else if (growJob != null)
            {
                if (TrenchPlanner.StepPlan(growJob, PlanBudgetMs)) FinishGrowth();
            }
            else
            {
                if (Time.unscaledTime >= nextTraceRefresh)
                {
                    nextTraceRefresh = Time.unscaledTime + TraceRefreshSeconds;
                    RebuildFactionList();
                    RefreshTraces();
                    RefreshAirfields();
                }
                int maximum = Math.Min(MaximumActiveLines, settings.MaxTrenchPositions.Value);
                if (lines.Count < maximum && Time.unscaledTime >= nextBuildAttempt)
                {
                    nextBuildAttempt = Time.unscaledTime + BuildAttemptSeconds;
                    TryBuildOne(maximum);
                }
            }

            float now = Time.time;
            if (now >= nextSimulationTick)
            {
                nextSimulationTick = now + SimulationSeconds;
                TickSimulation(now);
            }
        }

        /// <summary>
        /// Rebuilds the faction list without restarting the scan. Resetting the index here
        /// (the first shape of this loop) meant only the first faction's first windows were
        /// ever planned: every refresh sent the scan back to the start of the same front.
        /// A slot whose HQ changed under the index gets a fresh cursor.
        /// </summary>
        private void RebuildFactionList()
        {
            int previousIndex = factionIndex;
            factionCount = 0;
            foreach (FactionHQ owner in FactionRegistry.GetAllHQs())
            {
                if (owner == null) continue;
                if (factionCount >= MaximumFactions) break;
                int slot = factionCount;
                factions[slot] = owner;
                int id = owner.GetInstanceID();
                if (factionIds[slot] != id)
                {
                    factionIds[slot] = id;
                    factionTrace[slot] = 0;
                    factionWindow[slot] = 0;
                    factionLastOrdered[slot] = -1;
                    factionExhaustedAt[slot] = -1f;
                }
                factionCount++;
            }
            factionIndex = factionCount > 0 ? previousIndex % factionCount : 0;
        }

        /// <summary>
        /// Saves the live cursor and rotates to the next faction whose front currently has
        /// traces, restoring its cursor. Traceless factions are skipped without burning an
        /// attempt; when no front has traces the scan idles on an empty one.
        /// </summary>
        private void RotateFaction()
        {
            if (factionCount <= 0)
            {
                traceCount = traceIndex = windowStart = 0;
                return;
            }
            SaveCursor();
            for (int step = 0; step < factionCount; step++)
            {
                factionIndex = TrenchTraceMath.RotateFaction(factionIndex, factionCount);
                CopyTraces();
                if (traceCount > 0)
                {
                    RestoreCursor();
                    return;
                }
            }
            traceIndex = windowStart = 0;
        }

        private void SaveCursor()
        {
            if (factionIndex < 0 || factionIndex >= factionCount) return;
            factionTrace[factionIndex] = traceIndex;
            factionWindow[factionIndex] = windowStart;
        }

        /// <summary>
        /// Restores this faction's cursor onto freshly copied traces. The saved window only
        /// survives on the same ordered trace, or a position would be fitted to the wrong
        /// ground after a pressure reorder; an exhausted front waits RescanSeconds, then
        /// walks again from its hottest trace.
        /// </summary>
        private void RestoreCursor()
        {
            if (factionIndex < 0 || factionIndex >= factionCount || traceCount <= 0)
            {
                traceIndex = windowStart = 0;
                return;
            }
            int savedTrace = factionTrace[factionIndex];
            if (savedTrace >= traceCount)
            {
                float since = factionExhaustedAt[factionIndex];
                if (since < 0f) factionExhaustedAt[factionIndex] = since = Time.unscaledTime;
                if (Time.unscaledTime - since < RescanSeconds)
                {
                    traceIndex = traceCount;
                    windowStart = 0;
                    return;
                }
                // Held stretches refuse again on spacing; lost and moved ones dig anew.
                Plugin.Logger?.LogInfo($"[TRENCHES] Front for {factions[factionIndex]?.name} walked to its end " +
                    $"{RescanSeconds:0}s ago: rescanning from its hottest trace ({lines.Count} positions standing).");
                savedTrace = 0;
                factionWindow[factionIndex] = 0;
                factionLastOrdered[factionIndex] = -1;
            }
            factionExhaustedAt[factionIndex] = -1f;
            traceIndex = Math.Max(0, savedTrace);
            // The scan walks the traces hottest first, so the cursor can land on a different
            // stretch of front after a refresh: the saved window only survives when it is
            // still on the same trace, or a position would be fitted to the wrong ground.
            int ordered = traceOrder[traceIndex];
            windowStart = ordered == factionLastOrdered[factionIndex] ? Math.Max(0, factionWindow[factionIndex]) : 0;
            factionLastOrdered[factionIndex] = ordered;
        }

        /// <summary>
        /// Re-reads the current front on the trace refresh timer while keeping every
        /// faction's own scan cursor, so each long front is walked window by window and
        /// the build rotation keeps alternating sides. A faction whose front vanished
        /// yields to the next live one.
        /// </summary>
        private void RefreshTraces()
        {
            if (factionCount <= 0)
            {
                traceCount = traceIndex = windowStart = 0;
                return;
            }
            SaveCursor();
            CopyTraces();
            if (traceCount <= 0)
            {
                RotateFaction();
                return;
            }
            RestoreCursor();
        }

        private void CopyTraces()
        {
            traceCount = 0;
            if (factionIndex < 0 || factionIndex >= factionCount) return;
            FactionHQ faction = factions[factionIndex];
            if (faction == null) return;
            traceCount = territory.CopyFrontlineTraces(faction.GetInstanceID(),
                tracePoints, traceLengths, tracePressure);
            TrenchTraceMath.OrderByPressure(tracePressure, traceCount, traceOrder);
            int offset = 0;
            for (int t = 0; t < traceCount; t++)
            {
                traceOffset[t] = offset;
                offset += traceLengths[t];
            }
            // A front that is reported but never planned is the one failure that used to be
            // silent; the intake line makes that state visible in the log.
            int slot = factionIndex;
            if (slot < 0 || slot >= lastTraceReport.Length || lastTraceReport[slot] == traceCount) return;
            lastTraceReport[slot] = traceCount;
            int stations = 0;
            for (int t = 0; t < traceCount; t++) stations += traceLengths[t];
            Plugin.Logger?.LogInfo($"[TRENCHES] Front traces for {factions[factionIndex].name}: " +
                $"{traceCount} trace(s), {stations} stations.");
        }

        /// <summary>
        /// One planning attempt on the current faction's front, then the scan rotates to
        /// the next live front: positions dig where the fighting is on every side, never
        /// one faction's whole frontier first.
        /// </summary>
        private void TryBuildOne(int maximum)
        {
            if (traceCount <= 0 || traceIndex >= traceCount)
            {
                RotateFaction();
                if (traceCount <= 0 || traceIndex >= traceCount) return;
            }
            FactionHQ owner = factions[factionIndex];
            int trace = traceOrder[traceIndex];
            int offset = traceOffset[trace];
            int length = traceLengths[trace];
            if (length < 2 || owner == null || lines.Count >= maximum)
            {
                NextTrace();
                RotateFaction();
                return;
            }

            // Strategic siting probes, both optional: a missing forest index or road
            // network plans the same relief-only position as before.
            Func<float, float, bool> foliageAt = null;
            if (ModuleServices.TryGet<IFoliageCover>(out IFoliageCover foliage) && foliage.Ready)
                foliageAt = foliage.Contains;
            Func<float, float, float> roadAt = null;
            if (TrenchRoadIndex.EnsureBuilt())
                roadAt = (x, z) => TrenchRoadIndex.TryDistance(x, z, TrenchTraceMath.RoadClearDistance,
                    out float ditch) ? ditch : float.NaN;
            planJob = TrenchPlanner.BeginPlanWindow(nextLineId, owner.name + "_Front_" + nextLineId, owner,
                tracePressure[trace], tracePoints, offset, length, windowStart, territory,
                foliageAt, roadAt, airfieldX, airfieldZ, airfieldCount);
            planMaximum = maximum;
        }

        /// <summary>The rest of one attempt, once its plan has finished stepping.</summary>
        private void FinishBuild()
        {
            TrenchPlanner.PlanJob job = planJob;
            planJob = null;
            int maximum = planMaximum;
            bool planned = job.Planned;
            TrenchLine line = job.Line;
            int next = job.NextStation;
            TrenchRefusal refusal = job.Refusal;
            if (!planned)
            {
                NotePlanRefusal(refusal);
                AdvanceWindow(next);
                RotateFaction();
                return;
            }
            nextLineId++;
            if (!SpacingOk(line))
            {
                NotePlanRefusal(TrenchRefusal.TooClose);
                AdvanceWindow(next);
                RotateFaction();
                return;
            }
            planRefusals = 0;
            commitJob = new CommitJob { Line = line, Maximum = maximum };   // raised from the next frame, a stage per frame
            AdvanceWindow(next);
            RotateFaction();
        }

        /// <summary>
        /// Bounded honesty: a front that keeps refusing positions says so once a minute
        /// instead of leaving an empty theater unexplained.
        /// </summary>
        private void NotePlanRefusal(TrenchRefusal refusal)
        {
            planRefusals++;
            if (lines.Count > 0 || planRefusals < RefusalReportAttempts ||
                Time.unscaledTime < nextRefusalWarning) return;
            nextRefusalWarning = Time.unscaledTime + RefusalReportSeconds;
            planRefusals = 0;
            Plugin.Logger?.LogWarning($"[TRENCHES] No position accepted yet: the front was refused ({refusal}) " +
                "on every attempt. Check the control field and terrain probe.");
        }

        private void AdvanceWindow(int next)
        {
            if (next <= 0 || next <= windowStart) NextTrace();
            else windowStart = next;
            SaveCursor();
        }

        private void NextTrace()
        {
            traceIndex++;
            windowStart = 0;
            SaveCursor();
        }

        private bool SpacingOk(TrenchLine candidate)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                TrenchLine existing = lines[i];
                float spacing = TrenchTraceMath.SpacingFor(existing.OwnerHq == candidate.OwnerHq);
                float dx = existing.Center.x - candidate.Center.x;
                float dz = existing.Center.z - candidate.Center.z;
                if (dx * dx + dz * dz < spacing * spacing) return false;
                if (existing.OwnerHq == candidate.OwnerHq && OverlapsAlong(candidate, existing))
                {
                    if (Time.unscaledTime >= nextOverlapLog)
                    {
                        nextOverlapLog = Time.unscaledTime + 30f;
                        Plugin.Logger?.LogInfo($"[TRENCHES] '{candidate.Name}' refused: it lies along '{existing.Name}'.");
                    }
                    return false;
                }
            }
            return true;
        }

        private static bool OverlapsAlong(TrenchLine candidate, TrenchLine existing)
        {
            Vector3[] anchors = candidate.Anchors;
            Vector3[] curve = existing.Curve;
            if (anchors == null || curve == null || anchors.Length == 0) return false;
            float radiusSq = TrenchTraceMath.OverlapRadius * TrenchTraceMath.OverlapRadius;
            float reach = existing.Radius + TrenchTraceMath.OverlapRadius;
            int near = 0;
            for (int a = 0; a < anchors.Length; a++)
            {
                Vector3 p = anchors[a];
                float cx = p.x - existing.Center.x, cz = p.z - existing.Center.z;
                if (cx * cx + cz * cz > reach * reach) continue;   // outside the line's circle: no station can be near
                for (int s = 0; s < curve.Length; s++)
                {
                    float dx = curve[s].x - p.x, dz = curve[s].z - p.z;
                    if (dx * dx + dz * dz <= radiusSq) { near++; break; }
                }
            }
            return TrenchTraceMath.OverlapsExisting(near, anchors.Length);
        }

        /// <summary>One frame of the commit in flight; the position joins the theater when its last stage is done, or is rolled back.</summary>
        private void StepCommit()
        {
            CommitJob job = commitJob;
            try
            {
                if (!BuildStage(job)) { commitJob = null; return; }
            }
            catch (Exception ex)
            {
                job.Works?.Remove();
                if (job.Chunk != null) Destroy(job.Chunk.gameObject);
                job.Garrison?.Remove();
                commitJob = null;
                Plugin.Logger?.LogWarning("[TRENCHES] Position creation rolled back: " + ex.Message);
                return;
            }
            if (++job.Stage < CommitStages) return;
            commitJob = null;
            Commit(job);
        }

        /// <summary>One stage of a commit: the garrison's spawns, then the ditch chunk, then the works. False gives the position up.</summary>
        private bool BuildStage(CommitJob job)
        {
            TrenchLine line = job.Line;
            if (job.Stage == 0)
            {
                if (lines.Count >= job.Maximum) return false;
                TrenchGarrison garrison = job.Garrison = new TrenchGarrison(line);
                if (TrenchRoadIndex.EnsureBuilt())
                    garrison.RoadDistanceAt = (x, z) => TrenchRoadIndex.TryDistance(x, z,
                        TrenchTraceMath.RoadWatchDistance, out float watch) ? watch : float.NaN;
                if (garrison.Establish()) return true;
                if (Time.unscaledTime >= nextGarrisonWarning)
                {
                    nextGarrisonWarning = Time.unscaledTime + 30f;
                    Plugin.Logger?.LogWarning("[TRENCHES] Position rejected: " + garrison.LastFailure +
                        ". No empty cosmetic position was created.");
                }
                return false;
            }
            if (job.Stage == 1)
            {
                job.Chunk = CreateVisualChunk(line);
                return true;
            }
            job.Works = new TrenchWorks(line);
            job.Works.Deploy(line.Stage, WorksSpawnsPerTick);
            return true;
        }

        private void Commit(CommitJob job)
        {
            TrenchLine line = job.Line;
            TrenchGarrison garrison = job.Garrison;
            lines.Add(line);
            garrisons.Add(line.Id, garrison);
            works.Add(line.Id, job.Works);
            visualChunks.Add(line.Id, job.Chunk);
            line.OwnerHash = TrenchWire.OwnerHashFor(line.OwnerHq != null ? line.OwnerHq.name : null);
            line.DefenderCount = garrison.Alive;
            line.DugAt = Time.time;
            line.HostileSince = -1f;
            line.NextGrowthAt = Time.time + TrenchTraceMath.GrowthInterval(settings.GrowthIntervalSeconds.Value, line.Pressure);
            Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}' dug at global {line.Center}: {line.Curve.Length} curve stations, {line.Anchors.Length} anchors, pressure {line.Pressure:0.00}.");
            TrenchNet.BroadcastGeometry(line);
            TrenchNet.BroadcastState(line);
            OnLinesChanged?.Invoke();
        }

        private TrenchVisualChunk CreateVisualChunk(TrenchLine line)
        {
            var go = new GameObject($"TrenchVisual_{line.Id}");
            var chunk = go.AddComponent<TrenchVisualChunk>();
            chunk.Lod2Distance = settings != null ? settings.LODDistanceFar.Value : 12000f;
            chunk.Lod0Distance = settings != null ? settings.LODDistanceNear.Value : 600f;
            chunk.Lod1Distance = Mathf.Max(chunk.Lod0Distance * 2f, chunk.Lod2Distance * 0.22f);
            chunk.EarthTint = EarthTintFor(line);

            try { chunk.Initialize(line); }
            catch { Destroy(go); throw; }
            Plugin.Logger?.LogInfo($"[TRENCHES] World chunk {line.Id}: {chunk.MeshCount} meshes, center={chunk.WorldCenter}, camera distance={chunk.CameraDistance:0}m, lod={chunk.ActiveLod} (near {chunk.Lod0Distance:0}/{chunk.Lod1Distance:0}/{chunk.Lod2Distance:0}m), material={chunk.EarthMaterial}.");
            return chunk;
        }

        /// <summary>
        /// The map's own terrain colour along the line (the sampler vanilla's emplacement rings tint
        /// from), as a half-strength multiplier over the earthwork palette's mean, so the spoil keeps its
        /// turned-earth read but takes the local hue. White when the map gives no colour.
        /// </summary>
        private static Color EarthTintFor(TrenchLine line)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null || line?.Curve == null || line.Curve.Length == 0) return Color.white;
            Vector3[] curve = line.Curve;
            Color sum = Color.clear;
            int count = 0;
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = curve[i * (curve.Length - 1) / 4];
                if (!level.TryGetTerrainColorAtCoordinate(new GlobalPosition(p.x, p.y, p.z), out Color c)) continue;
                sum += c;
                count++;
            }
            if (count == 0) return Color.white;
            Color ground = sum / count;
            var paletteMean = new Color(0.50f, 0.40f, 0.26f);
            Color tint = new Color(
                Mathf.Clamp(ground.r / paletteMean.r, 0.65f, 1.35f),
                Mathf.Clamp(ground.g / paletteMean.g, 0.65f, 1.35f),
                Mathf.Clamp(ground.b / paletteMean.b, 0.65f, 1.35f), 1f);
            return Color.Lerp(Color.white, tint, 0.5f);
        }

        private void TickSimulation(float now)
        {
            bool anyChanged = false;
            bool advancedOne = false;
            int worksLeft = WorksSpawnsPerTick;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                TrenchLine line = lines[i];
                TrenchGarrison garrison = garrisons[line.Id];
                bool changed = garrison.Poll(now);
                // One works budget per tick shared by every line (8 lines used to place up to 16 scenery spawns in one frame).
                if (worksLeft > 0 && works.TryGetValue(line.Id, out TrenchWorks lineWorks)) worksLeft -= lineWorks.Deploy(line.Stage, worksLeft);
                int previousDefenders = line.DefenderCount;
                line.DefenderCount = garrison.Alive;

                bool suppressed = now < garrison.SuppressedUntil;
                changed |= line.Suppressed != suppressed;
                line.Suppressed = suppressed;

                // The field's verdict is hysteretic: one hostile sample never ends a line.
                if (StillOwned(line))
                {
                    line.HostileSince = -1f;
                    if (line.OwnedSince < 0f) line.OwnedSince = now;
                }
                else
                {
                    line.OwnedSince = -1f;
                    if (line.HostileSince < 0f) line.HostileSince = now;
                }

                if (!line.Overrun && garrison.Overrun)
                {
                    line.Overrun = true;
                    line.RetireAt = now + RetireSeconds;
                    line.DefenderCount = 0;
                    changed = true;
                    Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}' neutralized: no defenders remain; growth stopped, no respawns.");
                }
                else if (!line.Overrun && TrenchTraceMath.FieldAbandons(now, line.DugAt, line.HostileSince))
                {
                    // Cut off, not destroyed: living nests keep fighting until they are killed,
                    // and the earthwork retires only after the last of them.
                    line.Overrun = true;
                    line.RetireAt = now + RetireSeconds;
                    changed = true;
                    Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}' cut off: the front moved past it; growth stopped, {line.DefenderCount} defenders fight on without relief.");
                }
                else if (line.Overrun && !garrison.Overrun && garrison.Alive > 0 && line.OwnedSince >= 0f &&
                    now - line.OwnedSince >= TrenchTraceMath.AbandonSeconds)
                {
                    // Relieved: the front swung back over a cut-off line whose nests held. It digs
                    // and shells again; nothing respawns, the surviving slots simply carry on. A
                    // cut-off line held its quota slot and 360m of spacing forever otherwise.
                    line.Overrun = false;
                    changed = true;
                    Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}' relieved: the front is back over it; {line.DefenderCount} defenders resume.");
                }
                if (line.Overrun && previousDefenders > 0 && line.DefenderCount == 0)
                    line.RetireAt = now + RetireSeconds;
                if (line.Overrun && now >= line.RetireAt && garrison.Alive == 0)
                {
                    if (visualChunks.TryGetValue(line.Id, out TrenchVisualChunk obsolete) && obsolete != null)
                        Destroy(obsolete.gameObject);
                    visualChunks.Remove(line.Id);
                    if (works.TryGetValue(line.Id, out TrenchWorks obsoleteWorks)) obsoleteWorks.Remove();
                    works.Remove(line.Id);
                    garrison.Remove();
                    garrisons.Remove(line.Id);
                    TrenchNet.BroadcastRemoved(line.Id);
                    lines.RemoveAt(i);
                    anyChanged = true;
                    continue;
                }

                if (suppressed) line.NextGrowthAt = Math.Max(line.NextGrowthAt, garrison.SuppressedUntil);
                if (!advancedOne && planJob == null && growJob == null && reinforceLine == null && commitJob == null &&
                    TrenchTraceMath.CanAdvance(line.Overrun, now, garrison.SuppressedUntil, line.NextGrowthAt))
                {
                    advancedOne = true;
                    line.NextGrowthAt = now + TrenchTraceMath.GrowthInterval(settings.GrowthIntervalSeconds.Value, line.Pressure);
                    growBefore = line.Stage;
                    growJob = TrenchPlanner.BeginGrowBelt(line, territory, airfieldX, airfieldZ, airfieldCount);
                }

                if (changed)
                {
                    anyChanged = true;
                    Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}': {line.Stage}, {line.DefenderCount} defenders, {line.Curve.Length} curve stations/{line.Anchors.Length} anchors, suppressed={line.Suppressed}, overrun={line.Overrun}.");
                    TrenchNet.BroadcastState(line);
                }
            }

            if (anyChanged) OnLinesChanged?.Invoke();
        }

        /// <summary>
        /// The rest of one growth step, once its belt has finished stepping: the mesh
        /// rebuild now, the nests a frame later, so no frame pays for all three.
        /// </summary>
        private void FinishGrowth()
        {
            TrenchLine line = growJob.Line;
            bool grown = growJob.Planned;
            growJob = null;
            if (!lines.Contains(line)) return; // retired while its belt was being planned
            if (!grown)
            {
                if (line.Stage != TrenchStage.Saps && Time.unscaledTime >= nextGrowthWarning)
                {
                    nextGrowthWarning = Time.unscaledTime + 60f;
                    Plugin.Logger?.LogWarning($"[TRENCHES] '{line.Name}' growth held at {line.Stage}: the ground refused the next belt trace.");
                }
                return;
            }
            string missing = MissingBeltTrace(line, growBefore);
            if (missing != null)
                Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}' advanced to {line.Stage} without its {missing}: the ground refused it {TrenchTraceMath.BeltRefusalLimit} times.");
            if (visualChunks.TryGetValue(line.Id, out TrenchVisualChunk chunk) && chunk != null) chunk.Rebuild();
            TrenchNet.BroadcastGeometry(line);
            reinforceLine = line;
        }

        private void FinishReinforce()
        {
            TrenchLine line = reinforceLine;
            if (!lines.Contains(line) || !garrisons.TryGetValue(line.Id, out TrenchGarrison garrison))
            {
                reinforceLine = null;
                reinforceBegun = false;
                return;
            }
            if (!reinforceBegun)
            {
                reinforceBegun = true;
                garrison.ResetAttempts();
            }
            if (!garrison.Reinforce(ReinforceSpawnsPerFrame)) return;   // the rest of the nests: next frame
            reinforceLine = null;
            reinforceBegun = false;
            garrison.Poll(Time.time);
            line.DefenderCount = garrison.Alive;
            int desired = Math.Min(TrenchTraceMath.DefenderBudget(line.Stage), TrenchTraceMath.MaxDefendersForPressure(line.Pressure));
            string shortfall = line.DefenderCount < desired ? $" (of {desired}; last refusal: {garrison.LastFailure})" : "";
            Plugin.Logger?.LogInfo($"[TRENCHES] '{line.Name}': {line.Stage}, {line.DefenderCount} defenders{shortfall}, {line.Curve.Length} curve stations/{line.Anchors.Length} anchors, suppressed={line.Suppressed}, overrun={line.Overrun}.");
            TrenchNet.BroadcastState(line);
            OnLinesChanged?.Invoke();
        }

        /// <summary>Name of the belt trace the stage just entered was meant to add but could not.</summary>
        private static string MissingBeltTrace(TrenchLine line, TrenchStage before)
        {
            if (line.Stage == before) return null;
            if (line.Stage == TrenchStage.Support && line.Support == null) return "support trace";
            if (line.Stage == TrenchStage.Redoubt && line.Redoubt == null) return "redoubt trace";
            if (line.Stage == TrenchStage.Saps && line.Spurs == null) return "saps";
            return null;
        }

        /// <summary>
        /// Caches every airbase centre once per trace refresh, so the planner keeps the
        /// ditch off runways. Fields never move, but the lookup can appear late.
        /// </summary>
        private void RefreshAirfields()
        {
            airfieldCount = 0;
            if (FactionRegistry.airbaseLookup == null) return;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airfieldCount >= airfieldX.Length) break;
                if (airbase == null || airbase.AttachedAirbase) continue;
                Vector3 centre = airbase.center != null ? airbase.center.position : airbase.transform.position;
                GlobalPosition at = centre.ToGlobalPosition();
                airfieldX[airfieldCount] = at.x;
                airfieldZ[airfieldCount] = at.z;
                airfieldCount++;
            }
        }

        private bool StillOwned(TrenchLine line)
        {
            // The line sits inside the contested band, where only the signed control field
            // separates the sides. A line the enemy has pushed past is neutralized; existing
            // defenders must not erase their own position while pressure advances the border.
            // Read at five stations along the ditch, not the centroid: a curved line's centroid
            // (a ring round a pocket) lies off the ditch, often inside the enemy's cells.
            if (line.OwnerHq == null) return false;
            Vector3[] curve = line.Curve;
            if (curve == null || curve.Length == 0)
                return territory.TryGetHoldStrength(line.OwnerHq.GetInstanceID(), line.Center.x, line.Center.z,
                    out float centre) && TrenchTraceMath.HoldsOwnSide(centre);
            int id = line.OwnerHq.GetInstanceID();
            int own = 0;
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = curve[i * (curve.Length - 1) / 4];
                if (territory.TryGetHoldStrength(id, p.x, p.z, out float hold) && TrenchTraceMath.HoldsOwnSide(hold)) own++;
            }
            return own >= 3;
        }
    }
}

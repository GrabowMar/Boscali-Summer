using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Server-authoritative field positions. Command's front traces arrive as ordered
    /// contour polylines — a beachhead ring, a diagonal front, a whole frontier — and each
    /// becomes a natural Bezier line offset onto its owner's side, trimmed to the ground
    /// that accepts it and matured into a belt. Placement and ditch geometry are host-local;
    /// native defenders and scenery replicate through vanilla Mirage spawning.
    /// </summary>
    internal sealed class TrenchManager : MonoBehaviour, ISceneService, IFieldworksReadiness
    {
        public const int MaximumActiveLines = 16;
        private const float TraceRefreshSeconds = 5f;
        private const float BuildAttemptSeconds = 2f;
        private const float SimulationSeconds = 0.75f;
        private const float RetireSeconds = 300f;
        private const float SameOwnerSpacing = 360f;
        private const float OtherOwnerSpacing = 250f;
        private const int MaximumFactions = 8;
        private const int RefusalReportAttempts = 15;
        private const float RefusalReportSeconds = 60f;

        private TrenchesSettings settings;
        private ManualLogSource logger;
        private ITerritoryIngress territory;

        private readonly List<TrenchLine> lines = new List<TrenchLine>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchVisualChunk> visualChunks = new Dictionary<int, TrenchVisualChunk>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchGarrison> garrisons = new Dictionary<int, TrenchGarrison>(MaximumActiveLines);
        private readonly Dictionary<int, TrenchWorks> works = new Dictionary<int, TrenchWorks>(MaximumActiveLines);

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
        private int lastOrderedTrace;

        private int nextLineId = 1;
        private float nextTraceRefresh;
        private float nextBuildAttempt;
        private float nextSimulationTick;
        private float nextSeedDelay;
        private float nextGrowthWarning;
        private float nextGarrisonWarning;
        private float nextRefusalWarning;
        private int planRefusals;
        private readonly int[] lastTraceReport = new int[MaximumFactions];

        public event Action OnLinesChanged;

        public IReadOnlyList<TrenchLine> Lines => lines;

        public void CountNear(FactionHQ observer, float x, float z, float radius,
            out int friendlyDefenders, out int observedHostileDefenders, out int suppressedFriendly)
        {
            friendlyDefenders = observedHostileDefenders = suppressedFriendly = 0;
            if (observer == null || settings == null || !settings.Enabled.Value ||
                !GameAccess.IsServer() || radius <= 0f) return;
            float radiusSq = radius * radius;
            for (int i = 0; i < lines.Count; i++)
            {
                TrenchLine line = lines[i];
                if (line == null || line.Overrun || line.DefenderCount <= 0 || line.Curve == null) continue;
                float dx = line.Center.x - x, dz = line.Center.z - z;
                float reach = radius + line.Radius;
                if (dx * dx + dz * dz > reach * reach) continue;
                bool near = false;
                for (int station = 0; station < line.Curve.Length; station++)
                {
                    dx = line.Curve[station].x - x;
                    dz = line.Curve[station].z - z;
                    if (dx * dx + dz * dz > radiusSq) continue;
                    near = true;
                    break;
                }
                if (!near) continue;
                if (ReferenceEquals(line.OwnerHq, observer))
                {
                    friendlyDefenders += line.DefenderCount;
                    if (line.Suppressed) suppressedFriendly += line.DefenderCount;
                }
                else if (garrisons.TryGetValue(line.Id, out TrenchGarrison garrison) &&
                         garrison.ObservedBy(observer))
                    observedHostileDefenders += line.DefenderCount;
            }
        }

        public void Configure(TrenchesSettings config, ManualLogSource log, ITerritoryIngress control)
        {
            settings = config;
            logger = log;
            territory = control;
        }

        public void ResetForScene()
        {
            foreach (var work in works.Values) work.Remove();
            works.Clear();
            foreach (var garrison in garrisons.Values) garrison.Remove();
            garrisons.Clear();
            foreach (var chunk in visualChunks.Values)
            {
                if (chunk != null) Destroy(chunk.gameObject);
            }
            visualChunks.Clear();
            lines.Clear();

            TrenchMaterialResolver.ResetForScene();
            TrenchRoadIndex.ResetForScene();

            nextLineId = 1;
            factionCount = 0;
            factionIndex = 0;
            traceCount = traceIndex = windowStart = 0;
            lastOrderedTrace = -1;
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
            if (settings == null || !settings.Enabled.Value || !GameAccess.IsServer() || Datum.origin == null) return;
            if (Time.unscaledTime < nextSeedDelay) return;

            if (Time.unscaledTime >= nextTraceRefresh)
            {
                nextTraceRefresh = Time.unscaledTime + TraceRefreshSeconds;
                RebuildFactionList();
                RefreshTraces();
            }
            int maximum = Math.Min(MaximumActiveLines, settings.MaxTrenchPositions.Value);
            if (lines.Count < maximum && Time.unscaledTime >= nextBuildAttempt)
            {
                nextBuildAttempt = Time.unscaledTime + BuildAttemptSeconds;
                TryBuildOne(maximum);
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
        /// </summary>
        private void RebuildFactionList()
        {
            int previousIndex = factionIndex;
            factionCount = 0;
            foreach (FactionHQ owner in FactionRegistry.GetAllHQs())
            {
                if (owner == null) continue;
                if (factionCount >= MaximumFactions) break;
                factions[factionCount++] = owner;
            }
            factionIndex = factionCount > 0 ? previousIndex % factionCount : 0;
        }

        private void AdvanceFaction()
        {
            if (factionCount <= 0)
            {
                traceCount = traceIndex = windowStart = 0;
                return;
            }
            factionIndex = (factionIndex + 1) % factionCount;
            CopyTraces();
        }

        /// <summary>
        /// Re-reads the current front on the trace refresh timer while keeping the scan
        /// cursor, so a long front is walked window by window. The cursor only resets when
        /// its faction has no traces or a trace is exhausted, which rotates the faction.
        /// </summary>
        private void RefreshTraces()
        {
            if (factionCount <= 0)
            {
                traceCount = traceIndex = windowStart = 0;
                return;
            }
            int previousTrace = traceIndex;
            int previousWindow = windowStart;
            CopyTraces();
            if (traceCount <= 0)
            {
                AdvanceFaction();
                return;
            }
            traceIndex = Math.Min(previousTrace, traceCount - 1);
            // The scan walks the traces hottest first, so the cursor can land on a different
            // stretch of front after a refresh: the saved window only survives when it is
            // still on the same trace, or a position would be fitted to the wrong ground.
            int ordered = traceOrder[traceIndex];
            windowStart = ordered == lastOrderedTrace ? Math.Max(0, previousWindow) : 0;
            lastOrderedTrace = ordered;
        }

        private void CopyTraces()
        {
            traceIndex = 0;
            windowStart = 0;
            traceCount = territory.CopyFrontlineTraces(factions[factionIndex].GetInstanceID(),
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
            logger?.LogInfo($"[TRENCHES] Front traces for {factions[factionIndex].name}: " +
                $"{traceCount} trace(s), {stations} stations.");
        }

        /// <summary>One planning attempt per call; the scan walks the hottest traces first so
        /// positions dig where the fighting is before the quiet stretches.</summary>
        private void TryBuildOne(int maximum)
        {
            if (traceCount <= 0 || traceIndex >= traceCount)
            {
                AdvanceFaction();
                return;
            }
            FactionHQ owner = factions[factionIndex];
            int trace = traceOrder[traceIndex];
            int offset = traceOffset[trace];
            int length = traceLengths[trace];
            if (length < 2 || owner == null || lines.Count >= maximum)
            {
                NextTrace();
                return;
            }

            // Strategic siting probes, both optional: a missing forest index or road
            // network plans the same relief-only position as before.
            Func<float, float, bool> foliageAt = null;
            if (ModServices.TryGet<IFoliageCover>(out IFoliageCover foliage) && foliage.Ready)
                foliageAt = foliage.Contains;
            Func<float, float, float> roadAt = null;
            if (TrenchRoadIndex.EnsureBuilt())
                roadAt = (x, z) => TrenchRoadIndex.TryDistance(x, z, TrenchTraceMath.RoadClearDistance,
                    out float ditch) ? ditch : float.NaN;
            bool planned = TrenchPlanner.TryPlanWindow(nextLineId, owner.name + "_Front_" + nextLineId, owner,
                tracePressure[trace], tracePoints, offset, length, windowStart, territory,
                out TrenchLine line, out int next, out TrenchRefusal refusal, foliageAt, roadAt);
            if (!planned)
            {
                NotePlanRefusal(refusal);
                AdvanceWindow(next);
                return;
            }
            nextLineId++;
            if (!SpacingOk(line))
            {
                NotePlanRefusal(TrenchRefusal.TooClose);
                AdvanceWindow(next);
                return;
            }
            planRefusals = 0;
            Commit(line, maximum);
            AdvanceWindow(next);
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
            logger?.LogWarning($"[TRENCHES] No position accepted yet: the front was refused ({refusal}) " +
                "on every attempt. Check the control field and terrain probe.");
        }

        private void AdvanceWindow(int next)
        {
            if (next <= 0 || next <= windowStart) NextTrace();
            else windowStart = next;
        }

        private void NextTrace()
        {
            traceIndex++;
            windowStart = 0;
            if (traceIndex >= traceCount) AdvanceFaction();
        }

        private bool SpacingOk(TrenchLine candidate)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                TrenchLine existing = lines[i];
                float spacing = existing.OwnerHq == candidate.OwnerHq ? SameOwnerSpacing : OtherOwnerSpacing;
                float dx = existing.Center.x - candidate.Center.x;
                float dz = existing.Center.z - candidate.Center.z;
                if (dx * dx + dz * dz < spacing * spacing) return false;
            }
            return true;
        }

        private bool Commit(TrenchLine line, int maximum)
        {
            if (lines.Count >= maximum) return false;
            TrenchGarrison garrison = null;
            TrenchWorks lineWorks = null;
            TrenchVisualChunk chunk = null;
            try
            {
                garrison = new TrenchGarrison(line);
                if (TrenchRoadIndex.EnsureBuilt())
                    garrison.RoadDistanceAt = (x, z) => TrenchRoadIndex.TryDistance(x, z,
                        TrenchTraceMath.RoadWatchDistance, out float watch) ? watch : float.NaN;
                if (!garrison.Establish())
                {
                    if (Time.unscaledTime >= nextGarrisonWarning)
                    {
                        nextGarrisonWarning = Time.unscaledTime + 30f;
                        logger?.LogWarning("[TRENCHES] Position rejected: " + garrison.LastFailure +
                            ". No empty cosmetic position was created.");
                    }
                    return false;
                }
                chunk = CreateVisualChunk(line);
                lineWorks = new TrenchWorks(line);
                lineWorks.Deploy(line.Stage);
            }
            catch (Exception ex)
            {
                lineWorks?.Remove();
                if (chunk != null) Destroy(chunk.gameObject);
                garrison?.Remove();
                logger?.LogWarning("[TRENCHES] Position creation rolled back: " + ex.Message);
                return false;
            }

            lines.Add(line);
            garrisons.Add(line.Id, garrison);
            works.Add(line.Id, lineWorks);
            visualChunks.Add(line.Id, chunk);
            line.DefenderCount = garrison.Alive;
            line.DugAt = Time.time;
            line.HostileSince = -1f;
            line.NextGrowthAt = Time.time + Math.Max(15f, settings.GrowthIntervalSeconds.Value);
            logger?.LogInfo($"[TRENCHES] '{line.Name}' dug at global {line.Center}: {line.Curve.Length} curve stations, {line.Anchors.Length} anchors, pressure {line.Pressure:0.00}.");
            OnLinesChanged?.Invoke();
            return true;
        }

        private TrenchVisualChunk CreateVisualChunk(TrenchLine line)
        {
            var go = new GameObject($"TrenchVisual_{line.Id}");
            var chunk = go.AddComponent<TrenchVisualChunk>();
            chunk.Lod2Distance = settings != null ? settings.LODDistanceFar.Value : 12000f;
            chunk.Lod0Distance = settings != null ? settings.LODDistanceNear.Value : 600f;
            chunk.Lod1Distance = Mathf.Max(chunk.Lod0Distance * 2f, chunk.Lod2Distance * 0.22f);

            try { chunk.Initialize(line); }
            catch { Destroy(go); throw; }
            logger?.LogInfo($"[TRENCHES] World chunk {line.Id}: {chunk.MeshCount} meshes, center={chunk.WorldCenter}, camera distance={chunk.CameraDistance:0}m, lod={chunk.ActiveLod} (near {chunk.Lod0Distance:0}/{chunk.Lod1Distance:0}/{chunk.Lod2Distance:0}m), material={chunk.EarthMaterial}.");
            return chunk;
        }

        private void TickSimulation(float now)
        {
            bool anyChanged = false;
            bool advancedOne = false;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                TrenchLine line = lines[i];
                TrenchGarrison garrison = garrisons[line.Id];
                bool changed = garrison.Poll(now);
                if (works.TryGetValue(line.Id, out TrenchWorks lineWorks)) lineWorks.Deploy(line.Stage);
                int previousDefenders = line.DefenderCount;
                line.DefenderCount = garrison.Alive;

                bool suppressed = now < garrison.SuppressedUntil;
                changed |= line.Suppressed != suppressed;
                line.Suppressed = suppressed;

                // The field's verdict is hysteretic: one hostile sample never ends a line.
                if (StillOwned(line)) line.HostileSince = -1f;
                else if (line.HostileSince < 0f) line.HostileSince = now;

                if (!line.Overrun && garrison.Overrun)
                {
                    line.Overrun = true;
                    line.RetireAt = now + RetireSeconds;
                    line.DefenderCount = 0;
                    changed = true;
                    logger?.LogInfo($"[TRENCHES] '{line.Name}' neutralized: no defenders remain; growth stopped, no respawns.");
                }
                else if (!line.Overrun && TrenchTraceMath.FieldAbandons(now, line.DugAt, line.HostileSince))
                {
                    // Cut off, not destroyed: living nests keep fighting until they are killed,
                    // and the earthwork retires only after the last of them.
                    line.Overrun = true;
                    line.RetireAt = now + RetireSeconds;
                    changed = true;
                    logger?.LogInfo($"[TRENCHES] '{line.Name}' cut off: the front moved past it; growth stopped, {line.DefenderCount} defenders fight on without relief.");
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
                    lines.RemoveAt(i);
                    anyChanged = true;
                    continue;
                }

                if (suppressed) line.NextGrowthAt = Math.Max(line.NextGrowthAt, garrison.SuppressedUntil);
                if (!advancedOne && TrenchTraceMath.CanAdvance(line.Overrun, now, garrison.SuppressedUntil, line.NextGrowthAt))
                {
                    advancedOne = true;
                    line.NextGrowthAt = now + Math.Max(15f, settings.GrowthIntervalSeconds.Value);
                    TrenchStage before = line.Stage;
                    if (TrenchPlanner.TryGrowBelt(line, territory))
                    {
                        changed = true;
                        string missing = MissingBeltTrace(line, before);
                        if (missing != null)
                            logger?.LogInfo($"[TRENCHES] '{line.Name}' advanced to {line.Stage} without its {missing}: the ground refused it {TrenchTraceMath.BeltRefusalLimit} times.");
                        if (visualChunks.TryGetValue(line.Id, out TrenchVisualChunk chunk) && chunk != null) chunk.Rebuild();
                        garrison.Reinforce();
                        garrison.Poll(now);
                        line.DefenderCount = garrison.Alive;
                    }
                    else if (line.Stage != TrenchStage.Saps && Time.unscaledTime >= nextGrowthWarning)
                    {
                        nextGrowthWarning = Time.unscaledTime + 60f;
                        logger?.LogWarning($"[TRENCHES] '{line.Name}' growth held at {line.Stage}: the ground refused the next belt trace.");
                    }
                }

                if (changed)
                {
                    anyChanged = true;
                    logger?.LogInfo($"[TRENCHES] '{line.Name}': {line.Stage}, {line.DefenderCount} defenders, {line.Curve.Length} curve stations/{line.Anchors.Length} anchors, suppressed={line.Suppressed}, overrun={line.Overrun}.");
                }
            }

            if (anyChanged) OnLinesChanged?.Invoke();
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

        private bool StillOwned(TrenchLine line)
        {
            // The line sits inside the contested band, where only the signed control field
            // separates the sides. A line the enemy has pushed past is neutralized; existing
            // defenders must not erase their own position while pressure advances the border.
            return line.OwnerHq != null &&
                territory.TryGetHoldStrength(line.OwnerHq.GetInstanceID(), line.Center.x, line.Center.z,
                    out float hold) && TrenchTraceMath.HoldsOwnSide(hold);
        }
    }
}

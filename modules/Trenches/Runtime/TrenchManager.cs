using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Framework.Contracts;
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
    internal sealed class TrenchManager : MonoBehaviour, ISceneService
    {
        public const int MaximumActiveLines = 16;
        private const float TraceRefreshSeconds = 5f;
        private const float BuildAttemptSeconds = 2f;
        private const float SimulationSeconds = 0.5f;
        private const float RetireSeconds = 300f;
        private const float SameOwnerSpacing = 360f;
        private const float OtherOwnerSpacing = 250f;
        private const int MaximumFactions = 8;

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
        private readonly FactionHQ[] factions = new FactionHQ[MaximumFactions];
        private int factionCount;
        private int factionIndex;
        private bool factionStarted;
        private int traceCount;
        private int traceIndex;
        private int windowStart;

        private int nextLineId = 1;
        private float nextTraceRefresh;
        private float nextBuildAttempt;
        private float nextSimulationTick;
        private float nextSeedDelay;
        private float nextGrowthWarning;
        private float nextGarrisonWarning;

        public event Action OnLinesChanged;

        public IReadOnlyList<TrenchLine> Lines => lines;

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

            nextLineId = 1;
            factionCount = 0;
            factionIndex = 0;
            factionStarted = false;
            traceCount = traceIndex = windowStart = 0;
            nextTraceRefresh = 0f;
            nextBuildAttempt = 0f;
            nextSimulationTick = 0f;
            nextGrowthWarning = 0f;
            nextGarrisonWarning = 0f;
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
                AdvanceFaction();
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

        private void RebuildFactionList()
        {
            factionCount = 0;
            factionIndex = 0;
            factionStarted = false;
            foreach (FactionHQ owner in FactionRegistry.GetAllHQs())
            {
                if (owner == null) continue;
                if (factionCount >= MaximumFactions) break;
                factions[factionCount++] = owner;
            }
        }

        private void AdvanceFaction()
        {
            if (factionCount <= 0)
            {
                traceCount = traceIndex = windowStart = 0;
                return;
            }
            if (factionStarted) factionIndex = (factionIndex + 1) % factionCount;
            factionStarted = true;
            CopyTraces();
        }

        private void CopyTraces()
        {
            traceIndex = 0;
            windowStart = 0;
            traceCount = territory.CopyFrontlineTraces(factions[factionIndex].GetInstanceID(),
                tracePoints, traceLengths, tracePressure);
        }

        /// <summary>One planning attempt per call; the scan advances so nothing stalls.</summary>
        private void TryBuildOne(int maximum)
        {
            if (traceCount <= 0 || traceIndex >= traceCount)
            {
                AdvanceFaction();
                return;
            }
            FactionHQ owner = factions[factionIndex];
            int offset = 0;
            for (int t = 0; t < traceIndex; t++) offset += traceLengths[t];
            int length = traceLengths[traceIndex];
            if (length < 2 || owner == null || lines.Count >= maximum)
            {
                NextTrace();
                return;
            }

            bool planned = TrenchPlanner.TryPlanWindow(nextLineId, owner.name + "_Front", owner,
                tracePressure[traceIndex], tracePoints, offset, length, windowStart, territory,
                out TrenchLine line, out int next);
            if (!planned)
            {
                AdvanceWindow(next);
                return;
            }
            nextLineId++;
            if (SpacingOk(line)) Commit(line, maximum);
            AdvanceWindow(next);
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
                if (!garrison.Establish())
                {
                    if (Time.unscaledTime >= nextGarrisonWarning)
                    {
                        nextGarrisonWarning = Time.unscaledTime + 30f;
                        logger?.LogWarning("[TRENCHES] Position rejected: native MG definition, clear footprint or spawn unavailable. No empty cosmetic position was created.");
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
            line.NextGrowthAt = Time.time + Math.Max(15f, settings.GrowthIntervalSeconds.Value);
            logger?.LogInfo($"[TRENCHES] '{line.Name}' dug at global {line.Center}: {line.Curve.Length} curve stations, {line.Anchors.Length} anchors, pressure {line.Pressure:0.00}.");
            OnLinesChanged?.Invoke();
            return true;
        }

        private TrenchVisualChunk CreateVisualChunk(TrenchLine line)
        {
            var go = new GameObject($"TrenchVisual_{line.Id}");
            var chunk = go.AddComponent<TrenchVisualChunk>();
            chunk.Lod0Distance = settings != null ? settings.LODDistanceNear.Value : 250f;
            chunk.Lod1Distance = 1200f;
            chunk.Lod2Distance = settings != null ? settings.LODDistanceFar.Value : 3500f;

            try { chunk.Initialize(line); }
            catch { Destroy(go); throw; }
            logger?.LogInfo($"[TRENCHES] World chunk {line.Id}: {chunk.MeshCount} meshes, center={chunk.WorldCenter}, camera distance={chunk.CameraDistance:0}m, material={chunk.EarthMaterial}.");
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
                line.DefenderCount = garrison.Alive;

                bool suppressed = now < garrison.SuppressedUntil;
                changed |= line.Suppressed != suppressed;
                line.Suppressed = suppressed;

                if (!line.Overrun && (garrison.Overrun || !StillOwned(line)))
                {
                    line.Overrun = true;
                    line.RetireAt = now + RetireSeconds;
                    if (!garrison.Overrun) garrison.Remove();
                    line.DefenderCount = 0;
                    changed = true;
                    logger?.LogInfo($"[TRENCHES] '{line.Name}' neutralized or abandoned; growth stopped, no defender respawns.");
                }
                if (line.Overrun && now >= line.RetireAt)
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
                    if (TrenchPlanner.TryGrowBelt(line, territory))
                    {
                        changed = true;
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

        private bool StillOwned(TrenchLine line)
        {
            // Frontline proximity gates new positions. Existing defenders must not erase
            // their own position when their troop pressure advances the border.
            return line.OwnerHq != null &&
                territory.OwnsPosition(line.OwnerHq.GetInstanceID(), line.Center.x, line.Center.z);
        }
    }
}

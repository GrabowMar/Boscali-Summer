using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
#if !NET8_0_OR_GREATER
using UnityEngine;
#endif

namespace BoscaliSummer.Features.Command.Runtime
{
#if NET8_0_OR_GREATER
    internal struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }
#endif

    internal enum SectorControl : byte
    {
        Neutral = 0,
        Friendly = 1,
        Hostile = 2,
        Contested = 3
    }

    /// <summary>
    /// Advisory frontlines from actual strategic ownership and objective ground presence.
    /// Persistent control follows elapsed mission time; no vanilla capture state is mutated.
    ///
    /// <para>Cells sit on the map's base grid (its 1 km minor lattice, aligned through the
    /// map's own grid offset), so the tint lands on the squares the map already draws.
    /// Encircled ground with no opposing presence is claimed for the enclosing side —
    /// airbase cells stay anchored to their real ownership. The texture bake reads
    /// quadtree clusters, so the quiet rear is a few blocks and only the front stays fine.</para>
    /// </summary>
    internal sealed class TacticalSectorGrid
    {
        /// <summary>The map's base grid: one vanilla minor square.</summary>
        public const float DefaultCellSize = 1000f;
        public const int MaximumCells = 16384;
        public const int MaximumNodes = 128;
        private const int MaximumInfluenceCells = 8;
        private const int MaximumContourSegments = 4096;
        private const float MinimumCellSize = 100f;
        private const float PresenceThreshold = 0.05f;
        /// <summary>Ground-control weight of one vehicle or ordinary building.</summary>
        public const float VehicleWeight = 2.5f;
        /// <summary>
        /// Ground-control weight of one defensive emplacement. Dug-in weapons anchor their
        /// ground harder than a passing vehicle: a full eight-nest field position outweighs
        /// one six-vehicle group in its cell and yields to three.
        /// </summary>
        public const float DefensiveBuildingWeight = 5f;
        private const float CaptureSeconds = 15f;
        private const float RecoverySeconds = 90f;
        private bool hasEvaluated;

        /// <summary>Control-field inks. Shared so the map legend cannot drift from the bake.</summary>
        public static readonly Color32 FriendlyTint = new Color32(35, 130, 235, 255);
        public static readonly Color32 HostileTint = new Color32(230, 45, 45, 255);

        public float CellSize { get; private set; }
        public uint GridVersion { get; private set; }

        public int ResolutionX { get; private set; }
        public int ResolutionY { get; private set; }

        public float WorldSizeX { get; private set; }
        public float WorldSizeY { get; private set; }
        public float WorldSize => Math.Max(WorldSizeX, WorldSizeY);

        /// <summary>Alignment of the map's own grid labels; cell edges land on that lattice.</summary>
        public float AlignmentX { get; private set; }
        public float AlignmentY { get; private set; }

        /// <summary>World coordinate of the grid's south-west corner (cell 0,0 minimum edge).</summary>
        public float OriginX { get; private set; }
        public float OriginZ { get; private set; }

        // Strategic Node Definition (Airbases, Forward LZs, Encampments, Strategic POIs)
        public struct TacticalNode
        {
            public int Id;
            public string Name;
            public float X;
            public float Z;
            public SectorControl Faction;
            public float MaxRadius;
            public bool IsAirbase;
            public bool IsContested;
            public float CaptureProgress; // Local opposing pressure ratio, not vanilla capture progress.

            public TacticalNode(int id, string name, float x, float z, SectorControl faction, float maxRadius, bool isAirbase)
            {
                Id = id;
                Name = name ?? ("Node_" + id);
                X = x;
                Z = z;
                Faction = faction;
                MaxRadius = Math.Max(maxRadius, 2000f);
                IsAirbase = isAirbase;
                IsContested = false;
                CaptureProgress = 0f;
            }
        }

        // Discrete Sector Grid Buffers
        // holdStrength: continuous range from -1.0 (Full Hostile) to +1.0 (Full Friendly), 0.0 = Neutral
        private readonly float[] holdStrength;
        private readonly float[] friendlyForce;
        private readonly float[] hostileForce;
        private readonly SectorControl[] sectorStates;
        private readonly SectorFrontSegment[] frontSegments;
        private readonly byte[] frontBand;
        private readonly byte[] nodeAnchors;

        // Cached strategic influence: node influence only changes when the node set does,
        // so the per-cell sqrt sweep is skipped on the 2 Hz observation refresh.
        private readonly float[] strategicField;
        private bool hasStrategicInfluence;
        private ulong nodeSignature;
        private ulong strategicSignature;
        private bool strategicDirty = true;

        // Encirclement pass: 1 reachable from the map border, 2 inside an enclosed region;
        // claims 1 = friendly, 2 = hostile (0 = none).
        private readonly byte[] pocketVisit;
        private readonly byte[] pocketClaim;
        private readonly int[] pocketStack;
        private readonly int[] pocketRegion;

        // Quadtree clusters of the current field, rebuilt with every evaluation.
        private readonly byte[] cellKeys;
        private SectorClusterTree.Cluster[] displayClusters;
        private int displayQuadCount;

        // Ordered front traces, chained from frontSegments on demand (never per 2 Hz eval).
        private readonly FrontlineTracePoint[] tracePoints = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] traceLengths = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] tracePressure = new float[FrontlineTraceLimits.MaximumTraces];
        private readonly bool[] traceUsed = new bool[MaximumContourSegments];
        private long[] traceEndpointKeys;
        private int[] traceEndpointValues;
        private int traceEndpointCount;
        private int traceCount;
        private int tracePointCount;
        private bool tracesReady;

        // Active nodes
        private readonly List<TacticalNode> nodes = new List<TacticalNode>(MaximumNodes);

        // Cached Pixel Buffer for Instant GPU Texture Baking
        private Color32[] pixelBuffer;
        private int cachedTexWidth;
        private int cachedTexHeight;

        // Telemetry
        public int FriendlySectorCount { get; private set; }
        public int HostileSectorCount { get; private set; }
        public int ContestedSectorCount { get; private set; }
        public int NeutralSectorCount { get; private set; }
        public int ActiveClashesCount => ContestedSectorCount;

        /// <summary>Uniform blocks the last evaluation clustered the cells into.</summary>
        public int ClusterCount => displayQuadCount;

        /// <summary>
        /// Cell edges where friendly control meets hostile or contested control — the
        /// frontline's length, in grid segments.
        ///
        /// <para>Counted from the same border pass that builds the edge mask, so it costs
        /// nothing beyond an increment, and it is the one number that separates a long thin
        /// front from a compact pocket holding the same number of sectors.</para>
        /// </summary>
        public int FrontlineSegmentCount { get; private set; }

        /// <summary>Total length of the contour, in meters. The honest version of the segment count.</summary>
        public float FrontlineLengthMetres { get; private set; }
        public int TotalNodesCount => nodes.Count;
        public int TotalSectors => ResolutionX * ResolutionY;
        public float TerritoryControlRatio => (FriendlySectorCount + HostileSectorCount > 0)
            ? (float)FriendlySectorCount / (FriendlySectorCount + HostileSectorCount)
            : float.NaN;

        public TacticalSectorGrid(float cellSize = DefaultCellSize, float worldSize = 100000f)
            : this(cellSize, worldSize, worldSize)
        {
        }

        public TacticalSectorGrid(float cellSize, float worldSizeX, float worldSizeY,
            float alignmentX = 0f, float alignmentY = 0f)
        {
            WorldSizeX = ValidWorldSize(worldSizeX) ? worldSizeX : 100000f;
            WorldSizeY = ValidWorldSize(worldSizeY) ? worldSizeY : 100000f;
            AlignmentX = IsFinite(alignmentX) ? alignmentX : 0f;
            AlignmentY = IsFinite(alignmentY) ? alignmentY : 0f;

            int total = MaximumCells;
            holdStrength = new float[total];
            friendlyForce = new float[total];
            hostileForce = new float[total];
            sectorStates = new SectorControl[total];
            frontSegments = new SectorFrontSegment[MaximumContourSegments];
            frontBand = new byte[total];
            nodeAnchors = new byte[total];
            strategicField = new float[total];
            pocketVisit = new byte[total];
            pocketClaim = new byte[total];
            cellKeys = new byte[total];
            pocketStack = new int[total];
            pocketRegion = new int[total];
            UpdateLayout(cellSize);
            NeutralSectorCount = TotalSectors;
        }

        /// <summary>
        /// Snaps the cell grid to the base lattice and coarsens it in powers of two until
        /// the theater fits <see cref="MaximumCells"/>, so alignment survives giant maps.
        /// </summary>
        private void UpdateLayout(float requestedCellSize)
        {
            float cell = IsFinite(requestedCellSize) ? Math.Max(requestedCellSize, MinimumCellSize) : DefaultCellSize;
            while (CellCount(cell) > MaximumCells && cell < 1000000f) cell *= 2f;
            CellSize = cell;

            OriginX = (float)Math.Floor((WorldSizeX * -0.5f + AlignmentX) / cell) * cell - AlignmentX;
            OriginZ = (float)Math.Floor((WorldSizeY * -0.5f + AlignmentY) / cell) * cell - AlignmentY;
            ResolutionX = Math.Max(1, (int)Math.Ceiling((WorldSizeX * 0.5f - OriginX) / cell));
            ResolutionY = Math.Max(1, (int)Math.Ceiling((WorldSizeY * 0.5f - OriginZ) / cell));
            displayClusters = new SectorClusterTree.Cluster[TotalSectors];
        }

        private long CellCount(float cell)
            => (long)Math.Ceiling(WorldSizeX / cell) * (long)Math.Ceiling(WorldSizeY / cell);

        public void SetWorldSize(float worldSize)
        {
            SetWorldSize(worldSize, worldSize);
        }

        public void SetWorldSize(float worldSizeX, float worldSizeY)
        {
            bool changed = false;
            if (ValidWorldSize(worldSizeX) && Math.Abs(WorldSizeX - worldSizeX) > 0.01f)
            {
                WorldSizeX = worldSizeX;
                changed = true;
            }
            if (ValidWorldSize(worldSizeY) && Math.Abs(WorldSizeY - worldSizeY) > 0.01f)
            {
                WorldSizeY = worldSizeY;
                changed = true;
            }
            if (changed)
            {
                UpdateLayout(CellSize);
                ResetAll();
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidWorldSize(float value) => IsFinite(value) && value > 1000f && value <= 10000000f;

        /// <summary>Starts a fresh observation snapshot while retaining control history.</summary>
        public void Clear()
        {
            Array.Clear(friendlyForce, 0, friendlyForce.Length);
            Array.Clear(hostileForce, 0, hostileForce.Length);
            Array.Clear(nodeAnchors, 0, nodeAnchors.Length);
            nodes.Clear();
            nodeSignature = 0;
            tracesReady = false;
        }

        /// <summary>
        /// Stable hash of the node set for the observer-side snapshot check. Unlike the
        /// accumulating registration signature this is order-independent and does not
        /// change when the same nodes are re-registered.
        /// </summary>
        public ulong ComputeNodeHash()
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < nodes.Count; i++)
            {
                TacticalNode node = nodes[i];
                hash = (hash ^ (uint)node.Id) * 1099511628211UL;
                hash = (hash ^ (uint)node.X.GetHashCode()) * 1099511628211UL;
                hash = (hash ^ (uint)node.Z.GetHashCode()) * 1099511628211UL;
                hash = (hash ^ (byte)node.Faction) * 1099511628211UL;
            }
            return hash ^ (uint)nodes.Count;
        }

        /// <summary>
        /// Fully resets persistent sector hold strengths and states (e.g. on new mission load).
        /// </summary>
        public void ResetAll()
        {
            Clear();
            Array.Clear(holdStrength, 0, holdStrength.Length);
            Array.Clear(sectorStates, 0, sectorStates.Length);
            FriendlySectorCount = 0;
            HostileSectorCount = 0;
            ContestedSectorCount = 0;
            NeutralSectorCount = TotalSectors;
            hasEvaluated = false;
            FrontlineSegmentCount = 0;
            FrontlineLengthMetres = 0f;
            strategicDirty = true;
            displayQuadCount = 0;
            GridVersion++;
        }

        public bool WorldToCell(float worldX, float worldZ, out int col, out int row)
        {
            col = row = 0;
            if (!IsFinite(worldX) || !IsFinite(worldZ) || CellSize <= 0f) return false;
            double x = Math.Floor((worldX - OriginX) / CellSize);
            double z = Math.Floor((worldZ - OriginZ) / CellSize);
            col = (int)Math.Clamp(x, 0, ResolutionX - 1);
            row = (int)Math.Clamp(z, 0, ResolutionY - 1);
            return x >= 0 && x < ResolutionX && z >= 0 && z < ResolutionY;
        }

        public void CellToCenter(int col, int row, out float centerX, out float centerZ)
        {
            centerX = OriginX + (col + 0.5f) * CellSize;
            centerZ = OriginZ + (row + 0.5f) * CellSize;
        }

        public SectorControl GetSectorControl(int col, int row)
        {
            if (col < 0 || col >= ResolutionX || row < 0 || row >= ResolutionY)
                return SectorControl.Neutral;
            return sectorStates[row * ResolutionX + col];
        }

        public bool TryNearestControlledEdge(float playerX, float playerZ, out float x, out float z)
        {
            x = z = 0;
            if (!IsFinite(playerX) || !IsFinite(playerZ) || WorldSizeX < 8000 || WorldSizeY < 8000) return false;
            const float inset = 1200f, footprint = 900f, minimumDistance = 9000f;
            float halfX = WorldSizeX * 0.5f - inset, halfZ = WorldSizeY * 0.5f - inset;
            double best = double.MaxValue;
            // Project onto every boundary cell as well as its endpoints. Choose by distance,
            // not iteration order, so a far enemy edge cannot win over a nearer eligible one.
            for (int side = 0; side < 4; side++)
            {
                int cells = side < 2 ? ResolutionY : ResolutionX;
                float half = side < 2 ? halfZ : halfX;
                float playerAxis = side < 2 ? playerZ : playerX;
                for (int cell = 0; cell < cells; cell++)
                {
                    float low = -half + 2 * half * cell / cells;
                    float high = -half + 2 * half * (cell + 1) / cells;
                    for (int sample = 0; sample < 3; sample++)
                    {
                        float along = sample == 0 ? Math.Clamp(playerAxis, low, high) : sample == 1 ? low : high;
                        float cx = side < 2 ? (side == 0 ? -halfX : halfX) : along;
                        float cz = side >= 2 ? (side == 2 ? -halfZ : halfZ) : along;
                        double dx = cx - playerX, dz = cz - playerZ, distance = dx * dx + dz * dz;
                        if (distance < minimumDistance * minimumDistance || distance >= best) continue;
                        bool controlled = true;
                        for (int a = -1; a <= 1 && controlled; a++)
                        for (int b = -1; b <= 1; b++)
                            if (!WorldToCell(cx + a * footprint, cz + b * footprint, out int col, out int row) ||
                                GetSectorControl(col, row) != SectorControl.Friendly) { controlled = false; break; }
                        if (!controlled) continue;
                        best = distance; x = cx; z = cz;
                    }
                }
            }
            return best < double.MaxValue;
        }

        public float GetSectorHoldStrength(int col, int row)
        {
            if (col < 0 || col >= ResolutionX || row < 0 || row >= ResolutionY)
                return 0f;
            return holdStrength[row * ResolutionX + col];
        }

        public void RegisterNode(int id, string name, float worldX, float worldZ, SectorControl faction, float maxRadius, bool isAirbase)
        {
            if (!WorldToCell(worldX, worldZ, out _, out _) || !IsFinite(maxRadius) || maxRadius < 0f ||
                (faction != SectorControl.Friendly && faction != SectorControl.Hostile && faction != SectorControl.Neutral)) return;
            float radius = maxRadius > 0f ? Math.Clamp(maxRadius, 2000f, WorldSize) : WorldSize * 0.25f;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Id != id) continue;
                TacticalNode node = nodes[i];
                node.Name = name ?? ("Node_" + id);
                node.X = worldX;
                node.Z = worldZ;
                node.Faction = faction;
                node.MaxRadius = radius;
                node.IsAirbase = isAirbase;
                nodes[i] = node;
                HashNode(worldX, worldZ, faction, radius, id);
                return;
            }
            if (nodes.Count < MaximumNodes)
            {
                nodes.Add(new TacticalNode(id, name, worldX, worldZ, faction, radius, isAirbase));
                HashNode(worldX, worldZ, faction, radius, id);
            }
        }

        /// <summary>
        /// Participation in the influence field, in registration order: Clear plus the same
        /// observation snapshot must replay to the same signature and skip the rebuild.
        /// </summary>
        private void HashNode(float worldX, float worldZ, SectorControl faction, float radius, int id)
        {
            nodeSignature = nodeSignature * 1099511628211UL ^ (uint)id;
            nodeSignature = nodeSignature * 1099511628211UL ^ (uint)worldX.GetHashCode();
            nodeSignature = nodeSignature * 1099511628211UL ^ (uint)worldZ.GetHashCode();
            nodeSignature = nodeSignature * 1099511628211UL ^ (uint)(byte)faction;
            nodeSignature = nodeSignature * 1099511628211UL ^ (uint)radius.GetHashCode();
        }

        /// <summary>Weight one ground observation adds to its side's force.</summary>
        public static float GroundObservationWeight(bool defensiveBuilding)
            => defensiveBuilding ? DefensiveBuildingWeight : VehicleWeight;

        public void AddTroopPresence(float worldX, float worldZ, float weight, bool isHostile, float influenceRadius = 12000f)
        {
            if (!IsFinite(weight) || weight <= 0f || !IsFinite(influenceRadius) || influenceRadius < 0f ||
                !WorldToCell(worldX, worldZ, out int col, out int row)) return;
            weight = Math.Min(weight, 100f);
            float radius = Math.Min(influenceRadius, 16000f);
            // Even the finest grid scans at most a 17 x 17 stencil per unit.
            int reachX = Math.Min(MaximumInfluenceCells, (int)Math.Ceiling(radius / CellSize));
            int reachY = Math.Min(MaximumInfluenceCells, (int)Math.Ceiling(radius / CellSize));
            float[] force = isHostile ? hostileForce : friendlyForce;
            for (int r = Math.Max(0, row - reachY); r <= Math.Min(ResolutionY - 1, row + reachY); r++)
            {
                for (int c = Math.Max(0, col - reachX); c <= Math.Min(ResolutionX - 1, col + reachX); c++)
                {
                    CellToCenter(c, r, out float x, out float z);
                    float distance = (float)Math.Sqrt((x - worldX) * (x - worldX) + (z - worldZ) * (z - worldZ));
                    float falloff = c == col && r == row ? 1f : (radius > 0f ? Math.Max(0f, 1f - distance / radius) : 0f);
                    int index = r * ResolutionX + c;
                    force[index] = Math.Min(1000f, force[index] + weight * falloff);
                }
            }
        }

        public void AddAirbasePresence(float worldX, float worldZ, bool isHostile, float influenceRadius = 25000f)
        {
            SectorControl faction = isHostile ? SectorControl.Hostile : SectorControl.Friendly;
            RegisterNode(nodes.Count + 1, "Airbase_" + (nodes.Count + 1), worldX, worldZ, faction, influenceRadius, true);
        }

        public IReadOnlyList<TacticalNode> GetNodes() => nodes;

        /// <summary>
        /// Advances the advisory control field using a fresh observation snapshot. The
        /// exponential response is independent of map refresh rate for unchanged inputs.
        /// </summary>
        public void EvaluateSectors(float elapsedSeconds = 0.5f)
        {
            float elapsed = IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0f, 300f) : 0f;
            int cellCount = TotalSectors;
            Array.Clear(nodeAnchors, 0, nodeAnchors.Length);
            for (int i = 0; i < nodes.Count; i++)
            {
                TacticalNode node = nodes[i];
                WorldToCell(node.X, node.Z, out int c, out int r);
                int index = r * ResolutionX + c;
                float opposing = node.Faction == SectorControl.Friendly ? hostileForce[index] : friendlyForce[index];
                node.IsContested = node.Faction != SectorControl.Neutral && opposing > PresenceThreshold;
                node.CaptureProgress = node.IsContested ? opposing / Math.Max(PresenceThreshold, friendlyForce[index] + hostileForce[index]) : 0f;
                nodes[i] = node;
                if (node.IsAirbase)
                    nodeAnchors[index] |= node.Faction == SectorControl.Friendly ? (byte)1 : node.Faction == SectorControl.Hostile ? (byte)2 : (byte)0;
            }

            EnsureStrategicField();
            // Encirclements are read from the previous field, then claimed through the same
            // exponential response: stable while the pocket fills in, gone when relief arrives.
            BuildPocketClaims(cellCount);

            bool changed = false;
            for (int r = 0; r < ResolutionY; r++)
            {
                for (int c = 0; c < ResolutionX; c++)
                {
                    int index = r * ResolutionX + c;
                    float friendly = friendlyForce[index], hostile = hostileForce[index];
                    float total = friendly + hostile;
                    float pressure = (friendly - hostile) / Math.Max(3f, total);
                    float target = Math.Clamp(strategicField[index] + 2f * pressure, -1f, 1f);
                    byte claim = pocketClaim[index];
                    if (claim != 0 && nodeAnchors[index] == 0 &&
                        (claim == 1 ? hostile <= PresenceThreshold : friendly <= PresenceThreshold))
                        target = claim == 1 ? 1f : -1f;
                    float response = 1f - (float)Math.Exp(-elapsed / (total > PresenceThreshold ? CaptureSeconds : RecoverySeconds));
                    float previousHold = holdStrength[index];
                    holdStrength[index] = hasEvaluated ? previousHold + (target - previousHold) * response : target;

                    // Actual vanilla airbase ownership anchors its cell; neither this overlay
                    // nor an encirclement claim flips a base without a real capture.
                    if (nodeAnchors[index] != 0)
                        holdStrength[index] = nodeAnchors[index] == 1 ? 1f : nodeAnchors[index] == 2 ? -1f : 0f;
                    float hold = holdStrength[index];
                    bool balancedClash = friendly > PresenceThreshold && hostile > PresenceThreshold &&
                        friendly / total > 0.34f && friendly / total < 0.66f;
                    bool advancing = Math.Abs(pressure) > PresenceThreshold && pressure * hold < 0f;
                    SectorControl previousState = sectorStates[index];
                    sectorStates[index] = nodeAnchors[index] == 3 || balancedClash || advancing
                        ? SectorControl.Contested
                        : hold > 0.01f ? SectorControl.Friendly : hold < -0.01f ? SectorControl.Hostile
                        : hasStrategicInfluence || total > PresenceThreshold ? SectorControl.Contested : SectorControl.Neutral;

                    // Only a real change bumps the grid version: a repainted but identical
                    // front must not trigger a texture upload. The hold epsilon catches
                    // contested stripe widths, which read the control value directly.
                    if (sectorStates[index] != previousState || Math.Abs(hold - previousHold) > 0.01f)
                        changed = true;
                }
            }
            hasEvaluated = true;

            // Extract the front after every cell has advanced (no traversal-order bias).
            FriendlySectorCount = 0;
            HostileSectorCount = 0;
            ContestedSectorCount = 0;
            NeutralSectorCount = 0;

            for (int i = 0; i < cellCount; i++)
            {
                switch (sectorStates[i])
                {
                    case SectorControl.Friendly: FriendlySectorCount++; break;
                    case SectorControl.Hostile: HostileSectorCount++; break;
                    case SectorControl.Contested: ContestedSectorCount++; break;
                    default: NeutralSectorCount++; break;
                }
            }

            ComputeFrontBand();
            BuildDisplayQuads(cellCount);

            int segmentCount = SectorContour.Extract(holdStrength, friendlyForce, hostileForce,
                ResolutionX, ResolutionY, CellSize, OriginX, OriginZ, frontSegments);
            FrontlineSegmentCount = segmentCount;
            tracesReady = false;
            float frontLength = 0f;
            for (int i = 0; i < segmentCount; i++) frontLength += frontSegments[i].HalfLength * 2f;
            FrontlineLengthMetres = frontLength;
            if (changed) GridVersion++;
        }

        /// <summary>
        /// Rebuilds the node influence field only when the observation snapshot actually
        /// changed it — the 2 Hz refresh re-registers the same airbases every cycle.
        /// </summary>
        private void EnsureStrategicField()
        {
            if (!strategicDirty && strategicSignature == nodeSignature) return;
            strategicSignature = nodeSignature;
            strategicDirty = false;

            hasStrategicInfluence = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Faction == SectorControl.Neutral) continue;
                hasStrategicInfluence = true;
                break;
            }

            // ponytail: at most 16384 cells x 128 strategic nodes, avoiding pathfinding
            // and terrain claims. Add terrain-aware supply only after profiling/playtests.
            for (int r = 0; r < ResolutionY; r++)
            {
                for (int c = 0; c < ResolutionX; c++)
                {
                    CellToCenter(c, r, out float x, out float z);
                    float friendlyInfluence = 0f, hostileInfluence = 0f;
                    for (int n = 0; n < nodes.Count; n++)
                    {
                        TacticalNode node = nodes[n];
                        if (node.Faction == SectorControl.Neutral) continue;
                        float dx = x - node.X, dz = z - node.Z;
                        float influence = node.MaxRadius / (node.MaxRadius + (float)Math.Sqrt(dx * dx + dz * dz));
                        if (node.Faction == SectorControl.Friendly) friendlyInfluence = Math.Max(friendlyInfluence, influence);
                        else hostileInfluence = Math.Max(hostileInfluence, influence);
                    }
                    float strongest = Math.Max(friendlyInfluence, hostileInfluence);
                    strategicField[r * ResolutionX + c] = strongest > 0f
                        ? Math.Clamp(3f * (friendlyInfluence - hostileInfluence) / strongest, -1f, 1f)
                        : 0f;
                }
            }
        }

        /// <summary>
        /// Marks every ground pocket that is fully enclosed by one side and holds no opposing
        /// presence (troops or an anchored airbase) as that side's claim. The map border is
        /// not a wall, so only regions unreachable from it are pockets. Pass order settles
        /// nested pockets: the outer encirclement claims last, which stays stable once its
        /// ring flips and the pocket is enclosed by the new owner.
        /// </summary>
        private void BuildPocketClaims(int cellCount)
        {
            Array.Clear(pocketClaim, 0, cellCount);
            ClaimEnclosed(SectorControl.Friendly, cellCount);
            ClaimEnclosed(SectorControl.Hostile, cellCount);
        }

        private void ClaimEnclosed(SectorControl owner, int cellCount)
        {
            byte claim = owner == SectorControl.Friendly ? (byte)1 : (byte)2;
            Array.Clear(pocketVisit, 0, cellCount);

            int top = 0;
            for (int c = 0; c < ResolutionX; c++)
            {
                SeedPocketCell(c, 0, owner, ref top);
                SeedPocketCell(c, ResolutionY - 1, owner, ref top);
            }
            for (int r = 1; r < ResolutionY - 1; r++)
            {
                SeedPocketCell(0, r, owner, ref top);
                SeedPocketCell(ResolutionX - 1, r, owner, ref top);
            }
            while (top > 0)
            {
                int index = pocketStack[--top];
                int c = index % ResolutionX, r = index / ResolutionX;
                PushPocketCell(c - 1, r, owner, ref top);
                PushPocketCell(c + 1, r, owner, ref top);
                PushPocketCell(c, r - 1, owner, ref top);
                PushPocketCell(c, r + 1, owner, ref top);
            }

            for (int index = 0; index < cellCount; index++)
            {
                if (pocketVisit[index] != 0 || sectorStates[index] == owner) continue;
                int count = 0;
                top = 0;
                bool opposed = false;
                pocketVisit[index] = 2;
                pocketStack[top++] = index;
                while (top > 0)
                {
                    int cell = pocketStack[--top];
                    pocketRegion[count++] = cell;
                    if (!opposed) opposed = OpposedAt(cell, owner);
                    int c = cell % ResolutionX, r = cell / ResolutionX;
                    ExplorePocketCell(c - 1, r, owner, ref top);
                    ExplorePocketCell(c + 1, r, owner, ref top);
                    ExplorePocketCell(c, r - 1, owner, ref top);
                    ExplorePocketCell(c, r + 1, owner, ref top);
                }
                if (opposed) continue;
                for (int i = 0; i < count; i++) pocketClaim[pocketRegion[i]] = claim;
            }
        }

        private bool OpposedAt(int index, SectorControl owner)
        {
            if (owner == SectorControl.Friendly)
                return hostileForce[index] > PresenceThreshold || (nodeAnchors[index] & 2) != 0;
            return friendlyForce[index] > PresenceThreshold || (nodeAnchors[index] & 1) != 0;
        }

        private void SeedPocketCell(int c, int r, SectorControl owner, ref int top)
        {
            int index = r * ResolutionX + c;
            if (pocketVisit[index] != 0 || sectorStates[index] == owner) return;
            pocketVisit[index] = 1;
            pocketStack[top++] = index;
        }

        private void PushPocketCell(int c, int r, SectorControl owner, ref int top)
        {
            if (c < 0 || c >= ResolutionX || r < 0 || r >= ResolutionY) return;
            SeedPocketCell(c, r, owner, ref top);
        }

        private void ExplorePocketCell(int c, int r, SectorControl owner, ref int top)
        {
            if (c < 0 || c >= ResolutionX || r < 0 || r >= ResolutionY) return;
            int index = r * ResolutionX + c;
            if (pocketVisit[index] != 0 || sectorStates[index] == owner) return;
            pocketVisit[index] = 2;
            pocketStack[top++] = index;
        }

        /// <summary>
        /// 2 for a cell whose hold opposes a neighbour, 1 for the ring behind it. The map
        /// tints the forward band and lets the quiet rear read as ownership, not noise.
        /// </summary>
        private void ComputeFrontBand()
        {
            for (int r = 0; r < ResolutionY; r++)
            for (int c = 0; c < ResolutionX; c++)
            {
                int idx = r * ResolutionX + c;
                frontBand[idx] = 0;
                float hold = holdStrength[idx];
                if (hold <= 0.01f && hold >= -0.01f) continue;
                bool friendly = hold > 0f;
                if (r + 1 < ResolutionY && Opposing(friendly, holdStrength[idx + ResolutionX])) frontBand[idx] = 2;
                else if (c + 1 < ResolutionX && Opposing(friendly, holdStrength[idx + 1])) frontBand[idx] = 2;
                else if (r > 0 && Opposing(friendly, holdStrength[idx - ResolutionX])) frontBand[idx] = 2;
                else if (c > 0 && Opposing(friendly, holdStrength[idx - 1])) frontBand[idx] = 2;
            }

            for (int r = 0; r < ResolutionY; r++)
            for (int c = 0; c < ResolutionX; c++)
            {
                int idx = r * ResolutionX + c;
                if (frontBand[idx] != 0) continue;
                if (r + 1 < ResolutionY && frontBand[idx + ResolutionX] == 2) frontBand[idx] = 1;
                else if (c + 1 < ResolutionX && frontBand[idx + 1] == 2) frontBand[idx] = 1;
                else if (r > 0 && frontBand[idx - ResolutionX] == 2) frontBand[idx] = 1;
                else if (c > 0 && frontBand[idx - 1] == 2) frontBand[idx] = 1;
            }
        }

        private static bool Opposing(bool friendly, float neighborHold)
            => friendly ? neighborHold < -0.01f : neighborHold > 0.01f;

        /// <summary>Clusters the current field for the texture bake (see <see cref="SectorClusterTree"/>).</summary>
        private void BuildDisplayQuads(int cellCount)
        {
            for (int i = 0; i < cellCount; i++)
            {
                byte key = (byte)(((int)sectorStates[i] << 2) | frontBand[i]);
                if (sectorStates[i] == SectorControl.Contested) key |= SectorClusterTree.NonMergeable;
                cellKeys[i] = key;
            }
            displayQuadCount = SectorClusterTree.Build(cellKeys, ResolutionX, ResolutionY, displayClusters);
        }

        /// <summary>
        /// Ordered front traces (a coastline pocket, a diagonal front, a whole frontier in
        /// one chain). Adjacent cells compute the shared-edge intersection with identical
        /// arithmetic, so the stretches chain by quantized endpoint while the grid only
        /// ever keeps flat buffers.
        /// </summary>
        public int CopyFrontlineTraces(FrontlineTracePoint[] points, int[] lengths, float[] pressure)
        {
            if (points == null || lengths == null || pressure == null) return 0;
            if (!tracesReady) StitchTraces();

            int budget = Math.Min(tracePointCount, points.Length);
            Array.Copy(tracePoints, 0, points, 0, budget);

            int count = 0, used = 0;
            while (count < traceCount && count < lengths.Length && count < pressure.Length &&
                used + traceLengths[count] <= budget)
            {
                lengths[count] = traceLengths[count];
                pressure[count] = tracePressure[count];
                used += traceLengths[count];
                count++;
            }
            return count;
        }

        private void StitchTraces()
        {
            tracesReady = true;
            traceCount = 0;
            tracePointCount = 0;
            if (FrontlineSegmentCount <= 0) return;

            Array.Clear(traceUsed, 0, Math.Min(traceUsed.Length, FrontlineSegmentCount));
            BuildEndpointIndex();

            int written = 0;
            for (int start = 0; start < FrontlineSegmentCount &&
                traceCount < FrontlineTraceLimits.MaximumTraces &&
                written <= tracePoints.Length - 3; start++)
            {
                if (traceUsed[start]) continue;
                int mark = written;
                int length = WalkTrace(start, ref written, out float peak, out float metres);
                if (length < 2 || metres < 1f)
                {
                    written = mark;
                    continue;
                }
                traceLengths[traceCount] = length;
                tracePressure[traceCount] = peak;
                traceCount++;
            }
            tracePointCount = written;
        }

        /// <summary>
        /// Walks one chain from its start stretch. The backward half is written first and
        /// reversed, so a trace is one forward slice of the flat point buffer.
        /// </summary>
        private int WalkTrace(int start, ref int written, out float peak, out float metres)
        {
            SectorFrontSegment first = frontSegments[start];
            traceUsed[start] = true;
            peak = first.Pressure;
            metres = first.HalfLength * 2f;

            int mark = written;
            tracePoints[written++] = new FrontlineTracePoint(first.AX, first.AZ);
            float atX = first.AX, atZ = first.AZ;
            while (written < tracePoints.Length && FindUnusedEndpoint(atX, atZ, out int next, out int end))
            {
                traceUsed[next] = true;
                SectorFrontSegment segment = frontSegments[next];
                if (segment.Pressure > peak) peak = segment.Pressure;
                metres += segment.HalfLength * 2f;
                atX = end == 0 ? segment.BX : segment.AX;
                atZ = end == 0 ? segment.BZ : segment.AZ;
                tracePoints[written++] = new FrontlineTracePoint(atX, atZ);
            }
            ReversePoints(mark, written - mark);

            tracePoints[written++] = new FrontlineTracePoint(first.BX, first.BZ);
            atX = first.BX;
            atZ = first.BZ;
            while (written < tracePoints.Length && FindUnusedEndpoint(atX, atZ, out int next, out int end))
            {
                traceUsed[next] = true;
                SectorFrontSegment segment = frontSegments[next];
                if (segment.Pressure > peak) peak = segment.Pressure;
                metres += segment.HalfLength * 2f;
                atX = end == 0 ? segment.BX : segment.AX;
                atZ = end == 0 ? segment.BZ : segment.AZ;
                tracePoints[written++] = new FrontlineTracePoint(atX, atZ);
            }
            // A pocket ring walks back to its own start, so the first point repeats last;
            // consumers read that as a closed trace.
            return written - mark;
        }

        private void ReversePoints(int start, int count)
        {
            for (int i = 0, j = count - 1; i < j; i++, j--)
            {
                FrontlineTracePoint swap = tracePoints[start + i];
                tracePoints[start + i] = tracePoints[start + j];
                tracePoints[start + j] = swap;
            }
        }

        private void BuildEndpointIndex()
        {
            int needed = FrontlineSegmentCount * 2;
            if (traceEndpointKeys == null || traceEndpointKeys.Length < needed)
            {
                int size = 256;
                while (size < needed) size <<= 1;
                traceEndpointKeys = new long[size];
                traceEndpointValues = new int[size];
            }
            traceEndpointCount = 0;
            for (int i = 0; i < FrontlineSegmentCount; i++)
            {
                SectorFrontSegment segment = frontSegments[i];
                traceEndpointKeys[traceEndpointCount] = EndpointKey(segment.AX, segment.AZ);
                traceEndpointValues[traceEndpointCount++] = i << 1;
                traceEndpointKeys[traceEndpointCount] = EndpointKey(segment.BX, segment.BZ);
                traceEndpointValues[traceEndpointCount++] = (i << 1) | 1;
            }
            Array.Sort(traceEndpointKeys, traceEndpointValues, 0, traceEndpointCount);
        }

        /// <summary>Quarter-metre key, so the identical shared-edge intersections coincide.</summary>
        private static long EndpointKey(float x, float z)
        {
            long kx = (long)Math.Round(x * 4f);
            long kz = (long)Math.Round(z * 4f);
            return (kx << 32) ^ (kz & 0xffffffffL);
        }

        private bool FindUnusedEndpoint(float x, float z, out int segment, out int end)
        {
            segment = 0;
            end = 0;
            long key = EndpointKey(x, z);
            int at = LowerBound(key);
            for (; at < traceEndpointCount && traceEndpointKeys[at] == key; at++)
            {
                int packed = traceEndpointValues[at];
                int candidate = packed >> 1;
                if (traceUsed[candidate]) continue;
                segment = candidate;
                end = packed & 1;
                return true;
            }
            return false;
        }

        private int LowerBound(long key)
        {
            int low = 0, high = traceEndpointCount;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (traceEndpointKeys[mid] < key) low = mid + 1;
                else high = mid;
            }
            return low;
        }

        /// <summary>
        /// Fast procedural CPU rasterizer that bakes the sector tint one cluster at a time.
        /// Reuses a bounded pixel buffer after the initial allocation. The front line itself
        /// is vector geometry (`Presentation/MapUi/FrontlineGraphic`), never baked here.
        /// </summary>
        public Color32[] BakeTexture(
            int texWidth,
            int texHeight,
            bool showSectors,
            float globalOpacity)
        {
            if (texWidth < 1 || texHeight < 1 || texWidth > 1024 || texHeight > 1024)
                throw new ArgumentOutOfRangeException(nameof(texWidth), "Tactical overlay dimensions must be 1..1024.");
            globalOpacity = IsFinite(globalOpacity) ? Math.Clamp(globalOpacity, 0f, 1f) : 0f;
            int totalPixels = texWidth * texHeight;
            if (pixelBuffer == null || pixelBuffer.Length != totalPixels || cachedTexWidth != texWidth || cachedTexHeight != texHeight)
            {
                pixelBuffer = new Color32[totalPixels];
                cachedTexWidth = texWidth;
                cachedTexHeight = texHeight;
            }

            Color32 clearColor = new Color32(0, 0, 0, 0);
            Array.Fill(pixelBuffer, clearColor);

            if (!showSectors || globalOpacity <= 0f) return pixelBuffer;

            byte fillAlpha = (byte)Math.Clamp((int)(globalOpacity * 255f * 0.45f), 18, 56);
            byte gridLineAlpha = 12;

            Color32 friendlyBase = new Color32(FriendlyTint.r, FriendlyTint.g, FriendlyTint.b, fillAlpha);
            Color32 hostileBase = new Color32(HostileTint.r, HostileTint.g, HostileTint.b, fillAlpha);
            Color32 gridLineColor = new Color32(80, 110, 130, gridLineAlpha);

            float pxPerCellX = CellSize / WorldSizeX * texWidth;
            float pxPerCellY = CellSize / WorldSizeY * texHeight;
            float originPxX = (OriginX + WorldSizeX * 0.5f) / WorldSizeX * texWidth;
            float originPxY = (OriginZ + WorldSizeY * 0.5f) / WorldSizeY * texHeight;

            for (int q = 0; q < displayQuadCount; q++)
            {
                SectorClusterTree.Cluster quad = displayClusters[q];
                SectorControl state = (SectorControl)((quad.Key >> 2) & 3);
                if (state == SectorControl.Neutral) continue;

                int band = quad.Key & 3;
                int pxMin = Math.Max(0, (int)Math.Floor(originPxX + quad.X * pxPerCellX));
                int pyMin = Math.Max(0, (int)Math.Floor(originPxY + quad.Y * pxPerCellY));
                int pxMax = Math.Min(texWidth - 1, (int)Math.Floor(originPxX + (quad.X + quad.Width) * pxPerCellX) - 1);
                int pyMax = Math.Min(texHeight - 1, (int)Math.Floor(originPxY + (quad.Y + quad.Height) * pxPerCellY) - 1);
                if (pxMax < pxMin || pyMax < pyMin) continue;

                float weight = band == 2 ? 1f : band == 1 ? 0.55f : 0.22f;
                Color32 friendly = WeightedAlpha(friendlyBase, weight);
                Color32 hostile = WeightedAlpha(hostileBase, weight);
                bool striped = state == SectorControl.Contested;
                // A lone 1 km square is about three texture pixels: its own grid line would be
                // most of the square, so only clusters big enough to read as a block get one.
                bool drawLines = band != 0 && pxMax - pxMin >= 3 && pyMax - pyMin >= 3;

                // A contested square is hatched red/blue, never a third colour: the stripe
                // runs follow the cell's control value, so the wider run is the side that
                // holds more of the square. Every square keeps at least one pixel of each
                // side, so "contested" never reads as solid.
                int period = striped ? Math.Clamp((pxMax - pxMin + 1) / 3, 4, 12) : 0;
                int friendlyRun = striped ? StripeRun(quad.Y * ResolutionX + quad.X, period) : 0;

                for (int y = pyMin; y <= pyMax; y++)
                {
                    int rowOffset = y * texWidth;
                    for (int x = pxMin; x <= pxMax; x++)
                    {
                        if (striped)
                        {
                            int stripe = (x - pxMin + y - pyMin) % period;
                            pixelBuffer[rowOffset + x] = stripe < friendlyRun ? friendly : hostile;
                            continue;
                        }
                        pixelBuffer[rowOffset + x] = state == SectorControl.Friendly ? friendly : hostile;
                    }
                }

                if (!drawLines) continue;
                for (int y = pyMin; y <= pyMax; y++) pixelBuffer[y * texWidth + pxMin] = gridLineColor;
                for (int x = pxMin; x <= pxMax; x++) pixelBuffer[pyMin * texWidth + x] = gridLineColor;
            }

            // 2. The front line itself is no longer rasterised here: the map draws it as one
            // plain vector line (`Presentation/MapUi/FrontlineGraphic`) so it stays sharp at
            // every zoom instead of magnifying the overlay texture's resolution.
            return pixelBuffer;
        }

        private static Color32 WeightedAlpha(Color32 color, float weight)
            => new Color32(color.r, color.g, color.b, (byte)Math.Max(1f, color.a * weight));

        /// <summary>
        /// Pixels of one stripe period that belong to the friendly side: the cell's control
        /// value, so the wider run is the side that holds more of the square.
        /// </summary>
        private int StripeRun(int index, int period)
        {
            float hold = holdStrength[index];
            float share = Math.Clamp((IsFinite(hold) ? hold : 0f) * 0.5f + 0.5f, 0f, 1f);
            return Math.Clamp((int)Math.Round(share * period), 1, period - 1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Command.Domain;
#if !NET8_0_OR_GREATER
using UnityEngine;
#endif

namespace BoscaliSummer.Modules.Command.Runtime
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
        private const float PresenceThreshold = ControlFieldCore.PresenceThreshold;
        /// <summary>Ground-control weight of one vehicle or ordinary building.</summary>
        public const float VehicleWeight = 2.5f;
        /// <summary>
        /// Ground-control weight of one defensive emplacement. Dug-in weapons anchor their
        /// ground harder than a passing vehicle: a full eight-nest field position outweighs
        /// one six-vehicle group in its cell and yields to three.
        /// </summary>
        public const float DefensiveBuildingWeight = 5f;
        private bool hasEvaluated;

        /// <summary>Control-field inks. Shared so the map legend cannot drift from the bake.</summary>
        public static readonly Color32 FriendlyTint = new Color32(35, 130, 235, 255);
        public static readonly Color32 HostileTint = new Color32(230, 45, 45, 255);

        public float CellSize { get; private set; }
        public uint GridVersion { get; private set; }

        /// <summary>What the texture bake draws: display clusters plus contested stripe shares.</summary>
        private ulong displayHash;
        public ulong DisplayHash
        {
            get { EnsureDisplay(); return displayHash; }
            private set { displayHash = value; }
        }

        /// <summary>What the vector front draws: contour segments, metre endpoints, pressure.</summary>
        public ulong FrontlineHash { get; private set; }

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
        private ulong nodeSignature;
        private ulong strategicSignature;
        private bool strategicDirty = true;

        // Encirclement pass: 1 reachable from the map border, 2 inside an enclosed region;
        // claims 1 = friendly, 2 = hostile (0 = none).
        private readonly byte[] pocketVisit;
        private readonly byte[] pocketClaim;
        private readonly int[] pocketStack;
        private readonly int[] pocketRegion;

        // Neighbour rule scratch: last pass's hold and this pass's target, plus which cells
        // carry no presence, claim or anchor of their own and so take their neighbours' lead.
        private readonly float[] previousHold;
        private readonly float[] passStartHold;
        private readonly float[] cellTarget;
        private readonly bool[] cellFree;
        private readonly bool[] cellClaimed;

        // Open water (1): it keeps its hold so the field stays continuous across a strait,
        // but it takes no tint and carries no front. Empty until the map's land mask is known.
        private readonly byte[] sea;
        private bool hasSea;
        private ulong seaHash;

        // Quadtree clusters are only needed when the large map draws its tint.
        private readonly byte[] cellKeys;
        private SectorClusterTree.Cluster[] displayClusters;
        private int displayQuadCount;
        private bool displayDirty = true;

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
        // True while every pixel of pixelBuffer is still transparent: a repeated clear bake costs nothing.
        private bool pixelBufferBlank = true;
        private BakeScratch bakeScratch;

        // Telemetry
        public int FriendlySectorCount { get; private set; }
        public int HostileSectorCount { get; private set; }
        public int ContestedSectorCount { get; private set; }
        public int NeutralSectorCount { get; private set; }

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
        public int TotalSectors => ResolutionX * ResolutionY;

        public TacticalSectorGrid(float cellSize = DefaultCellSize, float worldSize = 100000f)
            : this(cellSize, worldSize, worldSize)
        {
        }

        public TacticalSectorGrid(float cellSize, float worldSizeX, float worldSizeY,
            float alignmentX = 0f, float alignmentY = 0f)
        {
            WorldSizeX = ValidWorldSize(worldSizeX) ? worldSizeX : 100000f;
            WorldSizeY = ValidWorldSize(worldSizeY) ? worldSizeY : 100000f;
            AlignmentX = float.IsFinite(alignmentX) ? alignmentX : 0f;
            AlignmentY = float.IsFinite(alignmentY) ? alignmentY : 0f;

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
            previousHold = new float[total];
            passStartHold = new float[total];
            cellTarget = new float[total];
            cellFree = new bool[total];
            cellClaimed = new bool[total];
            sea = new byte[total];
            UpdateLayout(cellSize);
            NeutralSectorCount = TotalSectors;
        }

        /// <summary>
        /// Snaps the cell grid to the base lattice and coarsens it in powers of two until
        /// the theater fits <see cref="MaximumCells"/>, so alignment survives giant maps.
        /// </summary>
        private void UpdateLayout(float requestedCellSize)
        {
            float cell = float.IsFinite(requestedCellSize) ? Math.Max(requestedCellSize, MinimumCellSize) : DefaultCellSize;
            while (CellCount(cell) > MaximumCells && cell < 1000000f) cell *= 2f;
            CellSize = cell;

            OriginX = (float)Math.Floor((WorldSizeX * -0.5f + AlignmentX) / cell) * cell - AlignmentX;
            OriginZ = (float)Math.Floor((WorldSizeY * -0.5f + AlignmentY) / cell) * cell - AlignmentY;
            ResolutionX = Math.Max(1, (int)Math.Ceiling((WorldSizeX * 0.5f - OriginX) / cell));
            ResolutionY = Math.Max(1, (int)Math.Ceiling((WorldSizeY * 0.5f - OriginZ) / cell));
            displayClusters = new SectorClusterTree.Cluster[TotalSectors];
            Array.Clear(sea, 0, sea.Length);
            hasSea = false;
            seaHash = 0UL;
        }

        /// <summary>
        /// Marks open water from the map's land test (global metres). A cell is sea only when its
        /// centre and four inner points are all water, so a coast keeps its tint. A test that
        /// finds no land, or no sea, leaves the grid as it was: nothing to mask.
        /// </summary>
        internal void SetSeaMask(Func<float, float, bool> isLand)
        {
            if (isLand == null) return;
            int cells = TotalSectors, seaCells = 0;
            float inset = CellSize * 0.35f;
            ulong hash = 1469598103934665603UL;
            for (int r = 0; r < ResolutionY; r++)
            for (int c = 0; c < ResolutionX; c++)
            {
                CellToCenter(c, r, out float x, out float z);
                bool land = isLand(x, z) || isLand(x - inset, z - inset) || isLand(x + inset, z - inset) ||
                    isLand(x - inset, z + inset) || isLand(x + inset, z + inset);
                int index = r * ResolutionX + c;
                sea[index] = land ? (byte)0 : (byte)1;
                if (!land) { seaCells++; hash = HashStep(hash, (uint)index); }
            }
            hasSea = seaCells > 0 && seaCells < cells;
            if (!hasSea) Array.Clear(sea, 0, cells);
            seaHash = hasSea ? hash : 0UL;
            displayDirty = true;
        }

        private long CellCount(float cell)
            => (long)Math.Ceiling(WorldSizeX / cell) * (long)Math.Ceiling(WorldSizeY / cell);

        private static bool ValidWorldSize(float value) => float.IsFinite(value) && value > 1000f && value <= 10000000f;

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

        /// <summary>Finishes the lazy display clusters and front traces, so the first reader pays nothing.</summary>
        internal void Prewarm()
        {
            EnsureDisplay();
            if (!tracesReady) StitchTraces();
        }

        /// <summary>
        /// Takes over everything another grid of the same layout evaluated: the territory
        /// view evaluates a spare grid off the main thread and publishes it here in one
        /// step, so readers of this grid never see a half-advanced field. Scratch buffers
        /// and the baked pixel buffer stay this grid's own.
        /// </summary>
        internal void CopyResultFrom(TacticalSectorGrid other)
        {
            if (other.ResolutionX != ResolutionX || other.ResolutionY != ResolutionY || other.CellSize != CellSize ||
                other.OriginX != OriginX || other.OriginZ != OriginZ)
                throw new InvalidOperationException("Territory grids differ in layout.");
            int cells = TotalSectors;
            Array.Copy(other.holdStrength, holdStrength, cells);
            Array.Copy(other.friendlyForce, friendlyForce, cells);
            Array.Copy(other.hostileForce, hostileForce, cells);
            Array.Copy(other.sectorStates, sectorStates, cells);
            Array.Copy(other.frontBand, frontBand, cells);
            Array.Copy(other.nodeAnchors, nodeAnchors, cells);
            Array.Copy(other.frontSegments, frontSegments, other.FrontlineSegmentCount);
            Array.Copy(other.displayClusters, displayClusters, other.displayQuadCount);
            Array.Copy(other.tracePoints, tracePoints, other.tracePointCount);
            Array.Copy(other.traceLengths, traceLengths, other.traceCount);
            Array.Copy(other.tracePressure, tracePressure, other.traceCount);
            CopySeaFrom(other);
            nodes.Clear();
            nodes.AddRange(other.nodes);
            hasEvaluated = other.hasEvaluated;
            GridVersion = other.GridVersion;
            displayHash = other.displayHash;
            displayQuadCount = other.displayQuadCount;
            displayDirty = other.displayDirty;
            FrontlineHash = other.FrontlineHash;
            FrontlineSegmentCount = other.FrontlineSegmentCount;
            FrontlineLengthMetres = other.FrontlineLengthMetres;
            traceCount = other.traceCount;
            tracePointCount = other.tracePointCount;
            tracesReady = other.tracesReady;
            FriendlySectorCount = other.FriendlySectorCount;
            HostileSectorCount = other.HostileSectorCount;
            ContestedSectorCount = other.ContestedSectorCount;
            NeutralSectorCount = other.NeutralSectorCount;
        }

        /// <summary>
        /// A grid with the same layout and nothing in it: the map overlay copies what it
        /// shows into one with <see cref="CopyBakeStateFrom"/> and bakes the copy on a worker,
        /// so the published grid is never read while the main thread changes it.
        /// </summary>
        internal TacticalSectorGrid NewBakeTwin()
            => new TacticalSectorGrid(CellSize, WorldSizeX, WorldSizeY, AlignmentX, AlignmentY);

        internal bool SharesBakeLayout(TacticalSectorGrid other)
            => other.ResolutionX == ResolutionX && other.ResolutionY == ResolutionY && other.CellSize == CellSize &&
               other.OriginX == OriginX && other.OriginZ == OriginZ &&
               other.WorldSizeX == WorldSizeX && other.WorldSizeY == WorldSizeY;

        /// <summary>
        /// Copies exactly what <see cref="BakeTexture"/> reads from another grid of the same layout:
        /// the cell states, the hold field and the front band. The copy bakes the same pixels as
        /// the source and can be baked on another thread while the source goes on changing.
        /// </summary>
        internal void CopyBakeStateFrom(TacticalSectorGrid other)
        {
            if (!SharesBakeLayout(other)) throw new InvalidOperationException("Territory grids differ in layout.");
            other.EnsureDisplay();
            int cells = TotalSectors;
            Array.Copy(other.sectorStates, sectorStates, cells);
            Array.Copy(other.holdStrength, holdStrength, cells);
            Array.Copy(other.frontBand, frontBand, cells);
            CopySeaFrom(other);
            // The front band is the display state the bake needs; the cluster tree stays untouched.
            displayHash = other.displayHash;
            displayDirty = false;
        }

        private void CopySeaFrom(TacticalSectorGrid other)
        {
            if (seaHash == other.seaHash && hasSea == other.hasSea) return;
            Array.Copy(other.sea, sea, TotalSectors);
            hasSea = other.hasSea;
            seaHash = other.seaHash;
        }

        public bool WorldToCell(float worldX, float worldZ, out int col, out int row)
        {
            col = row = 0;
            if (!float.IsFinite(worldX) || !float.IsFinite(worldZ) || CellSize <= 0f) return false;
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
            if (!float.IsFinite(playerX) || !float.IsFinite(playerZ) || WorldSizeX < 8000 || WorldSizeY < 8000) return false;
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
            if (!WorldToCell(worldX, worldZ, out _, out _) || !float.IsFinite(maxRadius) || maxRadius < 0f ||
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

        /// <summary>
        /// Linear falloff for stencil offset (dc, dr) with the unit's sub-cell offset
        /// (ox, oz) in cell units: exact at quarter-cell offsets, within an eighth of a
        /// cell of truth elsewhere, so one kernel serves every similarly-placed unit.
        /// </summary>
        internal static float KernelFalloff(int dc, int dr, float cellSize, float radius, float ox, float oz)
        {
            if (dc == 0 && dr == 0) return 1f;
            if (!(radius > 0f)) return 0f;
            float dx = (dc - ox) * cellSize, dz = (dr - oz) * cellSize;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            return Math.Max(0f, 1f - distance / radius);
        }

        /// <summary>Snaps a sub-cell offset to the nearest quarter cell, as the kernel reads it.</summary>
        internal static int QuantizeOffset(float fraction)
        {
            int q = (int)Math.Round(fraction * 4.0);
            return q < -2 ? -2 : q > 2 ? 2 : q;
        }

        // Cached presence stencils keyed by cell size, influence radius and quantized
        // sub-cell offset: one build serves every similarly-placed unit until any of
        // them changes — no sqrt per cell per unit on the observation refresh.
        private const int KernelCacheCap = 128;
        private readonly Dictionary<long, float[]> kernelCache = new Dictionary<long, float[]>();

        private static long KernelKey(float cellSize, float radius, int qx, int qz)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = (hash ^ cellSize.GetHashCode()) * 1099511628211L;
                hash = (hash ^ radius.GetHashCode()) * 1099511628211L;
                hash = (hash ^ (qx + 2)) * 1099511628211L;
                hash = (hash ^ (qz + 2)) * 1099511628211L;
                return hash;
            }
        }

        private float[] EnsureFalloffKernel(float radius, int qx, int qz)
        {
            long key = KernelKey(CellSize, radius, qx, qz);
            if (kernelCache.TryGetValue(key, out float[] kernel)) return kernel;
            if (kernelCache.Count >= KernelCacheCap) kernelCache.Clear();
            int stride = MaximumInfluenceCells * 2 + 1;
            kernel = new float[stride * stride];
            float ox = qx * 0.25f, oz = qz * 0.25f;
            for (int dr = -MaximumInfluenceCells; dr <= MaximumInfluenceCells; dr++)
                for (int dc = -MaximumInfluenceCells; dc <= MaximumInfluenceCells; dc++)
                    kernel[(dr + MaximumInfluenceCells) * stride + dc + MaximumInfluenceCells] =
                        KernelFalloff(dc, dr, CellSize, radius, ox, oz);
            kernelCache.Add(key, kernel);
            return kernel;
        }

        public void AddTroopPresence(float worldX, float worldZ, float weight, bool isHostile, float influenceRadius = 12000f)
        {
            if (!float.IsFinite(weight) || weight <= 0f || !float.IsFinite(influenceRadius) || influenceRadius < 0f ||
                !WorldToCell(worldX, worldZ, out int col, out int row)) return;
            weight = Math.Min(weight, 100f);
            float radius = Math.Min(influenceRadius, 16000f);
            // Even the finest grid scans at most a 17 x 17 stencil per unit.
            int reach = Math.Min(MaximumInfluenceCells, (int)Math.Ceiling(radius / CellSize));
            CellToCenter(col, row, out float centerX, out float centerZ);
            int qx = QuantizeOffset((worldX - centerX) / CellSize);
            int qz = QuantizeOffset((worldZ - centerZ) / CellSize);
            float[] kernel = EnsureFalloffKernel(radius, qx, qz);
            int stride = MaximumInfluenceCells * 2 + 1;
            float[] force = isHostile ? hostileForce : friendlyForce;
            for (int r = Math.Max(0, row - reach); r <= Math.Min(ResolutionY - 1, row + reach); r++)
            {
                for (int c = Math.Max(0, col - reach); c <= Math.Min(ResolutionX - 1, col + reach); c++)
                {
                    float falloff = kernel[(r - row + MaximumInfluenceCells) * stride +
                        c - col + MaximumInfluenceCells];
                    if (!(falloff > 0f)) continue;
                    int index = r * ResolutionX + c;
                    force[index] = Math.Min(1000f, force[index] + weight * falloff);
                }
            }
        }

        public IReadOnlyList<TacticalNode> GetNodes() => nodes;

        /// <summary>
        /// Advances the advisory control field using a fresh observation snapshot. The
        /// exponential response is independent of map refresh rate for unchanged inputs.
        /// </summary>
        public void EvaluateSectors(float elapsedSeconds = 0.5f)
        {
            float elapsed = float.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0f, 300f) : 0f;
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

            // Own targets first: strategic lean, troop pressure, encirclement claims.
            for (int index = 0; index < cellCount; index++)
            {
                float friendly = friendlyForce[index], hostile = hostileForce[index];
                float total = friendly + hostile;
                float pressure = (friendly - hostile) / Math.Max(3f, total);
                float target = Math.Clamp(strategicField[index] + 2f * pressure, -1f, 1f);
                byte claim = pocketClaim[index];
                bool claimed = claim != 0 && nodeAnchors[index] == 0 &&
                    (claim == 1 ? hostile <= PresenceThreshold : friendly <= PresenceThreshold);
                if (claimed) target = claim == 1 ? 1f : -1f;
                cellTarget[index] = target;
                cellClaimed[index] = claimed;
                cellFree[index] = !claimed && nodeAnchors[index] == 0 && total <= PresenceThreshold;
            }

            // The automaton step. Ground nobody stands on leans toward its eight neighbours, so
            // fronts are carried by contact and spread outward, one-cell wedges and trails fill in,
            // and straight airbase bisectors round off. The coupling is under one, so the field
            // settles to one answer instead of growing blobs. A free cell its neighbours mostly
            // oppose falls at capture speed: a pocket or a tendril closes quickly, a held line does not.
            Array.Copy(holdStrength, passStartHold, cellCount);
            if (!hasEvaluated)
            {
                // First look: settle the neighbour rule now rather than over minutes of recovery.
                Array.Copy(cellTarget, holdStrength, cellCount);
                for (int sweep = 0; sweep < NeighbourSettleSweeps; sweep++)
                {
                    Array.Copy(holdStrength, previousHold, cellCount);
                    for (int index = 0; index < cellCount; index++)
                        if (cellFree[index]) holdStrength[index] = Coupled(index, out _);
                }
            }
            else
            {
                // Fixed sub-steps: neighbours trade hold every step, so equal elapsed time gives
                // equal control whether the field is read at 2 Hz or every few seconds.
                int steps = Math.Clamp((int)Math.Ceiling(elapsed / FieldStepSeconds - 1e-4f), 1, MaximumFieldSteps);
                float step = elapsed / steps;
                for (int s = 0; s < steps; s++)
                {
                    Array.Copy(holdStrength, previousHold, cellCount);
                    for (int index = 0; index < cellCount; index++)
                    {
                        float target = cellTarget[index], response;
                        if (cellFree[index])
                        {
                            target = Coupled(index, out bool surrounded);
                            response = ControlFieldCore.Response(step, surrounded ? 1f : 0f);
                        }
                        // An encircled pocket is cut off: it falls at capture speed, not over the slow recovery.
                        else response = ControlFieldCore.Response(step, cellClaimed[index] ? 1f : friendlyForce[index] + hostileForce[index]);
                        float was = previousHold[index];
                        holdStrength[index] = nodeAnchors[index] != 0 ? AnchorHold(index) : was + (target - was) * response;
                    }
                }
            }

            bool changed = false;
            for (int r = 0; r < ResolutionY; r++)
            {
                for (int c = 0; c < ResolutionX; c++)
                {
                    int index = r * ResolutionX + c;
                    float friendly = friendlyForce[index], hostile = hostileForce[index];
                    float target = cellTarget[index];
                    float before = passStartHold[index];

                    // Actual vanilla airbase ownership anchors its cell; neither this overlay
                    // nor an encirclement claim flips a base without a real capture.
                    if (nodeAnchors[index] != 0) holdStrength[index] = AnchorHold(index);
                    float hold = holdStrength[index];
                    SectorControl previousState = sectorStates[index];
                    SectorControl state = (SectorControl)ControlFieldCore.Classify(hold, friendly, hostile, nodeAnchors[index] == 3);
                    // Ground under anyone's influence is never a hole: a hold inside the epsilon
                    // goes to the side it leans (or is heading), so the zero contour has owned
                    // squares on both sides. Neutral stays for ground no one reaches.
                    if (state == SectorControl.Neutral && target != 0f)
                        state = (hold != 0f ? hold : target) > 0f ? SectorControl.Friendly : SectorControl.Hostile;
                    sectorStates[index] = state;

                    // Only a real change bumps the grid version: a repainted but identical
                    // front must not trigger a texture upload. The hold epsilon catches
                    // contested stripe widths, which read the control value directly.
                    if (sectorStates[index] != previousState || Math.Abs(hold - before) > 0.01f)
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

            displayDirty = true;

            int segmentCount = SectorContour.Extract(holdStrength, friendlyForce, hostileForce,
                ResolutionX, ResolutionY, CellSize, OriginX, OriginZ, frontSegments);
            if (hasSea) segmentCount = DropSeaSegments(segmentCount);
            FrontlineSegmentCount = segmentCount;
            tracesReady = false;
            float frontLength = 0f;
            for (int i = 0; i < segmentCount; i++) frontLength += frontSegments[i].HalfLength * 2f;
            FrontlineLengthMetres = frontLength;
            FrontlineHash = HashFrontline(frontSegments, segmentCount);
            if (changed) GridVersion++;
        }

        /// <summary>Share of a free cell's target its eight neighbours decide.</summary>
        internal const float NeighbourCoupling = 0.4f;
        /// <summary>Opposing neighbours (of eight) that make a free cell fall at capture speed.</summary>
        internal const int SurroundedNeighbours = 5;
        private const int NeighbourSettleSweeps = 12;
        private const float FieldStepSeconds = 0.5f;
        private const int MaximumFieldSteps = 12;

        private float AnchorHold(int index)
            => nodeAnchors[index] == 1 ? 1f : nodeAnchors[index] == 2 ? -1f : 0f;

        /// <summary>
        /// A free cell's target under the neighbour rule, from last pass's hold. The map edge
        /// is not a neighbour: an edge cell averages the ones it has.
        /// </summary>
        private float Coupled(int index, out bool surrounded)
        {
            surrounded = false;
            // Ground no side reaches stays nobody's: neighbours sharpen a claim, they never make one.
            if (cellTarget[index] == 0f) return 0f;
            int r = index / ResolutionX, c = index - r * ResolutionX;
            float own = previousHold[index], sum = 0f;
            int count = 0, opposing = 0;
            for (int dr = -1; dr <= 1; dr++)
            {
                int rr = r + dr;
                if (rr < 0 || rr >= ResolutionY) continue;
                for (int dc = -1; dc <= 1; dc++)
                {
                    int cc = c + dc;
                    if ((dr == 0 && dc == 0) || cc < 0 || cc >= ResolutionX) continue;
                    float h = previousHold[rr * ResolutionX + cc];
                    sum += h;
                    count++;
                    if (own > ControlFieldCore.HoldEpsilon ? h < -ControlFieldCore.HoldEpsilon
                        : own < -ControlFieldCore.HoldEpsilon && h > ControlFieldCore.HoldEpsilon) opposing++;
                }
            }
            surrounded = opposing >= SurroundedNeighbours;
            float mean = count > 0 ? sum / count : 0f;
            return Math.Clamp(cellTarget[index] + (mean - cellTarget[index]) * NeighbourCoupling, -1f, 1f);
        }

        /// <summary>Keeps only front stretches on land: a front at sea is two coasts looking at each other.</summary>
        private int DropSeaSegments(int count)
        {
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                SectorFrontSegment s = frontSegments[i];
                WorldToCell((s.AX + s.BX) * 0.5f, (s.AZ + s.BZ) * 0.5f, out int c, out int r);
                if (sea[r * ResolutionX + c] != 0) continue;
                frontSegments[kept++] = s;
            }
            return kept;
        }

        private static ulong HashFrontline(SectorFrontSegment[] segments, int count)
        {
            ulong hash = 1469598103934665603UL;
            hash = HashStep(hash, (uint)count);
            for (int i = 0; i < count; i++)
            {
                SectorFrontSegment s = segments[i];
                hash = HashStep(hash, (uint)(int)Math.Round(s.AX));
                hash = HashStep(hash, (uint)(int)Math.Round(s.AZ));
                hash = HashStep(hash, (uint)(int)Math.Round(s.BX));
                hash = HashStep(hash, (uint)(int)Math.Round(s.BZ));
                hash = HashStep(hash, (uint)(int)Math.Round(s.Pressure * 1000f));
            }
            return hash;
        }

        private static ulong HashStep(ulong hash, uint word) => (hash ^ word) * 1099511628211UL;

        /// <summary>
        /// Rebuilds the node influence field only when the observation snapshot actually
        /// changed it — the 2 Hz refresh re-registers the same airbases every cycle.
        /// </summary>
        private void EnsureStrategicField()
        {
            if (!strategicDirty && strategicSignature == nodeSignature) return;
            strategicSignature = nodeSignature;
            strategicDirty = false;

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
                        float influence = node.MaxRadius / (node.MaxRadius + Scalar.Distance2D(x, z, node.X, node.Z));
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

            // An unheld square with held ground on three or four sides is a gap in the picture, not
            // open country: left bare, each side's border line bent round it and drew a ring on the
            // front. It carries its strongest neighbour's band, stored one up (1..3) because 0 on
            // unheld ground means bare. Last pass, so the ring pass above never reads these values.
            for (int r = 0; r < ResolutionY; r++)
            for (int c = 0; c < ResolutionX; c++)
            {
                int idx = r * ResolutionX + c;
                if (sectorStates[idx] != SectorControl.Neutral) continue;
                int held = 0, band = 0;
                if (r + 1 < ResolutionY) GapNeighbour(idx + ResolutionX, ref held, ref band);
                if (c + 1 < ResolutionX) GapNeighbour(idx + 1, ref held, ref band);
                if (r > 0) GapNeighbour(idx - ResolutionX, ref held, ref band);
                if (c > 0) GapNeighbour(idx - 1, ref held, ref band);
                if (held >= 3) frontBand[idx] = (byte)(band + 1);
            }
        }

        private void GapNeighbour(int index, ref int held, ref int band)
        {
            if (sectorStates[index] == SectorControl.Neutral) return;
            held++;
            if (frontBand[index] > band) band = frontBand[index];
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
            DisplayHash = HashDisplay(displayClusters, displayQuadCount);
            displayDirty = false;
        }

        private void EnsureDisplay()
        {
            if (!displayDirty) return;
            ComputeFrontBand();
            BuildDisplayQuads(ResolutionX * ResolutionY);
        }

        /// <summary>
        /// What the texture bake actually draws: every display cluster plus the
        /// contested stripe share, so the overlay re-bakes only on visible change.
        /// </summary>
        private ulong HashDisplay(SectorClusterTree.Cluster[] quads, int count)
        {
            ulong hash = 1469598103934665603UL ^ seaHash;
            hash = HashStep(hash, (uint)count);
            for (int q = 0; q < count; q++)
            {
                SectorClusterTree.Cluster quad = quads[q];
                hash = HashStep(hash, quad.Key);
                hash = HashStep(hash, (uint)quad.X);
                hash = HashStep(hash, (uint)quad.Y);
                hash = HashStep(hash, (uint)quad.Width);
                hash = HashStep(hash, (uint)quad.Height);
                if (((quad.Key >> 2) & 3) == (int)SectorControl.Contested)
                {
                    float hold = holdStrength[quad.Y * ResolutionX + quad.X];
                    float share = Math.Clamp((float.IsFinite(hold) ? hold : 0f) * 0.5f + 0.5f, 0f, 1f);
                    hash = HashStep(hash, (uint)(share * 256f));
                }
            }
            // The two colours meet on the hold contour, so a front that slides inside
            // unchanged cells still has to re-bake - one coarse step at a time.
            int cells = ResolutionX * ResolutionY;
            for (int i = 0; i < cells; i++)
                if (frontBand[i] != 0) hash = HashStep(hash, (uint)(i * 64 + TintStep(i) + 32));
            return hash;
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
            WalkFrom(first.AX, first.AZ, ref written, ref peak, ref metres);
            ReversePoints(mark, written - mark);

            tracePoints[written++] = new FrontlineTracePoint(first.BX, first.BZ);
            WalkFrom(first.BX, first.BZ, ref written, ref peak, ref metres);
            // A pocket ring walks back to its own start, so the first point repeats last;
            // consumers read that as a closed trace.
            return written - mark;
        }

        /// <summary>Follows unused segments from an endpoint, appending each far end as a trace point.</summary>
        private void WalkFrom(float atX, float atZ, ref int written, ref float peak, ref float metres)
        {
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

        /// <summary>Longest side of the baked tint the map overlay may ask for.</summary>
        public const int MaximumTextureSize = 2048;

        /// <summary>
        /// CPU rasterizer for the sector tint. Territory is a smooth field sampled between cell
        /// centres, so borders run diagonal and round instead of stepping on the grid: a soft
        /// fill that deepens toward the fighting, a fine rim where held ground meets open
        /// ground, and the two colours meeting on the hold field's zero contour - the same one
        /// the vector front line (`Presentation/MapUi/FrontlineGraphic`) traces. Quads whose
        /// four cells agree are plain fills, so only border ground pays per-pixel work.
        /// </summary>
        public Color32[] BakeTexture(
            int texWidth,
            int texHeight,
            bool showSectors,
            float globalOpacity)
        {
            if (texWidth < 1 || texHeight < 1 || texWidth > MaximumTextureSize || texHeight > MaximumTextureSize)
                throw new ArgumentOutOfRangeException(nameof(texWidth), "Tactical overlay dimensions must be 1.." + MaximumTextureSize + ".");
            globalOpacity = float.IsFinite(globalOpacity) ? Math.Clamp(globalOpacity, 0f, 1f) : 0f;
            int totalPixels = texWidth * texHeight;
            if (pixelBuffer == null || pixelBuffer.Length != totalPixels || cachedTexWidth != texWidth || cachedTexHeight != texHeight)
            {
                pixelBuffer = new Color32[totalPixels];
                cachedTexWidth = texWidth;
                cachedTexHeight = texHeight;
                pixelBufferBlank = true;
            }

            Color32 clear = new Color32(0, 0, 0, 0);
            if (!showSectors || globalOpacity <= 0f)
            {
                if (!pixelBufferBlank) Array.Fill(pixelBuffer, clear);
                pixelBufferBlank = true;
                return pixelBuffer;
            }
            EnsureDisplay();
            // Below, every pixel is written exactly once (fills, runs of fills, or per-pixel
            // paint), so the buffer needs no clear first.
            pixelBufferBlank = false;

            // A wash, not a paint job: the terrain underneath has to stay readable.
            float fillAlpha = Math.Clamp(globalOpacity * 255f * 0.32f, 12f, 40f);
            float pxPerCellX = CellSize / WorldSizeX * texWidth;
            float pxPerCellY = CellSize / WorldSizeY * texHeight;
            float originPxX = (OriginX + WorldSizeX * 0.5f) / WorldSizeX * texWidth;
            float originPxY = (OriginZ + WorldSizeY * 0.5f) / WorldSizeY * texHeight;
            // Smoothstep weights steepen the field to 1.5 per cell at a border: this turns a
            // field value into "pixels inside the border".
            float edgeScale = Math.Min(pxPerCellX, pxPerCellY) / 1.5f;
            int period = Math.Clamp((int)(Math.Min(pxPerCellX, pxPerCellY) / 2f), 4, 12);
            int hairline = Math.Max(1, period / 5);

            // The pixel centre's place in the field depends on its column alone, or its row
            // alone, so those terms are worked out once per column and per row, not once per
            // quad row and pixel. Every expression below is the original one, hoisted.
            BakeScratch scratch = bakeScratch ?? (bakeScratch = new BakeScratch());
            scratch.Reserve(texWidth, texHeight, ResolutionX, ResolutionY);
            BakeColumn[] columns = scratch.Columns;
            BakeRow[] rows = scratch.Rows;
            int[] quadX0 = scratch.QuadX0, quadX1 = scratch.QuadX1;
            int[] quadY0 = scratch.QuadY0, quadY1 = scratch.QuadY1;
            for (int qc = -1; qc < ResolutionX; qc++)
            {
                int xMin = Math.Max(0, QuadStart(originPxX, pxPerCellX, qc, ResolutionX));
                int xMax = Math.Min(texWidth, QuadStart(originPxX, pxPerCellX, qc + 1, ResolutionX)) - 1;
                quadX0[qc + 1] = xMin;
                quadX1[qc + 1] = xMax;
                for (int x = xMin; x <= xMax; x++)
                {
                    float cellX = (x + 0.5f - originPxX) / pxPerCellX;
                    float tx = Math.Clamp(cellX - 0.5f - qc, 0f, 1f);
                    columns[x].Tx = tx;
                    columns[x].Sx = tx * tx * (3f - 2f * tx);
                }
            }
            for (int qr = -1; qr < ResolutionY; qr++)
            {
                int yMin = Math.Max(0, QuadStart(originPxY, pxPerCellY, qr, ResolutionY));
                int yMax = Math.Min(texHeight, QuadStart(originPxY, pxPerCellY, qr + 1, ResolutionY)) - 1;
                quadY0[qr + 1] = yMin;
                quadY1[qr + 1] = yMax;
                for (int y = yMin; y <= yMax; y++)
                {
                    float cellY = (y + 0.5f - originPxY) / pxPerCellY;
                    float ty = Math.Clamp(cellY - 0.5f - qr, 0f, 1f);
                    rows[y].Ty = ty;
                    rows[y].Sy = ty * ty * (3f - 2f * ty);
                }
            }

            float hostileR = HostileTint.r, hostileG = HostileTint.g, hostileB = HostileTint.b;
            float spanR = FriendlyTint.r - HostileTint.r, spanG = FriendlyTint.g - HostileTint.g, spanB = FriendlyTint.b - HostileTint.b;
            // A quad whose four corners are all held is owned in full: owned is exactly 1 there.
            float insideHeld = (1f - 0.5f) * edgeScale;
            float coverHeld = Math.Min(1f, insideHeld + 0.5f);
            float rimHeld = Math.Max(0f, 1f - Math.Abs(insideHeld - RimInset) / RimInset);
            // (insideHeld <= -0.5f never holds: edgeScale is positive.)

            // Quads tile the texture, so each pixel belongs to one quad: plain quads and the
            // unheld ground between them are written as runs of one colour, only the border
            // pays per pixel.
            int nextRow = 0;
            for (int qr = -1; qr < ResolutionY; qr++)
            {
                int yMin = quadY0[qr + 1], yMax = quadY1[qr + 1];
                if (yMax < yMin) continue;
                if (yMin > nextRow) FillSpan(pixelBuffer, nextRow * texWidth, (yMin - nextRow) * texWidth, clear);
                nextRow = yMax + 1;

                int r0 = Math.Max(qr, 0), r1 = Math.Min(qr + 1, ResolutionY - 1);
                int runFrom = 0, runTo = 0, nextCol = 0;
                Color32 runColor = clear;
                for (int qc = -1; qc < ResolutionX; qc++)
                {
                    int xMin = quadX0[qc + 1], xMax = quadX1[qc + 1];
                    if (xMax < xMin) continue;
                    if (xMin > nextCol) AddRun(pixelBuffer, texWidth, ref runFrom, ref runTo, ref runColor, yMin, yMax, nextCol, xMin, clear);
                    nextCol = xMax + 1;

                    int c0 = Math.Max(qc, 0), c1 = Math.Min(qc + 1, ResolutionX - 1);
                    int i00 = r0 * ResolutionX + c0, i10 = r0 * ResolutionX + c1;
                    int i01 = r1 * ResolutionX + c0, i11 = r1 * ResolutionX + c1;
                    SectorControl s00 = sectorStates[i00], s10 = sectorStates[i10];
                    SectorControl s01 = sectorStates[i01], s11 = sectorStates[i11];
                    float w00 = BandWeight(i00), w10 = BandWeight(i10), w01 = BandWeight(i01), w11 = BandWeight(i11);
                    if ((s00 == SectorControl.Neutral && s10 == SectorControl.Neutral &&
                         s01 == SectorControl.Neutral && s11 == SectorControl.Neutral) ||
                        (w00 == 0f && w10 == 0f && w01 == 0f && w11 == 0f))
                    {
                        AddRun(pixelBuffer, texWidth, ref runFrom, ref runTo, ref runColor, yMin, yMax, xMin, xMax + 1, clear);
                        continue;
                    }

                    if (s00 != SectorControl.Contested && s00 == s10 && s00 == s01 && s00 == s11 &&
                        w00 == w10 && w00 == w01 && w00 == w11)
                    {
                        Color32 flat = s00 == SectorControl.Friendly ? FriendlyTint : HostileTint;
                        flat.a = (byte)Math.Max(1f, fillAlpha * w00);
                        AddRun(pixelBuffer, texWidth, ref runFrom, ref runTo, ref runColor, yMin, yMax, xMin, xMax + 1, flat);
                        continue;
                    }

                    FlushRun(pixelBuffer, texWidth, runFrom, runTo, runColor, yMin, yMax);
                    runFrom = runTo = 0;

                    float o00 = w00 > 0f ? 1f : 0f, o10 = w10 > 0f ? 1f : 0f, o01 = w01 > 0f ? 1f : 0f, o11 = w11 > 0f ? 1f : 0f;
                    float k00 = s00 == SectorControl.Contested ? 1f : 0f, k10 = s10 == SectorControl.Contested ? 1f : 0f;
                    float k01 = s01 == SectorControl.Contested ? 1f : 0f, k11 = s11 == SectorControl.Contested ? 1f : 0f;
                    bool hatched = k00 + k10 + k01 + k11 > 0f;
                    bool held = w00 > 0f && w10 > 0f && w01 > 0f && w11 > 0f;
                    float h00 = TintHold(i00), h10 = TintHold(i10), h01 = TintHold(i01), h11 = TintHold(i11);

                    // Bilinear terms of this quad, split into the part that depends on the
                    // column and the part the row adds: value = low + delta * rowT.
                    float hx0 = h10 - h00, hx1 = (h11 - h01) - hx0;
                    float hy0 = h01 - h00, hy1 = (h11 - h10) - hy0;
                    for (int x = xMin; x <= xMax; x++)
                    {
                        ref BakeColumn column = ref columns[x];
                        float sx = column.Sx, tx = column.Tx;
                        float low = w00 + (w10 - w00) * sx;
                        column.WLow = low;
                        column.WDelta = w01 + (w11 - w01) * sx - low;
                        low = h00 + (h10 - h00) * tx;
                        column.HLow = low;
                        column.HDelta = h01 + (h11 - h01) * tx - low;
                        float gy = (hy0 + hy1 * tx) / pxPerCellY;
                        column.Gy2 = gy * gy;
                        if (!held)
                        {
                            low = o00 + (o10 - o00) * sx;
                            column.OLow = low;
                            column.ODelta = o01 + (o11 - o01) * sx - low;
                        }
                        if (hatched)
                        {
                            low = k00 + (k10 - k00) * sx;
                            column.KLow = low;
                            column.KDelta = k01 + (k11 - k01) * sx - low;
                        }
                    }

                    for (int y = yMin; y <= yMax; y++)
                    {
                        float ty = rows[y].Ty, sy = rows[y].Sy;
                        float gx = (hx0 + hx1 * ty) / pxPerCellX;
                        float gx2 = gx * gx;
                        int rowOffset = y * texWidth;
                        int phase = (xMin + y) % period;
                        for (int x = xMin; x <= xMax; x++, phase = phase + 1 == period ? 0 : phase + 1)
                        {
                            ref BakeColumn column = ref columns[x];
                            float cover, rim, weight;
                            if (held)
                            {
                                cover = coverHeld;
                                rim = rimHeld;
                                weight = column.WLow + column.WDelta * sy;
                            }
                            else
                            {
                                float owned = column.OLow + column.ODelta * sy;
                                float inside = (owned - 0.5f) * edgeScale;
                                if (inside <= -0.5f)
                                {
                                    pixelBuffer[rowOffset + x] = clear;
                                    continue;
                                }
                                cover = Math.Min(1f, inside + 0.5f);
                                float shape = column.WLow + column.WDelta * sy;
                                weight = shape / owned;
                                rim = Math.Max(0f, 1f - Math.Abs(inside - RimInset) / RimInset);
                            }
                            float alpha = fillAlpha * (weight + rim * RimStrength);

                            // Which side: the hold field itself, one pixel of blend across its zero.
                            float hold = column.HLow + column.HDelta * ty;
                            float slope = (float)Math.Sqrt(gx2 + column.Gy2);
                            float friendly = slope > 1e-6f ? ClampF(hold / slope + 0.5f, 0f, 1f) : hold >= 0f ? 1f : 0f;

                            if (hatched)
                            {
                                // No man's land, never a third colour: the wash fades and thin
                                // diagonal hairlines in the challenger's colour cross it. The base
                                // keeps the side that leads (the hold contour), so a glance still
                                // says who is ahead without a field of full-width stripes.
                                float contested = column.KLow + column.KDelta * sy;
                                float hatch = ClampF((contested - 0.5f) * edgeScale + 0.5f, 0f, 1f);
                                if (hatch > 0f)
                                {
                                    if (phase < hairline)
                                    {
                                        friendly += ((friendly >= 0.5f ? 0f : 1f) - friendly) * hatch;
                                        alpha += fillAlpha * HatchStrength * hatch;
                                    }
                                    else alpha *= 1f - NoMansLandFade * hatch;
                                }
                            }

                            ref Color32 pixel = ref pixelBuffer[rowOffset + x];
                            pixel.r = (byte)(hostileR + spanR * friendly);
                            pixel.g = (byte)(hostileG + spanG * friendly);
                            pixel.b = (byte)(hostileB + spanB * friendly);
                            pixel.a = (byte)ClampF(alpha * cover, 1f, 255f);
                        }
                    }
                }
                if (nextCol < texWidth) AddRun(pixelBuffer, texWidth, ref runFrom, ref runTo, ref runColor, yMin, yMax, nextCol, texWidth, clear);
                FlushRun(pixelBuffer, texWidth, runFrom, runTo, runColor, yMin, yMax);
            }
            if (nextRow < texHeight) FillSpan(pixelBuffer, nextRow * texWidth, (texHeight - nextRow) * texWidth, clear);

            return pixelBuffer;
        }

        /// <summary><c>Math.Clamp</c> for floats, small enough for the JIT to inline into the pixel loop.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ClampF(float value, float low, float high)
            => value < low ? low : value > high ? high : value;

        private static void FillSpan(Color32[] buffer, int start, int count, Color32 color)
        {
            if (count > 0) Array.Fill(buffer, color, start, count);
        }

        /// <summary>
        /// Queues columns [<paramref name="from"/>, <paramref name="to"/>) of rows y0..y1 in one colour,
        /// extending the pending run when it is the same colour right next door.
        /// </summary>
        private static void AddRun(Color32[] buffer, int texWidth, ref int runFrom, ref int runTo, ref Color32 runColor,
            int y0, int y1, int from, int to, Color32 color)
        {
            if (runTo > runFrom && runTo == from &&
                runColor.r == color.r && runColor.g == color.g && runColor.b == color.b && runColor.a == color.a)
            {
                runTo = to;
                return;
            }
            FlushRun(buffer, texWidth, runFrom, runTo, runColor, y0, y1);
            runFrom = from;
            runTo = to;
            runColor = color;
        }

        private static void FlushRun(Color32[] buffer, int texWidth, int runFrom, int runTo, Color32 color, int y0, int y1)
        {
            int count = runTo - runFrom;
            if (count <= 0) return;
            if (count == texWidth)
            {
                // Full-width rows are one block of memory.
                FillSpan(buffer, y0 * texWidth, (y1 - y0 + 1) * texWidth, color);
                return;
            }
            for (int y = y0; y <= y1; y++) Array.Fill(buffer, color, y * texWidth + runFrom, count);
        }

        /// <summary>Per pixel column: the quad-independent field position, then the current quad's bilinear terms.</summary>
        private struct BakeColumn
        {
            public float Tx, Sx;
            public float OLow, ODelta, WLow, WDelta, HLow, HDelta, KLow, KDelta, Gy2;
        }

        private struct BakeRow
        {
            public float Ty, Sy;
        }

        /// <summary>Per-bake working tables, kept between bakes so a re-bake allocates nothing.</summary>
        private sealed class BakeScratch
        {
            public BakeColumn[] Columns = new BakeColumn[0];
            public BakeRow[] Rows = new BakeRow[0];
            public int[] QuadX0 = new int[0], QuadX1 = new int[0], QuadY0 = new int[0], QuadY1 = new int[0];

            public void Reserve(int texWidth, int texHeight, int resolutionX, int resolutionY)
            {
                if (Columns.Length < texWidth) Columns = new BakeColumn[texWidth];
                if (Rows.Length < texHeight) Rows = new BakeRow[texHeight];
                if (QuadX0.Length < resolutionX + 1) { QuadX0 = new int[resolutionX + 1]; QuadX1 = new int[resolutionX + 1]; }
                if (QuadY0.Length < resolutionY + 1) { QuadY0 = new int[resolutionY + 1]; QuadY1 = new int[resolutionY + 1]; }
            }
        }

        /// <summary>Pixels inside the border where the rim line peaks; it is twice this wide.</summary>
        private const float RimInset = 1.25f;
        private const float RimStrength = 1.6f;
        private const float HatchStrength = 0.9f;
        /// <summary>How far contested ground's wash fades between the hairlines.</summary>
        private const float NoMansLandFade = 0.55f;
        /// <summary>Steps of hold the tint can tell apart; the display hash re-bakes on one step.</summary>
        private const float TintHoldSteps = 8f;

        /// <summary>First pixel whose centre lies in the quad starting at cell centre <paramref name="quad"/>;
        /// the outermost quads run to the grid edge.</summary>
        private static int QuadStart(float originPx, float pxPerCell, int quad, int resolution)
        {
            float cell = quad < 0 ? 0f : quad >= resolution ? resolution : quad + 0.5f;
            return (int)Math.Ceiling(originPx + cell * pxPerCell - 0.5f);
        }

        /// <summary>Full at the front, quiet in the rear, nothing on unheld ground.</summary>
        private float BandWeight(int index)
        {
            if (hasSea && sea[index] != 0) return 0f;
            byte band = frontBand[index];
            // Unheld ground is bare unless ComputeFrontBand marked it a closed-in gap (band + 1).
            if (sectorStates[index] == SectorControl.Neutral)
                return band == 3 ? 1f : band == 2 ? 0.55f : band == 1 ? 0.22f : 0f;
            return band == 2 ? 1f : band == 1 ? 0.55f : 0.22f;
        }

        /// <summary>The cell's hold in coarse signed steps, never zero for held ground.</summary>
        private float TintHold(int index) => TintStep(index) / TintHoldSteps;

        private int TintStep(int index)
        {
            float hold = holdStrength[index];
            if (sectorStates[index] == SectorControl.Neutral) return frontBand[index] != 0 ? GapStep(index) : 0;
            if (!float.IsFinite(hold) || hold == 0f) return 0;
            int step = (int)Math.Round(hold * TintHoldSteps);
            return step != 0 ? step : hold > 0f ? 1 : -1;
        }

        /// <summary>
        /// The side a closed-in gap leans to: one step toward whoever holds more of its four
        /// neighbours, zero when they balance, so on the line the two colours meet inside it.
        /// </summary>
        private int GapStep(int index)
        {
            int r = index / ResolutionX, c = index - r * ResolutionX;
            float sum = 0f;
            if (r + 1 < ResolutionY) sum += GapHold(index + ResolutionX);
            if (c + 1 < ResolutionX) sum += GapHold(index + 1);
            if (r > 0) sum += GapHold(index - ResolutionX);
            if (c > 0) sum += GapHold(index - 1);
            return sum > 0.01f ? 1 : sum < -0.01f ? -1 : 0;
        }

        private float GapHold(int index)
        {
            float hold = holdStrength[index];
            return sectorStates[index] == SectorControl.Neutral || !float.IsFinite(hold) ? 0f : Math.Sign(hold);
        }
    }
}

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
    /// </summary>
    internal sealed class TacticalSectorGrid
    {
        public const int DefaultResolution = 32;
        public const int MaximumNodes = 128;
        private const int MaximumInfluenceCells = 8;
        private const float CaptureSeconds = 15f;
        private const float RecoverySeconds = 90f;
        private bool hasEvaluated;
        public readonly int Resolution;

        public int ResolutionX { get; private set; }
        public int ResolutionY { get; private set; }

        public float WorldSizeX { get; private set; }
        public float WorldSizeY { get; private set; }
        public float WorldSize => Math.Max(WorldSizeX, WorldSizeY);

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
        private readonly byte[] frontlineBorders; // bitmask: 1=N, 2=E, 4=S, 8=W
        private readonly byte[] nodeAnchors;

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

        /// <summary>
        /// Cell edges where friendly control meets hostile or contested control — the
        /// frontline's length, in grid segments.
        ///
        /// <para>Counted from the same border pass that builds the edge mask, so it costs
        /// nothing beyond an increment, and it is the one number that separates a long thin
        /// front from a compact pocket holding the same number of sectors.</para>
        /// </summary>
        public int FrontlineSegmentCount { get; private set; }
        public int TotalNodesCount => nodes.Count;
        public int TotalSectors => ResolutionX * ResolutionY;
        public float TerritoryControlRatio => (FriendlySectorCount + HostileSectorCount > 0)
            ? (float)FriendlySectorCount / (FriendlySectorCount + HostileSectorCount)
            : 0.5f;

        public TacticalSectorGrid(int resolution = DefaultResolution, float worldSize = 100000f)
            : this(resolution, worldSize, worldSize)
        {
        }

        public TacticalSectorGrid(int resolution, float worldSizeX, float worldSizeY)
        {
            Resolution = Math.Clamp(resolution, 16, 64);
            WorldSizeX = ValidWorldSize(worldSizeX) ? worldSizeX : 100000f;
            WorldSizeY = ValidWorldSize(worldSizeY) ? worldSizeY : 100000f;

            UpdateResolutions();

            // Sized with generous safety margin for maximum theoretical resolution capacity
            int total = 64 * 64;
            holdStrength = new float[total];
            friendlyForce = new float[total];
            hostileForce = new float[total];
            sectorStates = new SectorControl[total];
            frontlineBorders = new byte[total];
            nodeAnchors = new byte[total];
            NeutralSectorCount = TotalSectors;
        }

        private void UpdateResolutions()
        {
            // The longest axis owns the budget, including portrait maps. Integer rounding
            // approximates square cells; extreme aspect ratios bottom out at one cell.
            float longest = Math.Max(WorldSizeX, WorldSizeY);
            ResolutionX = Math.Clamp((int)Math.Round(Resolution * WorldSizeX / longest), 1, Resolution);
            ResolutionY = Math.Clamp((int)Math.Round(Resolution * WorldSizeY / longest), 1, Resolution);
        }

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
                UpdateResolutions();
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
            Array.Clear(frontlineBorders, 0, frontlineBorders.Length);
            FrontlineSegmentCount = 0;
        }

        public bool WorldToCell(float worldX, float worldZ, out int col, out int row)
        {
            col = row = 0;
            if (!IsFinite(worldX) || !IsFinite(worldZ)) return false;
            double x = (double)worldX / WorldSizeX + 0.5;
            double z = (double)worldZ / WorldSizeY + 0.5;
            col = (int)Math.Clamp(Math.Floor(x * ResolutionX), 0, ResolutionX - 1);
            row = (int)Math.Clamp(Math.Floor(z * ResolutionY), 0, ResolutionY - 1);
            return x >= 0 && x < 1 && z >= 0 && z < 1;
        }

        public void CellToWorldBounds(int col, int row, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            float halfX = WorldSizeX * 0.5f;
            float halfY = WorldSizeY * 0.5f;
            float cellSizeX = WorldSizeX / ResolutionX;
            float cellSizeY = WorldSizeY / ResolutionY;

            minX = col * cellSizeX - halfX;
            minZ = row * cellSizeY - halfY;
            maxX = minX + cellSizeX;
            maxZ = minZ + cellSizeY;
        }

        public void CellToCenter(int col, int row, out float centerX, out float centerZ)
        {
            CellToWorldBounds(col, row, out float minX, out float minZ, out float maxX, out float maxZ);
            centerX = (minX + maxX) * 0.5f;
            centerZ = (minZ + maxZ) * 0.5f;
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
                return;
            }
            if (nodes.Count < MaximumNodes)
                nodes.Add(new TacticalNode(id, name, worldX, worldZ, faction, radius, isAirbase));
        }

        public void AddTroopPresence(float worldX, float worldZ, float weight, bool isHostile, float influenceRadius = 12000f)
        {
            if (!IsFinite(weight) || weight <= 0f || !IsFinite(influenceRadius) || influenceRadius < 0f ||
                !WorldToCell(worldX, worldZ, out int col, out int row)) return;
            weight = Math.Min(weight, 100f);
            float radius = Math.Min(influenceRadius, 16000f);
            // Even tiny custom theaters scan at most a 17 x 17 stencil per unit.
            int reachX = Math.Min(MaximumInfluenceCells, (int)Math.Ceiling(radius / (WorldSizeX / ResolutionX)));
            int reachY = Math.Min(MaximumInfluenceCells, (int)Math.Ceiling(radius / (WorldSizeY / ResolutionY)));
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
            Array.Clear(nodeAnchors, 0, nodeAnchors.Length);
            for (int i = 0; i < nodes.Count; i++)
            {
                TacticalNode node = nodes[i];
                WorldToCell(node.X, node.Z, out int c, out int r);
                int index = r * ResolutionX + c;
                float opposing = node.Faction == SectorControl.Friendly ? hostileForce[index] : friendlyForce[index];
                node.IsContested = node.Faction != SectorControl.Neutral && opposing > 0.05f;
                node.CaptureProgress = node.IsContested ? opposing / Math.Max(0.05f, friendlyForce[index] + hostileForce[index]) : 0f;
                nodes[i] = node;
                if (node.IsAirbase)
                    nodeAnchors[index] |= node.Faction == SectorControl.Friendly ? (byte)1 : node.Faction == SectorControl.Hostile ? (byte)2 : (byte)0;
            }

            // ponytail: at most 4096 cells x 128 strategic nodes, avoiding pathfinding
            // and terrain claims. Add terrain-aware supply only after profiling/playtests.
            for (int r = 0; r < ResolutionY; r++)
            {
                for (int c = 0; c < ResolutionX; c++)
                {
                    int index = r * ResolutionX + c;
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
                    float strategic = strongest > 0f ? Math.Clamp(3f * (friendlyInfluence - hostileInfluence) / strongest, -1f, 1f) : 0f;
                    float friendly = friendlyForce[index], hostile = hostileForce[index];
                    float total = friendly + hostile;
                    float pressure = (friendly - hostile) / Math.Max(3f, total);
                    float target = Math.Clamp(strategic + 2f * pressure, -1f, 1f);
                    float response = 1f - (float)Math.Exp(-elapsed / (total > 0.05f ? CaptureSeconds : RecoverySeconds));
                    holdStrength[index] = hasEvaluated ? holdStrength[index] + (target - holdStrength[index]) * response : target;

                    // Actual vanilla airbase ownership anchors its cell; this overlay never
                    // captures a base merely because enough observed vehicles surround it.
                    if (nodeAnchors[index] != 0)
                        holdStrength[index] = nodeAnchors[index] == 1 ? 1f : nodeAnchors[index] == 2 ? -1f : 0f;
                    float hold = holdStrength[index];
                    bool balancedClash = friendly > 0.05f && hostile > 0.05f && friendly / total > 0.34f && friendly / total < 0.66f;
                    bool advancing = Math.Abs(pressure) > 0.05f && pressure * hold < 0f;
                    sectorStates[index] = nodeAnchors[index] == 3 || balancedClash || advancing
                        ? SectorControl.Contested
                        : hold > 0.01f ? SectorControl.Friendly : hold < -0.01f ? SectorControl.Hostile
                        : strongest > 0f || total > 0.05f ? SectorControl.Contested : SectorControl.Neutral;
                }
            }
            hasEvaluated = true;

            // Extract crisp borders after every cell has advanced (no traversal-order bias).
            FriendlySectorCount = 0;
            HostileSectorCount = 0;
            ContestedSectorCount = 0;
            NeutralSectorCount = 0;

            Array.Clear(frontlineBorders, 0, frontlineBorders.Length);
            int segments = 0;

            for (int r = 0; r < ResolutionY; r++)
            {
                for (int c = 0; c < ResolutionX; c++)
                {
                    int idx = r * ResolutionX + c;
                    SectorControl state = sectorStates[idx];

                    switch (state)
                    {
                        case SectorControl.Friendly: FriendlySectorCount++; break;
                        case SectorControl.Hostile: HostileSectorCount++; break;
                        case SectorControl.Contested: ContestedSectorCount++; break;
                        default: NeutralSectorCount++; break;
                    }

                    // Evaluate 4 neighbors to detect frontline edges
                    byte edgeMask = 0;

                    // North (r + 1)
                    if (r < ResolutionY - 1 && IsOpposing(state, sectorStates[(r + 1) * ResolutionX + c]))
                        edgeMask |= 1;

                    // East (c + 1)
                    if (c < ResolutionX - 1 && IsOpposing(state, sectorStates[r * ResolutionX + (c + 1)]))
                        edgeMask |= 2;

                    // South (r - 1)
                    if (r > 0 && IsOpposing(state, sectorStates[(r - 1) * ResolutionX + c]))
                        edgeMask |= 4;

                    // West (c - 1)
                    if (c > 0 && IsOpposing(state, sectorStates[r * ResolutionX + (c - 1)]))
                        edgeMask |= 8;

                    frontlineBorders[idx] = edgeMask;

                    // Each shared edge is seen from both of its cells; count it once.
                    if ((edgeMask & 1) != 0) segments++;
                    if ((edgeMask & 2) != 0) segments++;
                }
            }

            FrontlineSegmentCount = segments;
        }

        public int CopyFrontlineSites(FrontlineSite[] destination)
        {
            if (destination == null || destination.Length == 0) return 0;
            int count = 0;
            for (int r = 0; r < ResolutionY; r++)
            for (int c = 0; c < ResolutionX; c++)
            {
                if (GetSectorControl(c, r) != SectorControl.Friendly) continue;
                CellToWorldBounds(c, r, out float x0, out float z0, out float x1, out float z1);
                byte borders = frontlineBorders[r * ResolutionX + c];
                for (int side = 0; side < 4; side++)
                {
                    if ((borders & (1 << side)) == 0) continue;
                    float dx = side == 1 ? 1 : side == 3 ? -1 : 0;
                    float dz = side == 0 ? 1 : side == 2 ? -1 : 0;
                    float x = (x0 + x1) * 0.5f + dx * ((x1 - x0) * 0.5f - 60f);
                    float z = (z0 + z1) * 0.5f + dz * ((z1 - z0) * 0.5f - 60f);
                    destination[count++] = new FrontlineSite(x, z, dx, dz,
                        (dx == 0 ? x1 - x0 : z1 - z0) * 0.5f, BorderPressure(c, r, c + (int)dx, r + (int)dz));
                    if (count == destination.Length) return count;
                }
            }
            return count;
        }

        /// <summary>
        /// Opposing ground presence across one border edge. Balanced forces read near 1;
        /// a border facing an empty sector reads 0, so fortification follows the fighting.
        /// </summary>
        private float BorderPressure(int c, int r, int otherCol, int otherRow)
        {
            if (otherCol < 0 || otherCol >= ResolutionX || otherRow < 0 || otherRow >= ResolutionY) return 0f;
            int own = r * ResolutionX + c, other = otherRow * ResolutionX + otherCol;
            float friendly = friendlyForce[own] + friendlyForce[other];
            float hostile = hostileForce[own] + hostileForce[other];
            if (friendly < 0.05f || hostile < 0.05f) return 0f;
            return 2f * Math.Min(friendly, hostile) / (friendly + hostile);
        }

        private static bool IsOpposing(SectorControl a, SectorControl b)
        {
            if (a == SectorControl.Neutral || b == SectorControl.Neutral) return false;
            if (a == SectorControl.Friendly && (b == SectorControl.Hostile || b == SectorControl.Contested)) return true;
            if (a == SectorControl.Hostile && (b == SectorControl.Friendly || b == SectorControl.Contested)) return true;
            if (a == SectorControl.Contested && (b == SectorControl.Friendly || b == SectorControl.Hostile)) return true;
            return false;
        }

        /// <summary>
        /// Fast procedural CPU rasterizer that bakes the discrete military grid,
        /// translucent sector fills, crisp frontline boundaries, and hazard striping.
        /// Reuses a bounded pixel buffer after the initial allocation.
        /// </summary>
        public Color32[] BakeTexture(
            int texWidth,
            int texHeight,
            bool showSectors,
            bool showFrontlines,
            float globalOpacity)
        {
            if (texWidth < 1 || texHeight < 1 || texWidth > 512 || texHeight > 512)
                throw new ArgumentOutOfRangeException(nameof(texWidth), "Tactical overlay dimensions must be 1..512.");
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

            if ((!showSectors && !showFrontlines) || globalOpacity <= 0f) return pixelBuffer;

            byte sectorAlpha = (byte)Math.Clamp((int)(globalOpacity * 255f * 0.45f), 20, 60); // 8% to 24% opacity
            byte borderAlpha = (byte)Math.Clamp((int)(globalOpacity * 255f * 0.85f), 120, 230);
            byte gridLineAlpha = 22; // subtle tactical grid line

            Color32 friendlyFill = new Color32(35, 130, 235, sectorAlpha); // Allied Cyan/Blue
            Color32 hostileFill = new Color32(230, 45, 45, sectorAlpha);    // OPFOR Crimson
            Color32 contestedFill1 = new Color32(245, 175, 25, (byte)(sectorAlpha + 15)); // Amber hazard
            Color32 contestedFill2 = new Color32(30, 30, 30, (byte)(sectorAlpha + 10));  // Dark hazard stripe

            Color32 friendlyBorder = new Color32(60, 180, 255, borderAlpha);
            Color32 hostileBorder = new Color32(255, 60, 60, borderAlpha);
            Color32 clashBorder = new Color32(255, 210, 40, borderAlpha);

            Color32 gridLineColor = new Color32(80, 110, 130, gridLineAlpha);

            float pxPerCellX = (float)texWidth / ResolutionX;
            float pxPerCellY = (float)texHeight / ResolutionY;

            // 1. Sector Fills & Grid Borders
            for (int r = 0; r < ResolutionY; r++)
            {
                int pyMin = (int)Math.Floor(r * pxPerCellY);
                int pyMax = (int)Math.Min(texHeight - 1, Math.Floor((r + 1) * pxPerCellY));

                for (int c = 0; c < ResolutionX; c++)
                {
                    int pxMin = (int)Math.Floor(c * pxPerCellX);
                    int pxMax = (int)Math.Min(texWidth - 1, Math.Floor((c + 1) * pxPerCellX));

                    int idx = r * ResolutionX + c;
                    SectorControl state = sectorStates[idx];
                    byte edges = frontlineBorders[idx];

                    for (int y = pyMin; y <= pyMax; y++)
                    {
                        int rowOffset = y * texWidth;
                        bool isNorthEdge = (y >= pyMax - 1) && ((edges & 1) != 0);
                        bool isSouthEdge = (y <= pyMin + 1) && ((edges & 4) != 0);

                        for (int x = pxMin; x <= pxMax; x++)
                        {
                            bool isEastEdge = (x >= pxMax - 1) && ((edges & 2) != 0);
                            bool isWestEdge = (x <= pxMin + 1) && ((edges & 8) != 0);

                            // Check Frontline Edge
                            if (showFrontlines && (isNorthEdge || isSouthEdge || isEastEdge || isWestEdge))
                            {
                                if (state == SectorControl.Contested)
                                    pixelBuffer[rowOffset + x] = clashBorder;
                                else if (state == SectorControl.Friendly)
                                    pixelBuffer[rowOffset + x] = friendlyBorder;
                                else if (state == SectorControl.Hostile)
                                    pixelBuffer[rowOffset + x] = hostileBorder;
                                else
                                    pixelBuffer[rowOffset + x] = clashBorder;
                                continue;
                            }

                            // Subtle grid matrix line at sector cell boundaries
                            if (showSectors && (x == pxMin || y == pyMin))
                            {
                                pixelBuffer[rowOffset + x] = gridLineColor;
                                continue;
                            }

                            // Sector Fill
                            if (showSectors)
                            {
                                if (state == SectorControl.Friendly)
                                {
                                    pixelBuffer[rowOffset + x] = friendlyFill;
                                }
                                else if (state == SectorControl.Hostile)
                                {
                                    pixelBuffer[rowOffset + x] = hostileFill;
                                }
                                else if (state == SectorControl.Contested)
                                {
                                    // Diagonal hazard hash pattern
                                    pixelBuffer[rowOffset + x] = ((x + y) % 10 < 5) ? contestedFill1 : contestedFill2;
                                }
                            }
                        }
                    }
                }
            }

            return pixelBuffer;
        }
    }
}

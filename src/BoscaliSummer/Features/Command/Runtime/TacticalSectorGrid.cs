using System;
using System.Collections.Generic;
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
    /// Discrete tactical grid and node territory engine inspired by Running With Rifles (RWR).
    /// Bases and landing zones act as nodes that organically grow control outward.
    /// Major concentrations of combat armor flip and capture contested sectors.
    /// Grid cells maintain a 1:1 square aspect ratio across all theater dimensions.
    /// </summary>
    internal sealed class TacticalSectorGrid
    {
        public const int DefaultResolution = 32;
        public readonly int Resolution;

        public int ResolutionX { get; private set; }
        public int ResolutionY { get; private set; }

        public float WorldSizeX { get; private set; }
        public float WorldSizeY { get; private set; }
        public float WorldSize => Math.Max(WorldSizeX, WorldSizeY);

        // Strategic Node Definition (Airbases, Forward LZs, Encampments, Strategic POIs)
        public sealed class TacticalNode
        {
            public int Id;
            public string Name;
            public float X;
            public float Z;
            public SectorControl Faction;
            public float MaxRadius;
            public bool IsAirbase;
            public bool IsContested;
            public float CaptureProgress; // 0.0 to 1.0

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
        private readonly bool[] isSupplied;
        private readonly int[] bfsQueue;
        private readonly int[] bfsDist;

        // Active nodes
        private readonly List<TacticalNode> nodes = new List<TacticalNode>(32);

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
            WorldSizeX = worldSizeX > 1000f ? worldSizeX : 100000f;
            WorldSizeY = worldSizeY > 1000f ? worldSizeY : 100000f;

            UpdateResolutions();

            // Sized with generous safety margin for maximum theoretical resolution capacity
            int total = 64 * 64;
            holdStrength = new float[total];
            friendlyForce = new float[total];
            hostileForce = new float[total];
            sectorStates = new SectorControl[total];
            frontlineBorders = new byte[total];
            isSupplied = new bool[total];
            bfsQueue = new int[total * 2];
            bfsDist = new int[total];
        }

        private void UpdateResolutions()
        {
            ResolutionX = Resolution;
            ResolutionY = (int)Math.Max(8, Math.Round(ResolutionX * (WorldSizeY / WorldSizeX)));
        }

        public void SetWorldSize(float worldSize)
        {
            SetWorldSize(worldSize, worldSize);
        }

        public void SetWorldSize(float worldSizeX, float worldSizeY)
        {
            bool changed = false;
            if (worldSizeX > 1000f && Math.Abs(WorldSizeX - worldSizeX) > 0.01f)
            {
                WorldSizeX = worldSizeX;
                changed = true;
            }
            if (worldSizeY > 1000f && Math.Abs(WorldSizeY - worldSizeY) > 0.01f)
            {
                WorldSizeY = worldSizeY;
                changed = true;
            }
            if (changed)
            {
                UpdateResolutions();
            }
        }

        public void Clear()
        {
            Array.Clear(friendlyForce, 0, friendlyForce.Length);
            Array.Clear(hostileForce, 0, hostileForce.Length);
            Array.Clear(frontlineBorders, 0, frontlineBorders.Length);
            Array.Clear(isSupplied, 0, isSupplied.Length);
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
            NeutralSectorCount = 0;
        }

        public bool WorldToCell(float worldX, float worldZ, out int col, out int row)
        {
            float halfX = WorldSizeX * 0.5f;
            float halfY = WorldSizeY * 0.5f;
            float cellSizeX = WorldSizeX / ResolutionX;
            float cellSizeY = WorldSizeY / ResolutionY;

            col = (int)Math.Floor((worldX + halfX) / cellSizeX);
            row = (int)Math.Floor((worldZ + halfY) / cellSizeY);

            if (col < 0 || col >= ResolutionX || row < 0 || row >= ResolutionY)
            {
                col = Math.Clamp(col, 0, ResolutionX - 1);
                row = Math.Clamp(row, 0, ResolutionY - 1);
                return false;
            }
            return true;
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

        public float GetSectorHoldStrength(int col, int row)
        {
            if (col < 0 || col >= ResolutionX || row < 0 || row >= ResolutionY)
                return 0f;
            return holdStrength[row * ResolutionX + col];
        }

        public void RegisterNode(int id, string name, float worldX, float worldZ, SectorControl faction, float maxRadius, bool isAirbase)
        {
            nodes.Add(new TacticalNode(id, name, worldX, worldZ, faction, maxRadius, isAirbase));
        }

        public void AddTroopPresence(float worldX, float worldZ, float weight, bool isHostile, float influenceRadius = 12000f)
        {
            if (WorldToCell(worldX, worldZ, out int col, out int row))
            {
                int idx = row * ResolutionX + col;
                if (isHostile)
                    hostileForce[idx] += weight;
                else
                    friendlyForce[idx] += weight;
            }
        }

        public void AddAirbasePresence(float worldX, float worldZ, bool isHostile, float influenceRadius = 25000f)
        {
            SectorControl faction = isHostile ? SectorControl.Hostile : SectorControl.Friendly;
            RegisterNode(nodes.Count + 1, "Airbase_" + (nodes.Count + 1), worldX, worldZ, faction, influenceRadius, true);
        }

        public IReadOnlyList<TacticalNode> GetNodes() => nodes;

        /// <summary>
        /// Simulates organic node growth via unbounded multi-source wavefront BFS expansion,
        /// vehicle concentrations with the 66% force superiority rule, and extracts frontline edge borders.
        /// Friendly and enemy wavefronts propagate outward across the theater until colliding at the frontline.
        /// </summary>
        public void EvaluateSectors()
        {
            int totalSectors = ResolutionX * ResolutionY;
            int qHead = 0;
            int qTail = 0;

            // -------------------------------------------------------------
            // PHASE 1: SEED STRATEGIC NODES (Airbases, LZs, Outposts)
            // -------------------------------------------------------------
            for (int i = 0; i < totalSectors; i++)
            {
                bfsDist[i] = int.MaxValue;
                sectorStates[i] = SectorControl.Neutral;
                holdStrength[i] = 0f;
            }

            int friendlySeedCount = 0;
            int hostileSeedCount = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                TacticalNode node = nodes[i];
                if (!WorldToCell(node.X, node.Z, out int c, out int r)) continue;

                int nodeIdx = r * ResolutionX + c;
                float fForce = friendlyForce[nodeIdx];
                float hForce = hostileForce[nodeIdx];

                // Check if node is contested
                if (node.Faction == SectorControl.Friendly && hForce > 0f)
                {
                    node.IsContested = true;
                    float total = fForce + hForce;
                    node.CaptureProgress = (total > 0.01f) ? Math.Clamp(hForce / total, 0f, 1f) : 0f;
                }
                else if (node.Faction == SectorControl.Hostile && fForce > 0f)
                {
                    node.IsContested = true;
                    float total = fForce + hForce;
                    node.CaptureProgress = (total > 0.01f) ? Math.Clamp(fForce / total, 0f, 1f) : 0f;
                }
                else
                {
                    node.IsContested = false;
                    node.CaptureProgress = 0f;
                }

                // Check if cell is already claimed by an opposing node
                if (sectorStates[nodeIdx] != SectorControl.Neutral && sectorStates[nodeIdx] != node.Faction)
                {
                    sectorStates[nodeIdx] = SectorControl.Contested;
                    holdStrength[nodeIdx] = 0f;
                    node.IsContested = true;
                    continue;
                }

                // Seed home cell
                sectorStates[nodeIdx] = node.Faction;
                bfsDist[nodeIdx] = 0;
                holdStrength[nodeIdx] = (node.Faction == SectorControl.Friendly) ? 1.0f : ((node.Faction == SectorControl.Hostile) ? -1.0f : 0f);

                // CRITICAL: Only Friendly and Hostile wavefronts expand! Neutral nodes do NOT flood-fill.
                if (node.Faction == SectorControl.Friendly)
                {
                    friendlySeedCount++;
                    if (qTail < bfsQueue.Length) bfsQueue[qTail++] = nodeIdx;
                }
                else if (node.Faction == SectorControl.Hostile)
                {
                    hostileSeedCount++;
                    if (qTail < bfsQueue.Length) bfsQueue[qTail++] = nodeIdx;
                }

                // If airbase, firmly anchor immediate 3x3 perimeter sectors
                if (node.IsAirbase)
                {
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr;
                            int nc = c + dc;
                            if (nc >= 0 && nc < ResolutionX && nr >= 0 && nr < ResolutionY)
                            {
                                int nIdx = nr * ResolutionX + nc;
                                if (sectorStates[nIdx] == SectorControl.Neutral && bfsDist[nIdx] > 1)
                                {
                                    sectorStates[nIdx] = node.Faction;
                                    bfsDist[nIdx] = 1;
                                    holdStrength[nIdx] = (node.Faction == SectorControl.Friendly) ? 0.9f : ((node.Faction == SectorControl.Hostile) ? -0.9f : 0f);
                                    if (node.Faction == SectorControl.Friendly || node.Faction == SectorControl.Hostile)
                                    {
                                        if (qTail < bfsQueue.Length) bfsQueue[qTail++] = nIdx;
                                    }
                                }
                                else if (sectorStates[nIdx] != SectorControl.Neutral && sectorStates[nIdx] != node.Faction)
                                {
                                    sectorStates[nIdx] = SectorControl.Contested;
                                    holdStrength[nIdx] = 0f;
                                }
                            }
                        }
                    }
                }
            }

            // Fallback for made missions: if a faction has zero airbases/nodes, seed from significant unit presence
            if (friendlySeedCount == 0 || hostileSeedCount == 0)
            {
                for (int i = 0; i < totalSectors; i++)
                {
                    float fF = friendlyForce[i];
                    float hF = hostileForce[i];
                    if (friendlySeedCount == 0 && fF >= 1.5f && hF < 0.1f && sectorStates[i] == SectorControl.Neutral)
                    {
                        sectorStates[i] = SectorControl.Friendly;
                        bfsDist[i] = 0;
                        holdStrength[i] = 0.9f;
                        if (qTail < bfsQueue.Length) bfsQueue[qTail++] = i;
                    }
                    else if (hostileSeedCount == 0 && hF >= 1.5f && fF < 0.1f && sectorStates[i] == SectorControl.Neutral)
                    {
                        sectorStates[i] = SectorControl.Hostile;
                        bfsDist[i] = 0;
                        holdStrength[i] = -0.9f;
                        if (qTail < bfsQueue.Length) bfsQueue[qTail++] = i;
                    }
                }
            }

            // -------------------------------------------------------------
            // PHASE 2: UNBOUNDED WAVEFRONT BFS EXPANSION
            // Expands outward cell-by-cell until colliding with opposing factions.
            // -------------------------------------------------------------
            while (qHead < qTail)
            {
                int currIdx = bfsQueue[qHead++];
                int cc = currIdx % ResolutionX;
                int cr = currIdx / ResolutionX;
                SectorControl currFaction = sectorStates[currIdx];
                if (currFaction != SectorControl.Friendly && currFaction != SectorControl.Hostile) continue;
                int nextDist = bfsDist[currIdx] + 1;

                // 4-way orthogonal expansion: North, South, East, West
                for (int d = 0; d < 4; d++)
                {
                    int nc = cc;
                    int nr = cr;
                    if (d == 0) nr++;
                    else if (d == 1) nr--;
                    else if (d == 2) nc++;
                    else if (d == 3) nc--;

                    if (nc < 0 || nc >= ResolutionX || nr < 0 || nr >= ResolutionY) continue;

                    int nIdx = nr * ResolutionX + nc;

                    // Unclaimed / Neutral sector: expand into it!
                    if (sectorStates[nIdx] == SectorControl.Neutral)
                    {
                        sectorStates[nIdx] = currFaction;
                        bfsDist[nIdx] = nextDist;
                        float decay = 1.0f / (1.0f + 0.04f * nextDist);
                        holdStrength[nIdx] = (currFaction == SectorControl.Friendly) ? decay : -decay;
                        if (qTail < bfsQueue.Length) bfsQueue[qTail++] = nIdx;
                    }
                    // Same faction with shorter distance: relax distance & supply
                    else if (sectorStates[nIdx] == currFaction && bfsDist[nIdx] > nextDist)
                    {
                        bfsDist[nIdx] = nextDist;
                        float decay = 1.0f / (1.0f + 0.04f * nextDist);
                        holdStrength[nIdx] = (currFaction == SectorControl.Friendly) ? decay : -decay;
                        if (qTail < bfsQueue.Length) bfsQueue[qTail++] = nIdx;
                    }
                    // Opposing faction: COLLISION! Frontline formed here.
                }
            }

            // -------------------------------------------------------------
            // PHASE 3: VEHICLE CONCENTRATION & THE 66% FORCE SUPERIORITY RULE
            // Active ground forces overpower passive hold and contest/flip sectors.
            // -------------------------------------------------------------
            for (int r = 0; r < ResolutionY; r++)
            {
                for (int c = 0; c < ResolutionX; c++)
                {
                    int idx = r * ResolutionX + c;
                    float fF = friendlyForce[idx];
                    float hF = hostileForce[idx];

                    if (fF > 0.05f && hF > 0.05f)
                    {
                        // Active combat clash in this sector!
                        float total = fF + hF;
                        float friendlyRatio = fF / total;

                        // 66% Superiority Rule (RWR)
                        if (friendlyRatio >= 0.66f)
                        {
                            // Friendly superiority: push toward Friendly
                            holdStrength[idx] = Math.Clamp(holdStrength[idx] + 0.35f, -1.0f, 1.0f);
                            sectorStates[idx] = holdStrength[idx] > 0f ? SectorControl.Friendly : SectorControl.Contested;
                        }
                        else if (friendlyRatio <= 0.34f)
                        {
                            // Hostile superiority: push toward Hostile
                            holdStrength[idx] = Math.Clamp(holdStrength[idx] - 0.35f, -1.0f, 1.0f);
                            sectorStates[idx] = holdStrength[idx] < 0f ? SectorControl.Hostile : SectorControl.Contested;
                        }
                        else
                        {
                            // Tactical stalemate
                            sectorStates[idx] = SectorControl.Contested;
                        }
                    }
                    else if (fF > 0.05f && hF <= 0.05f)
                    {
                        // Only Friendly forces present: capture / solidify
                        if (sectorStates[idx] == SectorControl.Hostile)
                        {
                            holdStrength[idx] = Math.Clamp(holdStrength[idx] + (0.3f * fF), -1.0f, 1.0f);
                            if (holdStrength[idx] > 0.1f)
                                sectorStates[idx] = SectorControl.Friendly;
                        }
                        else
                        {
                            holdStrength[idx] = Math.Clamp(holdStrength[idx] + (0.2f * fF), -1.0f, 1.0f);
                            sectorStates[idx] = SectorControl.Friendly;
                        }
                    }
                    else if (hF > 0.05f && fF <= 0.05f)
                    {
                        // Only Hostile forces present: capture / solidify
                        if (sectorStates[idx] == SectorControl.Friendly)
                        {
                            holdStrength[idx] = Math.Clamp(holdStrength[idx] - (0.3f * hF), -1.0f, 1.0f);
                            if (holdStrength[idx] < -0.1f)
                                sectorStates[idx] = SectorControl.Hostile;
                        }
                        else
                        {
                            holdStrength[idx] = Math.Clamp(holdStrength[idx] - (0.2f * hF), -1.0f, 1.0f);
                            sectorStates[idx] = SectorControl.Hostile;
                        }
                    }
                    else
                    {
                        // No forces present in this sector
                        if (nodes.Count == 0)
                        {
                            sectorStates[idx] = SectorControl.Neutral;
                            holdStrength[idx] = 0f;
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // PHASE 4: FRONTLINE EDGE EXTRACTION & ATTACK THRUSTS
            // Crisp grid borders where Friendly meets Hostile or Contested.
            // -------------------------------------------------------------
            FriendlySectorCount = 0;
            HostileSectorCount = 0;
            ContestedSectorCount = 0;
            NeutralSectorCount = 0;

            Array.Clear(frontlineBorders, 0, frontlineBorders.Length);

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
                }
            }
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
        /// Runs in < 1ms on a 512x256 buffer with zero memory allocations.
        /// </summary>
        public Color32[] BakeTexture(
            int texWidth,
            int texHeight,
            bool showSectors,
            bool showFrontlines,
            float globalOpacity)
        {
            int totalPixels = texWidth * texHeight;
            if (pixelBuffer == null || pixelBuffer.Length != totalPixels || cachedTexWidth != texWidth || cachedTexHeight != texHeight)
            {
                pixelBuffer = new Color32[totalPixels];
                cachedTexWidth = texWidth;
                cachedTexHeight = texHeight;
            }

            Color32 clearColor = new Color32(0, 0, 0, 0);
            Array.Fill(pixelBuffer, clearColor);

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
                            if (x == pxMin || y == pyMin)
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

            // 2. Return rendered pixel buffer
            return pixelBuffer;
        }

        // Backwards compatibility overload
        public Color32[] BakeTexture(
            int texWidth,
            int texHeight,
            bool showSectors,
            bool showFrontlines,
            bool showThreatRings,
            float globalOpacity)
            => BakeTexture(texWidth, texHeight, showSectors, showFrontlines, globalOpacity);
    }
}

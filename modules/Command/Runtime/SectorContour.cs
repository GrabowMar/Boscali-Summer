using System;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// One interpolated stretch of the front between cells of opposing hold, in world
    /// coordinates. Threat points from the grid's own side toward the enemy side.
    /// </summary>
    internal struct SectorFrontSegment
    {
        /// <summary>The raw contour endpoints, for chaining stretches into ordered traces.</summary>
        public float AX, AZ, BX, BZ;
        public float ThreatX, ThreatZ;
        public float HalfLength;
        public float Pressure;
    }

    /// <summary>
    /// Extracts the control field's zero contour with marching squares. Cell-edge bitmasks
    /// could only ever express cardinal fronts on cell boundaries; this yields real
    /// positions, real normals and real lengths, so fortification traces and map symbols
    /// follow the fighting instead of the grid.
    /// </summary>
    internal static class SectorContour
    {
        public static int Extract(
            float[] holdStrength,
            float[] friendlyForce,
            float[] hostileForce,
            int resX,
            int resY,
            float cellSize,
            float originX,
            float originZ,
            SectorFrontSegment[] destination)
        {
            if (holdStrength == null || destination == null || destination.Length == 0 ||
                resX < 2 || resY < 2 || !(cellSize > 0f)) return 0;

            int written = 0;
            for (int qr = 0; qr < resY - 1; qr++)
            {
                for (int qc = 0; qc < resX - 1; qc++)
                {
                    float v00 = holdStrength[qr * resX + qc];
                    float v10 = holdStrength[qr * resX + qc + 1];
                    float v01 = holdStrength[(qr + 1) * resX + qc];
                    float v11 = holdStrength[(qr + 1) * resX + qc + 1];
                    int code = Bit(v00) | (Bit(v10) << 1) | (Bit(v11) << 2) | (Bit(v01) << 3);
                    if (code == 0 || code == 15) continue;

                    float x0 = originX + (qc + 0.5f) * cellSize;
                    float x1 = x0 + cellSize;
                    float z0 = originZ + (qr + 0.5f) * cellSize;
                    float z1 = z0 + cellSize;

                    float posX = 0f, posZ = 0f, negX = 0f, negZ = 0f;
                    int positives = 0, negatives = 0;
                    Accumulate(v00, x0, z0, ref posX, ref posZ, ref negX, ref negZ, ref positives, ref negatives);
                    Accumulate(v10, x1, z0, ref posX, ref posZ, ref negX, ref negZ, ref positives, ref negatives);
                    Accumulate(v11, x1, z1, ref posX, ref posZ, ref negX, ref negZ, ref positives, ref negatives);
                    Accumulate(v01, x0, z1, ref posX, ref posZ, ref negX, ref negZ, ref positives, ref negatives);
                    if (positives == 0 || negatives == 0) continue;

                    float threatX = negX / negatives - posX / positives;
                    float threatZ = negZ / negatives - posZ / positives;
                    if (!Normalize(ref threatX, ref threatZ)) continue;

                    float tx0 = 0f, tz0 = 0f, tx1 = 0f, tz1 = 0f, tx2 = 0f, tz2 = 0f, tx3 = 0f, tz3 = 0f;
                    if ((v00 > 0f) != (v10 > 0f)) { float t = v00 / (v00 - v10); tx0 = x0 + (x1 - x0) * t; tz0 = z0; }
                    if ((v10 > 0f) != (v11 > 0f)) { float t = v10 / (v10 - v11); tx1 = x1; tz1 = z0 + (z1 - z0) * t; }
                    if ((v01 > 0f) != (v11 > 0f)) { float t = v01 / (v01 - v11); tx2 = x0 + (x1 - x0) * t; tz2 = z1; }
                    if ((v00 > 0f) != (v01 > 0f)) { float t = v00 / (v00 - v01); tx3 = x0; tz3 = z0 + (z1 - z0) * t; }

                    // Saddle quads split on the centre's sign; the hold field's exponential
                    // response smooths true saddles out at cell resolution.
                    bool saddleAway = (v00 + v10 + v11 + v01) * 0.25f > 0f;
                    switch (code)
                    {
                        case 1: case 14:
                            Emit(tx3, tz3, tx0, tz0, threatX, threatZ, friendlyForce, hostileForce,
                                resX, resY, cellSize, originX, originZ, destination, ref written);
                            break;
                        case 2: case 13:
                            Emit(tx0, tz0, tx1, tz1, threatX, threatZ, friendlyForce, hostileForce,
                                resX, resY, cellSize, originX, originZ, destination, ref written);
                            break;
                        case 3: case 12:
                            Emit(tx3, tz3, tx1, tz1, threatX, threatZ, friendlyForce, hostileForce,
                                resX, resY, cellSize, originX, originZ, destination, ref written);
                            break;
                        case 4: case 11:
                            Emit(tx1, tz1, tx2, tz2, threatX, threatZ, friendlyForce, hostileForce,
                                resX, resY, cellSize, originX, originZ, destination, ref written);
                            break;
                        case 6: case 9:
                            Emit(tx0, tz0, tx2, tz2, threatX, threatZ, friendlyForce, hostileForce,
                                resX, resY, cellSize, originX, originZ, destination, ref written);
                            break;
                        case 7: case 8:
                            Emit(tx3, tz3, tx2, tz2, threatX, threatZ, friendlyForce, hostileForce,
                                resX, resY, cellSize, originX, originZ, destination, ref written);
                            break;
                        case 5:
                            if (saddleAway) { EmitPair(tx0, tz0, tx1, tz1, tx2, tz2, tx3, tz3, threatX, threatZ, friendlyForce, hostileForce, resX, resY, cellSize, originX, originZ, destination, ref written); }
                            else { EmitPair(tx3, tz3, tx0, tz0, tx1, tz1, tx2, tz2, threatX, threatZ, friendlyForce, hostileForce, resX, resY, cellSize, originX, originZ, destination, ref written); }
                            break;
                        case 10:
                            if (saddleAway) { EmitPair(tx3, tz3, tx0, tz0, tx1, tz1, tx2, tz2, threatX, threatZ, friendlyForce, hostileForce, resX, resY, cellSize, originX, originZ, destination, ref written); }
                            else { EmitPair(tx0, tz0, tx1, tz1, tx2, tz2, tx3, tz3, threatX, threatZ, friendlyForce, hostileForce, resX, resY, cellSize, originX, originZ, destination, ref written); }
                            break;
                    }

                    if (written >= destination.Length) return written;
                }
            }

            return written;
        }

        private static void EmitPair(
            float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz,
            float threatX, float threatZ,
            float[] friendlyForce, float[] hostileForce,
            int resX, int resY, float cellSize, float originX, float originZ,
            SectorFrontSegment[] destination, ref int written)
        {
            Emit(ax, az, bx, bz, threatX, threatZ, friendlyForce, hostileForce,
                resX, resY, cellSize, originX, originZ, destination, ref written);
            Emit(cx, cz, dx, dz, threatX, threatZ, friendlyForce, hostileForce,
                resX, resY, cellSize, originX, originZ, destination, ref written);
        }

        private static void Emit(
            float ax, float az, float bx, float bz, float threatX, float threatZ,
            float[] friendlyForce, float[] hostileForce,
            int resX, int resY, float cellSize, float originX, float originZ,
            SectorFrontSegment[] destination, ref int written)
        {
            if (written >= destination.Length) return;

            float length = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            if (length < 1f) return;

            float mx = (ax + bx) * 0.5f, mz = (az + bz) * 0.5f;
            int ownCol = Cell(mx - threatX * cellSize * 0.5f, originX, cellSize, resX);
            int ownRow = Cell(mz - threatZ * cellSize * 0.5f, originZ, cellSize, resY);
            int otherCol = Cell(mx + threatX * cellSize * 0.5f, originX, cellSize, resX);
            int otherRow = Cell(mz + threatZ * cellSize * 0.5f, originZ, cellSize, resY);
            int own = ownRow * resX + ownCol, other = otherRow * resX + otherCol;

            float friendly = friendlyForce[own] + friendlyForce[other];
            float hostile = hostileForce[own] + hostileForce[other];
            float pressure = friendly > 0.05f && hostile > 0.05f
                ? 2f * Math.Min(friendly, hostile) / (friendly + hostile)
                : 0f;

            destination[written++] = new SectorFrontSegment
            {
                AX = ax,
                AZ = az,
                BX = bx,
                BZ = bz,
                ThreatX = threatX,
                ThreatZ = threatZ,
                HalfLength = length * 0.5f,
                Pressure = pressure
            };
        }

        private static void Accumulate(float value, float x, float z,
            ref float posX, ref float posZ, ref float negX, ref float negZ,
            ref int positives, ref int negatives)
        {
            if (value > 0f) { posX += x; posZ += z; positives++; }
            else if (value < 0f) { negX += x; negZ += z; negatives++; }
        }

        private static int Bit(float value) => value > 0f ? 1 : 0;

        private static int Cell(float world, float origin, float cellSize, int resolution)
            => Math.Clamp((int)Math.Floor((world - origin) / cellSize), 0, resolution - 1);

        private static bool Normalize(ref float x, ref float z)
        {
            float magnitude = (float)Math.Sqrt(x * x + z * z);
            if (magnitude < 1e-4f) return false;
            x /= magnitude;
            z /= magnitude;
            return true;
        }
    }
}

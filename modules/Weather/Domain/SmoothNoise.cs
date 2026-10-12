using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// A smooth two-channel value noise computed from an integer hash, with no texture: the warps of the look pass need a field whose slope is
    /// bounded, and the shipped noise volume carries octaves down to two texels whose slope folded every warped coordinate (the cell bend folded
    /// 38 % of the plane, the lobe warp 32 %: the concentric arcs of cirrocumulus and the straight-edged facets of the low cumulus).
    /// A cell of the lattice has corner values in [0,1] and a quintic fade, so the slope never exceeds 1.875 per cell times the corner spread.
    /// FlightCloud.shader's SmoothNoise2 is the twin (an integer hash, so the two agree to float rounding). Change both together.
    /// </summary>
    internal static class SmoothNoise
    {
        public const uint SaltBend = 0x1B3Du, SaltCurl = 0x2C57u, SaltMeander = 0x3E91u, SaltLobeA = 0x4A05u, SaltLobeB = 0x5D73u;
        /// <summary>Look pass round 5: the three cell octaves and their size field, the two streak octaves with the width field and the segment mask, the ripple wavelength field.</summary>
        public const uint SaltCellA = 0x6A11u, SaltCellB = 0x6A22u, SaltCellC = 0x6A33u, SaltCellSize = 0x6A44u,
            SaltStreakWidth = 0x7A10u, SaltStreakA = 0x7A21u, SaltStreakB = 0x7A32u, SaltStreakSeg = 0x7A43u, SaltRows = 0x8B15u, SaltMeso = 0x9D41u;

        /// <summary>The hash of one lattice corner: a murmur3-style finaliser over the corner and the salt.</summary>
        public static uint Hash(int x, int z, uint salt)
        {
            unchecked
            {
                uint h = (uint)x * 0x27D4EB2Fu + (uint)z * 0x165667B1u + salt * 0x9E3779B1u;
                h ^= h >> 15; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Both channels (each in [0,1], mean 0.5) of the noise at lattice coordinates (u, v).</summary>
        public static void Vec2(float u, float v, uint salt, out float a, out float b)
        {
            float fu = (float)Math.Floor(u), fv = (float)Math.Floor(v);
            int iu = (int)fu, iv = (int)fv;
            float tu = Fade(u - fu), tv = Fade(v - fv);
            uint h00 = Hash(iu, iv, salt), h10 = Hash(iu + 1, iv, salt), h01 = Hash(iu, iv + 1, salt), h11 = Hash(iu + 1, iv + 1, salt);
            const float k = 1f / 65535f;
            float a0 = (h00 & 0xFFFFu) * k, a1 = (h10 & 0xFFFFu) * k, a2 = (h01 & 0xFFFFu) * k, a3 = (h11 & 0xFFFFu) * k;
            float b0 = (h00 >> 16) * k, b1 = (h10 >> 16) * k, b2 = (h01 >> 16) * k, b3 = (h11 >> 16) * k;
            a = (a0 + (a1 - a0) * tu) + ((a2 + (a3 - a2) * tu) - (a0 + (a1 - a0) * tu)) * tv;
            b = (b0 + (b1 - b0) * tu) + ((b2 + (b3 - b2) * tu) - (b0 + (b1 - b0) * tu)) * tv;
        }
    }
}

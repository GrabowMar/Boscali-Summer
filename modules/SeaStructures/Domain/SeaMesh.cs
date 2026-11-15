using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Domain
{
    /// <summary>
    /// The look of one section at a given health. Loose pieces fall away in a fixed order as hit points drop, so every
    /// peer draws the same wreck from the hit points the game already replicates: nothing extra goes over the network.
    /// </summary>
    internal static class SeaMesh
    {
        internal sealed class Data
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(), Normals = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        /// <summary>The hit points (of 100) below which a piece is gone; core pieces never go on their own.</summary>
        public static float BreakAt(SeaPiece piece, int seed, int index) =>
            piece.Core ? float.NegativeInfinity : 12f + 82f * Roll(seed, index);

        public static bool Stands(SeaPiece piece, int seed, int index, float hitPoints) => hitPoints >= BreakAt(piece, seed, index);

        /// <summary>How many pieces have fallen at this health; the mesh only changes when this does.</summary>
        public static int Fallen(SeaSection section, int seed, float hitPoints)
        {
            int n = 0;
            for (int i = 0; i < section.Pieces.Length; i++)
                if (!Stands(section.Pieces[i], seed, i, hitPoints)) n++;
            return n;
        }

        public static Data Build(SeaSection section, int seed, float hitPoints)
        {
            var data = new Data();
            for (int i = 0; i < section.Pieces.Length; i++)
                if (Stands(section.Pieces[i], seed, i, hitPoints)) Add(data, section.Pieces[i], Vector3.zero);
            return data;
        }

        /// <summary>One piece on its own, about its centre: the chunk that falls.</summary>
        public static Data Chunk(SeaPiece piece)
        {
            var data = new Data();
            Add(data, piece, -piece.Centre);
            return data;
        }

        private static void Add(Data data, SeaPiece piece, Vector3 offset)
        {
            int at = data.Vertices.Count;
            for (int v = 0; v < piece.Vertices.Length; v++) data.Vertices.Add(piece.Vertices[v] + offset);
            data.Normals.AddRange(piece.Normals);
            data.Uv.AddRange(piece.Uv);
            for (int t = 0; t < piece.Triangles.Length; t++) data.Triangles.Add(at + piece.Triangles[t]);
        }

        /// <summary>A plain box in one paint, for a donor part that has no model of its own (a magazine, a launcher cell block).</summary>
        public static Data Box(Vector3 c, Vector3 size, Vector2 uv)
        {
            var d = new Data();
            Vector3 x = Vector3.right * (size.x * 0.5f), y = Vector3.up * (size.y * 0.5f), z = Vector3.forward * (size.z * 0.5f);
            Face(d, uv, c, x, y, z);
            Face(d, uv, c, -x, y, -z);
            Face(d, uv, c, y, z, x);
            Face(d, uv, c, -y, z, -x);
            Face(d, uv, c, z, y, -x);
            Face(d, uv, c, -z, y, x);
            return d;
        }

        /// <summary>One quad at centre + n, spanned by u and v, facing along n (n = u x v), wound clockwise from outside.</summary>
        private static void Face(Data d, Vector2 uv, Vector3 centre, Vector3 n, Vector3 u, Vector3 v)
        {
            int at = d.Vertices.Count;
            Vector3 o = centre + n, normal = n.normalized;
            d.Vertices.Add(o - u - v); d.Vertices.Add(o + u - v); d.Vertices.Add(o + u + v); d.Vertices.Add(o - u + v);
            for (int i = 0; i < 4; i++) { d.Normals.Add(normal); d.Uv.Add(uv); }
            d.Triangles.Add(at); d.Triangles.Add(at + 1); d.Triangles.Add(at + 2);
            d.Triangles.Add(at); d.Triangles.Add(at + 2); d.Triangles.Add(at + 3);
        }

        private static float Roll(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)(index + 1) * 2246822519u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}

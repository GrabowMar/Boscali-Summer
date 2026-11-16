using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Domain
{
    internal enum SeaKind { OilRig = 0, SeaFort = 1 }
    internal enum MountKind { Gun = 0, Missile = 1, Store = 2 }

    /// <summary>One piece of a section: what falls off together. Geometry is relative to the section's origin; UVs
    /// address the model atlas in version 2, or the paint/shade ramp in version 1.</summary>
    internal sealed class SeaPiece
    {
        public bool Core; // never falls: goes only when the section does
        public Vector3 Centre, Size;
        public Vector3[] Vertices, Normals;
        public Vector2[] Uv;
        public int[] Triangles;
    }

    /// <summary>A hit box, relative to the section's origin.</summary>
    internal readonly struct SeaBox
    {
        public readonly Vector3 Centre, Size;
        public SeaBox(Vector3 centre, Vector3 size) { Centre = centre; Size = size; }
    }

    /// <summary>A part of the structure that takes damage and is destroyed on its own. <see cref="At"/> is relative to the
    /// structure's origin (sea level, centre); a section stands on <see cref="Parent"/> and goes with it.</summary>
    internal sealed class SeaSection
    {
        public string Name;
        public int Parent = -1;
        public Vector3 At;
        public bool Critical; // gets the donor's critical parts
        public bool Sensor;   // high and clear: gets the donor's radar and lookouts
        public bool Pad;      // gets the donor's hangar part
        public SeaBox[] Boxes;
        public SeaPiece[] Pieces;
    }

    /// <summary>A hardpoint: where a donor turret (or magazine) stands, relative to the structure's origin.</summary>
    internal readonly struct SeaMount
    {
        public readonly int Section;
        public readonly Vector3 At;
        public readonly float Yaw;
        public readonly MountKind Kind;

        public SeaMount(int section, Vector3 at, float yaw, MountKind kind)
        {
            Section = section; At = at; Yaw = yaw; Kind = kind;
        }
    }

    /// <summary>
    /// A structure's model, read from a .seamodel file written by modules/SeaStructures/Assets/Source/sealib.py (the
    /// format is described there, in <c>_write</c>): palette, sections with hit boxes and pieces, hardpoints.
    /// </summary>
    internal sealed class SeaLayout
    {
        public int Version = 1;
        public SeaKind Kind;
        public string Key, UnitName;
        public byte[] Palette; // rgb triples
        public SeaSection[] Sections; // [0] is the root: it stands until the rest is gone
        public SeaMount[] Mounts;

        public int Paints => Palette.Length / 3;

        // Version 2 reserves the bottom 2% of every atlas for small runtime-generated fittings.
        public Vector2 PaintUv(int paint, float shade) => new Vector2((paint + 0.5f) / Paints,
            Version >= 2 ? 0.004f + 0.012f * Mathf.Clamp01(shade) : shade);

        public static SeaLayout Read(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadByte() != 'B' || r.ReadByte() != 'S' || r.ReadByte() != 'S' || r.ReadByte() != 'M')
                    throw new InvalidDataException("not a .seamodel");
                byte version = r.ReadByte();
                if (version != 1 && version != 2) throw new InvalidDataException("unsupported .seamodel version " + version);
                var layout = new SeaLayout { Version = version, Kind = (SeaKind)r.ReadByte(), Key = Text(r), UnitName = Text(r) };
                layout.Palette = r.ReadBytes(r.ReadByte() * 3);
                float paints = layout.Paints;
                layout.Sections = new SeaSection[r.ReadByte()];
                for (int s = 0; s < layout.Sections.Length; s++)
                {
                    var section = new SeaSection { Name = Text(r), Parent = r.ReadSByte(), At = Vec(r) };
                    byte flags = r.ReadByte();
                    section.Critical = (flags & 1) != 0;
                    section.Sensor = (flags & 2) != 0;
                    section.Pad = (flags & 4) != 0;
                    section.Boxes = new SeaBox[r.ReadByte()];
                    for (int b = 0; b < section.Boxes.Length; b++) section.Boxes[b] = new SeaBox(Vec(r), Vec(r));
                    section.Pieces = new SeaPiece[r.ReadUInt16()];
                    for (int p = 0; p < section.Pieces.Length; p++)
                    {
                        var piece = new SeaPiece { Core = r.ReadByte() != 0, Centre = Vec(r), Size = Vec(r) };
                        int count = (int)r.ReadUInt32();
                        piece.Vertices = new Vector3[count];
                        piece.Normals = new Vector3[count];
                        piece.Uv = new Vector2[count];
                        for (int v = 0; v < count; v++)
                        {
                            piece.Vertices[v] = Vec(r);
                            piece.Normals[v] = new Vector3(r.ReadSByte() / 127f, r.ReadSByte() / 127f, r.ReadSByte() / 127f);
                            byte paint = r.ReadByte(), shade = r.ReadByte();
                            piece.Uv[v] = version == 2 ? new Vector2(r.ReadSingle(), r.ReadSingle()) :
                                new Vector2((paint + 0.5f) / paints, shade / 255f);
                        }
                        piece.Triangles = new int[r.ReadUInt32()];
                        for (int t = 0; t < piece.Triangles.Length; t++) piece.Triangles[t] = count < 65536 ? r.ReadUInt16() : (int)r.ReadUInt32();
                        section.Pieces[p] = piece;
                    }
                    layout.Sections[s] = section;
                }
                layout.Mounts = new SeaMount[r.ReadByte()];
                for (int m = 0; m < layout.Mounts.Length; m++)
                    layout.Mounts[m] = new SeaMount(r.ReadByte(), Vec(r), r.ReadSingle(), (MountKind)r.ReadByte());
                return layout;
            }
        }

        private static string Text(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(r.ReadByte()));
        private static Vector3 Vec(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    internal static class SeaLayouts
    {
        public const string RigFile = "BS_OilRig.seamodel", FortFile = "BS_SeaFort.seamodel";

        /// <summary>Where the model bytes come from: the mod's embedded resources in game, the files on disk in the tests.</summary>
        public static Func<string, byte[]> Source;

        private static readonly SeaLayout[] Loaded = new SeaLayout[2];

        /// <summary>The model of a kind; null (and logged by the caller) when its file is missing.</summary>
        public static SeaLayout Of(SeaKind kind)
        {
            if (Loaded[(int)kind] != null) return Loaded[(int)kind];
            byte[] bytes = Source?.Invoke(kind == SeaKind.OilRig ? RigFile : FortFile);
            return bytes == null ? null : Loaded[(int)kind] = SeaLayout.Read(bytes);
        }
    }
}

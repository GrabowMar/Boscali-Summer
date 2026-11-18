using System;
using Newtonsoft.Json;

namespace BoscaliSummer.Modules.Maps.Domain
{
    /// <summary>The <c>kit</c> text of <c>mapkit.bskit</c> (contract 3.3): which scenery kinds the kit holds. The prototype
    /// of a kind's variant is the bundle prefab's child <c>kit_&lt;name&gt;_&lt;variant&gt;</c>.</summary>
#pragma warning disable 0649 // every field is filled from json
    [Serializable]
    internal sealed class KitManifest
    {
        public int version;
        public Kind[] kinds = new Kind[0];

        [Serializable]
        internal sealed class Kind
        {
            public string name = "";
            public int variants, triangles;
            /// <summary>A solid kind is placed one object at a time with its collider; the others merge into scenery meshes.</summary>
            public bool solid;
            /// <summary>Draw range class: "s" to 3.5 km, "m" to 12 km, "l" always; "" for a tree-species mesh.</summary>
            public string draw = "";
            /// <summary>The mesh's bounding radius at scale 1.</summary>
            public float radius;
        }

        public static KitManifest Parse(string json) =>
            JsonConvert.DeserializeObject<KitManifest>((json ?? "").TrimStart('﻿'), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

        public Kind Find(string name)
        {
            foreach (Kind kind in kinds ?? new Kind[0])
                if (kind != null && string.Equals(kind.name, name, StringComparison.Ordinal)) return kind;
            return null;
        }
    }
}

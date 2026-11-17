using System;

namespace BoscaliSummer.Modules.Maps.Domain
{
    /// <summary>The <c>map.json</c> that <c>nomod map gen</c> writes and the bake puts in the bundle as TextAsset <c>map</c>.
    /// Field names are the json keys. Plain data, so the unit tier can check it without Unity.</summary>
#pragma warning disable 0649 // every field is filled from json
    [Serializable]
    internal sealed class MapManifest
    {
        public const float SupportedSize = 81920f;

        public string id, displayName, prefabName, workshopTag, hash;
        public float sizeX, sizeY, latitude;
        public int gridX, gridY, offsetX, offsetY;
        public Pose camera = new Pose();
        public Strip[] strips = new Strip[0];
        public Lane[] lanes = new Lane[0];

        [Serializable] internal sealed class Pose { public float x, y, z, pitch, yaw, roll; }
        [Serializable] internal sealed class Strip { public float x, y, z, length; public int heading; }
        /// <summary>One sea lane: x0, z0, x1, z1, ... in map metres.</summary>
        [Serializable] internal sealed class Lane { public float[] points = new float[0]; }

        /// <summary>What stops this map from loading, or null. The game's battlefield grid, map screen and blast buffers
        /// are built for the vanilla size, so only that size is accepted.</summary>
        public string Problem()
        {
            if (string.IsNullOrEmpty(id)) return "id is missing";
            if (string.IsNullOrEmpty(prefabName)) return "prefabName is missing";
            if (string.IsNullOrEmpty(displayName)) return "displayName is missing";
            if (Math.Abs(sizeX - SupportedSize) > 1f || Math.Abs(sizeY - SupportedSize) > 1f)
                return "size is " + sizeX + " x " + sizeY + "; only " + SupportedSize + " x " + SupportedSize + " is supported";
            if (gridX <= 0 || gridY <= 0 || !GridFits(offsetX, sizeX, gridX) || !GridFits(offsetY, sizeY, gridY))
                return "the grid (" + gridX + " x " + gridY + ", offset " + offsetX + ", " + offsetY + ") does not cover the map";
            foreach (Lane lane in lanes ?? new Lane[0])
                if (lane == null || lane.points == null || lane.points.Length < 4 || lane.points.Length % 2 != 0)
                    return "a sea lane has fewer than two points";
            return null;
        }

        /// <summary>Grid cells are 10 km; the map, centred on the offset, has to lie inside the grid.</summary>
        private static bool GridFits(int offset, float size, int cells) => offset >= size / 2f && offset + size / 2f <= cells * 10000f;
    }
}

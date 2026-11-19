using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BoscaliSummer.Core.Contracts;
using Newtonsoft.Json;

namespace BoscaliSummer.Modules.Maps.Domain
{
    /// <summary>The <c>map.json</c> that <c>nomod map gen</c> writes (map builder v2, contract 3.2), read from the sidecar
    /// <c>&lt;id&gt;.map.json</c> the bake leaves next to the bundle: the same bytes plus <c>bundleHash</c> and
    /// <c>bundleBytes</c>. Field names are the json keys. Plain data, so a stand-alone test can check it without Unity.</summary>
#pragma warning disable 0649 // every field is filled from json
    [Serializable]
    internal sealed class MapManifest
    {
        public const float SupportedSize = 81920f;
        private static readonly string[] Biomes = { "tropical", "desert", "alpine", "arctic" };

        public string id, displayName, prefabName, workshopTag, hash, generator, biome, description;
        /// <summary>First 8 hex digits (lower case) of the bundle's SHA-256, and its length; spliced in by the bake.</summary>
        public string bundleHash = "";
        public long bundleBytes, seed;
        public float sizeX, sizeY, latitude, cell, heightFloor, heightSpan, detailScale = 1f;
        public int gridX, gridY, offsetX, offsetY, res, tile;
        public Pose camera = new Pose();
        public float[] tagColor;
        public Breakup breakup = new Breakup();
        /// <summary>Null when the file has none: Weather then treats the map like a vanilla one.</summary>
        public ClimateProfile climate;
        /// <summary>Null when the file has none: the map's mission keeps the default mission's weather.</summary>
        public MissionWeather mission;
        /// <summary>Ground skin per slot (sand, dunes, grass, grassDetail, rock, lush, fields): textures skin_&lt;name&gt;_b/_n.</summary>
        public Dictionary<string, string> skins = new Dictionary<string, string>();
        public Grass grass = new Grass();
        public Forest[] forests = new Forest[0];
        /// <summary>The kit kind name of each <c>scenery.bin</c> kind byte.</summary>
        public string[] kinds = new string[0];
        public Strip[] strips = new Strip[0];
        /// <summary>The game's own buildings and scenery objects the map's mission stands (airfield buildings, depots,
        /// factories, defences): the generator places them, <c>MapMissions</c> writes them into the mission.</summary>
        public Structure[] structures = new Structure[0];
        public Zone[] detail = new Zone[0];
        public Lake[] lakes = new Lake[0];
        public Lane[] lanes = new Lane[0];
        public Way[] roads = new Way[0];
        public View[] views = new View[0];
        public Stats stats = new Stats();

        [Serializable] internal sealed class Pose { public float x, y, z, pitch, yaw, roll; }
        /// <summary>Terrain shader breakup floats; -1 keeps the game material's value.</summary>
        [Serializable] internal sealed class Breakup { public float terrain = -1f, normal = -1f, splat = -1f; }
        [Serializable] internal sealed class MissionWeather { public float conditions, cloudBaseM = 1800f, windScale = 1f; }
        [Serializable] internal sealed class Grass { public float density = 1f; public float[][] tints; }
        [Serializable]
        internal sealed class Forest
        {
            public string name = "", mesh = "", texture = "";
            public float[] tint, lushColor, altitudeColor;
            public float scaleMin = 1f, scaleMax = 1f, altitudeMin, altitudeMax = 1000f;
            public int count;
        }
        [Serializable]
        internal sealed class Strip
        {
            public string name = "", role = "", surface = "", side = "";
            public float x, y, z, length, width = 40f, padLength, padWidth;
            public int heading;
            /// <summary>The AI's taxi routes of the airfield, made by the generator: flat x, y, z, x, y, z — each pair
            /// one straight road, joining the others at shared end points. The bake paves them and MapBuilder gives
            /// them to the game's <see cref="NuclearOption.Airbase"/> as its taxi network.</summary>
            public float[] taxi = new float[0];
            /// <summary>Runway exits, for what lands: flat x, y, z, heading, four of them.</summary>
            public float[] exits = new float[0];
            /// <summary>Where aircraft are serviced: flat x, y, z.</summary>
            public float[] service = new float[0];
            /// <summary>The vertical-landing pad: x, y, z and the heading it faces.</summary>
            public float[] pad = new float[0];
        }
        /// <summary>One of the game's buildings (or, <c>scenery</c>, scenery objects) by its mission key, at map metres,
        /// facing compass <c>yaw</c>; <c>side</c> red | blue | ""; <c>airbase</c> the strip it belongs to, or -1.</summary>
        [Serializable] internal sealed class Structure { public string type = "", side = ""; public float x, y, z, yaw; public int airbase = -1; public bool scenery; }
        [Serializable] internal sealed class Zone { public float x, z, radius; }
        [Serializable] internal sealed class Lake { public float x, z, level, area; public float[] color; }
        /// <summary>One sea lane: x0, z0, x1, z1, ... in map metres.</summary>
        [Serializable] internal sealed class Lane { public float[] points = new float[0]; }
        /// <summary>One road: its kind (paved, dirt) and x0, y0, z0, x1, ... in map metres. Roads meet at shared points.</summary>
        [Serializable] internal sealed class Way { public string kind = ""; public float[] points = new float[0]; }
        [Serializable] internal sealed class View { public string name = ""; public float x, y, z, heading, pitch; }
        [Serializable]
        internal sealed class Stats
        {
            public long worldgenSeed;
            public float landPercent, shallowPercent, fastIcePercent, peak, gulfKm;
            public int islands, islets, forestTrees, scenery, sceneryTriangles, solid, lakes;
        }

        /// <summary>Reads a map.json or sidecar. A json null leaves the field's default, so "tints": null and a missing
        /// key read the same. Throws <see cref="JsonException"/> on text that is not this file.</summary>
        public static MapManifest Parse(string json)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, DateParseHandling = DateParseHandling.None };
            settings.Converters.Add(new WholeNumbers());
            return JsonConvert.DeserializeObject<MapManifest>((json ?? "").TrimStart('﻿'), settings);
        }

        /// <summary>A whole-number field written as 90.0 (python floats) reads as 90; Json.NET alone fails the whole file on it.</summary>
        private sealed class WholeNumbers : JsonConverter
        {
            public override bool CanWrite => false;
            public override bool CanConvert(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(byte);

            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return existing;
                if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
                    throw new JsonSerializationException("a number was expected at " + reader.Path + ", not " + reader.TokenType);
                try
                {
                    object value = reader.TokenType == JsonToken.Integer ? reader.Value
                        : Math.Round(Convert.ToDouble(reader.Value, System.Globalization.CultureInfo.InvariantCulture));
                    return Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception error) when (error is OverflowException || error is InvalidCastException || error is FormatException)
                {
                    throw new JsonSerializationException("the number at " + reader.Path + " does not fit: " + error.Message);
                }
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
        }

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
            if (Array.IndexOf(Biomes, biome) < 0) return "biome '" + biome + "' is not tropical, desert, alpine or arctic";
            foreach (string kind in kinds ?? new string[0])
                if (string.IsNullOrEmpty(kind)) return "kinds has an empty entry";
            if (strips == null || strips.Length < 2) return "it has fewer than 2 strips";
            for (int n = 0; n < strips.Length; n++)
                if (strips[n] == null || string.IsNullOrEmpty(strips[n].surface)) return "strip " + n + " has no surface";
            foreach (Lane lane in lanes ?? new Lane[0])
                if (lane == null || lane.points == null || lane.points.Length < 4 || lane.points.Length % 2 != 0)
                    return "a sea lane has fewer than two points";
            return null;
        }

        /// <summary>First 8 hex digits, lower case, of the SHA-256 of a bundle: the build id peers compare at the handshake.</summary>
        public static string HashOf(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes ?? new byte[0]), 0, 4).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Why these bytes are not the bundle this sidecar was written for, or null when they are.</summary>
        public string BundleProblem(byte[] bytes)
        {
            if (string.IsNullOrEmpty(bundleHash)) return "the sidecar has no bundleHash (it was not written by the bake)";
            if (bytes == null || bytes.Length == 0) return "the bundle is missing or empty";
            if (bundleBytes > 0 && bytes.Length != bundleBytes) return "the bundle is " + bytes.Length + " bytes, the sidecar says " + bundleBytes;
            string actual = HashOf(bytes);
            return string.Equals(actual, bundleHash, StringComparison.Ordinal) ? null : "the bundle is build " + actual + ", the sidecar says " + bundleHash;
        }

        /// <summary>Grid cells are 10 km; the map, centred on the offset, has to lie inside the grid.</summary>
        private static bool GridFits(int offset, float size, int cells) => offset >= size / 2f && offset + size / 2f <= cells * 10000f;
    }
}

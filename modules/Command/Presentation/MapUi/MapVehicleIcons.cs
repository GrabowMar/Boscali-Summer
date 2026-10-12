using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Plan-view map icons for ground vehicles, one per vehicle model.
    ///
    /// <para>The game draws each aircraft and ship with its own top-down silhouette and every ground
    /// vehicle with one capsule. This is the missing half, made the way the game made its own: each
    /// vehicle's real model seen from above, nose up, shaded by height so a turret, gun, dish or
    /// launcher shows on the hull. The sheet is baked offline by <c>tools/Build-VehicleMapIcons.py</c>
    /// and embedded; cells are grey on clear, so the map's side tint carries through.</para>
    ///
    /// <para>One texture, loaded on first use; a sprite per vehicle is cut on first request. A vehicle
    /// the sheet does not know (a newer game build, another mod) gets null and keeps what it had.</para>
    /// </summary>
    internal static partial class MapVehicleIcons
    {
        private const string Resource = "BoscaliSummer.Command.vehicle-map-icons.png";

        private static readonly Dictionary<string, Sprite> sprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static Texture2D sheet;
        private static bool loaded;

        /// <summary>The icon baked for a vehicle prefab name; null when the sheet has none.</summary>
        internal static Sprite For(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            if (!loaded) Load();
            if (sheet == null) return null;
            if (sprites.TryGetValue(prefab, out Sprite sprite)) return sprite;
            int index = Array.FindIndex(Names, name => string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                // Sheet rows run top to bottom; texture rows run bottom to top.
                var cell = new Rect(index % Columns * Cell, sheet.height - (index / Columns + 1) * Cell, Cell, Cell);
                sprite = Sprite.Create(sheet, cell, new Vector2(.5f, .5f), Cell, 0, SpriteMeshType.FullRect);
                sprite.name = "NOAvionics.VehicleIcon." + Names[index];
                sprite.hideFlags = HideFlags.HideAndDontSave;
            }
            sprites[prefab] = sprite;
            return sprite;
        }

        private static void Load()
        {
            loaded = true;
            byte[] bytes = EmbeddedResources.ReadAll(typeof(MapVehicleIcons).Assembly, Resource, 4 * 1024 * 1024);
            if (bytes == null) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "NOAvionics.VehicleIcons",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                // Icons draw at a third of a cell: lean toward the sharper mip level.
                mipMapBias = -.6f,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (texture.LoadImage(bytes, true) && texture.width >= Columns * Cell) sheet = texture;
            else UnityEngine.Object.Destroy(texture);
        }
    }
}

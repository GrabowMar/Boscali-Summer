using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BoscaliSummer.Core.Util;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    internal static class MegaplaneStatusDisplay
    {
        private const int Tile = 256;
        private const string Prefix = "BoscaliSummer.Megaplane.";

        internal static GameObject Build(GameObject donor, Transform holder)
        {
            var assembly = typeof(MegaplaneStatusDisplay).Assembly;
            byte[] atlas = EmbeddedResources.ReadAll(assembly, Prefix + "BS_Megaplane_Status.png", 4 * 1024 * 1024);
            byte[] manifest = EmbeddedResources.ReadAll(assembly, Prefix + "megaplane-status.txt", 4096);
            if (atlas == null || manifest == null) return donor;
            string[] names = Encoding.UTF8.GetString(manifest).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "Groza damage schematic", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            if (!ImageConversion.LoadImage(texture, atlas, true) || names.Length == 0 || names[0] != "background" ||
                texture.width != 8 * Tile || texture.height < (names.Length + 7) / 8 * Tile)
                throw new System.IO.InvalidDataException("Invalid Groza damage schematic.");

            var template = Object.Instantiate(donor, holder);
            template.name = "StatusDisplay_KY44";
            var status = Traverse.Create(template.GetComponent<StatusDisplay>());
            Image backdrop = status.Field<Image>("aircraftBackground").Value;
            var parts = status.Field<List<PartStatusDisplay>>("statusDisplays").Value;
            var thresholds = parts.GroupBy(p => p.partImage.name).ToDictionary(g => g.Key, g => g.First().redStatusThreshold);
            foreach (PartStatusDisplay part in parts) part.partImage.enabled = false;
            parts.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                var rect = new Rect(i % 8 * Tile, texture.height - (i / 8 + 1) * Tile, Tile, Tile);
                Sprite sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = "Groza_" + names[i];
                if (i == 0)
                {
                    backdrop.sprite = sprite;
                    backdrop.type = Image.Type.Simple;
                    backdrop.preserveAspect = true;
                    continue;
                }
                var partImage = new GameObject(names[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                partImage.transform.SetParent(backdrop.transform, false);
                partImage.rectTransform.anchorMin = Vector2.zero;
                partImage.rectTransform.anchorMax = Vector2.one;
                partImage.rectTransform.offsetMin = partImage.rectTransform.offsetMax = Vector2.zero;
                partImage.raycastTarget = false;
                partImage.sprite = sprite;
                partImage.preserveAspect = true;
                partImage.color = new Color(1f, 1f, 0f, 0f);
                thresholds.TryGetValue(names[i], out float threshold);
                parts.Add(new PartStatusDisplay { partImage = partImage, redStatusThreshold = threshold });
            }
            return template;
        }
    }
}

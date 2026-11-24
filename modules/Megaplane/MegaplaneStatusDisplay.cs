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
            thresholds["reactor"] = 35f;
            thresholds["coolant_L"] = thresholds["coolant_R"] = 50f;
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

    // Stock HUD tiles subscribe on boarding and only repaint after the next hit.
    // Show damage already present when a pilot takes this flight deck.
    [HarmonyPatch(typeof(StatusDisplay), nameof(StatusDisplay.Initialize))]
    internal static class MegaplaneStatusInitializePatch
    {
        private static readonly AccessTools.FieldRef<StatusDisplay, List<PartStatusDisplay>> Tiles =
            AccessTools.FieldRefAccess<StatusDisplay, List<PartStatusDisplay>>("statusDisplays");

        private static void Postfix(StatusDisplay __instance, Aircraft aircraft)
        {
            if (!MegaplaneModule.Is(aircraft)) return;
            bool damaged = false;
            foreach (PartStatusDisplay tile in Tiles(__instance))
            {
                UnitPart part = aircraft.partLookup.Find(p => p != null && p.name == tile.partImage.name);
                if (part == null || !part.IsDetached() && part.hitPoints >= 100f) continue;
                if (part.IsDetached())
                {
                    tile.DamageUnsubscribe();
                    tile.displayCondition = 0f;
                    tile.partImage.color = new Color(.7f, 0f, .25f, 1f);
                }
                else
                {
                    tile.displayCondition = Mathf.InverseLerp(tile.redStatusThreshold, 100f, part.hitPoints);
                    tile.partImage.color = new Color(1f, Mathf.Min(1f, tile.displayCondition * 2f), 0f, 1f - tile.displayCondition);
                }
                damaged = true;
            }
            if (damaged) __instance.DisplayDamage();
        }
    }
}

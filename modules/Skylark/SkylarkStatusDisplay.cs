using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Skylark
{
    // Keeps native damage events, timing, warnings and audio; replaces only donor diagram artwork.
    internal static class SkylarkStatusDisplay
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        internal static int BoundParts { get; private set; }

        internal static void Refit(Aircraft aircraft, AircraftParameters parameters, Dictionary<string, Transform> nodes)
        {
            Sprite outline = LoadSprite("BS_Skylark_Status", true);
            // Two retained donor systems have no separate visible Skylark damage island.
            UnitPart[] parts = aircraft.GetComponentsInChildren<UnitPart>(true)
                .Where(part => part.name != "elevator_R" && part.name != "nozzle_main").ToArray();
            var masks = parts.ToDictionary(part => part, part => LoadSprite("BS_Skylark_Status_" + part.name, true));
            if (parameters.StatusDisplay == null) throw new InvalidOperationException("Native status display is unavailable.");
            // The parent is already inactive. Keep activeSelf true so the game's later root clone activates normally.
            GameObject prefab = Object.Instantiate(parameters.StatusDisplay, aircraft.transform.parent);
            prefab.name = "BS_Skylark_StatusDisplay";
            StatusDisplay status = prefab.GetComponent<StatusDisplay>();
            Image background = SkylarkTemplate.Get<Image>(status, "aircraftBackground");
            var oldParts = SkylarkTemplate.Get<List<PartStatusDisplay>>(status, "statusDisplays");
            var thresholds = oldParts.Where(p => p.partImage != null).GroupBy(p => p.partImage.name)
                .ToDictionary(g => g.Key, g => g.First().redStatusThreshold);
            foreach (Image image in oldParts.Select(p => p.partImage).Distinct().ToArray())
                if (image != null && image != background) Object.DestroyImmediate(image.gameObject);
            background.sprite = outline;
            background.preserveAspect = true;
            background.raycastTarget = false;
            var displays = new List<PartStatusDisplay>();
            foreach (UnitPart part in parts)
            {
                Sprite mask = masks[part];
                GameObject go = new GameObject(part.name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(background.transform.parent, false);
                RectTransform rect = (RectTransform)go.transform, source = background.rectTransform;
                rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
                rect.sizeDelta = source.sizeDelta; rect.anchoredPosition3D = source.anchoredPosition3D;
                rect.localRotation = source.localRotation; rect.localScale = source.localScale;
                rect.SetSiblingIndex(background.transform.GetSiblingIndex() + 1 + displays.Count);
                Image image = go.GetComponent<Image>();
                image.sprite = mask; image.preserveAspect = true; image.raycastTarget = false;
                image.color = new Color(1f, 1f, 0f, 0f);
                displays.Add(new PartStatusDisplay
                {
                    partImage = image, redStatusThreshold = thresholds.TryGetValue(part.name, out float threshold) ? threshold : 30f,
                    displayCondition = 1f
                });
            }
            SkylarkTemplate.Set(status, "statusDisplays", displays);
            parameters.StatusDisplay = prefab;
            BoundParts = displays.Count;
            Sprite map = LoadSprite("BS_Skylark_Icon", false);
            if (map != null)
            {
                ((Unit)aircraft).definition.mapIcon = map;
                ((Unit)aircraft).definition.mapOrient = true;
            }
        }

        private static Sprite LoadSprite(string name, bool required)
        {
            if (sprites.TryGetValue(name, out Sprite cached) && cached != null) return cached;
            string path = "assets/skylarkreview/textures/" + name.ToLowerInvariant() + ".png";
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (!bundle.Contains(path)) continue;
                Texture2D texture = bundle.LoadAsset<Texture2D>(path);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = name; sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprites[name] = sprite;
                return sprite;
            }
            if (required) throw new InvalidOperationException("Skylark status artwork is missing: " + path);
            return null;
        }

        internal static object Inspect(Aircraft aircraft)
        {
            StatusDisplay status = SkylarkTemplate.Get<StatusDisplay>(aircraft, "statusDisplay");
            if (status == null) return new { available = false, expected = 26 };
            var entries = SkylarkTemplate.Get<List<PartStatusDisplay>>(status, "statusDisplays");
            var rows = entries.Select(entry =>
            {
                UnitPart part = SkylarkTemplate.Get<UnitPart>(entry, "unitPart");
                Image image = entry.partImage;
                return new
                {
                    image = image != null ? image.name : null, part = part != null ? part.name : null,
                    sprite = image != null && image.sprite != null ? image.sprite.name : null,
                    bound = part != null, nameMatches = part != null && image != null && part.name == image.name,
                    ownerMatches = SkylarkTemplate.Get<StatusDisplay>(entry, "statusDisplay") == status
                };
            }).ToArray();
            Image background = SkylarkTemplate.Get<Image>(status, "aircraftBackground");
            return new
            {
                available = true, initialized = SkylarkTemplate.Get<bool>(status, "initialized"), expected = 26,
                count = rows.Length, bound = rows.Count(row => row.bound), matchingNames = rows.Count(row => row.nameMatches),
                originalSprites = rows.Count(row => row.sprite != null && row.sprite.StartsWith("BS_Skylark_Status_", StringComparison.Ordinal)),
                background = background != null && background.sprite != null ? background.sprite.name : null,
                mapIcon = ((Unit)aircraft).definition.mapIcon != null ? ((Unit)aircraft).definition.mapIcon.name : null, rows
            };
        }
    }

    [HarmonyPatch(typeof(NozzleGauge), nameof(NozzleGauge.Initialize))]
    internal static class SkylarkNozzleGaugePatch
    {
        private static void Prefix(NozzleGauge __instance, Aircraft aircraft)
        {
            if (aircraft == null || ((Unit)aircraft).definition == null || ((Unit)aircraft).definition.jsonKey != SkylarkTemplate.Key) return;
            SkylarkTemplate.Set(__instance, "sourceName", SkylarkTemplate.Key);
            SkylarkTemplate.Set(__instance, "inputRange", -90f);
        }
    }
}

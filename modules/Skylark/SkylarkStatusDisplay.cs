using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Util;
using HarmonyLib;
using TMPro;
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

        internal static void RefitStores(Aircraft aircraft, Cockpit cockpit)
        {
            if (GameManager.IsHeadless) return;
            GameObject source = SkylarkTemplate.Get<GameObject>(cockpit, "tacScreenUIPrefab");
            if (source == null || source.GetComponentInChildren<PylonIndicator>(true) == null) return;
            byte[] png = EmbeddedResources.ReadAll(typeof(SkylarkStatusDisplay).Assembly, "BoscaliSummer.Skylark.FrontProfile.png", 1024 * 1024);
            if (png == null) return;

            GameObject display = Object.Instantiate(source, aircraft.transform.parent);
            display.name = "BS_Skylark_TacScreen";
            PylonIndicator indicator = display.GetComponentInChildren<PylonIndicator>(true);
            Image profile = indicator.GetComponent<Image>();
            Array oldSets = SkylarkTemplate.Get<Array>(indicator, "pylonElementSets");
            HardpointSet[] stations = aircraft.weaponManager.hardpointSets;
            if (profile == null || oldSets == null || stations == null || oldSets.Length < stations.Length)
            {
                Object.DestroyImmediate(display);
                return;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Skylark stores profile", wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, png, true))
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(display);
                return;
            }
            profile.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            profile.sprite.name = "BS_Skylark_StoresProfile";
            profile.color = Color.white;
            profile.preserveAspect = true;

            // Reuse the native indicator's ammo and rearm events. Set indices follow our actual hardpoint order.
            Array sets = Array.CreateInstance(oldSets.GetType().GetElementType(), stations.Length);
            for (int i = 0; i < oldSets.Length; i++)
            {
                object set = oldSets.GetValue(i);
                Image[] markers = SkylarkTemplate.Get<Image[]>(set, "pylonImages");
                if (i >= stations.Length)
                {
                    foreach (Image marker in markers) marker.gameObject.SetActive(false);
                    continue;
                }
                sets.SetValue(set, i);
                Vector3 location = aircraft.transform.InverseTransformPoint(stations[i].hardpoints[0].transform.position);
                // Facing the nose reverses aircraft-left. Separate the two centerline indicators.
                float x = -location.x * 47f / SkylarkTemplate.Scale;
                if (Mathf.Abs(x) < 1f) x = i == 0 ? -10f : 10f;
                foreach (Image marker in markers)
                {
                    marker.rectTransform.anchoredPosition = new Vector2(x, -46f);
                    marker.rectTransform.sizeDelta = new Vector2(11f, 8f);
                }
            }
            SkylarkTemplate.Set(indicator, "pylonElementSets", sets);
            SkylarkTacticalVector.Install(display);
            SkylarkTemplate.Set(cockpit, "tacScreenUIPrefab", display);
        }

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
            Texture2D texture = SkylarkTemplate.LoadTexture(name.ToLowerInvariant() + ".png");
            if (texture == null)
            {
                if (required) throw new InvalidOperationException("Skylark status artwork is missing: " + name);
                return null;
            }
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name; sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprites[name] = sprite;
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

    // The native gauge continues to show both commanded and actual duct positions. Its caption identifies the selected mode.
    internal sealed class SkylarkTacticalVector : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        private SkylarkFlightMode flight;
        private VectorMode shownMode;
        private int shownAngle = -1;
        private string caption;

        internal static void Install(GameObject display)
        {
            NozzleGauge gauge = display.GetComponentInChildren<NozzleGauge>(true);
            if (gauge == null) return;
            TextMeshProUGUI label = AccessTools.Field(typeof(NozzleGauge), "label")?.GetValue(gauge) as TextMeshProUGUI;
            if (label == null) return;
            gauge.gameObject.AddComponent<SkylarkTacticalVector>().label = label;
        }

        internal void Bind(Aircraft aircraft) => flight = aircraft.GetComponent<SkylarkFlightMode>();

        internal void RefreshCaption()
        {
            if (flight == null || label == null || !label.isActiveAndEnabled) return;
            VectorMode mode = flight.Mode;
            int angle = Mathf.RoundToInt(flight.Angle);
            if (shownMode != mode || shownAngle != angle)
            {
                shownMode = mode;
                shownAngle = angle;
                caption = SkylarkFlightMode.Name(mode) + " " + angle + "°";
            }
            if (label.text != caption) label.text = caption;
        }

#if !PUBLIC_RELEASE
        internal string Caption => label != null ? label.text : null;
#endif
    }

    [HarmonyPatch]
    internal static class SkylarkNozzleGaugePatch
    {
        private static readonly FieldInfo SourceName = AccessTools.Field(typeof(NozzleGauge), "sourceName");
        private static readonly FieldInfo InputRange = AccessTools.Field(typeof(NozzleGauge), "inputRange");
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(NozzleGauge), nameof(NozzleGauge.Initialize));
        private static bool Prepare() => TargetMethod() != null && SourceName?.FieldType == typeof(string) && InputRange?.FieldType == typeof(float);

        private static void Prefix(NozzleGauge __instance, Aircraft aircraft)
        {
            if (aircraft == null || ((Unit)aircraft).definition == null || ((Unit)aircraft).definition.jsonKey != SkylarkTemplate.Key) return;
            SourceName.SetValue(__instance, SkylarkTemplate.Key);
            InputRange.SetValue(__instance, -90f);
            if (__instance.TryGetComponent(out SkylarkTacticalVector vector)) vector.Bind(aircraft);
        }
    }

    [HarmonyPatch]
    internal static class SkylarkNozzleCaptionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(NozzleGauge), nameof(NozzleGauge.Refresh));
        private static bool Prepare() => TargetMethod() != null && AccessTools.Field(typeof(NozzleGauge), "sourceName")?.FieldType == typeof(string);

        private static void Postfix(NozzleGauge __instance, string ___sourceName)
        {
            if (___sourceName != SkylarkTemplate.Key) return;
            try
            {
                if (__instance.TryGetComponent(out SkylarkTacticalVector vector)) vector.RefreshCaption();
            }
            catch (Exception error) { PatchGuard.Report(nameof(SkylarkNozzleCaptionPatch), error); }
        }
    }
}

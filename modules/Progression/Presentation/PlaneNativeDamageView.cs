using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>Displays the aircraft's own HUD damage art without starting its audio or subscriptions.</summary>
    internal sealed class PlaneNativeDamageView
    {
        private static readonly System.Reflection.FieldInfo PartsField = AccessTools.Field(typeof(StatusDisplay), "statusDisplays");
        private static readonly System.Reflection.FieldInfo BackgroundField = AccessTools.Field(typeof(StatusDisplay), "aircraftBackground");
        private static readonly System.Reflection.FieldInfo FailuresField = AccessTools.Field(typeof(StatusDisplay), "failureIndicators");
        private readonly RectTransform parent;
        private readonly Rect area;
        private GameObject copy;
        private GameObject source;
        private List<PartStatusDisplay> overlays;
        private Color[] originalInk;

        internal bool Available => copy != null && overlays != null;

        internal PlaneNativeDamageView(RectTransform parent, Rect area)
        {
            this.parent = parent;
            this.area = area;
        }

        internal void Refresh(Aircraft aircraft)
        {
            GameObject prefab = aircraft != null ? aircraft.GetAircraftParameters()?.StatusDisplay : null;
            if (prefab != source) Build(prefab);
            if (copy == null || overlays == null) return;
            List<UnitPart> parts = aircraft.partLookup;
            for (int i = 0; i < overlays.Count; i++)
            {
                PartStatusDisplay overlay = overlays[i];
                if (overlay?.partImage == null) continue;
                UnitPart match = null;
                if (parts != null)
                    for (int j = 0, count = Mathf.Min(parts.Count, 128); j < count; j++)
                        if (parts[j] != null && parts[j].gameObject.name == overlay.partImage.gameObject.name)
                        { match = parts[j]; break; }
                if (match == null) { overlay.partImage.color = Color.clear; continue; }
                if (match.IsDetached()) { overlay.partImage.color = new Color(.7f, 0f, .25f, 1f); continue; }
                float denominator = Mathf.Max(1f, 100f - overlay.redStatusThreshold);
                float condition = Mathf.Max((match.hitPoints - overlay.redStatusThreshold) / denominator, 0f);
                if (float.IsNaN(condition) || float.IsInfinity(condition)) condition = 1f;
                Color ink = originalInk[i];
                ink.g = Mathf.Min(condition * 2f, 1f);
                ink.a = 1f - Mathf.Clamp01(condition);
                overlay.partImage.color = ink;
            }
        }

        internal void Clear()
        {
            if (copy != null) Object.Destroy(copy);
            copy = source = null;
            overlays = null;
            originalInk = null;
        }

        private void Build(GameObject prefab)
        {
            Clear();
            source = prefab;
            if (prefab == null || PartsField == null || BackgroundField == null) return;
            copy = Object.Instantiate(prefab, parent, false);
            StatusDisplay status = copy.GetComponent<StatusDisplay>();
            RectTransform rect = copy.GetComponent<RectTransform>();
            if (status == null || rect == null) { Clear(); source = prefab; return; }
            status.enabled = false;
            foreach (Graphic graphic in copy.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            overlays = PartsField.GetValue(status) as List<PartStatusDisplay>;
            if (overlays != null)
            {
                originalInk = new Color[overlays.Count];
                for (int i = 0; i < overlays.Count; i++)
                    originalInk[i] = overlays[i]?.partImage != null ? overlays[i].partImage.color : Color.white;
            }
            Image background = BackgroundField.GetValue(status) as Image;
            if (background != null) background.color = Color.white;
            if (FailuresField?.GetValue(status) is List<GameObject> failures)
                foreach (GameObject failure in failures) if (failure != null) failure.SetActive(false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(.5f, .5f);
            Bounds art = RectTransformUtility.CalculateRelativeRectTransformBounds(rect,
                background != null ? background.rectTransform : rect);
            float width = art.size.x > 1f ? art.size.x : rect.rect.width;
            float height = art.size.y > 1f ? art.size.y : rect.rect.height;
            if (width <= 1f || height <= 1f) { Clear(); source = prefab; return; }
            float scale = Mathf.Min(area.width / width, area.height / height);
            rect.localScale = Vector3.one * scale;
            rect.anchoredPosition = new Vector2(area.x + area.width * .5f - art.center.x * scale,
                area.y - area.height * .5f - art.center.y * scale);
            if (overlays == null) { Clear(); source = prefab; }
        }
    }
}

using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>
    /// Hell's territory and breach signs on the flat and relief maps. World-sized corruption fields sit below readable,
    /// constant-screen-size labels and animated black-hole sigils. Local and passive: no clicks.
    /// </summary>
    [DefaultExecutionOrder(101)] // after the map commits the displayed relief pose, like ContractMapHud
    internal sealed class HellPortalMapLayer : MonoBehaviour, ISceneService
    {
        private const int MaxSigns = 4;
        private const int MaxZones = 32, ZoneSegments = 32;
        private static readonly Rgba Hole = new Rgba(0f, 0f, 0f, .92f);
        private static readonly Rgba Outer = new Rgba(.82f, .32f, 1f, .9f);
        private static readonly Rgba TerritoryFill = new Rgba(.48f, .02f, .68f, .18f);
        private static readonly float[] Xs = new float[12], Ys = new float[12];
        private static readonly float[] ZoneXs = new float[ZoneSegments], ZoneYs = new float[ZoneSegments];

        private RectTransform rootRect;
        private readonly List<RectTransform> signs = new List<RectTransform>();
        private readonly List<AvVector> strokes = new List<AvVector>();
        private readonly List<TMP_Text> portalLabels = new List<TMP_Text>();
        private readonly List<TMP_Text> zoneLabels = new List<TMP_Text>();
        private AvVector territory;

        public void ResetForScene()
        {
            if (rootRect != null) Destroy(rootRect.gameObject);
            rootRect = null;
            signs.Clear();
            strokes.Clear();
            portalLabels.Clear();
            zoneLabels.Clear();
            territory = null;
        }

        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if ((HellPortal.Count == 0 && HellTerritory.Zones.Count == 0) || map == null || map.mapImage == null || !DynamicMap.mapMaximized)
            {
                Show(0);
                if (rootRect != null) rootRect.gameObject.SetActive(false);
                return;
            }
            if (rootRect == null) Build();
            rootRect.gameObject.SetActive(true);
            if (rootRect.parent != map.mapImage.transform)
            {
                rootRect.SetParent(map.mapImage.transform, false);
                rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(.5f, .5f);
                rootRect.anchoredPosition = Vector2.zero;
            }
            rootRect.SetAsLastSibling();
            float factor = map.mapDisplayFactor;
            float screenScale = Mathf.Abs(rootRect.lossyScale.x);
            if (!float.IsFinite(factor) || factor <= 0f || !float.IsFinite(screenScale) || screenScale < .001f)
            {
                rootRect.gameObject.SetActive(false);
                return;
            }
            float inverse = 1.4f / screenScale; // 1.4x: at 1x the sigil (about 40 px) sat under the unit stack badges
            bool projected = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
            DrawTerritory(factor, screenScale, projected ? projection : null);
            float t = Time.unscaledTime;
            int shown = 0;
            foreach (HellPortal portal in HellPortal.All)
            {
                if (shown >= MaxSigns || !portal.Alive || portal.IsDimensionExit != HellDimension.Inside) continue;
                GlobalPosition at = portal.Root.transform.position.ToGlobalPosition();
                float x = at.x * factor, y = at.z * factor;
                if (projected && !projection.TryProject(at.x, at.z, out x, out y)) continue;
                RectTransform sign = signs[shown];
                sign.anchoredPosition = new Vector2(x, y);
                sign.localScale = new Vector3(inverse, inverse, 1f);
                TMP_Text label = portalLabels[shown];
                string name = portal.IsDimensionExit ? "RETURN RIFT" : "HELLGATE";
                if (label.text != name) label.text = name;
                Draw(strokes[shown].Buffer, t + shown * 1.7f);
                strokes[shown].Commit();
                shown++;
            }
            Show(shown);
        }

        private void Show(int count)
        {
            for (int i = 0; i < signs.Count; i++)
                if (signs[i].gameObject.activeSelf != i < count) signs[i].gameObject.SetActive(i < count);
        }

        private void Build()
        {
            var root = new GameObject("Boscali Hellgate Map Layer", typeof(RectTransform));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            territory = AvVector.Create(rootRect, "Corruption fields", MaxZones * ZoneSegments * 2);
            for (int i = 0; i < MaxZones; i++)
            {
                TMP_Text label = AvText.Make(rootRect, "Territory " + i, AvTextRole.DataSmall, "", TextAlignmentOptions.Top);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
                label.rectTransform.sizeDelta = new Vector2(230f, 28f);
                label.fontSize = 12f;
                label.fontStyle = FontStyles.Bold;
                label.color = HellArmy.MapInkSelected;
                label.raycastTarget = false;
                label.gameObject.SetActive(false);
                zoneLabels.Add(label);
            }
            for (int i = 0; i < MaxSigns; i++)
            {
                RectTransform sign = AvLay.Child(rootRect, "Hellgate " + i);
                sign.anchorMin = sign.anchorMax = sign.pivot = new Vector2(.5f, .5f);
                sign.sizeDelta = Vector2.zero;
                strokes.Add(AvVector.Create(sign, "Sigil", 256));
                TMP_Text label = AvText.Make(sign, "Name", AvTextRole.DataSmall, "HELLGATE", TextAlignmentOptions.Top);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
                label.rectTransform.pivot = new Vector2(.5f, 1f);
                label.rectTransform.sizeDelta = new Vector2(120f, 20f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -30f);
                label.fontSize = 13f;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 6f;
                label.color = HellArmy.MapInkSelected;
                label.raycastTarget = false;
                portalLabels.Add(label);
                signs.Add(sign);
                sign.gameObject.SetActive(false);
            }
        }

        private void DrawTerritory(float factor, float screenScale, IMapProjection projection)
        {
            AvQuadBuffer b = territory.Buffer;
            b.Clear();
            if (HellDimension.Inside)
            {
                foreach (TMP_Text label in zoneLabels) label.gameObject.SetActive(false);
                territory.Commit();
                return;
            }
            int shown = 0;
            foreach (HellTerritory.Zone zone in HellTerritory.Zones)
            {
                if (shown >= MaxZones || !zone.Controlled && zone.Progress <= .01f) continue;
                GlobalPosition center = zone.Center;
                if (!Project(center.x, center.z, factor, projection, out float cx, out float cy)) continue;
                bool valid = true;
                for (int i = 0; i < ZoneSegments; i++)
                {
                    float a = i * (Mathf.PI * 2f / ZoneSegments);
                    if (!Project(center.x + Mathf.Cos(a) * zone.Radius, center.z + Mathf.Sin(a) * zone.Radius,
                        factor, projection, out ZoneXs[i], out ZoneYs[i])) { valid = false; break; }
                }
                if (!valid) continue;
                Rgba fill = TerritoryFill.WithAlpha(zone.Controlled ? .18f : .06f);
                for (int i = 0; i < ZoneSegments; i++)
                {
                    int next = (i + 1) % ZoneSegments;
                    // A degenerate quad is a triangle fan; every edge is projected separately for the relief map.
                    b.Add(cx, cy, ZoneXs[i], ZoneYs[i], ZoneXs[next], ZoneYs[next], cx, cy, fill, fill, fill, fill);
                    if (zone.Controlled || i % 2 == 0)
                        AvStrokes.Line(b, ZoneXs[i], ZoneYs[i], ZoneXs[next], ZoneYs[next], 1.7f / screenScale, Outer);
                }
                TMP_Text label = zoneLabels[shown];
                // The breach already has its own label. Bases carry words too, so color is never the only ownership cue.
                bool showLabel = zone.Base != null;
                label.gameObject.SetActive(showLabel);
                if (showLabel)
                {
                    string text = zone.Controlled ? "HELL · " + zone.Label : "CORRUPTING · " + Mathf.RoundToInt(zone.Progress * 100f) + "%";
                    if (label.text != text) label.text = text;
                    label.rectTransform.anchoredPosition = new Vector2(cx, cy - 18f / screenScale);
                    label.rectTransform.localScale = Vector3.one / screenScale;
                }
                shown++;
            }
            for (int i = shown; i < zoneLabels.Count; i++) zoneLabels[i].gameObject.SetActive(false);
            territory.Commit();
        }

        private static bool Project(float x, float z, float factor, IMapProjection projection, out float mapX, out float mapY)
        {
            if (projection != null) return projection.TryProject(x, z, out mapX, out mapY);
            mapX = x * factor;
            mapY = z * factor;
            return float.IsFinite(mapX) && float.IsFinite(mapY);
        }

        /// <summary>A dark, fractured tear in screen pixels. Its outline breathes slowly without turning.</summary>
        private static void Draw(AvQuadBuffer b, float t)
        {
            b.Clear();
            float breath = 1f + Mathf.Sin(t * .65f) * .025f;
            var shadow = new Rgba(.018f, .006f, .025f, .48f);
            var violet = new Rgba(.45f, .24f, .56f, .68f);
            var crimson = new Rgba(.47f, .06f, .14f, .7f);
            for (int i = 0; i < Xs.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / Xs.Length;
                float fracture = 1f + Mathf.Sin(i * 2.9f) * .16f + Mathf.Cos(i * 5.1f) * .09f;
                float x = Mathf.Cos(angle) * 23f * fracture * breath;
                float y = Mathf.Sin(angle) * 8f * fracture * breath;
                Xs[i] = x * .93f - y * .37f;
                Ys[i] = x * .37f + y * .93f;
            }
            for (int i = 0; i < Xs.Length; i++)
            {
                int next = (i + 1) % Xs.Length;
                b.Add(0f, 0f, Xs[i] * 1.18f, Ys[i] * 1.18f, Xs[next] * 1.18f, Ys[next] * 1.18f,
                    0f, 0f, shadow, shadow, shadow, shadow);
            }
            for (int i = 0; i < Xs.Length; i++)
            {
                int next = (i + 1) % Xs.Length;
                b.Add(0f, 0f, Xs[i], Ys[i], Xs[next], Ys[next], 0f, 0f, Hole, Hole, Hole, Hole);
                if (i == 2 || i == 6 || i == 9) continue;
                Rgba edge = i % 3 == 0 ? crimson : violet;
                AvStrokes.Line(b, Xs[i], Ys[i], Xs[next], Ys[next], 3f, edge.WithAlpha(.14f));
                AvStrokes.Line(b, Xs[i], Ys[i], Xs[next], Ys[next], .9f, edge);
            }
        }
    }
}

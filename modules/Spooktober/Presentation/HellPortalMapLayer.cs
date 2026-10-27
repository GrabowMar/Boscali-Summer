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
    /// The HELLGATE sign on the map, flat or relief (through <see cref="IMapProjection"/>), one per open portal: a black hole with
    /// a hot rim and five arms turning into it, flickering fangs round it, a dashed outer ring turning the other way and a red
    /// label. Constant screen size, vector strokes (no sprites), redrawn only while the map is open. Local, passive: no clicks.
    /// </summary>
    [DefaultExecutionOrder(101)] // after the map commits the displayed relief pose, like ContractMapHud
    internal sealed class HellPortalMapLayer : MonoBehaviour, ISceneService
    {
        private const int MaxSigns = 4;
        private static readonly Rgba Hole = new Rgba(0f, 0f, 0f, .92f);
        private static readonly Rgba Rim = new Rgba(1f, .45f, .05f, 1f);
        private static readonly Rgba Arm = new Rgba(1f, .18f, .02f, .95f);
        private static readonly Rgba Fang = new Rgba(.8f, .05f, .02f, .95f);
        private static readonly Rgba Outer = new Rgba(.55f, .03f, .02f, .85f);
        private static readonly float[] Xs = new float[12], Ys = new float[12];

        private RectTransform rootRect;
        private readonly List<RectTransform> signs = new List<RectTransform>();
        private readonly List<AvVector> strokes = new List<AvVector>();

        public void ResetForScene()
        {
            if (rootRect != null) Destroy(rootRect.gameObject);
            rootRect = null;
            signs.Clear();
            strokes.Clear();
        }

        private void OnDestroy() => ResetForScene();

        private void LateUpdate()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (HellPortal.Count == 0 || map == null || map.mapImage == null || !DynamicMap.mapMaximized)
            {
                Show(0);
                return;
            }
            if (rootRect == null) Build();
            if (rootRect.parent != map.mapImage.transform)
            {
                rootRect.SetParent(map.mapImage.transform, false);
                rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(.5f, .5f);
                rootRect.anchoredPosition = Vector2.zero;
            }
            rootRect.SetAsLastSibling();
            float factor = map.mapDisplayFactor;
            float screenScale = Mathf.Abs(rootRect.lossyScale.x);
            if (!float.IsFinite(factor) || factor <= 0f || !float.IsFinite(screenScale) || screenScale < .001f) { Show(0); return; }
            float inverse = 1.4f / screenScale; // 1.4x: at 1x the sigil (about 40 px) sat under the unit stack badges
            bool projected = ModuleServices.TryGet(out IMapProjection projection) && projection.IsActive;
            float t = Time.unscaledTime;
            int shown = 0;
            foreach (HellPortal portal in HellPortal.All)
            {
                if (shown >= MaxSigns || !portal.Alive) continue;
                GlobalPosition at = portal.Root.transform.position.ToGlobalPosition();
                float x = at.x * factor, y = at.z * factor;
                if (projected && !projection.TryProject(at.x, at.z, out x, out y)) continue;
                RectTransform sign = signs[shown];
                sign.anchoredPosition = new Vector2(x, y);
                sign.localScale = new Vector3(inverse, inverse, 1f);
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
                label.color = new Color(1f, .2f, .05f, 1f);
                label.raycastTarget = false;
                signs.Add(sign);
                sign.gameObject.SetActive(false);
            }
        }

        /// <summary>The sigil at the sign's origin, in screen pixels; <paramref name="t"/> drives the turning and the flicker.</summary>
        private static void Draw(AvQuadBuffer b, float t)
        {
            b.Clear();
            AvStrokes.Ring(b, 0f, 0f, 6f, 24, 12f, Hole); // a ring as wide as its diameter fills the disc
            // Five arms on a log spiral, winding in from the rim and turning clockwise.
            for (int arm = 0; arm < 5; arm++)
            {
                float start = arm * 72f - t * 70f;
                for (int k = 0; k < Xs.Length; k++)
                {
                    float f = k / (float)(Xs.Length - 1);
                    float r = Mathf.Lerp(11.5f, 1.5f, f);
                    float a = (start + f * 220f) * Mathf.Deg2Rad;
                    Xs[k] = Mathf.Cos(a) * r;
                    Ys[k] = Mathf.Sin(a) * r;
                }
                AvStrokes.Polyline(b, Xs, Ys, Xs.Length, false, 1.4f, Arm);
            }
            AvStrokes.Ring(b, 0f, 0f, 12.5f, 32, 2.2f, Rim);
            // Nine fangs, long and short in turn, each flickering on its own beat.
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + 20f) * Mathf.Deg2Rad;
                float reach = (i % 2 == 0 ? 21f : 18f) + Mathf.Sin(t * 7f + i * 2.3f) * 1.5f;
                float side = 3.5f * Mathf.Deg2Rad;
                float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                AvStrokes.Line(b, Mathf.Cos(a - side) * 14f, Mathf.Sin(a - side) * 14f, cx * reach, cy * reach, 1.3f, Fang);
                AvStrokes.Line(b, Mathf.Cos(a + side) * 14f, Mathf.Sin(a + side) * 14f, cx * reach, cy * reach, 1.3f, Fang);
            }
            // Twelve dashes on the outer ring, turning the other way.
            for (int i = 0; i < 12; i++)
            {
                float from = i * 30f + t * 25f;
                AvStrokes.Arc(b, 0f, 0f, 26f, from, from + 16f, 4, 1.2f, Outer);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Presentation;
using HarmonyLib;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Fail-open adapter that docks vanilla <see cref="StatusDisplay"/> (the aircraft top-down damage schematic)
    /// inside the TAC BOARD's AIRFRAME slot and re-tints its background and part tiles to match the active HUD palette.
    /// Restores the original <c>FlightHud.statusAnchor</c> transform and indicator state when hidden or disposed.
    /// </summary>
    internal sealed class NativeStatusDisplay : IDisposable
    {
        private static readonly AccessTools.FieldRef<StatusDisplay, List<PartStatusDisplay>> StatusDisplaysRef =
            AccessTools.FieldRefAccess<StatusDisplay, List<PartStatusDisplay>>("statusDisplays");
        private static readonly AccessTools.FieldRef<StatusDisplay, Image> AircraftBgRef =
            AccessTools.FieldRefAccess<StatusDisplay, Image>("aircraftBackground");
        private static readonly AccessTools.FieldRef<StatusDisplay, List<GameObject>> FailureIndicatorsRef =
            AccessTools.FieldRefAccess<StatusDisplay, List<GameObject>>("failureIndicators");

        private struct FailureEntry
        {
            public GameObject Go;
            public CanvasGroup Group;
            public bool Owned;
            public float PrevAlpha;
            public bool PrevBlocksRaycasts;
        }

        private readonly List<FailureEntry> failures = new List<FailureEntry>(8);
        private readonly HashSet<PartStatusDisplay> detachedParts = new HashSet<PartStatusDisplay>();

        private StatusDisplay display;
        private RectTransform rect;
        private Transform origParent;
        private int origSibling;
        private Vector2 origAnchorMin, origAnchorMax, origPivot, origSizeDelta, origAnchoredPosition;
        private Vector3 origLocalScale, origLocalPosition;
        private Quaternion origLocalRotation;
        private Color origBgColor;
        private bool hasOrigBgColor;
        private bool savedTransform;

        /// <summary>Reparents and updates the native StatusDisplay inside <paramref name="slot"/>. Returns false when no usable native display exists.</summary>
        public bool Sync(RectTransform slot, in ScopePalette pal, bool damaged)
        {
            if (slot == null)
            {
                Release();
                return false;
            }
            if (!Acquire()) return false;
            if (rect == null) return false;

            Image bg = AircraftBgRef(display);
            bool anyPart = HasAnyPartImage(display);
            if (bg == null && !anyPart) return false;

            if (rect.parent != slot)
            {
                rect.SetParent(slot, false);
                VisorGlitch.Rescan();
            }
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;

            Bounds bounds = ComputeSchematicBounds(rect, bg);
            float fitW = Mathf.Max(16f, slot.rect.width - 2f);
            float fitH = Mathf.Max(16f, slot.rect.height - 2f);
            float bw = Mathf.Max(1f, bounds.size.x);
            float bh = Mathf.Max(1f, bounds.size.y);
            float scale = Mathf.Clamp(Mathf.Min(fitW / bw, fitH / bh), 0.1f, 2f);
            rect.localScale = new Vector3(scale, scale, 1f);
            rect.anchoredPosition = new Vector2(-bounds.center.x * scale, -bounds.center.y * scale);

            SyncFailures();
            TintSchematic(bg, pal, damaged);
            return true;
        }

        /// <summary>Restores the native StatusDisplay to its original <c>FlightHud.statusAnchor</c> parent and transforms.</summary>
        public void Release()
        {
            RestoreFailures();
            if (display != null && rect != null)
            {
                ResetGlitchMaterials(rect);
                if (hasOrigBgColor && AircraftBgRef(display) != null)
                {
                    AircraftBgRef(display).color = origBgColor;
                }
                if (savedTransform && origParent != null && rect.parent != origParent)
                {
                    rect.SetParent(origParent, false);
                    rect.SetSiblingIndex(origSibling);
                    rect.anchorMin = origAnchorMin;
                    rect.anchorMax = origAnchorMax;
                    rect.pivot = origPivot;
                    rect.sizeDelta = origSizeDelta;
                    rect.anchoredPosition = origAnchoredPosition;
                    rect.localScale = origLocalScale;
                    rect.localRotation = origLocalRotation;
                    rect.localPosition = origLocalPosition;
                }
            }
            detachedParts.Clear();
            display = null;
            rect = null;
            origParent = null;
            hasOrigBgColor = false;
            savedTransform = false;
        }

        public void Dispose() => Release();

        private bool Acquire()
        {
            FlightHud fh = SceneSingleton<FlightHud>.i;
            Transform anchor = fh != null ? fh.statusAnchor : null;
            StatusDisplay found = null;
            if (anchor != null) found = anchor.GetComponentInChildren<StatusDisplay>(true);
            if (found == null && display != null) found = display;
            if (found == null)
            {
                if (display != null) Release();
                return false;
            }
            if (found == display && rect != null) return true;

            Release();
            display = found;
            rect = found.transform as RectTransform;
            if (rect == null)
            {
                detachedParts.Clear();
            display = null;
                return false;
            }

            origParent = rect.parent;
            origSibling = rect.GetSiblingIndex();
            origAnchorMin = rect.anchorMin;
            origAnchorMax = rect.anchorMax;
            origPivot = rect.pivot;
            origSizeDelta = rect.sizeDelta;
            origAnchoredPosition = rect.anchoredPosition;
            origLocalScale = rect.localScale;
            origLocalRotation = rect.localRotation;
            origLocalPosition = rect.localPosition;
            savedTransform = true;

            Image foundBg = AircraftBgRef(found);
            if (foundBg != null)
            {
                origBgColor = foundBg.color;
                hasOrigBgColor = true;
            }
            return true;
        }

        private static bool HasAnyPartImage(StatusDisplay sd)
        {
            List<PartStatusDisplay> list = StatusDisplaysRef(sd);
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                PartStatusDisplay p = list[i];
                if (p != null && p.partImage != null) return true;
            }
            return false;
        }

        private static Bounds ComputeSchematicBounds(RectTransform root, Image bg)
        {
            if (bg != null && bg.rectTransform != null)
            {
                Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(root, bg.rectTransform);
                if (b.size.x >= 1f && b.size.y >= 1f) return b;
            }
            Rect r = root.rect;
            return new Bounds(r.center, new Vector3(r.width, r.height, 0f));
        }

        private void SyncFailures()
        {
            List<GameObject> indicators = FailureIndicatorsRef(display);
            if (indicators == null) return;
            for (int i = 0; i < indicators.Count; i++)
            {
                GameObject go = indicators[i];
                if (go == null) continue;
                int idx = FindFailureIndex(go);
                if (idx < 0)
                {
                    CanvasGroup cg = go.GetComponent<CanvasGroup>();
                    bool owned = cg == null;
                    if (owned) cg = go.AddComponent<CanvasGroup>();
                    failures.Add(new FailureEntry
                    {
                        Go = go,
                        Group = cg,
                        Owned = owned,
                        PrevAlpha = cg.alpha,
                        PrevBlocksRaycasts = cg.blocksRaycasts
                    });
                    idx = failures.Count - 1;
                }
                CanvasGroup group = failures[idx].Group;
                if (group != null)
                {
                    if (group.alpha != 0f) group.alpha = 0f;
                    if (group.blocksRaycasts) group.blocksRaycasts = false;
                }
            }
        }

        private int FindFailureIndex(GameObject go)
        {
            for (int i = 0; i < failures.Count; i++)
            {
                if (failures[i].Go == go) return i;
            }
            return -1;
        }

        private void RestoreFailures()
        {
            for (int i = 0; i < failures.Count; i++)
            {
                FailureEntry e = failures[i];
                if (e.Group == null) continue;
                if (e.Owned)
                {
                    UnityEngine.Object.Destroy(e.Group);
                }
                else
                {
                    e.Group.alpha = e.PrevAlpha;
                    e.Group.blocksRaycasts = e.PrevBlocksRaycasts;
                }
            }
            failures.Clear();
        }

        private void TintSchematic(Image bg, in ScopePalette pal, bool damaged)
        {
            if (bg != null)
            {
                Rgba bgInk = pal.Of(ScopeTone.Ink, damaged ? 1f : 0.85f);
                bg.color = new Color(bgInk.R, bgInk.G, bgInk.B, bgInk.A);
            }

            List<PartStatusDisplay> parts = StatusDisplaysRef(display);
            if (parts == null) return;
            Rgba red = pal.Of(ScopeTone.Red, 1f);
            Rgba amber = pal.Of(ScopeTone.Amber, 1f);
            for (int i = 0; i < parts.Count; i++)
            {
                PartStatusDisplay p = parts[i];
                if (p == null || p.partImage == null) continue;
                float cond = Mathf.Clamp01(p.displayCondition);
                if (cond >= 0.999f)
                {
                    Color c = p.partImage.color;
                    if (detachedParts.Contains(p) || (c.a > 0.5f && c.g < 0.05f))
                    {
                        detachedParts.Add(p);
                        p.partImage.color = new Color(red.R * 0.8f, red.G * 0.8f, red.B * 0.8f, 0.9f);
                        continue;
                    }
                    if (c.a != 0f) p.partImage.color = new Color(c.r, c.g, c.b, 0f);
                    continue;
                }
                float a = Mathf.Clamp01(1f - cond);
                if (cond <= 0.001f)
                {
                    p.partImage.color = new Color(red.R, red.G, red.B, a);
                }
                else
                {
                    p.partImage.color = new Color(
                        Mathf.Lerp(red.R, amber.R, cond),
                        Mathf.Lerp(red.G, amber.G, cond),
                        Mathf.Lerp(red.B, amber.B, cond),
                        a);
                }
            }
        }

        private static void ResetGlitchMaterials(Transform root)
        {
            Material glitch = VisorGlitch.LineArt;
            if (glitch == null) return;
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic g = graphics[i];
                if (g != null && g.material == glitch) g.material = null;
            }
        }
    }
}

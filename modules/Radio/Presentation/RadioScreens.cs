using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>One built Boscali radio screen: root, the vanilla show/hide anchor, and the kit v2 console.</summary>
    internal sealed class RadioScreen
    {
        public GameObject Root;
        public GameObject DisplayPanel;
        public MFDScreen Screen;
        public AvConsole Console;
    }

    /// <summary>
    /// The scaffolding the radio panel needs: clone the stock panel's bay position, mount one
    /// kit v2 <see cref="AvConsole"/> inside it, and wire the vanilla bezel binding
    /// (<c>MFDScreen.displayPanel</c> / <c>label</c> / <c>highlight</c>). The receiver and deck
    /// pages differ in every part they add to their <see cref="AvFlow"/>; nothing about how the
    /// screen is mounted.
    /// </summary>
    internal static class RadioScreens
    {
        public static bool TryBuild(
            MFDScreen template, Button bezel, string id, string title,
            (AvIcon icon, string label)[] tabs, out RadioScreen built)
        {
            built = null;
            if (template == null) return false;

            var root = new GameObject("BoscaliRadio.Screen", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(template.transform.parent, false);

            RectTransform templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;
            // Position is deliberately not copied. VirtualMFD.showPos is Vector3.zero and
            // MFDScreen.ShowScreen assigns it straight to localPosition, so a screen has no
            // remembered home — it is placed by its parent and anchors, and an
            // anchoredPosition written here is overwritten whenever the panel is opened.
            float height = ResolvePanelHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            ClampPanelIntoCanvas(rootRect);

            // The vanilla MFDScreen toggles displayPanel's GameObject when the bezel button
            // cycles to a different screen on this slot; the whole kit v2 console mounts inside
            // it so one SetActive hides frame, chrome and every page together.
            var displayObject = new GameObject("Content", typeof(RectTransform));
            RectTransform display = displayObject.GetComponent<RectTransform>();
            display.SetParent(rootRect, false);
            display.anchorMin = Vector2.zero;
            display.anchorMax = Vector2.one;
            display.pivot = new Vector2(0.5f, 0.5f);
            display.offsetMin = Vector2.zero;
            display.offsetMax = Vector2.zero;

            AvConsole console = AvConsole.Build(display, id, title, tabs.Length, AvTokens.PanelWidth, height);
            console.Tabs(tabs);

            MFDScreen screen = root.AddComponent<MFDScreen>();
            screen.shortName = id;
            screen.displayPanel = displayObject;
            screen.aircraftOnly = false;
            screen.label = bezel == null ? null : bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            screen.highlight = FindHighlight(bezel);
            if (screen.label == null || screen.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return false;
            }

            built = new RadioScreen
            {
                Root = root,
                DisplayPanel = displayObject,
                Screen = screen,
                Console = console
            };
            return true;
        }

        public static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        /// <summary>
        /// The height this panel should take, given the slot it was parented into. A screen
        /// inherits its bay from the stock template it was cloned beside, and that bay is
        /// taller than the panels used to be; measuring it beats a second hard-coded constant
        /// that would be wrong at the next resolution. Kept as a local copy of the same
        /// arithmetic the frozen v1 kit's screen-height resolver used, so this slice never calls
        /// into that v1 API.
        /// </summary>
        private static float ResolvePanelHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;

            float available = parent.rect.height;
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }

            if (available <= 1f) return min;
            return Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        /// <summary>
        /// Pulls the panel back inside the root canvas when its anchored position would push it
        /// off-screen. A local copy of the same arithmetic the frozen v1 kit's canvas-clamp
        /// helper used, so this slice never calls into that v1 API.
        /// </summary>
        private static void ClampPanelIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null || panel.parent == null) return;

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRt.InverseTransformPoint(corners[i]);
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }

            Rect bounds = canvasRt.rect;

            float dx = 0f;
            if (minX < bounds.xMin + margin) dx = bounds.xMin + margin - minX;
            else if (maxX > bounds.xMax - margin) dx = bounds.xMax - margin - maxX;

            float dy = 0f;
            if (maxY > bounds.yMax - margin) dy = bounds.yMax - margin - maxY;
            else if (minY < bounds.yMin + margin) dy = bounds.yMin + margin - minY;

            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;

            Vector3 world = canvasRt.TransformVector(new Vector3(dx, dy, 0f));
            Vector3 local2 = panel.parent.InverseTransformVector(world);
            panel.anchoredPosition += new Vector2(local2.x, local2.y);
        }
    }
}

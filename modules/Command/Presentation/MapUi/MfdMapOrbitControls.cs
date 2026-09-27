using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>Client-only camera controls, anchored to the fixed map viewport.</summary>
    internal static class MfdMapOrbitControls
    {
        private static RectTransform root;
        private static TMP_Text readout;
        private static TMP_Text cursorText;
        private static bool dragging;
        private static Vector2 lastMouse;
        private static Vector2 dragDelta;
        private static float nextDragUpdate;
        private static int lastRevision = -1;
        private static float nextCursorRead;

        internal static bool Contains(Vector2 screenPoint) => MapUiPointer.Contains(root, screenPoint);

        internal static void Tick(DynamicMap map)
        {
            if (map?.mapBackground == null || !MfdTerrainRelief.IsDrawing) return;
            RectTransform viewport = map.mapBackground.rectTransform;
            if (root == null || root.parent != viewport) Build(viewport);
            if (root == null) return;
            root.SetAsLastSibling();

            if (!Input.GetMouseButton(2))
            {
                if (dragging && dragDelta.sqrMagnitude > 0f)
                    MfdTerrainRelief.Rotate(dragDelta.x * .25f, -dragDelta.y * .20f);
                dragging = false;
                dragDelta = Vector2.zero;
            }
            else if (dragging)
            {
                Vector2 current = Input.mousePosition;
                dragDelta += current - lastMouse;
                lastMouse = current;
                if (Time.unscaledTime >= nextDragUpdate)
                {
                    MfdTerrainRelief.Rotate(dragDelta.x * .25f, -dragDelta.y * .20f);
                    dragDelta = Vector2.zero;
                    nextDragUpdate = Time.unscaledTime + .05f;
                }
            }
            else if (Input.GetMouseButtonDown(2) &&
                MapUiPointer.Contains(viewport, Input.mousePosition) &&
                !MapUiPointer.OverControls())
            {
                dragging = true;
                lastMouse = Input.mousePosition;
                nextDragUpdate = 0f;
            }

            if (lastRevision != MfdTerrainRelief.ViewRevision)
            {
                lastRevision = MfdTerrainRelief.ViewRevision;
                if (readout != null)
                    readout.text = $"AZ {Mathf.RoundToInt(MfdTerrainRelief.Yaw):+000;-000;000}°  EL {Mathf.RoundToInt(MfdTerrainRelief.Pitch):00}°";
            }
            if (cursorText == null || Time.unscaledTime < nextCursorRead) return;
            nextCursorRead = Time.unscaledTime + .1f;
            if (MfdMapInteractions.Feedback != null)
                cursorText.text = MfdMapInteractions.Feedback;
            else if (MfdTerrainRelief.HoveredStackCount >= 5)
                cursorText.text = MfdTerrainRelief.HoveredStackCount + " TRACKS  /  ZOOM TO RESOLVE";
            else if (MfdTerrainRelief.TryInspectCursor(out GlobalPosition point, out float elevation))
                cursorText.text = $"X {point.x / 1000f:+0.0;-0.0;0.0}  Z {point.z / 1000f:+0.0;-0.0;0.0} km  ·  TERRAIN ~{elevation:0} m";
            else cursorText.text = "HOVER TERRAIN FOR GROUND FIX";
        }

        private static void Build(RectTransform viewport)
        {
            Restore();
            var go = new GameObject("NOAvionics.MapOrbit", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(viewport, false);
            root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-12f, -12f);
            root.sizeDelta = new Vector2(276f, 114f);
            AvKit.Panel(root, new Rect(0f, 0f, 276f, 114f),
                new Color(.025f, .055f, .068f, .94f));
            AvKit.Label(root, "TERRAIN VIEW  /  MIDDLE DRAG ORBIT",
                new Rect(8f, 4f, 260f, 15f), AvTheme.Dim, AvTokens.FontMicro);
            AvKit.Button(root, "-15°", new Rect(6f, 23f, 43f, 30f),
                () => MfdTerrainRelief.Rotate(-15f, 0f), AvTokens.FontMicro)
                .WithTooltip("Rotate view 15 degrees left. Middle drag to orbit freely.");
            AvKit.Button(root, "+15°", new Rect(51f, 23f, 43f, 30f),
                () => MfdTerrainRelief.Rotate(15f, 0f), AvTokens.FontMicro)
                .WithTooltip("Rotate view 15 degrees right. Middle drag to orbit freely.");
            AvKit.Button(root, "RESET", new Rect(96f, 23f, 59f, 30f),
                MfdTerrainRelief.ResetOrbit, AvTokens.FontMicro)
                .WithTooltip("Reset north and terrain tilt.");
            AvKit.Button(root, "LOW", new Rect(157f, 23f, 46f, 30f),
                () => MfdTerrainRelief.Rotate(0f, -10f), AvTokens.FontMicro)
                .WithTooltip("Lower the viewing angle by 10 degrees.");
            AvKit.Button(root, "HIGH", new Rect(205f, 23f, 65f, 30f),
                () => MfdTerrainRelief.Rotate(0f, 10f), AvTokens.FontMicro)
                .WithTooltip("Raise the viewing angle by 10 degrees.");
            readout = AvKit.Label(root, "", new Rect(6f, 59f, 264f, 17f),
                AvTheme.RailInfo, AvTokens.FontMicro);
            cursorText = AvKit.Label(root, "", new Rect(6f, 77f, 264f, 17f),
                AvTheme.Dim, AvTokens.FontMicro);
            AvKit.Label(root, "LMB PAN · CTRL BOX · ALT ADD · RMB ACTIONS",
                new Rect(6f, 96f, 264f, 14f), AvTheme.RailInfo, AvTokens.FontMicro);
            lastRevision = -1;
            nextCursorRead = 0f;
        }

        internal static void Restore()
        {
            dragging = false;
            dragDelta = Vector2.zero;
            readout = null;
            cursorText = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}

using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The overlay's own chrome under the map viewport (decision D10: no layer registry, the STR overlay owns these): the layer plate at the top left with its
    /// three toggles and the board's age, and the legend strip at the bottom with one swatch per shape the map shows. Both register with the pointer guard
    /// and carry no map input.
    /// </summary>
    internal sealed class StrMapChrome
    {
        private const float PlateH = 56f, ToggleH = 22f, LegendRow = 18f, LegendPad = 6f, SwatchW = 24f;

        private readonly StrMapOverlay owner;
        private readonly StrControls controls;
        private readonly RectTransform plate, legend;
        private readonly AvFrame plateFrame, legendFrame;
        private readonly TMP_Text plateTitle, plateLine;
        private readonly StrButton fronts, operation, sorties, draw;
        /// <summary>The DRAW tool's prompt or answer; shown in place of the board line while set.</summary>
        internal string Note = "";
        private readonly AvVector[] swatches = new AvVector[StrOverlayPlan.MaxLegend];
        private readonly TMP_Text[] labels = new TMP_Text[StrOverlayPlan.MaxLegend];
        private string lastLine = "";
        private int legendSignature;
        private bool legendBuilt;
        private int legendCount;
        private float viewportWidth = -1f, legendHeight;

        internal StrMapChrome(RectTransform viewport, StrMapOverlay owner, StrControls controls)
        {
            this.owner = owner;
            this.controls = controls;

            plate = Panel(viewport, "StrLayerPlate", new Vector2(0f, 1f), new Vector2(12f, -8f), out plateFrame);
            plateTitle = AvText.Make(plate, "Title", AvTextRole.Micro, "STR LAYER", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(plateTitle, false);
            plateTitle.raycastTarget = false;
            plateLine = AvText.Make(plate, "Line", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(plateLine, false);
            plateLine.raycastTarget = false;
            fronts = Toggle(StrLayers.Fronts);
            operation = Toggle(StrLayers.Operation);
            sorties = Toggle(StrLayers.Sorties);
            draw = new StrButton(plate, "DRAW", AvButtonStyle.Default, () => owner.ToggleDraw());

            legend = Panel(viewport, "StrLegend", new Vector2(0f, 0f), new Vector2(12f, 10f), out legendFrame);
            for (int i = 0; i < swatches.Length; i++)
            {
                swatches[i] = AvVector.Create(legend, "Swatch " + i, 8);
                RectTransform sw = swatches[i].rectTransform;
                sw.anchorMin = sw.anchorMax = new Vector2(0f, 1f);
                sw.pivot = Vector2.zero;   // the drawing origin is the swatch's bottom-left corner
                sw.sizeDelta = new Vector2(SwatchW, LegendRow);
                labels[i] = AvText.Make(legend, "Label " + i, AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(labels[i], false);
                labels[i].raycastTarget = false;
            }
            legend.gameObject.SetActive(false);
        }

        /// <summary>Height along the bottom edge the legend takes (with its margins), for the card to stay clear of; 0 while it is hidden.</summary>
        internal float BottomReserve => legend.gameObject.activeSelf ? legendHeight + 10f + 6f : 0f;

        internal RectTransform Plate => plate;
        internal RectTransform Legend => legend;

        /// <summary>Shows or hides both pieces; the legend also stays hidden while the plan has no shapes to explain.</summary>
        internal void SetShown(bool on)
        {
            if (plate.gameObject.activeSelf != on) plate.gameObject.SetActive(on);
            bool legendOn = on && legendCount > 0;
            if (legend.gameObject.activeSelf != legendOn) legend.gameObject.SetActive(legendOn);
        }

        private static RectTransform Panel(RectTransform viewport, string name, Vector2 corner, Vector2 offset, out AvFrame frame)
        {
            var rect = AvLay.Child(viewport, name);
            rect.anchorMin = rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = offset;
            frame = AvFrame.Add(rect, "Frame", AvChamfer.Diagonal(5f));
            AvLay.Fill(frame.rectTransform);
            frame.Stroke = 1f;
            frame.raycastTarget = true;   // the chrome takes its own clicks; the pointer guard keeps wheel and drag off the map behind it
            return rect;
        }

        private StrButton Toggle(StrLayers layer)
        {
            return new StrButton(plate, layer.ToString().ToUpperInvariant(), AvButtonStyle.Default, () =>
            {
                IStrHub hub = owner.Hub;
                if (hub == null) return new StrAct(StrWhy.NoBoard, "NO BOARD YET");
                StrUiState ui = hub.Ui;
                bool on = layer == StrLayers.Fronts ? ui.ShowFronts : layer == StrLayers.Operation ? ui.ShowOperation : ui.ShowSorties;
                hub.SetLayer(layer, !on);
                return new StrAct(StrWhy.None, "", true);
            });
        }

        /// <summary>The plate: three latched toggles that mirror the interface state, and the board's age.</summary>
        internal void BindPlate(StrViewModel vm, in StrUiState ui)
        {
            Bind(fronts, "layer.fronts", "FRONTS", ui.ShowFronts, "Platform badges and lane bars of every front.");
            Bind(operation, "layer.operation", "OPERATION", ui.ShowOperation, "The main effort, its axis, groups and wave route, and the proposals.");
            Bind(sorties, "layer.sorties", "SORTIES", ui.ShowSorties, "Sortie markers and zones, and the enemy command posts when the host reports any.");
            draw.Bind(controls, "layer.draw", "DRAW", () => StrMapOverlay.DrawArmed ? new StrAct(StrWhy.None, "") : StrGate.DrawGate(vm),
                "Draw your own offensive: press on a front, drag to the objective, release. Esc cancels.", StrMapOverlay.DrawArmed);
            string line = string.IsNullOrEmpty(Note) ? StrPanelText.LayerLine(vm) : Note;

            float x = 8f, y = 6f;
            float titleW = Mathf.Ceil(AvText.Width(plateTitle)) + 6f;
            AvLay.Place(plateTitle.rectTransform, x, y, titleW, ToggleH);
            x += titleW + 4f;
            x = Place(fronts, x, y);
            x = Place(operation, x, y);
            x = Place(sorties, x, y);
            x = Place(draw, x + 6f, y);
            if (line != lastLine)
            {
                lastLine = line;
                AvText.Set(plateLine, line);
            }
            plateLine.color = AvInk.Dim;
            plateTitle.color = AvInk.Key;
            float lineW = Mathf.Ceil(AvText.Width(plateLine));
            AvLay.Place(plateLine.rectTransform, 8f, y + ToggleH + 4f, Mathf.Max(lineW, 40f), 14f);
            plate.sizeDelta = new Vector2(Mathf.Max(x + 4f, 8f + lineW + 8f), PlateH);
            plateFrame.Paint(AvInk.Ground.WithAlpha(0.9f), AvInk.Frame.WithAlpha(0.9f));
        }

        private void Bind(StrButton button, string id, string text, bool on, string help) =>
            button.Bind(controls, id, text, () => new StrAct(StrWhy.None, ""), help, on);

        private float Place(StrButton button, float x, float y)
        {
            float w = Mathf.Ceil(AvText.Width(TextOf(button))) + 22f;
            button.Place(x, y, w, ToggleH);
            return x + w + 3f;
        }

        private static TMP_Text TextOf(StrButton button) => button.Control.GetComponentInChildren<TMP_Text>(true);

        /// <summary>The legend: one swatch and caption per shape the plan contains, flowed across the viewport width and wrapped to a second row when it must.</summary>
        internal void BindLegend(StrOverlayPlan plan, float width)
        {
            int signature = plan.LegendCount;
            for (int i = 0; i < plan.LegendCount; i++)
                signature = signature * 31 + plan.Legend[i].Label.GetHashCode() * 7 + (int)plan.Legend[i].Shape * 3 + (int)plan.Legend[i].Ink;
            if (legendBuilt && signature == legendSignature && Mathf.Abs(width - viewportWidth) < 1f) return;
            legendBuilt = true; legendSignature = signature; viewportWidth = width;

            legendCount = plan.LegendCount;
            legend.gameObject.SetActive(legendCount > 0 && plate.gameObject.activeSelf);
            float room = Mathf.Max(200f, width - 24f - 2f * LegendPad), x = LegendPad, row = 0f, widest = 0f;
            for (int i = 0; i < swatches.Length; i++)
            {
                bool on = i < plan.LegendCount;
                swatches[i].gameObject.SetActive(on);
                labels[i].gameObject.SetActive(on);
                if (!on) continue;
                StrLegendEntry e = plan.Legend[i];
                AvText.Set(labels[i], e.Label);
                labels[i].color = AvInk.Dim;
                float labelW = Mathf.Ceil(AvText.Width(labels[i])), entryW = SwatchW + 4f + labelW + 12f;
                if (x > LegendPad && x + entryW > room) { x = LegendPad; row += LegendRow + 2f; }
                swatches[i].rectTransform.anchoredPosition = new Vector2(x, -(LegendPad + row + LegendRow));
                AvLay.Place(labels[i].rectTransform, x + SwatchW + 4f, LegendPad + row, labelW + 2f, LegendRow);
                DrawSwatch(swatches[i], e);
                x += entryW;
                widest = Mathf.Max(widest, x);
            }
            legendHeight = LegendPad * 2f + row + LegendRow;
            legend.sizeDelta = new Vector2(Mathf.Min(room + 2f * LegendPad, widest + LegendPad - 6f), legendHeight);
            legendFrame.Paint(AvInk.Ground.WithAlpha(0.88f), AvInk.Frame.WithAlpha(0.8f));
        }

        private static readonly float[] swatchX = new float[3], swatchY = new float[3];

        private static void DrawSwatch(AvVector v, StrLegendEntry e)
        {
            AvQuadBuffer b = v.Buffer;
            b.Clear();
            Rgba ink = StrInk.RgbaOf(e.Ink), under = AvInk.Ground.WithAlpha(0.9f).ToRgba();
            float cx = SwatchW * 0.5f, cy = LegendRow * 0.5f;
            switch (e.Shape)
            {
                case StrShape.DashedArrow:
                case StrShape.Arrow:
                    StrStrokes.DashedArrow(b, 1f, cy, SwatchW - 1f, cy, 4f, 2.5f, 0f, StrShapeInk.StrokeWidth, 6f, ink);
                    break;
                case StrShape.DashedLine:
                    StrStrokes.DashedLine(b, 1f, cy, SwatchW - 1f, cy, 4f, 2.5f, 0f, StrShapeInk.StrokeWidth, ink);
                    break;
                case StrShape.Offensive:
                    swatchX[0] = 1f; swatchX[1] = SwatchW * 0.5f; swatchX[2] = SwatchW - 1f;
                    swatchY[0] = swatchY[1] = swatchY[2] = cy;
                    StrStrokes.Offensive(b, swatchX, swatchY, 3, 1.6f, 2.2f, 5f, 0.8f, new Rgba(ink.R, ink.G, ink.B, ink.A * 0.92f), new Rgba(0f, 0f, 0f, ink.A * 0.55f));
                    break;
                case StrShape.DashedRing:
                    AvStrokes.DashedRing(b, cx, cy, 6.5f, 8, 0.6f, StrShapeInk.StrokeWidth, ink);
                    break;
                case StrShape.Ring:
                    AvStrokes.Ring(b, cx, cy, 6.5f, 16, StrShapeInk.StrokeWidth, ink);
                    break;
                default:
                    StrShapeInk.Glyph(b, e.Shape, cx, cy, 1f, ink, under);
                    break;
            }
            v.Commit();
        }
    }
}

using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The click card of the selected item (annex 05 P10): a header row (chip, title, close), the item's words, and a button row only when the item has
    /// something to commit (pick or redirect, accept, send wing). Built once under the map viewport and registered with the pointer guard, so wheel, drag and clicks stop at it; it never raises itself.
    /// Every press goes through the hub, the same command path the panel uses, and a refused button shows its reason on the footer line.
    /// </summary>
    internal sealed class StrMapCardMarker : MonoBehaviour { }

    internal sealed class StrMapCard
    {
        internal const float Width = 236f, MaxHeight = 160f, Gap = 6f, Margin = 6f;
        private const float Pad = 6f, HeadH = 18f, ButtonH = 18f, CloseW = 18f;

        private readonly StrMapOverlay owner;
        private readonly StrControls controls;
        private readonly StrCardPlan plan;
        private readonly RectTransform root;
        private readonly AvFrame frame;
        private readonly StrSlab chip;
        private readonly TMP_Text title, reason;
        private readonly TMP_Text[] lines = new TMP_Text[StrCardPlan.MaxLines];
        private readonly StrButton primary, secondary, close;
        private Vector2 at = new Vector2(float.NaN, float.NaN);
        private float height;
        private StrItemId shown;

        internal StrMapCard(RectTransform viewport, StrMapOverlay owner, StrControls controls, StrCardPlan plan)
        {
            this.owner = owner;
            this.controls = controls;
            this.plan = plan;
            root = AvLay.Child(viewport, "StrMapCard");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(Width, 100f);
            frame = AvFrame.Add(root, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            frame.Stroke = 1.2f;
            frame.raycastTarget = true;
            chip = new StrSlab(root, "Chip");
            title = AvText.Make(root, "Title", AvTextRole.Head, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            title.raycastTarget = false;
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = AvText.Make(root, "Line " + i, i == 0 ? AvTextRole.DataSmall : AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                AvText.Fit(lines[i], true);
                lines[i].raycastTarget = false;
            }
            reason = AvText.Make(root, "Reason", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            AvText.Fit(reason, true);
            reason.raycastTarget = false;
            primary = new StrButton(root, "", AvButtonStyle.Primary, () => Run(plan.Primary));
            secondary = new StrButton(root, "", AvButtonStyle.Default, () => Run(plan.Secondary));
            close = new StrButton(root, "×", AvButtonStyle.Quiet, () => { owner.Hub?.ClearSelection(); return new StrAct(StrWhy.None, "", true); });
            root.gameObject.AddComponent<StrMapCardMarker>();
            root.gameObject.SetActive(false);
        }

        internal RectTransform Root => root;
        internal bool Visible => root.gameObject.activeSelf;

        // The one command path: the hub runs the gate again at press time, so a model that changed since the card was built cannot be pressed past its gate.
        private StrAct Run(in StrCardButton b)
        {
            IStrHub hub = owner.Hub;
            if (hub == null) return new StrAct(StrWhy.NoBoard, "NO BOARD YET");
            switch (b.Act)
            {
                case StrCardAct.Pick: return hub.Run(StrActionKind.Pick, b.Target);
                case StrCardAct.Accept: return hub.Run(StrActionKind.Accept, b.Target);
                default: return new StrAct(StrWhy.NoBoard, "NOT AN ACTION");
            }
        }

        /// <summary>Hides the card and forgets its buttons: no id is left that a sim press could poke.</summary>
        internal void Hide()
        {
            if (!root.gameObject.activeSelf) return;
            root.gameObject.SetActive(false);
            controls.Remove("map.card.primary");
            controls.Remove("map.card.secondary");
            controls.Remove("map.card.close");
            shown = StrItemId.None;
            at = new Vector2(float.NaN, float.NaN);
        }

        /// <summary>
        /// Shows the card for the plan beside the tag's rectangle, all in the viewport's local units (Y up), clamped inside the viewport. The content is
        /// rebound when the plan's item changed or <paramref name="rebind"/> says the model did; the card moves only when the tag moved a pixel or more.
        /// </summary>
        internal void Show(Rect tag, Rect viewport, float bottomReserve, bool rebind)
        {
            if (!root.gameObject.activeSelf) { root.gameObject.SetActive(true); rebind = true; }
            if (rebind || shown != plan.Id) { Bind(); shown = plan.Id; }

            float x = tag.xMax + Gap;
            if (x + Width > viewport.xMax - Margin) x = tag.xMin - Gap - Width;
            x = Mathf.Clamp(x, viewport.xMin + Margin, Mathf.Max(viewport.xMin + Margin, viewport.xMax - Margin - Width));
            // The card keeps clear of the legend strip along the bottom edge.
            float floor = viewport.yMin + Mathf.Max(Margin, bottomReserve) + height;
            float top = Mathf.Clamp(tag.yMax, floor, Mathf.Max(floor, viewport.yMax - Margin));
            var next = new Vector2(Mathf.Round(x), Mathf.Round(top));
            if (!float.IsNaN(at.x) && Mathf.Abs(next.x - at.x) < 1f && Mathf.Abs(next.y - at.y) < 1f) return;
            at = next;
            root.localPosition = new Vector3(next.x, next.y, 0f);
        }

        private void Bind()
        {
            Color ink = StrInk.Of(plan.Ink);
            bool hasChip = plan.Chip.Length > 0;
            chip.SetActive(hasChip);
            if (hasChip) chip.Set(plan.Chip, ink);
            string head = plan.Tag.Length > 0 ? plan.Tag + " " + plan.Title : plan.Title;
            AvText.Set(title, head);

            // Header: chip, title, and the close in the corner, so the close never costs a row of its own.
            float inner = Width - 2f * Pad, y = Pad;
            float chipW = hasChip ? chip.WidthFor() : 0f, titleX = Pad + (hasChip ? chipW + 4f : 0f);
            if (hasChip) chip.Place(Pad, y + 1f, chipW, HeadH - 2f);
            AvLay.Place(title.rectTransform, titleX, y, Width - Pad - CloseW - 4f - titleX, HeadH);
            close.Place(Width - Pad - CloseW, y, CloseW, HeadH);
            close.Bind(controls, "map.card.close", "×", () => new StrAct(StrWhy.None, ""), "Close this card. Esc does the same.");
            y += HeadH + 3f;
            for (int i = 0; i < lines.Length; i++)
            {
                bool on = i < plan.LineCount;
                lines[i].gameObject.SetActive(on);
                if (!on) continue;
                AvText.Set(lines[i], plan.Lines[i]);
                lines[i].color = i == 0 ? ink : AvInk.Dim;   // the first line is the item's state, in the item's own ink
                float h = AvText.Height(lines[i], inner);
                AvLay.Place(lines[i].rectTransform, Pad, y, inner, h);
                y += h + 2f;
            }
            bool hasReason = plan.Reason.Length > 0;
            reason.gameObject.SetActive(hasReason);
            if (hasReason)
            {
                AvText.Set(reason, plan.Reason);
                reason.color = AvInk.State(AvState.Caution);
                float h = AvText.Height(reason, inner);
                AvLay.Place(reason.rectTransform, Pad, y + 2f, inner, h);
                y += h + 4f;
            }

            // Buttons: none when there is nothing to commit; one verb gets the whole row, two split it 60/40.
            bool one = plan.Primary.Act != StrCardAct.None, two = one && plan.Secondary.Act != StrCardAct.None;
            primary.SetActive(one);
            secondary.SetActive(two);
            if (one)
            {
                y += 3f;
                float second = two ? Mathf.Floor((inner - 4f) * 0.4f) : 0f, first = inner - (two ? second + 4f : 0f);
                primary.Place(Pad, y, first, ButtonH);
                primary.Bind(controls, "map.card.primary", plan.Primary.Label, () => plan.Primary.Gate, Help(plan.Primary.Act));
                if (two)
                {
                    secondary.Place(Pad + first + 4f, y, second, ButtonH);
                    secondary.Bind(controls, "map.card.secondary", plan.Secondary.Label, () => plan.Secondary.Gate, Help(plan.Secondary.Act));
                }
                y += ButtonH;
            }
            else controls.Remove("map.card.primary");
            if (!two) controls.Remove("map.card.secondary");
            y += Pad;

            height = Mathf.Min(MaxHeight, y);
            root.sizeDelta = new Vector2(Width, height);
            frame.Paint(AvInk.Ground.WithAlpha(0.95f), ink.WithAlpha(0.9f));
            frame.BracketColor = ink;
            frame.Bracket = 4f;
            frame.SetVerticesDirty();
            at = new Vector2(float.NaN, float.NaN);   // the size changed, so the clamp must run again
        }

        private static string Help(StrCardAct act)
        {
            switch (act)
            {
                case StrCardAct.Pick: return "Make this the faction's main effort. The staff and the AI ground force carry it out.";
                case StrCardAct.Accept: return "Take this sortie. Finishing it counts toward the reinforcement wave.";
                default: return "";
            }
        }
    }
}

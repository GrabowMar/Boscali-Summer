using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The click card of the selected item (annex 05 P10): the item's words, and one button that commits (pick or redirect, accept, send wing) beside a second
    /// one and a close. Built once under the map viewport and registered with the pointer guard, so wheel, drag and clicks stop at it; it never raises itself.
    /// Every press goes through the hub, the same command path the panel uses, and a refused button shows its reason on the footer line.
    /// </summary>
    internal sealed class StrMapCardMarker : MonoBehaviour { }

    internal sealed class StrMapCard
    {
        internal const float Width = 320f, MaxHeight = 200f, Gap = 8f, Margin = 6f;
        private const float Pad = 8f, ButtonH = 20f, CloseW = 28f;

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
                case StrCardAct.SendWing: return hub.Run(StrActionKind.SendWing, b.Target);
                case StrCardAct.Recall: return hub.Run(StrActionKind.Recall, b.Target);
                case StrCardAct.Focus:
                    hub.Select(b.Target, true);
                    return new StrAct(StrWhy.None, "MAP FOCUSED", true);
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

            float inner = Width - 2f * Pad, y = Pad;
            float chipW = hasChip ? chip.WidthFor() : 0f;
            if (hasChip) chip.Place(Pad, y + 1f, chipW, 18f);
            AvLay.Place(title.rectTransform, Pad + (hasChip ? chipW + 6f : 0f), y, inner - (hasChip ? chipW + 6f : 0f), 20f);
            y += 24f;
            for (int i = 0; i < lines.Length; i++)
            {
                bool on = i < plan.LineCount;
                lines[i].gameObject.SetActive(on);
                if (!on) continue;
                AvText.Set(lines[i], plan.Lines[i]);
                lines[i].color = i == 0 ? AvInk.Ink : AvInk.Dim;
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
            y += 4f;

            // Buttons: the primary takes what the secondary and the close leave; a card with one verb gives it the whole row.
            bool two = plan.Secondary.Act != StrCardAct.None;
            float row = inner - CloseW - 4f, second = two ? Mathf.Floor((row - 4f) * 0.4f) : 0f, first = row - (two ? second + 4f : 0f);
            primary.Place(Pad, y, first, ButtonH);
            secondary.SetActive(two);
            if (two) secondary.Place(Pad + first + 4f, y, second, ButtonH);
            close.Place(Pad + row + 4f, y, CloseW, ButtonH);
            primary.Bind(controls, "map.card.primary", plan.Primary.Label, () => plan.Primary.Gate, Help(plan.Primary.Act));
            if (two) secondary.Bind(controls, "map.card.secondary", plan.Secondary.Label, () => plan.Secondary.Gate, Help(plan.Secondary.Act));
            else controls.Remove("map.card.secondary");
            close.Bind(controls, "map.card.close", "×", () => new StrAct(StrWhy.None, ""), "Close this card. Esc does the same.");
            y += ButtonH + Pad;

            height = Mathf.Min(MaxHeight, y);
            root.sizeDelta = new Vector2(Width, height);
            frame.Paint(AvInk.Ground.WithAlpha(0.95f), ink.WithAlpha(0.9f));
            frame.BracketColor = ink;
            frame.Bracket = 4f;
            at = new Vector2(float.NaN, float.NaN);   // the size changed, so the clamp must run again
        }

        private static string Help(StrCardAct act)
        {
            switch (act)
            {
                case StrCardAct.Pick: return "Make this the faction's main effort. The staff and the AI ground force carry it out.";
                case StrCardAct.Accept: return "Take this sortie. Finishing it counts toward the reinforcement wave.";
                case StrCardAct.SendWing: return "Task your recruited wing to this sortie. Their kills count toward it.";
                case StrCardAct.Recall: return "Recall the wing from this sortie.";
                case StrCardAct.Focus: return "Fly the map to this item.";
                default: return "";
            }
        }

        internal void Restyle()
        {
            primary.Restyle();
            secondary.Restyle();
            close.Restyle();
            shown = StrItemId.None;
        }
    }
}

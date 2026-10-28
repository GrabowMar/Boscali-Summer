using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// One pooled tag of the STR overlay: the plate that names an item on the map, its number in a box, its words, and for a front or the effort the same
    /// tug-of-war lane the panel draws. The plate is the only raycast object of an item (B4): the glyph under it is ink and takes no input. A click selects
    /// the item through the hub; the card commits. The tag is counter-scaled to the map image, so it is the same size on screen at every zoom.
    /// </summary>
    internal sealed class StrMapTag : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float RowH = 17f, Pad = 6f, LaneW = 84f, LaneH = 6f, MinW = 70f, MaxW = 300f, Gap = 4f, RestPad = 3f, RestLaneW = 40f;

        internal StrItemId Id;
        internal StrTagKind Kind;
        internal StrMapOverlay Owner;
        /// <summary>The anchor in map-local units, for the leader line and the placement.</summary>
        internal Vector2 AnchorLocal;

        private RectTransform root;
        private AvFrame plate;
        private StrSlab box, chip;
        private TMP_Text title, sub;
        private StrLaneBar lane;

        internal RectTransform Root => root;

        internal static StrMapTag Create(RectTransform parent, StrMapOverlay owner)
        {
            var go = new GameObject("StrMapTag", typeof(RectTransform));
            var tag = go.AddComponent<StrMapTag>();
            tag.Owner = owner;
            tag.Build(parent);
            return tag;
        }

        private void Build(RectTransform parent)
        {
            root = (RectTransform)transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            plate = AvFrame.Add(root, "Plate", AvChamfer.Diagonal(4f));
            AvLay.Fill(plate.rectTransform);
            plate.Stroke = 1f;
            plate.raycastTarget = true;
            box = new StrSlab(root, "Number");
            chip = new StrSlab(root, "Chip");
            title = AvText.Make(root, "Title", AvTextRole.Label, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            title.raycastTarget = false;
            sub = AvText.Make(root, "Sub", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(sub, false);
            sub.raycastTarget = false;
            lane = new StrLaneBar(root, "Lane");
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Fills the tag from its spec, sizes it and counter-scales it. It is not placed yet. A tag rests as one short row (the number, then the lane of a front
        /// or the item's first words); <paramref name="full"/> opens the second row with the place and the lane's words, for the plate under the cursor.
        /// </summary>
        internal void Show(in StrTagSpec s, float inverse, bool full)
        {
            Id = s.Id; Kind = s.Kind;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            root.localScale = new Vector3(inverse, inverse, 1f);

            Color ink = StrInk.Of(s.Ink);
            float fade = s.Ghost && !s.Hot && !s.Selected ? 0.6f : 1f;
            bool effort = s.Kind == StrTagKind.Effort, hasLane = s.Kind == StrTagKind.Front || effort;

            // The first word is the item's number ("P1", "O2", "S3") when it looks like one; it goes in a box. The effort is its front's plate ("P1 KESTREL
            // CROSSING") carrying the amber chip of the main effort, and its second row is the lane with the lane's own words.
            string number, rest, chipText = effort ? s.Text ?? "" : "", second = effort ? s.Note ?? "" : s.Sub ?? "";
            SplitNumber(effort ? s.Sub ?? "" : s.Text ?? "", out number, out rest);
            bool twoRows = full && (hasLane || (rest.Length > 0 && second.Length > 0)), laneFirst = hasLane && !twoRows;
            string row1 = laneFirst ? "" : rest.Length > 0 || twoRows ? rest : second;
            if (!twoRows) second = "";

            box.SetActive(number.Length > 0);
            if (number.Length > 0) box.SetOutline(number, ink.WithAlpha(fade));
            chip.SetActive(effort && chipText.Length > 0);
            if (effort) chip.Set(chipText, ink.WithAlpha(fade));
            AvText.Set(title, row1);
            title.gameObject.SetActive(row1.Length > 0);
            title.color = AvInk.Ink.WithAlpha(fade);
            AvText.Set(sub, second);
            sub.gameObject.SetActive(second.Length > 0);
            sub.color = (effort ? AvInk.Ink : ink).WithAlpha(fade);
            lane.SetActive(hasLane);
            if (hasLane) lane.Set(s.Value, s.Trend);

            // Size: row one is the box, the title (or, at rest, the lane) and the chip; row two is the lane and its words.
            float pad = full ? Pad : RestPad;
            float boxW = number.Length > 0 ? box.WidthFor() : 0f, chipW = effort && chipText.Length > 0 ? chip.WidthFor() : 0f;
            float titleW = row1.Length > 0 ? Mathf.Ceil(AvText.Width(title)) : 0f, lane1 = laneFirst ? RestLaneW : 0f;
            float w1 = boxW;
            if (titleW > 0f) w1 += (w1 > 0f ? Gap : 0f) + titleW;
            if (lane1 > 0f) w1 += (w1 > 0f ? Gap : 0f) + lane1;
            if (chipW > 0f) w1 += (w1 > 0f ? 2f * Gap : 0f) + chipW;
            float w2 = twoRows ? (hasLane ? LaneW + 6f : 0f) + (second.Length > 0 ? Mathf.Ceil(AvText.Width(sub)) : 0f) : 0f;
            float w = Mathf.Clamp(Mathf.Max(w1, w2) + 2f * pad, full ? MinW : 0f, MaxW), h = 2f * pad - 2f + RowH + (twoRows ? RowH - 2f : 0f);
            root.sizeDelta = new Vector2(w, h);

            float x = pad, top = pad - 1f;
            if (boxW > 0f) { box.Place(x, top, boxW, RowH - 2f); x += boxW + Gap; }
            float chipX = w - pad - chipW;
            AvLay.Place(title.rectTransform, x, top, Mathf.Max(20f, (chipW > 0f ? chipX - Gap : w - pad) - x), RowH - 2f);
            if (chipW > 0f) chip.Place(chipX, top, chipW, RowH - 2f);
            float y2 = top + RowH, sx = pad;
            if (laneFirst) lane.Place(x, h - pad - StrLaneBar.PadBottom - LaneH, lane1, LaneH);   // low in the row: the notch head needs the room above the track
            else if (hasLane) { lane.Place(pad, y2 + (RowH - 2f - LaneH) * 0.5f + 1f, LaneW, LaneH); sx += LaneW + 6f; }
            AvLay.Place(sub.rectTransform, sx, y2 - 1f, Mathf.Max(20f, w - sx - pad), RowH - 2f);

            Paint(ink, fade, s.Hot, s.Selected);
        }

        private void Paint(Color ink, float fade, bool hot, bool selected)
        {
            // At rest the plate is glass with a hairline, so the map reads through it; hover and selection bring the full ink back.
            bool lit = selected || hot;
            plate.Paint(AvInk.Ground.WithAlpha(lit ? 0.94f : 0.55f * fade), ink.WithAlpha(lit ? 1f : 0.38f * fade));
            plate.Stroke = lit ? 1.4f : 1f;
            plate.Bracket = selected ? 4f : 0f;
            plate.BracketColor = ink;
            plate.SetVerticesDirty();   // stroke and bracket are not colours, so Paint alone would not redraw them
        }

        // "P1 NORTH RIDGE" -> "P1" and "NORTH RIDGE"; a first word that is not a number-like token leaves the whole text as the title.
        private static void SplitNumber(string text, out string number, out string rest)
        {
            number = ""; rest = text;
            int space = text.IndexOf(' ');
            string head = space < 0 ? text : text.Substring(0, space);
            if (head.Length < 2 || head.Length > 3 || !char.IsLetter(head[0]) || !char.IsDigit(head[head.Length - 1])) return;
            for (int i = 1; i < head.Length; i++) if (!char.IsDigit(head[i])) return;
            number = head;
            rest = space < 0 ? "" : text.Substring(space + 1);
        }

        internal void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>The plate's rectangle in screen pixels, Y up.</summary>
        internal Rect ScreenBounds(Camera camera) => StrMapOverlay.ScreenRect(root, camera);

        /// <summary>Moves the tag so its plate is centred on a screen point.</summary>
        internal void PlaceAt(Vector2 screenCentre, Camera camera)
        {
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(root, screenCentre, camera, out Vector3 at)) root.position = at;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || Owner == null) return;
            // An armed Support or Wing pick owns the next click; a drag that ends on a tag is a drag, not a click.
            if (MapPicker.IsBusy || MfdMapInteractions.BlockIconClick() || ReliefNavigator.BlockIconClick()) return;
            MfdMapInteractions.ConsumeClick();
            Owner.Hub?.Select(Id, true);
        }

        public void OnPointerEnter(PointerEventData e) => Owner?.Hub?.Hover(Id);

        public void OnPointerExit(PointerEventData e) => Owner?.Hub?.Unhover(Id);
    }
}

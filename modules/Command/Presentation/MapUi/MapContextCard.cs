using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The map's right-click card (rule B: it opens on a right click that no armed tool or commanding selection claims). Built once per
    /// canvas and refilled on every open: a fill only changes text, icons and visibility, so a right click creates no objects (the old
    /// card built about forty per click, the hitch on open). Head (title, kind, the holder's tone on the rail), facts in two columns,
    /// then the actions under their group labels; two neighbouring Half actions share a line. Hidden on map close, destroyed on reset.
    /// </summary>
    internal static class MapContextCard
    {
        private const float Width = 276f, HeadH = 36f, CellH = 25f, RowH = 24f, GroupH = 15f, Gap = 2f, Inner = Width - 16f;
        /// <summary>The longest value that still reads in half the card's width at the data size.</summary>
        private const int HalfChars = 17;

        private sealed class Row
        {
            internal RectTransform Rect;
            internal AvFrame Back;
            internal TMP_Text Icon, Label;
            internal Action Invoke;
            internal bool Hover;
        }

        private static RectTransform root;
        private static AvFrame back;
        private static Image rail, split;
        private static TMP_Text title, kind;
        private static readonly List<TMP_Text> keys = new List<TMP_Text>(12), values = new List<TMP_Text>(12), groups = new List<TMP_Text>(4);
        private static readonly List<Row> rows = new List<Row>(16);

        internal static bool Visible => root != null && root.gameObject.activeSelf;

        /// <summary>Read-back for the automation hook: action lines and facts in the last fill, and the card's height.</summary>
        internal static int ShownActions { get; private set; }
        internal static int ShownFacts { get; private set; }
        internal static float ShownHeight => root != null ? root.sizeDelta.y : 0f;

        internal static bool Contains(Vector2 screen) => Visible && MapUiPointer.Contains(root, screen);

        internal static void Show(RectTransform parent, Vector2 screen, string head, string kindText, Color tone,
            List<MapMenuRow> facts, List<MapMenuAction> actions)
        {
            if (parent == null) return;
            if (root == null || root.parent != parent) Build(parent);
            root.gameObject.SetActive(true);

            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary), key = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            title.text = head;
            kind.text = kindText;
            kind.color = tone;
            rail.color = tone;

            // Two columns; a value too long for half the card takes a whole line (it spilled into its neighbour, 2026-10-29 capture).
            float column = (Inner - 8f) * .5f, row = HeadH;
            int col = 0;
            for (int i = 0; i < facts.Count; i++)
            {
                TMP_Text k = Slot(keys, i, AvTextRole.Micro), v = Slot(values, i, AvTextRole.DataSmall);
                bool wide = (facts[i].Value?.Length ?? 0) > HalfChars;
                if (wide && col == 1) { row += CellH; col = 0; }
                float x = 12f + col * (column + 8f), width = wide ? Inner - 4f : column;
                MfdChromeLay.Place(k.rectTransform, new Rect(x, -row, width, 11f));
                MfdChromeLay.Place(v.rectTransform, new Rect(x, -(row + 10f), width, 14f));
                k.text = facts[i].Key;
                k.color = key;
                v.text = facts[i].Value;
                v.color = facts[i].Tone.a > 0f ? facts[i].Tone : ink;
                if (wide || col == 1) { row += CellH; col = 0; }
                else col = 1;
            }
            if (col == 1) row += CellH;
            Park(keys, facts.Count);
            Park(values, facts.Count);

            float top = row + 4f;
            MfdChromeLay.Place(split.rectTransform, new Rect(8f, -top, Inner, 1f));
            top += 4f;
            int used = 0, labels = 0;
            string group = null;
            for (int i = 0; i < actions.Count; i++)
            {
                MapMenuAction a = actions[i];
                if (!string.IsNullOrEmpty(a.Group) && a.Group != group)
                {
                    group = a.Group;
                    TMP_Text g = Slot(groups, labels++, AvTextRole.Micro);
                    g.text = group;
                    g.color = AvStyleHost.FuiColor("muted", AvTheme.Dim);
                    MfdChromeLay.Place(g.rectTransform, new Rect(12f, -top, Inner - 4f, GroupH - 2f));
                    top += GroupH;
                }
                bool pair = a.Half && i + 1 < actions.Count && actions[i + 1].Half && actions[i + 1].Group == a.Group;
                float width = pair ? (Inner - Gap) * .5f : Inner;
                Fill(RowAt(used++), a, 8f, top, width);
                if (pair) Fill(RowAt(used++), actions[++i], 8f + width + Gap, top, width);
                top += RowH + Gap;
            }
            Park(groups, labels);
            for (int i = used; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(false);
            ShownActions = actions.Count;
            ShownFacts = facts.Count;

            float height = top + 5f;
            root.sizeDelta = new Vector2(Width, height);
            MfdChromeLay.Place(back.rectTransform, new Rect(0f, 0f, Width, height));
            MfdChromeLay.Place(rail.rectTransform, new Rect(1f, -6f, 3f, height - 12f));
            if (ToCanvas(parent, screen, out Vector2 at))
                root.anchoredPosition = new Vector2(Mathf.Clamp(at.x + 10f, 6f, parent.rect.width - Width - 6f),
                    Mathf.Clamp(at.y - 10f, height + 6f, parent.rect.height - 6f));
            root.SetAsLastSibling();
        }

        /// <summary>Cheap when already hidden: the map asks every frame while a tool is armed.</summary>
        internal static void Hide()
        {
            if (root == null || !root.gameObject.activeSelf) return;
            root.gameObject.SetActive(false);
            for (int i = 0; i < rows.Count; i++) rows[i].Invoke = null;
        }

        /// <summary>Scene reset: the pool goes with its canvas.</summary>
        internal static void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            keys.Clear();
            values.Clear();
            groups.Clear();
            rows.Clear();
        }

        private static void Build(RectTransform parent)
        {
            Destroy();
            var go = new GameObject("NOAvionics.MapContext", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0f, 1f);
            back = MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, Width, 100f),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.97f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame), AvChamfer.Diagonal(6f));
            rail = MfdChromeLay.Rule(root, "Tone", new Rect(1f, -6f, 3f, 80f), Color.clear);
            title = AvText.Make(root, "Title", AvTextRole.Head, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(title.rectTransform, new Rect(12f, -4f, Inner - 4f, 18f));
            title.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            AvText.Fit(title, false);
            kind = AvText.Make(root, "Kind", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(kind.rectTransform, new Rect(12f, -21f, Inner - 4f, 12f));
            AvText.Fit(kind, false);
            split = MfdChromeLay.Rule(root, "Split", new Rect(8f, -40f, Inner, 1f), AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            title.raycastTarget = kind.raycastTarget = rail.raycastTarget = split.raycastTarget = false;
        }

        private static TMP_Text Slot(List<TMP_Text> pool, int index, AvTextRole role)
        {
            while (pool.Count <= index)
            {
                TMP_Text t = AvText.Make(root, role + " " + pool.Count, role, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(t, false);
                t.raycastTarget = false;
                pool.Add(t);
            }
            TMP_Text slot = pool[index];
            if (!slot.gameObject.activeSelf) slot.gameObject.SetActive(true);
            return slot;
        }

        private static void Park(List<TMP_Text> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        private static Row RowAt(int index)
        {
            while (rows.Count <= index)
            {
                var row = new Row();
                row.Rect = AvLay.Child(root, "Action " + rows.Count);
                row.Back = AvFrame.Add(row.Rect, "Back", AvChamfer.Diagonal(3f));
                AvLay.Fill(row.Back.rectTransform);
                row.Icon = AvIcons.Make(row.Rect, AvIcon.Target, 15f, Color.white);
                MfdChromeLay.Place(row.Icon.rectTransform, new Rect(6f, -4f, 16f, 16f));
                row.Icon.raycastTarget = false;
                row.Label = AvText.Make(row.Rect, "Label", AvTextRole.Label, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(row.Label, false);
                row.Label.raycastTarget = false;
                AvHit hit = AvHit.On(row.Back);
                Row captured = row;
                hit.Hover = h => { captured.Hover = h; Paint(captured); };
                hit.Click = e =>
                {
                    if (e.button != PointerEventData.InputButton.Left) return;
                    AvInput.Deselect();
                    captured.Invoke?.Invoke();
                };
                rows.Add(row);
            }
            return rows[index];
        }

        private static void Fill(Row row, MapMenuAction action, float x, float y, float width)
        {
            row.Rect.gameObject.SetActive(true);
            MfdChromeLay.Place(row.Rect, new Rect(x, -y, width, RowH));
            MfdChromeLay.Place(row.Label.rectTransform, new Rect(26f, 0f, width - 30f, RowH));
            AvIcons.Set(row.Icon, action.Icon, 15f);
            row.Label.text = action.Label;
            row.Invoke = action.Invoke;
            row.Hover = false;
            Paint(row);
        }

        private static void Paint(Row row)
        {
            Color select = AvStyleHost.FuiColor("select", AvTheme.Accent);
            row.Back.Paint(row.Hover ? select.WithAlpha(.2f) : AvStyleHost.FuiColor("surface", AvTheme.Surface).WithAlpha(.55f),
                row.Hover ? select : AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            Color ink = row.Hover ? Color.white : AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            row.Label.color = ink;
            row.Icon.color = row.Hover ? Color.white : select;
        }

        private static bool ToCanvas(RectTransform parent, Vector2 screen, out Vector2 point)
        {
            Camera camera = MapUiPointer.UiCamera(parent.GetComponentInParent<Canvas>());
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out Vector2 local))
            {
                point = default;
                return false;
            }
            point = local + Vector2.Scale(parent.rect.size, parent.pivot);
            return true;
        }
    }
}

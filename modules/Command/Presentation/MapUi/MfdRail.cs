using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The MFD index: one column on the right of the maximised map with a key for every
    /// screen the display can show.
    ///
    /// <para>The rail owns its keys. The game's bezel buttons are stowed out of sight inside
    /// the rail (<see cref="Stowage"/>) and stay exactly as the game made them; a key press is
    /// forwarded to the stowed button's own <c>onClick</c>, so vanilla, the single-panel
    /// patch and every hosted screen still see the press they always saw. Nothing vanilla is
    /// repainted, which is why nothing has to be put back and why the game rewriting a bezel
    /// label can no longer disturb the rail.</para>
    ///
    /// <para>Every colour comes from the live kit sheet, and the rail carries its own
    /// <see cref="AvTicker"/>: a scheme change repaints it on the next frame, like any console.</para>
    /// </summary>
    internal static class MfdRail
    {
        /// <summary>The rail's object name on the map canvas; <see cref="MapUiPointer"/> guards its rectangle.</summary>
        public const string RailName = "NOAvionics.Rail";

        private static RectTransform rail;
        private static RectTransform stowage;
        private static Chrome chrome;
        private static Vector2 laidSize;
        private static readonly List<MfdRailKey> keys = new List<MfdRailKey>();

        /// <summary>The keys in display order.</summary>
        public static IReadOnlyList<MfdRailKey> Keys => keys;

        /// <summary>
        /// The inactive holder the game's bezel buttons wait in while the rail stands in for
        /// them. An inactive parent keeps them off screen whatever the game does to their own
        /// active flag on each map open.
        /// </summary>
        public static RectTransform Stowage => stowage;

        public static bool TryGetRail(out RectTransform rect)
        {
            rect = rail;
            return rail != null;
        }

        /// <summary>False when even bare 44px keys would not fit; the caller then leaves the stock bezel alone.</summary>
        public static bool Fits(float railHeight, int keyCount) => MfdRailPlan.Resolve(railHeight, keyCount, 0).Fits;

        /// <summary>Bezel slots that drive a screen. A spare slot is a live button with nothing behind it.</summary>
        public static int Count(List<Button> buttons, List<MFDScreen> screens)
        {
            if (buttons == null || screens == null) return 0;
            int count = 0;
            int length = Mathf.Min(buttons.Count, screens.Count);
            for (int i = 0; i < length; i++)
                if (buttons[i] != null && screens[i] != null) count++;
            return count;
        }

        /// <summary>Build (or move) the rail on the maximised map canvas.</summary>
        public static RectTransform Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return null;

            if (rail == null)
            {
                // A rail this session did not build has none of the parts the statics point at.
                Transform stale = canvas.transform.Find(RailName);
                if (stale != null) Kill(stale.gameObject);

                var go = new GameObject(RailName, typeof(RectTransform));
                rail = go.GetComponent<RectTransform>();
                rail.SetParent(canvas.transform, worldPositionStays: false);
                chrome = new Chrome(rail);
                stowage = AvLay.Child(rail, "Stowed bezel");
                stowage.gameObject.SetActive(false);

                AvTicker ticker = go.AddComponent<AvTicker>();
                ticker.Register(chrome);
                ticker.Add(-1, AvTickRate.Fast, Refresh);
            }

            rail.anchorMin = rail.anchorMax = rail.pivot = new Vector2(0.5f, 0.5f);
            rail.sizeDelta = new Vector2(columns.Rail.width, columns.Rail.height);
            rail.anchoredPosition = MfdLayout.CentreOf(columns.Rail);
            rail.localScale = Vector3.one;
            rail.SetAsLastSibling();

            if (rail.sizeDelta != laidSize) Lay();
            return rail;
        }

        /// <summary>
        /// Build the keys from the bezel's live slots, in the catalog's order. Safe to call
        /// on every structure pass: nothing is rebuilt unless a screen appeared or went.
        /// </summary>
        public static int Build(
            List<Button> left, List<MFDScreen> leftScreens,
            List<Button> right, List<MFDScreen> rightScreens)
        {
            if (rail == null) return 0;

            var slots = new List<Slot>();
            Collect(slots, left, leftScreens);
            Collect(slots, right, rightScreens);

            // Insertion sort: stable, so codes the catalog does not know keep the game's order.
            for (int i = 1; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                int j = i - 1;
                while (j >= 0 && slots[j].Entry.Rank > slot.Entry.Rank)
                {
                    slots[j + 1] = slots[j];
                    j--;
                }
                slots[j + 1] = slot;
            }

            // The same bezel as last time keeps its keys, and with them a pointer's hover.
            bool same = slots.Count == keys.Count;
            for (int i = 0; same && i < slots.Count; i++)
                same = keys[i].Source == slots[i].Button && keys[i].Screen == slots[i].Screen && keys[i].Entry.Code == slots[i].Entry.Code;
            if (same)
            {
                Refresh();
                return keys.Count;
            }

            for (int i = 0; i < keys.Count; i++) Kill(keys[i].Rect.gameObject);
            keys.Clear();
            for (int i = 0; i < slots.Count; i++)
                keys.Add(new MfdRailKey(rail, slots[i].Entry, i + 1, slots[i].Button, slots[i].Screen));

            Lay();
            Refresh();
            return keys.Count;
        }

        private struct Slot
        {
            public Button Button;
            public MFDScreen Screen;
            public MfdRailEntry Entry;
        }

        private static void Collect(List<Slot> into, List<Button> buttons, List<MFDScreen> screens)
        {
            if (buttons == null || screens == null) return;

            for (int i = 0; i < buttons.Count && i < screens.Count; i++)
            {
                if (buttons[i] == null || screens[i] == null) continue;
                into.Add(new Slot
                {
                    Button = buttons[i],
                    Screen = screens[i],
                    Entry = MfdRailCatalog.For(CodeOf(buttons[i], screens[i])),
                });
            }
        }

        private static string CodeOf(Button button, MFDScreen screen)
        {
            // The merged faction screen keeps a native HQ code underneath; the rail shows the merge.
            if (screen == FactionMfdMergePatch.Screen) return "FAC";
            if (!string.IsNullOrEmpty(screen.shortName)) return screen.shortName;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            return label == null ? null : label.text;
        }

        /// <summary>Light the key whose screen is on the panel. Runs on the rail's own 10 Hz tick and after a press.</summary>
        public static void Refresh()
        {
            for (int i = 0; i < keys.Count; i++)
                keys[i].SetLatched(keys[i].Screen != null && keys[i].Screen.isActive);
        }

        private static void Lay()
        {
            if (rail == null) return;

            float width = rail.sizeDelta.x;
            float height = rail.sizeDelta.y;
            int groups = 0;
            for (int i = 0; i < keys.Count; i++)
                if (i == 0 || keys[i].Entry.Group != keys[i - 1].Entry.Group) groups++;

            MfdRailPlan plan = MfdRailPlan.Resolve(height, keys.Count, groups);
            float y = MfdRailPlan.Pad;
            chrome.LayHeader(plan.Header, width, keys.Count);
            if (plan.Header) y += MfdRailPlan.HeaderHeight + MfdRailPlan.Gap;

            int labels = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (plan.Groups && (i == 0 || keys[i].Entry.Group != keys[i - 1].Entry.Group))
                {
                    chrome.LayGroup(labels++, MfdRailCatalog.GroupName(keys[i].Entry.Group), y, width);
                    y += MfdRailPlan.GroupHeight;
                }
                keys[i].Lay(new Rect(MfdRailPlan.Pad, -y, width - MfdRailPlan.Pad * 2f, plan.KeyHeight));
                y += plan.KeyHeight + MfdRailPlan.Gap;
            }
            chrome.HideGroupsFrom(labels);
            chrome.LayFooter(y, width, height);
            laidSize = rail.sizeDelta;
        }

        /// <summary>Forget the rail; the next map open builds its own.</summary>
        public static void Reset()
        {
            if (rail != null)
            {
                // The patch hands every bezel button back before it calls this. If one is
                // still here, destroying it with the rail would leave the game holding a dead
                // button, so it is released onto the canvas instead.
                while (stowage != null && stowage.childCount > 0)
                    stowage.GetChild(0).SetParent(rail.parent, worldPositionStays: false);
                Kill(rail.gameObject);
            }
            rail = null;
            stowage = null;
            chrome = null;
            laidSize = Vector2.zero;
            keys.Clear();
        }

        // Hidden first: Destroy lands at the end of the frame, and a rebuild in the same frame must not draw both.
        private static void Kill(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        internal static Color Role(string role, Color fallback) => AvStyleHost.FuiColor(role, fallback);

        /// <summary>The plate, the index header, the group labels and the scheme line; repaints the keys with itself.</summary>
        private sealed class Chrome : AvPart
        {
            private readonly AvFrame plate;
            private readonly AvFrame header;
            private readonly AvFrame slab;
            private readonly TMP_Text slabText;
            private readonly TMP_Text title;
            private readonly TMP_Text count;
            private readonly TMP_Text footer;
            private readonly List<TMP_Text> groupTexts = new List<TMP_Text>();
            private readonly List<Image> groupRules = new List<Image>();

            public Chrome(RectTransform root)
            {
                Rect = root;
                plate = AvFrame.Add(root, "Plate", AvChamfer.Diagonal(8f));
                AvLay.Fill(plate.rectTransform);
                plate.Stroke = 1f;
                plate.Bracket = 8f;

                header = AvFrame.Add(root, "Header", AvChamfer.Diagonal(4f));
                header.Stroke = 1f;
                slab = AvFrame.Add(header.rectTransform, "Slab", default(AvChamfer));
                slab.Stroke = 0f;
                slabText = AvText.Make(header.rectTransform, "Id", AvTextRole.Head, "MFD", TextAlignmentOptions.Center);
                title = AvText.Make(header.rectTransform, "Title", AvTextRole.Head, "INDEX");
                count = AvText.Make(header.rectTransform, "Count", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                footer = AvText.Make(root, "Scheme", AvTextRole.Micro, "", TextAlignmentOptions.Center);
                Restyle();
            }

            public void LayHeader(bool show, float width, int keyCount)
            {
                header.gameObject.SetActive(show);
                if (!show) return;

                float w = width - MfdRailPlan.Pad * 2f, h = MfdRailPlan.HeaderHeight;
                MfdChromeLay.Place(header.rectTransform, new Rect(MfdRailPlan.Pad, -MfdRailPlan.Pad, w, h));
                MfdChromeLay.Place(slab.rectTransform, new Rect(4f, -5f, 36f, h - 10f));
                MfdChromeLay.Place(slabText.rectTransform, new Rect(4f, -5f, 36f, h - 10f));
                MfdChromeLay.Place(title.rectTransform, new Rect(48f, 0f, w - 80f, h));
                MfdChromeLay.Place(count.rectTransform, new Rect(w - 34f, 0f, 26f, h));
                AvText.Set(count, MfdChromeLay.TwoDigits(keyCount));
            }

            public void LayGroup(int slot, string name, float y, float width)
            {
                while (groupTexts.Count <= slot)
                {
                    groupTexts.Add(AvText.Make(Rect, "Group", AvTextRole.Micro));
                    groupRules.Add(AvLay.Solid(Rect, "Group rule", Color.clear));
                    Restyle();
                }

                TMP_Text text = groupTexts[slot];
                text.gameObject.SetActive(true);
                groupRules[slot].gameObject.SetActive(true);
                AvText.Set(text, name);
                float left = MfdRailPlan.Pad + 4f;
                MfdChromeLay.Place(text.rectTransform, new Rect(left, -y, width - left * 2f, MfdRailPlan.GroupHeight));
                float ruleLeft = left + AvText.Width(text) + 6f;
                MfdChromeLay.Place(groupRules[slot].rectTransform,
                    new Rect(ruleLeft, -(y + MfdRailPlan.GroupHeight * 0.5f), Mathf.Max(0f, width - MfdRailPlan.Pad - 2f - ruleLeft), 1f));
            }

            public void HideGroupsFrom(int slot)
            {
                for (int i = slot; i < groupTexts.Count; i++)
                {
                    groupTexts[i].gameObject.SetActive(false);
                    groupRules[i].gameObject.SetActive(false);
                }
            }

            /// <summary>Spare height under the last key names the scheme in force, rather than sitting empty.</summary>
            public void LayFooter(float used, float width, float height)
            {
                bool show = height - used - MfdRailPlan.Pad >= 18f;
                footer.gameObject.SetActive(show);
                if (show) MfdChromeLay.Place(footer.rectTransform, new Rect(0f, -(height - MfdRailPlan.Pad - 18f), width, 18f));
            }

            public override void Restyle()
            {
                plate.Paint(AvStyleHost.FuiFill("console", AvTheme.Ground), AvStyleHost.FuiBorder("console", AvTheme.Frame));
                plate.BracketColor = Role("select", AvTheme.Accent).WithAlpha(0.8f);
                plate.SetVerticesDirty();
                header.Paint(AvStyleHost.FuiFill("header", AvTheme.Surface), AvStyleHost.FuiBorder("header", AvTheme.Hairline));
                slab.Paint(AvStyleHost.FuiFill("slab", AvTheme.Accent), Color.clear);
                slabText.color = AvStyleHost.FuiInk("slab", Color.black);
                title.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
                count.color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
                footer.color = Role("ink-muted", AvTheme.Disabled);
                AvText.Set(footer, "SCHEME  " + AvStyleHost.Theme.ToString().ToUpperInvariant());
                for (int i = 0; i < groupTexts.Count; i++)
                {
                    groupTexts[i].color = AvStyleHost.FuiInk("section-title", AvTheme.RailInfo);
                    groupRules[i].color = Role("hairline", AvTheme.Hairline);
                }
                for (int i = 0; i < keys.Count; i++) keys[i].Restyle();
            }
        }
    }

    /// <summary>
    /// One key of the MFD index: glyph, the game's short code, the word the code stands for,
    /// and its place in the index. The open screen's key carries the accent on its edge
    /// rail, corner brackets, glyph and lamp, and reads ON where its number was.
    /// </summary>
    internal sealed class MfdRailKey
    {
        private const string Class = "rail-key";
        private const float IconSize = 24f;
        private const float IconBay = 40f;
        private const float TextLeft = 48f;

        public readonly MfdRailEntry Entry;
        public readonly Button Source;
        public readonly MFDScreen Screen;

        private readonly AvFrame frame;
        private readonly AvFx fx;
        private readonly Image selection;
        private readonly Image divider;
        private readonly Image lamp;
        private readonly TMP_Text icon;
        private readonly TMP_Text code;
        private readonly TMP_Text name;
        private readonly TMP_Text index;
        private readonly string ordinal;
        private bool hover;
        private bool pressed;

        public RectTransform Rect { get; }
        public bool Latched { get; private set; }

        /// <summary>The glyph's present colour: the accent while the key's screen is open.</summary>
        public Color GlyphColor => icon.color;

        public MfdRailKey(RectTransform parent, MfdRailEntry entry, int position, Button source, MFDScreen screen)
        {
            Entry = entry;
            Source = source;
            Screen = screen;
            ordinal = MfdChromeLay.TwoDigits(position);

            Rect = AvLay.Child(parent, "Key " + entry.Code);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(5f));
            AvLay.Fill(frame.rectTransform);
            frame.Stroke = 1f;
            frame.Bracket = 5f;
            selection = AvLay.Solid(Rect, "Selection", Color.clear);
            divider = AvLay.Solid(Rect, "Divider", Color.clear);
            lamp = AvLay.Solid(Rect, "Lamp", Color.clear);
            icon = AvIcons.Make(Rect, entry.Glyph, IconSize, Color.white);
            code = AvText.Make(Rect, "Code", AvTextRole.Title, entry.Code);
            name = AvText.Make(Rect, "Name", AvTextRole.Micro, entry.Name);
            index = AvText.Make(Rect, "Index", AvTextRole.DataSmall, ordinal, TextAlignmentOptions.MidlineRight);

            AvHit hit = AvHit.On(frame);
            hit.Hover = on => { hover = on; Restyle(); };
            hit.Press = on => { pressed = on; Restyle(); };
            hit.Click = e => { if (e.button == PointerEventData.InputButton.Left) Press(); };
            fx = AvFx.On(frame);
            Restyle();
        }

        /// <summary>Forward the press to the stowed bezel button, whose own binding opens or closes the screen.</summary>
        public void Press()
        {
            if (Source != null) Source.onClick.Invoke();
            MfdRail.Refresh();
        }

        public void SetLatched(bool on)
        {
            if (Latched == on) return;
            Latched = on;
            if (on) fx.Play(AvFxKind.Shine, 0.2f);
            Restyle();
        }

        public void Lay(Rect slot)
        {
            float w = slot.width, h = slot.height;
            MfdChromeLay.Place(Rect, slot);
            MfdChromeLay.Place(selection.rectTransform, new Rect(0f, -3f, 3f, h - 6f));
            MfdChromeLay.Place(icon.rectTransform, new Rect((IconBay - IconSize) * 0.5f + 2f, -(h - IconSize) * 0.5f, IconSize, IconSize));
            MfdChromeLay.Place(divider.rectTransform, new Rect(IconBay, -8f, 1f, h - 16f));
            MfdChromeLay.Place(index.rectTransform, new Rect(w - 36f, -4f, 30f, 14f));
            MfdChromeLay.Place(lamp.rectTransform, new Rect(w - 12f, -(h - 12f), 6f, 6f));

            AvText.Size(code, Mathf.Clamp(h * 0.31f, 14f, 19f));
            float textWidth = w - TextLeft - 6f;
            if (Entry.HasName)
            {
                MfdChromeLay.Place(code.rectTransform, new Rect(TextLeft, -h * 0.13f, textWidth, h * 0.44f));
                MfdChromeLay.Place(name.rectTransform, new Rect(TextLeft, -h * 0.56f, textWidth, h * 0.30f));
            }
            else
            {
                MfdChromeLay.Place(code.rectTransform, new Rect(TextLeft, 0f, textWidth, h));
            }
        }

        public void Restyle()
        {
            string state = pressed ? "pressed" : hover ? "hover" : Latched ? "latched" : null;
            AvStyle style = AvStyleHost.FuiStyle(Class, state);
            Color hairline = MfdRail.Role("hairline", AvTheme.Hairline);
            Color edge = AvStyleHost.Resolve(style.Border, hairline);
            Color ink = MfdRail.Role("ink", AvTheme.TextPrimary);
            Color quiet = AvStyleHost.Resolve(style.Color, MfdRail.Role("ink-dim", AvTheme.Dim));
            Color accent = AvStyleHost.Resolve(AvStyleHost.FuiStyle(Class, "latched").Rail, MfdRail.Role("caution", AvTheme.Warning));

            frame.Paint(AvStyleHost.Resolve(style.Background, MfdRail.Role("surface-inert", AvTheme.Surface)), edge);
            frame.BracketColor = Latched ? accent : edge;
            frame.SetVerticesDirty();
            selection.color = Latched ? accent : Color.clear;
            divider.color = hairline;
            lamp.color = Latched ? accent : MfdRail.Role("inert", AvTheme.Disabled);
            icon.color = Latched ? accent : quiet;
            code.color = ink;
            name.color = Latched ? ink : quiet;
            index.color = Latched ? accent : MfdRail.Role("ink-muted", AvTheme.Disabled);
            AvText.Set(index, Latched ? "ON" : ordinal);
        }
    }
}

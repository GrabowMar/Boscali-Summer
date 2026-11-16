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
        private static readonly List<Slot> slots = new List<Slot>(16);

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
            return rail != null && rail.gameObject.activeInHierarchy;
        }

        /// <summary>Opens the screen behind <paramref name="code"/> unless it is already up (a second press would close it); false when no such key.</summary>
        public static bool Open(string code)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Entry.Code != code) continue;
                if (!keys[i].Latched) keys[i].Press();
                return true;
            }
            return false;
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

            if (!rail.gameObject.activeSelf) rail.gameObject.SetActive(true);
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

            slots.Clear();
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
                keys.Add(new MfdRailKey(rail, slots[i].Entry, slots[i].Button, slots[i].Screen));

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
                MfdRailEntry entry = MfdRailCatalog.For(CodeOf(buttons[i], screens[i]));
                if (MfdRailCatalog.OffRail(entry.Code)) continue;
                into.Add(new Slot { Button = buttons[i], Screen = screens[i], Entry = entry });
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
            {
                keys[i].RefreshAvailability();
                keys[i].SetLatched(keys[i].Entry.Code == "MAP" && MfdPresentation.Expanded
                    ? MfdMapControls.Shown && !MfdMapControls.Folded
                    : keys[i].Screen != null && keys[i].Screen.isActive);
            }
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

            // MISC stands on the rail's bottom edge, its group label over it; the spare height opens above it.
            int count = keys.Count;
            bool pinned = count > 1 && keys[count - 1].Entry.Code == "MISC";
            float pinTop = height - MfdRailPlan.Pad - plan.KeyHeight;
            if (pinned && plan.Groups && keys[count - 1].Entry.Group != keys[count - 2].Entry.Group) pinTop -= MfdRailPlan.GroupHeight;

            int labels = 0;
            for (int i = 0; i < count; i++)
            {
                if (pinned && i == count - 1) y = Mathf.Max(y, pinTop);
                if (plan.Groups && (i == 0 || keys[i].Entry.Group != keys[i - 1].Entry.Group))
                {
                    chrome.LayGroup(labels++, MfdRailCatalog.GroupName(keys[i].Entry.Group), y, width);
                    y += MfdRailPlan.GroupHeight;
                }
                keys[i].Lay(new Rect(MfdRailPlan.Pad, -y, width - MfdRailPlan.Pad * 2f, plan.KeyHeight));
                y += plan.KeyHeight + MfdRailPlan.Gap;
            }
            chrome.HideGroupsFrom(labels);
            laidSize = rail.sizeDelta;
        }

        /// <summary>Map closed: hide the rail and keep its keys; the next open shows it again instead of rebuilding (~20 ms).</summary>
        public static void Park()
        {
            if (rail == null) return;
            ReleaseStowed();
            rail.gameObject.SetActive(false);
        }

        // The patch hands every bezel button back before it parks or resets. If one is still
        // here, hiding or destroying it with the rail would strand the game's button, so it is
        // released onto the canvas instead.
        private static void ReleaseStowed()
        {
            while (stowage != null && stowage.childCount > 0)
                stowage.GetChild(0).SetParent(rail.parent, worldPositionStays: false);
        }

        /// <summary>Forget the rail; the next map open builds its own.</summary>
        public static void Reset()
        {
            if (rail != null)
            {
                ReleaseStowed();
                Kill(rail.gameObject);
            }
            rail = null;
            stowage = null;
            chrome = null;
            laidSize = Vector2.zero;
            keys.Clear();
            slots.Clear();
        }

        // Hidden first: Destroy lands at the end of the frame, and a rebuild in the same frame must not draw both.
        private static void Kill(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        internal static Color Role(string role, Color fallback) => AvStyleHost.FuiColor(role, fallback);

        /// <summary>The plate and the group labels; repaints the keys with itself.</summary>
        private sealed class Chrome : AvPart
        {
            private readonly AvFrame plate;
            private readonly List<TMP_Text> groupTexts = new List<TMP_Text>();
            private readonly List<Image> groupRules = new List<Image>();

            public Chrome(RectTransform root)
            {
                Rect = root;
                plate = AvFrame.Add(root, "Plate", AvChamfer.Diagonal(8f));
                AvLay.Fill(plate.rectTransform);
                plate.Stroke = 1f;
                plate.Bracket = 8f;

                Restyle();
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

            public override void Restyle()
            {
                plate.Paint(AvStyleHost.FuiFill("console", AvTheme.Ground), AvStyleHost.FuiBorder("console", AvTheme.Frame));
                plate.BracketColor = Role("select", AvTheme.Accent).WithAlpha(0.8f);
                plate.SetVerticesDirty();
                for (int i = 0; i < groupTexts.Count; i++)
                {
                    groupTexts[i].color = Role("ink-muted", AvTheme.Dim);
                    groupRules[i].color = Role("hairline", AvTheme.Hairline);
                }
                for (int i = 0; i < keys.Count; i++) keys[i].Restyle();
            }
        }
    }

    /// <summary>
    /// One key of the MFD index: glyph, the game's short code and the word the code stands for.
    /// The open screen owns the bright edge and filled row. Idle rows share the rail's glass plane.
    /// </summary>
    internal sealed class MfdRailKey
    {
        private const string Class = "rail-key";
        private const float IconSize = 20f;
        private const float IconBay = 34f;
        private const float TextLeft = 40f;

        public readonly MfdRailEntry Entry;
        public readonly Button Source;
        public readonly MFDScreen Screen;

        private readonly AvFrame frame;
        private readonly AvFx fx;
        private readonly AvHit hit;
        private readonly Image selection;
        private readonly Image divider;
        private readonly TMP_Text icon;
        private readonly TMP_Text code;
        private readonly TMP_Text name;
        private bool hover;
        private bool pressed;
        private bool available = true;

        public RectTransform Rect { get; }
        public bool Latched { get; private set; }

        public MfdRailKey(RectTransform parent, MfdRailEntry entry, Button source, MFDScreen screen)
        {
            Entry = entry;
            Source = source;
            Screen = screen;

            Rect = AvLay.Child(parent, "Key " + entry.Code);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(5f));
            AvLay.Fill(frame.rectTransform);
            frame.Stroke = 1f;
            frame.Bracket = 0f;
            selection = AvLay.Solid(Rect, "Selection", Color.clear);
            divider = AvLay.Solid(Rect, "Divider", Color.clear);
            icon = AvIcons.Make(Rect, entry.Glyph, IconSize, Color.white);
            code = AvText.Make(Rect, "Code", AvTextRole.Title, entry.Code);
            name = AvText.Make(Rect, "Name", AvTextRole.Micro, entry.Name);
            name.gameObject.SetActive(entry.HasName && entry.Name != entry.Code);
            hit = AvHit.On(frame);
            hit.Cue = AvUiCue.Open;
            hit.Refuses = true;
            hit.Hover = on => { hover = on; Restyle(); };
            hit.Press = on => { pressed = on; Restyle(); };
            hit.Click = e => { if (e.button == PointerEventData.InputButton.Left) Press(); };
            fx = AvFx.On(frame);
            Restyle();
        }

        /// <summary>Forward the press to the stowed bezel button, whose own binding opens or closes the screen.</summary>
        public void Press()
        {
            RefreshAvailability();
            if (!available) return;
            if (Entry.Code == "MAP" && MfdRailPatch.IsApplied)
            {
                MfdMapControls.Open();
                return;
            }
            Source.onClick.Invoke();
            MfdRail.Refresh();
        }

        public void RefreshAvailability()
        {
            // The native button is intentionally stowed under an inactive parent; its interactable
            // flag, not hierarchy activity, tells us whether the command can be used.
            bool next = Source != null && Source.enabled && Source.interactable;
            if (available == next) return;
            available = next;
            hit.Interactable = next;
            Restyle();
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
            MfdChromeLay.Place(selection.rectTransform, new Rect(0f, 0f, 3f, h));
            MfdChromeLay.Place(icon.rectTransform, new Rect((IconBay - IconSize) * 0.5f + 2f, -(h - IconSize) * 0.5f, IconSize, IconSize));
            MfdChromeLay.Place(divider.rectTransform, new Rect(TextLeft, -(h - 1f), w - TextLeft - 4f, 1f));

            AvText.Size(code, Mathf.Clamp(h * 0.29f, 14f, 18f));
            float textWidth = w - TextLeft - 6f;
            if (Entry.HasName && Entry.Name != Entry.Code)
            {
                MfdChromeLay.Place(code.rectTransform, new Rect(TextLeft, -h * 0.10f, textWidth, h * 0.44f));
                MfdChromeLay.Place(name.rectTransform, new Rect(TextLeft, -h * 0.56f, textWidth, Mathf.Max(16f, h * 0.30f)));
            }
            else
            {
                MfdChromeLay.Place(code.rectTransform, new Rect(TextLeft, 0f, textWidth, h));
            }
        }

        public void Restyle()
        {
            string state = !available ? "disabled" : pressed ? "pressed" : Latched ? "latched" : hover ? "hover" : null;
            AvStyle style = AvStyleHost.FuiStyle(Class, state);
            Color hairline = MfdRail.Role("hairline", AvTheme.Hairline);
            Color edge = AvStyleHost.Resolve(style.Border, hairline);
            Color ink = MfdRail.Role("ink", AvTheme.TextPrimary);
            Color quiet = AvStyleHost.Resolve(style.Color, MfdRail.Role("ink-dim", AvTheme.Dim));
            Color accent = AvStyleHost.Resolve(AvStyleHost.FuiStyle(Class, "latched").Rail, MfdRail.Role("select", AvTheme.Accent));

            bool active = available && (Latched || hover || pressed);
            Color fill = active ? AvStyleHost.Resolve(style.Background, MfdRail.Role("surface-raised", AvTheme.Surface)) : Color.clear;
            // Selected + hovered remains selected; the brighter edge supplies immediate feedback.
            if (Latched && hover && available) edge = Color.Lerp(edge, ink, .25f);
            frame.Paint(fill, active ? edge : Color.clear);
            selection.color = Latched ? accent.WithAlpha(available ? accent.a : accent.a * .4f)
                : hover && available ? accent.WithAlpha(accent.a * .45f) : Color.clear;
            divider.color = active ? Color.clear : hairline;
            icon.color = available && Latched ? accent : quiet;
            code.color = available ? Latched ? accent : ink : quiet;
            name.color = quiet;
        }
    }
}

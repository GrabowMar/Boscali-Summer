using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// The control rail: one thin column on the right of the maximised map holding every
    /// button on the screen.
    ///
    /// The stock layout hangs a bezel column down each side of the map and leaves the mods'
    /// map-overlay toggles floating somewhere else again, so the controls for one screen sit
    /// in three places. Collecting them into a single rail gives the map one contiguous
    /// rectangle, the panels one column of their own, and the player one place to look for
    /// anything clickable.
    ///
    /// Each borrowed button is branded with a vector glyph, the game's own short code, and
    /// the word that code stands for, and lights up while its screen is the open one. The
    /// code still leads because it is the dual-mod protocol's identity; the glyph and the
    /// descriptor stop it from being a wall of three-letter noise.
    ///
    /// <para><b>Cross-mod seam.</b> The rail object is named <see cref="RailName"/> and that
    /// name is the whole contract. Boscali Summer finds it with <c>GameObject.Find</c> and
    /// parents its overlay toggles in; it does not reference this assembly, and nothing of a
    /// custom type crosses the boundary. The <c>AppDomain</c> channel the rest of
    /// <c>NOAvionics</c> uses cannot carry a <c>RectTransform</c>, which is why this seam is
    /// a name rather than a registry entry.</para>
    /// </summary>
    internal static class MfdRail
    {
        /// <summary>
        /// The well-known name Boscali Summer looks for. Changing it is a breaking change to
        /// the dual-mod contract and belongs in <c>Avionics/README.md</c> along with the
        /// bezel and picker rules.
        /// </summary>
        public const string RailName = "NOAvionics.Rail";

        /// <summary>
        /// Height of one button in the rail.
        ///
        /// Deliberately larger than the stock bezel button. Those are small because they are
        /// squeezed into a cockpit frame; a rail with a whole screen edge to itself has no
        /// reason to inherit that constraint, and these are clicked mid-flight.
        /// </summary>
        private const float ButtonHeight = 58f;

        /// <summary>Vertical gap between buttons.</summary>
        private const float ButtonGap = 12f;

        private const float CompactGap = 4f;

        /// <summary>Desktop pointer target floor; below this the stock two-column bezel wins.</summary>
        private const float MinimumButtonHeight = 44f;

        /// <summary>Inset from the rail's own edges to a button.</summary>
        private const float RailPad = 8f;

        /// <summary>
        /// Label size fallback for a button the catalog could not brand. Branded buttons
        /// derive the code and descriptor sizes from their own slot height.
        /// </summary>
        private const float LabelSize = 24f;

        /// <summary>Icon box at the left of a branded button.</summary>
        private const float IconSize = 24f;

        private const float IconInset = 10f;

        /// <summary>Where a branded label starts, clear of the icon column.</summary>
        private const float LabelInset = 42f;

        /// <summary>Distance from the top of the rail to the first button.</summary>
        private const float RailTop = 8f;

        private static RectTransform rail;
        private static float effectiveButtonHeight = ButtonHeight;
        private static float effectiveButtonGap = ButtonGap;
        private static float effectiveLabelSize = LabelSize;

        /// <summary>Y offset of the next free slot, measured down from the rail's top.</summary>
        private static float cursor;

        /// <summary>
        /// The rail, if one has been built this mission.
        ///
        /// Wing Command's own callers use this; Boscali finds the same object by name.
        /// </summary>
        public static bool TryGetRail(out RectTransform rect)
        {
            rect = rail;
            return rail != null;
        }

        /// <summary>Build (or re-find) the rail on the maximised map canvas.</summary>
        public static RectTransform Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return null;

            if (rail == null)
            {
                // A previous mission's rail can outlive this static field if the scene kept
                // the canvas; adopt it rather than building a second one.
                Transform existing = canvas.transform.Find(RailName);
                rail = existing as RectTransform;
            }

            if (rail == null)
            {
                var go = new GameObject(RailName, typeof(RectTransform));
                rail = go.GetComponent<RectTransform>();
                rail.SetParent(canvas.transform, worldPositionStays: false);
            }

            rail.anchorMin = rail.anchorMax = rail.pivot = new Vector2(0.5f, 0.5f);
            rail.sizeDelta = new Vector2(columns.Rail.width, columns.Rail.height);
            rail.anchoredPosition = MfdLayout.CentreOf(columns.Rail);
            rail.localScale = Vector3.one;
            rail.SetAsLastSibling();

            cursor = RailTop;
            return rail;
        }

        /// <summary>
        /// Choose the largest rail keys that fit the live screen count. Returns false when
        /// even the 44px pointer-target floor would not fit, allowing the caller to leave
        /// the stock two-column bezel untouched.
        /// </summary>
        public static bool PrepareCapacity(float railHeight, int itemCount)
        {
            effectiveButtonHeight = ButtonHeight;
            effectiveButtonGap = ButtonGap;
            effectiveLabelSize = LabelSize;
            if (itemCount <= 0) return true;

            float roomy = RailTop + RailPad + itemCount * ButtonHeight +
                          Mathf.Max(0, itemCount - 1) * ButtonGap;
            if (roomy <= railHeight) return true;

            effectiveButtonGap = CompactGap;
            effectiveButtonHeight = (railHeight - RailTop - RailPad -
                                     Mathf.Max(0, itemCount - 1) * CompactGap) / itemCount;
            if (effectiveButtonHeight < MinimumButtonHeight)
            {
                effectiveButtonHeight = ButtonHeight;
                effectiveButtonGap = ButtonGap;
                return false;
            }

            effectiveLabelSize = Mathf.Clamp(effectiveButtonHeight * 0.42f, 16f, LabelSize);
            return true;
        }

        public static int Count(List<Button> buttons, List<MFDScreen> screens)
        {
            if (buttons == null || screens == null) return 0;
            int count = 0;
            int length = Mathf.Min(buttons.Count, screens.Count);
            for (int i = 0; i < length; i++)
                if (buttons[i] != null && screens[i] != null) count++;
            return count;
        }

        /// <summary>
        /// Claim the next slot down the rail.
        ///
        /// Both this mod's buttons and Boscali's overlay toggles advance the same cursor, so
        /// the two mods' controls stack rather than overlapping — without either needing to
        /// know how many buttons the other added.
        /// </summary>
        public static Rect NextSlot(float height = -1f)
        {
            if (height <= 0f) height = effectiveButtonHeight;
            float width = rail != null ? rail.rect.width - RailPad * 2f : MfdLayout.RailWidth - RailPad * 2f;
            var slot = new Rect(RailPad, -cursor, width, height);
            cursor += height + effectiveButtonGap;
            return slot;
        }

        /// <summary>Forget the rail at the end of a mission; the next scene builds its own.</summary>
        public static void Reset()
        {
            if (rail != null) Object.Destroy(rail.gameObject);
            rail = null;
            cursor = RailTop;
            effectiveButtonHeight = ButtonHeight;
            effectiveButtonGap = ButtonGap;
            effectiveLabelSize = LabelSize;
        }

        // ------------------------------------------------------------------- restyling

        /// <summary>
        /// What a vanilla bezel button looked like before the rail repainted it, so
        /// <c>Minimize</c> can put it back exactly.
        /// </summary>
        internal sealed class ButtonSkin
        {
            public Button Owner;
            public Selectable.Transition Transition;
            public ColorBlock Colors;

            public Image Background;
            public Sprite Sprite;
            public Image.Type Type;
            public Color Color;

            public TMP_Text Label;
            public Color LabelColor;
            public float LabelSize;
            public float LabelSizeMin;
            public float LabelSizeMax;
            public FontStyles LabelStyle;
            public bool LabelAutoSize;
            public float LabelSpacing;
            public string LabelText;
            public bool LabelRichText;
            public TextAlignmentOptions LabelAlignment;
            public RectTransform LabelRect;
            public Vector2 LabelAnchorMin;
            public Vector2 LabelAnchorMax;
            public Vector2 LabelPivot;
            public Vector2 LabelPosition;
            public Vector2 LabelSizeDelta;

            /// <summary>The mod click cue added to a borrowed native button; removed on restore.</summary>
            public AvClickSound Click;

            /// <summary>The crisp frame drawn over the fill, plus the branded glyph the rail
            /// added; destroyed on restore rather than undone value-by-value.</summary>
            public GameObject Decoration;
            public MfdGlyph Icon;

            /// <summary>
            /// The ownership mark the rail leaves on the button. It carries the branded
            /// text so a repeated adoption pass can re-assert the line instead of
            /// branding its own output again.
            /// </summary>
            public MfdRailBrand Brand;

            /// <summary>Fill a branded button returns to when it is not the open screen.</summary>
            internal Color RestFill;
            private bool latched;

            /// <summary>
            /// Whether this is the screen currently on the panel. Vanilla has no visual state
            /// for the adopted buttons, so the rail has to say which one is open.
            /// </summary>
            public void SetLatched(bool on)
            {
                if (latched == on) return;
                latched = on;
                if (Background != null)
                {
                    Background.color = on
                        ? AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(),
                                                      AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                        : RestFill;
                }
                if (Icon != null) Icon.color = on ? AvTheme.Accent : AvTheme.TextPrimary;
            }

            public void Restore()
            {
                if (Owner != null)
                {
                    Owner.transition = Transition;
                    Owner.colors = Colors;
                }

                if (Click != null) Object.Destroy(Click);
                Click = null;

                if (Background != null)
                {
                    Background.sprite = Sprite;
                    Background.type = Type;
                    Background.color = Color;
                }

                if (Label != null)
                {
                    Label.enableAutoSizing = LabelAutoSize;
                    Label.fontSizeMin = LabelSizeMin;
                    Label.fontSizeMax = LabelSizeMax;
                    Label.color = LabelColor;
                    Label.fontSize = LabelSize;
                    Label.fontStyle = LabelStyle;
                    Label.characterSpacing = LabelSpacing;
                    Label.alignment = LabelAlignment;
                    Label.richText = LabelRichText;
                    Label.text = LabelText;
                }

                if (LabelRect != null)
                {
                    LabelRect.anchorMin = LabelAnchorMin;
                    LabelRect.anchorMax = LabelAnchorMax;
                    LabelRect.pivot = LabelPivot;
                    LabelRect.anchoredPosition = LabelPosition;
                    LabelRect.sizeDelta = LabelSizeDelta;
                }

                if (Decoration != null) Object.Destroy(Decoration);
                Decoration = null;
                Icon = null;

                // Disabled first: Unity destroys at end of frame (and not at all in edit
                // mode), so a same-frame restore-and-rebuild must already see the button
                // as unmarked rather than skip its own fresh adoption.
                if (Brand != null)
                {
                    Brand.enabled = false;
                    Object.Destroy(Brand);
                    Brand = null;
                }
            }

            /// <summary>
            /// Put the rail's line back when the game has reset the label underneath it.
            ///
            /// <c>InfoPanel_Faction</c> calls <c>VirtualMFD.SetupButtons</c> whenever a
            /// faction panel refreshes, which rewrites every bezel label as its short
            /// code. The rail owns the borrowed label while the map is maximised, so it
            /// restores the branded line on the next tick.
            /// </summary>
            public void Reassert()
            {
                if (Brand != null) Brand.Reassert();
            }
        }

        /// <summary>
        /// Repaint a stock bezel button in the panels' language, and return what it was.
        ///
        /// A vanilla <c>BDF</c> button next to the mod's <c>WMC</c> button used to be two
        /// visibly different controls doing the same job. Only the button's own
        /// <c>Image</c> and label are touched — no hierarchy changes — so restoring is a
        /// matter of putting four values back.
        /// </summary>
        public static ButtonSkin Restyle(Button button)
        {
            if (button == null) return null;

            var skin = new ButtonSkin();

            Image bg = button.GetComponent<Image>();
            if (bg != null)
            {
                skin.Background = bg;
                skin.Sprite = bg.sprite;
                skin.Type = bg.type;
                skin.Color = bg.color;

                // A dark, near-opaque key rather than a washed-out grey: the rail sits over
                // a bright map, so anything translucent reads as murky. The accent frame is
                // baked into the sliced Control sprite; the fill just needs to be black
                // enough to make the label pop.
                bg.sprite = AvSprites.Control;
                bg.type = Image.Type.Sliced;
                bg.color = AvTheme.Unity(Rgba.Shade(0.55f));
                skin.RestFill = bg.color;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                skin.Label = label;
                skin.LabelColor = label.color;
                skin.LabelSize = label.fontSize;
                skin.LabelSizeMin = label.fontSizeMin;
                skin.LabelSizeMax = label.fontSizeMax;
                skin.LabelStyle = label.fontStyle;
                skin.LabelAutoSize = label.enableAutoSizing;
                skin.LabelSpacing = label.characterSpacing;
                skin.LabelText = label.text;
                skin.LabelRichText = label.richText;
                skin.LabelAlignment = label.alignment;
                RectTransform labelRect = label.rectTransform;
                if (labelRect != null)
                {
                    skin.LabelRect = labelRect;
                    skin.LabelAnchorMin = labelRect.anchorMin;
                    skin.LabelAnchorMax = labelRect.anchorMax;
                    skin.LabelPivot = labelRect.pivot;
                    skin.LabelPosition = labelRect.anchoredPosition;
                    skin.LabelSizeDelta = labelRect.sizeDelta;
                }

                // The stock bezel label is auto-sized to fit a small cockpit button, and
                // while enableAutoSizing is on TMP recomputes the size every layout and
                // discards whatever fontSize is assigned. Turning it off — and pinning the
                // min/max it would otherwise clamp to — is what makes the rail's type take.
                label.enableAutoSizing = false;
                label.fontSizeMin = effectiveLabelSize;
                label.fontSizeMax = effectiveLabelSize;
                label.color = AvTheme.TextPrimary;
                label.fontSize = effectiveLabelSize;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 1f;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                // Branding composes a two-line label with a size/colour tag; a label cloned
                // from a prefab that disabled rich text would print those tags verbatim.
                label.richText = true;
            }

            // A stock bezel button is a Selectable, so a flight stick can steer focus onto it
            // and fire it. Every control the mods build already disables that; the vanilla
            // ones the rail adopts have to be disarmed too.
            AvInput.StripNavigation(button);

            // The stock transition is SpriteSwap, so hovering would swap the restyled flat
            // fill for a vanilla highlight frame. ColorTint multiplies the same dark key
            // instead, keeping hover and press inside the rail's own language.
            skin.Owner = button;
            skin.Transition = button.transition;
            skin.Colors = button.colors;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f),
                pressedColor = new Color(0.70f, 0.70f, 0.70f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.50f, 0.50f, 0.60f, 0.60f),
                colorMultiplier = 1f,
                fadeDuration = 0.06f,
            };

            // The vanilla key has no avionics click; lend it one while the rail wears it.
            skin.Click = button.GetComponent<AvClickSound>();
            if (skin.Click == null) skin.Click = button.gameObject.AddComponent<AvClickSound>();

            return skin;
        }

        /// <summary>Place a button into a rail slot.</summary>
        public static void Place(Button button, Rect slot)
        {
            if (button == null || rail == null) return;

            Transform t = button.transform;
            if (t.parent != rail) t.SetParent(rail, worldPositionStays: false);

            var rt = button.GetComponent<RectTransform>();
            if (rt == null) return;

            AvKit.Place(rt, slot);
            rt.localRotation = Quaternion.identity;
            t.SetAsLastSibling();
        }

        /// <summary>
        /// A crisp frame and corner ticks over the flat fill, plus the branded icon and
        /// two-line label.
        ///
        /// <c>AvSprites.Control</c>'s baked edge is tinted the same colour as the fill,
        /// so a single flat Image reads as one dark blob with no definition; this draws the
        /// accent border and ticks as a child instead, and is torn down as one object on
        /// restore rather than undone value by value.
        /// </summary>
        private static void Decorate(Button button, ButtonSkin skin, Rect slot)
        {
            if (button == null || skin == null) return;

            var go = new GameObject("AvDecoration", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(button.transform, worldPositionStays: false);
            AvKit.Stretch(rt);
            rt.SetAsLastSibling();

            var area = new Rect(0f, 0f, slot.width, slot.height);
            AvKit.Outline(rt, area, AvTheme.Frame);
            AvKit.CornerTicks(rt, area, AvTheme.Hairline, 5f);

            var brand = button.gameObject.AddComponent<MfdRailBrand>();
            brand.Label = skin.Label;
            skin.Brand = brand;

            Brand(button, skin, rt, slot);
            skin.Decoration = go;
        }

        /// <summary>
        /// Add the glyph and the code-plus-descriptor label to a button the rail borrowed.
        /// The game's own code leads; the descriptor is the part a new player can read.
        /// </summary>
        private static void Brand(Button button, ButtonSkin skin, RectTransform decoration, Rect slot)
        {
            TMP_Text label = skin.Label;
            if (label == null) return;

            AvKit.Place(label.rectTransform,
                new Rect(LabelInset, 0f, Mathf.Max(0f, slot.width - LabelInset - RailPad), slot.height));
            label.alignment = TextAlignmentOptions.MidlineLeft;

            string raw = label.text;
            MfdRailEntry entry = MfdRailCatalog.For(raw);
            float codeSize = Mathf.Clamp(slot.height * 0.31f, 13f, 18f);
            float nameSize = Mathf.Max(AvTokens.FontMicro, Mathf.Clamp(slot.height * 0.18f, AvTokens.FontMicro, 11f));
            label.fontSize = codeSize;
            label.fontSizeMin = codeSize;
            label.fontSizeMax = codeSize;

            if (string.IsNullOrEmpty(entry.Code))
            {
                label.text = MfdRailCatalog.Sanitise(raw, 8);
                Remember(skin, null, label.text);
                return;
            }

            label.text = entry.HasName
                ? entry.Code + "\n<size=" +
                  nameSize.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                  "><color=#" + ColorUtility.ToHtmlStringRGB(AvTheme.Dim) + ">" + entry.Name +
                  "</color></size>"
                : entry.Code;
            Remember(skin, entry.Code, label.text);

            var iconObject = new GameObject("RailIcon", typeof(RectTransform), typeof(MfdGlyph));
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(decoration, worldPositionStays: false);
            AvKit.Place(iconRect, new Rect(IconInset, -(slot.height - IconSize) * 0.5f, IconSize, IconSize));

            MfdGlyph icon = iconObject.GetComponent<MfdGlyph>();
            icon.raycastTarget = false;
            icon.SetKind(string.IsNullOrEmpty(entry.Glyph) ? "list" : entry.Glyph, AvTheme.TextPrimary);
            skin.Icon = icon;
        }

        /// <summary>Record what the rail wrote, so a later pass can restore the exact line.</summary>
        private static void Remember(ButtonSkin skin, string code, string text)
        {
            if (skin == null || skin.Brand == null) return;
            skin.Brand.Code = code;
            skin.Brand.Text = text;
        }

        /// <summary>
        /// Lay both bezel columns down the rail in the catalog's preferred order.
        ///
        /// The game's columns arrive left-then-right, which put BDF at the top of the rail
        /// and PALA at the top of the right column. Sorting the merged sequence stably lets
        /// the catalog's lead codes sit together while every unranked button keeps the
        /// order the game gave it. Only placement changes: each button keeps its own click
        /// binding and screen.
        /// </summary>
        public static int Adopt(
            List<Button> left, List<MFDScreen> leftScreens,
            List<Button> right, List<MFDScreen> rightScreens,
            List<ButtonSkin> skins)
        {
            var pairs = new List<Pair>();
            Collect(pairs, left, leftScreens);
            Collect(pairs, right, rightScreens);

            // Insertion sort: at a dozen buttons the stable order of equal ranks is worth
            // more than the asymptotic win of List.Sort, which is not stable.
            for (int i = 1; i < pairs.Count; i++)
            {
                Pair pair = pairs[i];
                int j = i - 1;
                while (j >= 0 && pairs[j].Rank > pair.Rank)
                {
                    pairs[j + 1] = pairs[j];
                    j--;
                }
                pairs[j + 1] = pair;
            }

            int adopted = 0;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (AdoptOne(pairs[i].Button, pairs[i].Screen, skins)) adopted++;
            }
            return adopted;
        }

        private struct Pair
        {
            public Button Button;
            public MFDScreen Screen;
            public int Rank;
        }

        private static void Collect(List<Pair> into, List<Button> buttons, List<MFDScreen> screens)
        {
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                TMP_Text label = button == null ? null : button.GetComponentInChildren<TMP_Text>(true);
                into.Add(new Pair
                {
                    Button = button,
                    Screen = screens != null && i < screens.Count ? screens[i] : null,
                    Rank = MfdRailCatalog.OrderRank(RankCode(button, label)),
                });
            }
        }

        /// <summary>
        /// The live ownership mark on a button, or null. Markers are released by being
        /// disabled before destruction, so a released mark must never count as one.
        /// </summary>
        private static MfdRailBrand FindBrand(Button button)
        {
            if (button == null) return null;
            MfdRailBrand[] marks = button.GetComponents<MfdRailBrand>();
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i] != null && marks[i].enabled) return marks[i];
            }
            return null;
        }

        /// <summary>
        /// The clean short code behind a button's current label. A button the rail already
        /// wears reports its branded line, not a code; the mark carries the code the
        /// catalog matched so the lead ordering survives repeated adoption passes.
        /// </summary>
        private static string RankCode(Button button, TMP_Text label)
        {
            MfdRailBrand brand = FindBrand(button);
            if (brand != null && !string.IsNullOrEmpty(brand.Code)) return brand.Code;
            return label == null ? null : label.text;
        }

        private static bool AdoptOne(Button button, MFDScreen screen, List<ButtonSkin> skins)
        {
            // The stock bezel carries six slots per column with about three filled. A
            // spare is a live GameObject with no screen behind it — vanilla only clears
            // its Behaviour.enabled — so filtering on activeSelf would have put five
            // blank buttons in the rail. Ask whether the slot drives a screen instead.
            if (button == null || screen == null) return false;

            // A repeated maximise offers buttons the rail already wears. Branding again
            // would read the rail's own rich text back as a short code and paint the
            // button with sanitised fragments ("BDFSIZE="), so the mark only re-asserts.
            MfdRailBrand branded = FindBrand(button);
            if (branded != null)
            {
                branded.Reassert();
                return false;
            }

            ButtonSkin skin = Restyle(button);
            if (skin != null && skins != null) skins.Add(skin);

            Rect slot = NextSlot();
            Place(button, slot);
            Decorate(button, skin, slot);
            return true;
        }
    }

    /// <summary>
    /// The rail's ownership mark on a borrowed bezel button.
    ///
    /// Vanilla re-runs <c>VirtualMFD.SetupButtons</c> whenever a faction panel refreshes,
    /// resetting every bezel label to its short code, and a repeated map maximise runs
    /// adoption over the same buttons. Without a mark the rail brands its own output:
    /// the rich-text line is sanitised into fragments like <c>BDFSIZE=</c> and a second
    /// decoration stacks on the first. The mark makes adoption idempotent, carries the
    /// clean code for rail ordering, and lets the rail put its own line back after the
    /// game resets the label.
    /// </summary>
    internal sealed class MfdRailBrand : MonoBehaviour
    {
        /// <summary>The catalog code the button was branded with, or null when unknown.</summary>
        public string Code;

        /// <summary>The exact text the rail last wrote to the label.</summary>
        public string Text;

        /// <summary>The label the rail restyles; null when the button had no text.</summary>
        public TMP_Text Label;

        public void Reassert()
        {
            if (Label != null && Text != null && Label.text != Text) Label.text = Text;
        }
    }
}

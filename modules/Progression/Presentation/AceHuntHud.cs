using BoscaliSummer.Modules.Progression.Domain;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using NuclearOption.Networking;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// Passive local threat display; all encounter state comes from the squad host. This is a
    /// screen-space overlay outside any MFD bezel, so it keeps its own absolute layout (kit v2's
    /// <c>AvFlow</c> is a page-body concept); only the chrome primitives moved to kit v2
    /// (AvText/AvIcons and the vector kit instead of the retired v1 kit calls), geometry unchanged.
    /// Look (2026-10-12, the TAC BOARD's visor language): no plates and no slabs -- the words are in the game's own HUD theme
    /// ink (<see cref="ThemeManager"/>: ink, BRIGHT for the callsign, the caution colour in bold for the alert) over a soft feathered shade,
    /// with corner brackets round the panel and round its picture, ability and card cells, and the header's hairline shelf (interrupted by its
    /// two captions, ending in a node and a 45 degree stub). Every colour is read from the theme at each refresh.
    /// </summary>
    internal sealed class AceHuntHud : MonoBehaviour, ISceneService
    {
        /// <summary>The tones of the visor look, resolved against the player's HUD theme (with the old fixed colours when there is none).</summary>
        private enum Tone : byte { Ink, Bright, Caution, Dim }

        private static ColorTheme Theme => ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        private static Color Caution => Theme != null ? Theme.Warning : new Color32(246, 194, 66, 255);
        private static Color InkColor => Theme != null ? Theme.AllClear : new Color32(127, 223, 154, 255);

        private static Color Of(Tone tone)
        {
            Color ink = InkColor;
            switch (tone)
            {
                case Tone.Bright: return Color.Lerp(ink, Color.white, 0.7f);
                case Tone.Caution: return Caution;
                case Tone.Dim: return ink.WithAlpha(0.7f);
                default: return ink;
            }
        }

        /// <summary>A graphic and the tone (and alpha share) it is painted in: <see cref="Recolor"/> repaints every one from the theme.</summary>
        private struct Tinted
        {
            public Graphic Graphic;
            public Tone Tone;
            public float Alpha;
        }

        private readonly System.Collections.Generic.List<Tinted> tinted = new System.Collections.Generic.List<Tinted>(64);
        /// <summary>The shade stands this far outside the panel it backs (its feathered edge is <see cref="HudSprites.FeatherEdge"/> wide, so the panel's own edge is under the full part) at this alpha.</summary>
        private const float ShadeMargin = 44f, ShadeAlpha = 0.45f;
        private ISquadView squad;
        private GameObject root;
        private Canvas bannerCanvas;
        private TMP_Text callsign, identity, status, proficiency, formation, returning, crestCaption;
        private Image portrait, crest, compactCrest;
        private TMP_Text portraitFallback;
        private string crestKey;
        private readonly AvFrame[] tierPips = new AvFrame[5];
        private readonly GameObject[] abilitySlots = new GameObject[4];
        private TMP_Text noAbilities;
        private string portraitIdentity;
        private float nextRefresh;
        private RectTransform expandedPanel, compactPanel;
        private CanvasGroup expandedGroup, compactGroup;
        private TMP_Text compactStatus;
        private int shownHuntId = -1;
        private bool shown;
        private EnemyWingView shownWing;
        private float introducedAt;

        public void Configure(ISquadView view) => squad = view;

        private void Update()
        {
            if (Application.isBatchMode) return;
            AnimateCollapse();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            Canvas map = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas;
            if (squad == null || !squad.HuntActive || squad.ActiveEnemyWingIndex < 0 ||
                map != null && map.isActiveAndEnabled ||
                !GameManager.GetLocalPlayer<Player>(out Player player) || player == null ||
                player.Aircraft == null || player.Aircraft.disabled || player.Aircraft.HasEjected())
            {
                if (root != null) root.SetActive(false);
                shown = false;
                return;
            }

            if (root == null) { Build(); shown = false; }
            if (shownHuntId != squad.ActiveHuntId)
            { shownHuntId = squad.ActiveHuntId; introducedAt = Time.unscaledTime; }
            root.SetActive(true);
            AnimateCollapse();
            ClearBoard();
            EnemyWingView wing = squad.GetEnemyWing(squad.ActiveEnemyWingIndex);
            if (shown && SameWing(shownWing, wing)) return; // nothing displayed changed; the countdown-free text stays
            shown = true;
            shownWing = wing;
            string ace = wing.AceName ?? "UNKNOWN";
            int separator = ace.IndexOf(" / ", System.StringComparison.Ordinal);
            string name = separator < 0 ? ace : ace.Substring(0, separator);
            string handle = separator < 0 ? "UNIDENTIFIED ACE" : ace.Substring(separator + 3);
            callsign.text = handle.ToUpperInvariant();
            compactStatus.text = "ACE THREAT  ·  " + handle.ToUpperInvariant() + "  ·  " +
                wing.MembersAlive + "/" + wing.MemberCount + " ACTIVE";
            identity.text = name + "  ·  " + wing.Symbol + " " + wing.WingName;
            proficiency.text = (wing.Skill ?? "UNKNOWN").ToUpperInvariant();
            formation.text = wing.MembersAlive + " / " + wing.MemberCount + " ACTIVE";
            returning.text = wing.Returns > 0 ? "RETURNING ACE  ·  ENCOUNTER " + (wing.Returns + 1) : "ENEMY ACE  ·  TIER " + wing.Tier;
            status.text = "STATUS  " + (wing.Status ?? "HUNTING") + "   ·   TARGET YOU";
            for (int i = 0; i < abilitySlots.Length; i++)
                abilitySlots[i].SetActive((wing.AbilityMask & (1 << i)) != 0);
            noAbilities.gameObject.SetActive(wing.AbilityMask == 0);
            Recolor();
            for (int i = 0; i < tierPips.Length; i++)
            {
                bool lit = i < wing.Tier;      // a lit tier is a filled square, an unlit one a hollow one
                tierPips[i].Fill = lit;
                tierPips[i].Paint(lit ? Caution : Color.clear, lit ? Color.clear : Caution.WithAlpha(0.6f));
            }
            string crestIdentity = (wing.Symbol ?? string.Empty) + "|" + (wing.WingName ?? string.Empty);
            if (crestKey != crestIdentity)
            {
                crestKey = crestIdentity;
                Sprite mark = EmblemRenderer.Procedural(EmblemDesign.Hostile(crestIdentity));
                crest.sprite = compactCrest.sprite = mark;
                crest.enabled = compactCrest.enabled = mark != null;
                crestCaption.text = string.IsNullOrEmpty(wing.WingName) ? "HOSTILE" : wing.WingName;
            }
            int faction = PortraitFactions.OpposingLocal;
            string portraitKey = ace + "|" + faction;
            if (portraitIdentity != portraitKey)
            {
                portraitIdentity = portraitKey;
                // Borrow the same identity-based portrait as Wing Command; never own/destroy it.
                portrait.sprite = WingLink.PersonnelPortrait(name, separator < 0 ? "" : handle, PortraitRole.Pilot, faction);
                portrait.enabled = portrait.sprite != null;
                portraitFallback.gameObject.SetActive(portrait.sprite == null);
            }
        }

        private static bool SameWing(EnemyWingView a, EnemyWingView b) =>
            a.Tier == b.Tier && a.MembersAlive == b.MembersAlive && a.MemberCount == b.MemberCount &&
            a.Returns == b.Returns && a.AbilityMask == b.AbilityMask &&
            a.Symbol == b.Symbol && a.WingName == b.WingName && a.AceName == b.AceName &&
            a.Skill == b.Skill && a.Status == b.Status;

        private void Build()
        {
            root = new GameObject("Boscali Ace Hunt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            Canvas canvas = bannerCanvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            tinted.Clear();

            RectTransform panel = Holder((RectTransform)root.transform, new Rect(0, -48, 704, 184));
            panel.name = "Ace Threat Panel";
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            expandedPanel = panel;
            expandedGroup = panel.gameObject.AddComponent<CanvasGroup>();
            expandedGroup.blocksRaycasts = false;
            Shade(panel);
            Brackets(panel, new Rect(0, 0, 704, 184), 12f, Tone.Caution, 0.85f);
            TMP_Text headline = Label(panel, new Rect(14, -10, 360, 24), "HOSTILE ACE DETECTED", AvTextRole.Title, Tone.Caution, bold: true);
            TMP_Text alertWord = Label(panel, new Rect(420, -10, 266, 24), "AIR DEFENSE ALERT", AvTextRole.Label, Tone.Ink, TextAlignmentOptions.MidlineRight);
            Shelf(panel, 704f, 184f, -22f, 14f + AvText.Width(headline) + 4f, 686f - AvText.Width(alertWord) - 4f);

            // Pilot portrait: brackets round the picture
            Brackets(panel, new Rect(16, -44, 98, 114), 6f, Tone.Caution, 0.7f);
            portraitFallback = Label(panel, new Rect(20, -70, 90, 50), "NO VISUAL", AvTextRole.Micro, Tone.Ink, TextAlignmentOptions.Center);
            portrait = ImageBox(panel, new Rect(18, -46, 94, 110));
            portrait.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            portrait.rectTransform.anchoredPosition = new Vector2(65f, -101f);
            Label(panel, new Rect(16, -162, 98, 16), "ACE LEADER", AvTextRole.Micro, Tone.Caution, TextAlignmentOptions.Center, bold: true);

            // Callsign, rank badge & wing identity
            returning = Label(panel, new Rect(130, -43, 218, 16), "", AvTextRole.Micro, Tone.Caution, bold: true);
            callsign = Label(panel, new Rect(124, -58, 230, 32), "", AvTextRole.Display, Tone.Bright);
            identity = Label(panel, new Rect(126, -88, 228, 16), "", AvTextRole.ProseSmall, Tone.Ink);

            // Ability badges
            noAbilities = Label(panel, new Rect(362, -54, 260, 20), "NO ACTIVE THREAT ABILITIES", AvTextRole.Micro, Tone.Ink);
            string[] names = { "TOUGH", "CM", "NOTCH", "GHOST" };
            for (int i = 0; i < abilitySlots.Length; i++)
            {
                RectTransform slot = Holder(panel, new Rect(362 + i * 66, -43, 62, 35));
                abilitySlots[i] = slot.gameObject;
                Brackets(slot, new Rect(0, 0, 62, 35), 5f, Tone.Caution, 0.55f);
                Glyph(slot, new Rect(21, -2, 20, 20), (HuntMark)((int)HuntMark.Toughness + i));
                // A Micro line is 14 px tall: every Micro row here is 16.
                Label(slot, new Rect(0, -20, 62, 16), names[i], AvTextRole.Micro, Tone.Caution, TextAlignmentOptions.Center, bold: true);
            }

            // Generated squadron crest
            Rect crestFrame = new Rect(634, -43, 60, 60);
            crest = ImageBox(panel, new Rect(crestFrame.x + 2, crestFrame.y - 2, 56, 56));
            crest.enabled = false;
            Brackets(panel, crestFrame, 6f, Tone.Caution, 0.55f);
            // Two Micro lines across the free column beside the cards: a long wing name wraps instead of leaving the panel.
            crestCaption = Label(panel, new Rect(626, -105, 76, 30), "", AvTextRole.Micro, Tone.Ink, TextAlignmentOptions.Top);
            crestCaption.enableWordWrapping = true;

            // Three telemetry cards
            proficiency = SkillCard(panel, 126, 158, HuntMark.Skill, "COMBAT SKILL");
            SkillCard(panel, 290, 158, HuntMark.Target, "PURSUIT").text = "HUNTER";
            formation = SkillCard(panel, 454, 170, HuntMark.Formation, "WING LEADER");

            // Divider and footer status bar
            Rule(panel, new Rect(126, -149, 562, 1), Tone.Caution, 0.3f);
            for (int i = 0; i < tierPips.Length; i++)
                tierPips[i] = PanelFrame(panel, new Rect(126 + i * 14, -161, 10, 5), Caution);
            status = Label(panel, new Rect(206, -156, 482, 16), "", AvTextRole.Micro, Tone.Caution, bold: true);

            // Minimized compact panel
            compactPanel = Holder((RectTransform)root.transform, new Rect(0, -8, 420, 36));
            compactPanel.name = "Minimized Ace Hunt";
            compactPanel.anchorMin = compactPanel.anchorMax = new Vector2(0.5f, 1f);
            compactPanel.pivot = new Vector2(0.5f, 1f);
            compactGroup = compactPanel.gameObject.AddComponent<CanvasGroup>();
            compactGroup.blocksRaycasts = false;
            Shade(compactPanel);
            Brackets(compactPanel, new Rect(0, 0, 420, 36), 8f, Tone.Caution, 0.85f);
            Glyph(compactPanel, new Rect(12, -10, 16, 16), HuntMark.Target);
            compactCrest = ImageBox(compactPanel, new Rect(34, -9, 18, 18));
            compactCrest.enabled = false;
            compactStatus = Label(compactPanel, new Rect(58, -6, 350, 24), "", AvTextRole.Label, Tone.Caution, bold: true);

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            Recolor();
        }

        /// <summary>
        /// Steps both panels sideways, clear of the TAC BOARD's box (<see cref="AceHuntPlacement"/>): the board's stores side stands under the right end of the centred panel. Both panels are centred on the screen
        /// at x 0, so the shift is their anchored x; it is written only when it changes (a few times a second at most: the board's box changes with its page and the pilot's options).
        /// </summary>
        private void ClearBoard()
        {
            float k = bannerCanvas != null && bannerCanvas.scaleFactor > 0f ? 1f / bannerCanvas.scaleFactor : 1f, left = 0f, top = 0f, right = 0f, bottom = 0f;
            bool boxed = ModuleServices.TryGet(out IHudBoard board) && board.TryGetScreenBox(out left, out top, out right, out bottom);
            float screenW = Screen.width * k;
            Dodge(expandedPanel, boxed ? AceHuntPlacement.Shift(screenW, 704f, 48f, 184f, left * k, top * k, right * k, bottom * k) : 0f);
            Dodge(compactPanel, boxed ? AceHuntPlacement.Shift(screenW, 420f, 8f, 36f, left * k, top * k, right * k, bottom * k) : 0f);
        }

        private static void Dodge(RectTransform panel, float x)
        {
            if (panel != null && panel.anchoredPosition.x != x) panel.anchoredPosition = new Vector2(x, panel.anchoredPosition.y);
        }

        private void AnimateCollapse()
        {
            if (root == null || !root.activeSelf || expandedPanel == null) return;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - introducedAt - 10f) / 0.4f));
            expandedPanel.gameObject.SetActive(t < 1);
            expandedGroup.alpha = 1 - t;
            expandedPanel.localScale = Vector3.one * Mathf.Lerp(1, 0.85f, t);
            compactPanel.gameObject.SetActive(t > 0);
            compactGroup.alpha = t;
            compactPanel.localScale = Vector3.one * Mathf.Lerp(0.9f, 1, t);
        }

        private TMP_Text SkillCard(RectTransform panel, float x, float width, HuntMark mark, string caption)
        {
            Brackets(panel, new Rect(x, -109, width, 35), 5f, Tone.Caution, 0.4f);
            Glyph(panel, new Rect(x + 6, -115, 22, 22), mark);
            Label(panel, new Rect(x + 32, -109, width - 36, 16), caption, AvTextRole.Micro, Tone.Ink);
            return Label(panel, new Rect(x + 32, -123, width - 36, 18), "", AvTextRole.DataStrong, Tone.Caution, bold: true);
        }

        // ---- Kit v2 chrome primitives (absolute layout; this HUD lives outside any AvFlow page) ----

        /// <summary>Anchored top-left placement, y measured downward from the panel's own origin
        /// exactly as this widget's Rect literals already assume (matches the retired v1 Place helper
        /// convention, kept locally so none of the geometry below has to be re-derived).</summary>
        private static void Place(RectTransform t, Rect area)
        {
            t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 1f);
            t.anchoredPosition = new Vector2(area.x, area.y);
            t.sizeDelta = new Vector2(area.width, area.height);
            t.localScale = Vector3.one;
        }

        /// <summary>A bare rectangle (no fill, nothing drawn): a container the cells and the panels are placed in.</summary>
        private static RectTransform Holder(RectTransform parent, Rect area)
        {
            var go = new GameObject("Holder", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Place(rect, area);
            return rect;
        }

        private static AvFrame PanelFrame(RectTransform parent, Rect area, Color fill)
        {
            AvFrame frame = AvFrame.Add(parent, "Panel", default(AvChamfer));
            frame.Paint(fill, Color.clear);
            frame.raycastTarget = false;
            Place(frame.rectTransform, area);
            return frame;
        }

        private void Tint(Graphic graphic, Tone tone, float alpha = 1f) => tinted.Add(new Tinted { Graphic = graphic, Tone = tone, Alpha = alpha });

        /// <summary>Repaint every tinted graphic in the player's HUD theme (a colour is written only when it changed).</summary>
        private void Recolor()
        {
            for (int i = 0; i < tinted.Count; i++)
            {
                Tinted t = tinted[i];
                if (t.Graphic == null) continue;
                Color c = Of(t.Tone);
                c.a *= t.Alpha;
                if (t.Graphic.color != c) t.Graphic.color = c;
            }
        }

        /// <summary>A soft feathered patch of shade a little larger than <paramref name="panel"/>, under everything of it: the ink is read through it, not on a plate.</summary>
        private static void Shade(RectTransform panel)
        {
            Image shade = HudSprites.Child(panel, "Shade", HudSprites.Feather, sliced: true);
            shade.rectTransform.offsetMin = new Vector2(-ShadeMargin, -ShadeMargin);
            shade.rectTransform.offsetMax = new Vector2(ShadeMargin, ShadeMargin);
            shade.color = HudSprites.ShadeTint.WithAlpha(ShadeAlpha);
            shade.transform.SetAsFirstSibling();
        }

        /// <summary>
        /// Four corner brackets (arm <paramref name="arm"/>) round <paramref name="area"/> (the widget's own top-left, y-down rectangles), one small vector mesh in a holder of its own: a cell that is switched off takes its
        /// brackets with it when the holder is the cell itself. Drawn white and tinted by <paramref name="tone"/> at <paramref name="alpha"/>.
        /// </summary>
        private void Brackets(RectTransform parent, Rect area, float arm, Tone tone, float alpha)
        {
            RectTransform holder = Holder(parent, area);
            holder.name = "Brackets";
            AvVector vector = AvVector.Create(holder, "Strokes", 8);
            AvStrokes.Bracket(vector.Buffer, 0f, 0f, area.width, area.height, arm, 1.25f, new Rgba(1f, 1f, 1f, 1f));
            vector.Commit();
            Tint(vector, tone, alpha);
        }

        /// <summary>
        /// The header's hairline shelf at <paramref name="y"/> (the widget's y-down, negative): a short lead from the left edge, then the line between the two captions (<paramref name="from"/> to <paramref name="to"/>, 4 clear of each),
        /// then a short run to a 4 unit node 8 in from the right edge and a 45 degree stub up and to the right whose tip lands on the edge.
        /// </summary>
        private void Shelf(RectTransform panel, float width, float height, float y, float from, float to)
        {
            RectTransform holder = Holder(panel, new Rect(0, 0, width, height));
            holder.name = "Shelf";
            AvVector vector = AvVector.Create(holder, "Strokes", 8);
            AvQuadBuffer b = vector.Buffer;
            var white = new Rgba(1f, 1f, 1f, 1f);
            float cy = height + y, end = width - 8f;
            AvStrokes.Line(b, 0f, cy, 10f, cy, 1f, white);
            if (to > from) AvStrokes.Line(b, from, cy, to, cy, 1f, white);
            AvStrokes.Line(b, 686f + 4f, cy, end, cy, 1f, white);
            AvStrokes.Fill(b, end - 2f, cy - 2f, 4f, 4f, white);
            AvStrokes.Line(b, end + 2f, cy + 2f, end + 8f, cy + 8f, 1f, white);
            vector.Commit();
            Tint(vector, Tone.Dim);
        }

        /// <summary>A hairline (a 1 unit high rule) in <paramref name="tone"/> at <paramref name="alpha"/>.</summary>
        private void Rule(RectTransform parent, Rect area, Tone tone, float alpha)
        {
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, area);
            Image image = go.GetComponent<Image>();
            image.sprite = AvSprites.White;
            image.raycastTarget = false;
            Tint(image, tone, alpha);
        }

        private static Image ImageBox(RectTransform parent, Rect area)
        {
            var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Place((RectTransform)go.transform, area);
            return image;
        }

        private TMP_Text Label(RectTransform parent, Rect area, string text, AvTextRole role, Tone tone,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            TMP_Text label = AvText.Make(parent, "Label", role, text ?? "", align);
            Place(label.rectTransform, area);
            if (bold) label.fontStyle |= FontStyles.Bold;      // caution and warning words are bold
            label.richText = false;
            Tint(label, tone);
            return label;
        }

        private void Glyph(RectTransform parent, Rect area, HuntMark mark)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(CanvasRenderer), typeof(HuntGlyph));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, area);
            HuntGlyph glyph = go.GetComponent<HuntGlyph>();
            glyph.Mark = mark;
            glyph.raycastTarget = false;
            Tint(glyph, Tone.Caution);
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            portrait = null;
            crest = compactCrest = null;
            crestCaption = null;
            crestKey = null;
            portraitIdentity = null;
            callsign = identity = status = proficiency = formation = returning = portraitFallback = null;
            System.Array.Clear(tierPips, 0, tierPips.Length);
            System.Array.Clear(abilitySlots, 0, abilitySlots.Length);
            tinted.Clear();
            noAbilities = null;
            nextRefresh = 0f;
            expandedPanel = compactPanel = null;
            expandedGroup = compactGroup = null;
            compactStatus = null;
            shownHuntId = -1;
            shown = false;
            introducedAt = 0;
        }

        private void OnDestroy() => ResetForScene();
    }

    internal enum HuntMark { Stripes, Skill, Target, Formation, Toughness, Countermeasures, Notch, Ghost }

    /// <summary>
    /// Small vector marks for the ace-hunt HUD's ability badges: no font-symbol dependency,
    /// texture allocation, or asset lifetime. Kept as a local data glyph (spec §5.4 exempts
    /// per-module glyphs that draw domain-specific iconography the bundled Tabler set has no
    /// equivalent for, e.g. "jammed", "countermeasures dispensed", "notched").
    /// </summary>
    internal sealed class HuntGlyph : MaskableGraphic
    {
        public HuntMark Mark;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            if (Mark == HuntMark.Stripes)
            {
                // Clamp both edges so every diagonal stays inside the alert border.
                for (float x = -r.height; x < r.width; x += 22)
                    Quad(mesh, new Vector2(Mathf.Clamp(x, 0, r.width), 0),
                        new Vector2(Mathf.Clamp(x + 10, 0, r.width), 0),
                        new Vector2(Mathf.Clamp(x + 10 + r.height, 0, r.width), r.height),
                        new Vector2(Mathf.Clamp(x + r.height, 0, r.width), r.height), r.min);
                return;
            }

            // Every other mark is drawn in normalised units on both axes, so it must be
            // laid into the largest square the cell holds or a tall cell stretches it.
            r = Square(r);
            if (Mark == HuntMark.Skill)
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.22f + i * 0.28f;
                    Line(mesh, r, 0.12f, y, 0.5f, y + 0.22f);
                    Line(mesh, r, 0.5f, y + 0.22f, 0.88f, y);
                }
            else if (Mark == HuntMark.Target)
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8, b = (i + 1) * Mathf.PI / 8;
                    Line(mesh, r, 0.5f + Mathf.Cos(a) * 0.31f, 0.5f + Mathf.Sin(a) * 0.31f,
                        0.5f + Mathf.Cos(b) * 0.31f, 0.5f + Mathf.Sin(b) * 0.31f);
                }
                Line(mesh, r, 0, 0.5f, 0.32f, 0.5f);
                Line(mesh, r, 0.68f, 0.5f, 1, 0.5f);
                Line(mesh, r, 0.5f, 0, 0.5f, 0.32f);
                Line(mesh, r, 0.5f, 0.68f, 0.5f, 1);
            }
            else if (Mark == HuntMark.Formation)
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.16f + i * 0.34f, y = i == 1 ? 0.86f : 0.50f;
                    Line(mesh, r, x - 0.12f, y - 0.32f, x, y);
                    Line(mesh, r, x, y, x + 0.12f, y - 0.32f);
                    Line(mesh, r, x, y, x, y - 0.48f);
                }
            }
            else if (Mark == HuntMark.Toughness)
            {
                Line(mesh, r, 0.15f, 0.85f, 0.5f, 0.98f);
                Line(mesh, r, 0.5f, 0.98f, 0.85f, 0.85f);
                Line(mesh, r, 0.15f, 0.85f, 0.2f, 0.4f);
                Line(mesh, r, 0.85f, 0.85f, 0.8f, 0.4f);
                Line(mesh, r, 0.2f, 0.4f, 0.5f, 0.05f);
                Line(mesh, r, 0.5f, 0.05f, 0.8f, 0.4f);
            }
            else if (Mark == HuntMark.Countermeasures)
            {
                for (int i = 0; i < 5; i++)
                {
                    float a = (0.15f + i * 0.175f) * Mathf.PI;
                    Line(mesh, r, 0.5f + Mathf.Cos(a) * 0.2f, 0.1f + Mathf.Sin(a) * 0.2f,
                        0.5f + Mathf.Cos(a) * 0.48f, 0.1f + Mathf.Sin(a) * 0.85f);
                }
            }
            else if (Mark == HuntMark.Notch)
            {
                Line(mesh, r, 0.05f, 0.2f, 0.5f, 0.2f);
                Line(mesh, r, 0.5f, 0.2f, 0.5f, 0.85f);
                Line(mesh, r, 0.25f, 0.6f, 0.5f, 0.85f);
                Line(mesh, r, 0.75f, 0.6f, 0.5f, 0.85f);
            }
            else if (Mark == HuntMark.Ghost)
            {
                Line(mesh, r, 0.2f, 0.15f, 0.2f, 0.65f);
                Line(mesh, r, 0.2f, 0.65f, 0.5f, 0.95f);
                Line(mesh, r, 0.5f, 0.95f, 0.8f, 0.65f);
                Line(mesh, r, 0.8f, 0.65f, 0.8f, 0.15f);
                Line(mesh, r, 0.2f, 0.15f, 0.35f, 0.3f);
                Line(mesh, r, 0.65f, 0.3f, 0.8f, 0.15f);
                Line(mesh, r, 0.33f, 0.55f, 0.39f, 0.55f);
                Line(mesh, r, 0.61f, 0.55f, 0.67f, 0.55f);
            }
        }

        private void Line(VertexHelper mesh, Rect r, float x1, float y1, float x2, float y2)
        {
            Vector2 a = new Vector2(x1 * r.width, y1 * r.height);
            Vector2 b = new Vector2(x2 * r.width, y2 * r.height);
            Vector2 d = b - a;
            Vector2 n = new Vector2(-d.y, d.x).normalized * 0.85f;
            Quad(mesh, a - n, b - n, b + n, a + n, r.min);
        }

        private static Rect Square(Rect rect)
        {
            float side = Mathf.Min(rect.width, rect.height);
            return new Rect(rect.x + (rect.width - side) * 0.5f,
                            rect.y + (rect.height - side) * 0.5f, side, side);
        }

        private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 origin)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a + origin, color, Vector2.zero);
            mesh.AddVert(b + origin, color, Vector2.zero);
            mesh.AddVert(c + origin, color, Vector2.zero);
            mesh.AddVert(d + origin, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

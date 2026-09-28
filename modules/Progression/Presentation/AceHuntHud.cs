using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// Passive local threat display; all encounter state comes from the squad host. This is a
    /// screen-space overlay outside any MFD bezel, so it keeps its own absolute layout (kit v2's
    /// <c>AvFlow</c> is a page-body concept); only the chrome primitives moved to kit v2
    /// (AvFrame/AvText/AvIcons instead of the retired v1 kit calls), geometry unchanged.
    /// </summary>
    internal sealed class AceHuntHud : MonoBehaviour, ISceneService
    {
        private static readonly Color Caution = new Color32(246, 194, 66, 255);
        private static readonly Color Ink = new Color32(12, 16, 19, 250);
        private static readonly Color Secondary = new Color32(194, 201, 201, 255);
        private ISquadView squad;
        private GameObject root;
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
                return;
            }

            if (root == null) Build();
            if (shownHuntId != squad.ActiveHuntId)
            { shownHuntId = squad.ActiveHuntId; introducedAt = Time.unscaledTime; }
            root.SetActive(true);
            AnimateCollapse();
            EnemyWingView wing = squad.GetEnemyWing(squad.ActiveEnemyWingIndex);
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
            for (int i = 0; i < tierPips.Length; i++)
                tierPips[i].Paint(i < wing.Tier ? Caution : new Color32(50, 55, 60, 255), Color.clear);
            string crestIdentity = (wing.Symbol ?? string.Empty) + "|" + (wing.WingName ?? string.Empty);
            if (crestKey != crestIdentity)
            {
                crestKey = crestIdentity;
                Sprite mark = EmblemRenderer.Procedural(EmblemDesign.Hostile(crestIdentity));
                crest.sprite = compactCrest.sprite = mark;
                crest.enabled = compactCrest.enabled = mark != null;
                crestCaption.text = string.IsNullOrEmpty(wing.WingName) ? "HOSTILE" : wing.WingName;
            }
            if (portraitIdentity != ace)
            {
                portraitIdentity = ace;
                // Borrow the same identity-based portrait as Wing Command; never own/destroy it.
                portrait.sprite = WingLink.PilotPortrait(name, separator < 0 ? "" : handle);
                portrait.enabled = portrait.sprite != null;
                portraitFallback.gameObject.SetActive(portrait.sprite == null);
            }
        }

        private void Build()
        {
            root = new GameObject("Boscali Ace Hunt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform panel = Panel((RectTransform)root.transform, new Rect(0, -48, 704, 184), Ink);
            panel.name = "Ace Threat Panel";
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            expandedPanel = panel;
            expandedGroup = panel.gameObject.AddComponent<CanvasGroup>();
            expandedGroup.blocksRaycasts = false;
            Outline(panel, new Rect(0, 0, 704, 184), Caution.WithAlpha(0.55f));
            Panel(panel, new Rect(0, 0, 704, 4), Caution);
            Panel(panel, new Rect(2, -8, 700, 28), new Color32(20, 24, 28, 255));
            Label(panel, new Rect(14, -10, 360, 24), "HOSTILE ACE DETECTED", AvTextRole.Title, Caution);
            Label(panel, new Rect(420, -10, 266, 24), "AIR DEFENSE ALERT", AvTextRole.Label, Secondary,
                TextAlignmentOptions.MidlineRight);

            // Pilot portrait box
            Panel(panel, new Rect(16, -44, 98, 114), AvTheme.SurfaceInert);
            portraitFallback = Label(panel, new Rect(20, -70, 90, 50), "NO VISUAL", AvTextRole.Micro, Secondary,
                TextAlignmentOptions.Center);
            portrait = ImageBox(panel, new Rect(18, -46, 94, 110));
            portrait.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            portrait.rectTransform.anchoredPosition = new Vector2(65f, -101f);
            Panel(panel, new Rect(16, -162, 98, 16), new Color32(24, 28, 32, 255));
            TMP_Text leaderLabel = Label(panel, new Rect(16, -162, 98, 16), "ACE LEADER", AvTextRole.Micro, Caution,
                TextAlignmentOptions.Center);

            // Callsign, rank badge & wing identity
            Panel(panel, new Rect(126, -43, 224, 16), new Color32(28, 32, 36, 255));
            returning = Label(panel, new Rect(130, -43, 218, 16), "", AvTextRole.Micro, Caution);
            callsign = Label(panel, new Rect(124, -58, 230, 32), "", AvTextRole.Display, Color.white);
            identity = Label(panel, new Rect(126, -88, 228, 16), "", AvTextRole.ProseSmall, Secondary);

            // Ability badges
            noAbilities = Label(panel, new Rect(362, -54, 260, 20), "NO ACTIVE THREAT ABILITIES", AvTextRole.Micro, Secondary);
            string[] names = { "TOUGH", "CM", "NOTCH", "GHOST" };
            for (int i = 0; i < abilitySlots.Length; i++)
            {
                RectTransform slot = Panel(panel, new Rect(362 + i * 66, -43, 62, 35), new Color32(22, 26, 30, 255));
                abilitySlots[i] = slot.gameObject;
                Outline(slot, new Rect(0, 0, 62, 35), Caution.WithAlpha(0.35f));
                Glyph(slot, new Rect(21, -2, 20, 20), (HuntMark)((int)HuntMark.Toughness + i));
                TMP_Text badgeCaption = Label(slot, new Rect(0, -22, 62, 12), names[i], AvTextRole.Micro, Caution,
                    TextAlignmentOptions.Center);
            }

            // Generated squadron crest
            Rect crestFrame = new Rect(634, -43, 60, 60);
            Panel(panel, crestFrame, AvTheme.SurfaceInert);
            crest = ImageBox(panel, new Rect(crestFrame.x + 2, crestFrame.y - 2, 56, 56));
            crest.enabled = false;
            Outline(panel, crestFrame, Caution.WithAlpha(0.35f));
            crestCaption = Label(panel, new Rect(634, -105, 60, 14), "", AvTextRole.Micro, Secondary,
                TextAlignmentOptions.Center);

            // Three telemetry cards
            proficiency = SkillCard(panel, 126, 158, HuntMark.Skill, "COMBAT SKILL");
            SkillCard(panel, 290, 158, HuntMark.Target, "PURSUIT").text = "HUNTER";
            formation = SkillCard(panel, 454, 170, HuntMark.Formation, "WING LEADER");

            // Divider and footer status bar
            Panel(panel, new Rect(126, -149, 562, 1), Caution.WithAlpha(0.3f));
            for (int i = 0; i < tierPips.Length; i++)
                tierPips[i] = PanelFrame(panel, new Rect(126 + i * 14, -161, 10, 5), Caution);
            status = Label(panel, new Rect(206, -156, 482, 16), "", AvTextRole.Micro, Caution);

            // Minimized compact panel
            compactPanel = Panel((RectTransform)root.transform, new Rect(0, -8, 420, 36), Ink);
            compactPanel.name = "Minimized Ace Hunt";
            compactPanel.anchorMin = compactPanel.anchorMax = new Vector2(0.5f, 1f);
            compactPanel.pivot = new Vector2(0.5f, 1f);
            compactGroup = compactPanel.gameObject.AddComponent<CanvasGroup>();
            compactGroup.blocksRaycasts = false;
            Outline(compactPanel, new Rect(0, 0, 420, 36), Caution.WithAlpha(0.55f));
            Panel(compactPanel, new Rect(0, 0, 420, 3), Caution);
            Glyph(compactPanel, new Rect(12, -10, 16, 16), HuntMark.Target);
            compactCrest = ImageBox(compactPanel, new Rect(34, -9, 18, 18));
            compactCrest.enabled = false;
            compactStatus = Label(compactPanel, new Rect(58, -6, 350, 24), "", AvTextRole.Label, Caution);

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
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

        private static TMP_Text SkillCard(RectTransform panel, float x, float width, HuntMark mark, string caption)
        {
            RectTransform card = Panel(panel, new Rect(x, -109, width, 35), new Color32(22, 26, 30, 255));
            Outline(card, new Rect(0, 0, width, 35), Caution.WithAlpha(0.25f));
            Glyph(panel, new Rect(x + 6, -115, 22, 22), mark);
            Label(panel, new Rect(x + 32, -111, width - 36, 12), caption, AvTextRole.Micro, Secondary);
            return Label(panel, new Rect(x + 32, -123, width - 36, 18), "", AvTextRole.DataStrong, Caution);
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

        private static RectTransform Panel(RectTransform parent, Rect area, Color fill) =>
            PanelFrame(parent, area, fill).rectTransform;

        private static AvFrame PanelFrame(RectTransform parent, Rect area, Color fill)
        {
            AvFrame frame = AvFrame.Add(parent, "Panel", default(AvChamfer));
            frame.Paint(fill, Color.clear);
            frame.raycastTarget = false;
            Place(frame.rectTransform, area);
            return frame;
        }

        private static void Outline(RectTransform parent, Rect area, Color stroke)
        {
            AvFrame frame = AvFrame.Add(parent, "Outline", default(AvChamfer));
            frame.Fill = false;
            frame.Paint(Color.clear, stroke);
            frame.raycastTarget = false;
            Place(frame.rectTransform, area);
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

        private static TMP_Text Label(RectTransform parent, Rect area, string text, AvTextRole role, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text label = AvText.Make(parent, "Label", role, text ?? "", align);
            Place(label.rectTransform, area);
            label.color = color;
            label.richText = false;
            return label;
        }

        private static void Glyph(RectTransform parent, Rect area, HuntMark mark)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(CanvasRenderer), typeof(HuntGlyph));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, area);
            HuntGlyph glyph = go.GetComponent<HuntGlyph>();
            glyph.Mark = mark;
            glyph.color = Caution;
            glyph.raycastTarget = false;
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
            noAbilities = null;
            nextRefresh = 0f;
            expandedPanel = compactPanel = null;
            expandedGroup = compactGroup = null;
            compactStatus = null;
            shownHuntId = -1;
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

using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>Passive local threat dossier; all encounter state comes from the squad host.</summary>
    internal sealed class AceHuntHud : MonoBehaviour, ISceneService
    {
        private static readonly Color Caution = new Color32(246, 194, 66, 255);
        private static readonly Color Ink = new Color32(12, 16, 19, 250);
        private static readonly Color Secondary = new Color32(194, 201, 201, 255);
        private ISquadView squad;
        private GameObject root;
        private TMP_Text callsign, identity, status, proficiency, formation, returning;
        private Image portrait;
        private TMP_Text portraitFallback;
        private readonly Image[] tierPips = new Image[5];
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
            compactStatus.text = "ACE HUNT / " + handle.ToUpperInvariant() + "  ·  " +
                wing.MembersAlive + "/" + wing.MemberCount;
            identity.text = name + "  /  " + wing.Symbol + " " + wing.WingName;
            proficiency.text = (wing.Skill ?? "UNKNOWN").ToUpperInvariant();
            formation.text = wing.MembersAlive + " / " + wing.MemberCount + " ACTIVE";
            returning.text = wing.Returns > 0 ? "RETURNING ACE / " + wing.Returns : "ENEMY ACE / T" + wing.Tier;
            status.text = (wing.Status ?? "HUNTING") + "   //   PRIMARY TARGET: YOU";
            for (int i = 0; i < abilitySlots.Length; i++)
                abilitySlots[i].SetActive((wing.AbilityMask & (1 << i)) != 0);
            noAbilities.gameObject.SetActive(wing.AbilityMask == 0);
            for (int i = 0; i < tierPips.Length; i++)
                tierPips[i].color = i < wing.Tier ? Caution : new Color32(63, 65, 57, 255);
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

            RectTransform panel = AvKit.Panel((RectTransform)root.transform,
                new Rect(0, -54, 620, 174), Ink).rectTransform;
            panel.name = "Ace Threat Dossier";
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            expandedPanel = panel;
            expandedGroup = panel.gameObject.AddComponent<CanvasGroup>();
            expandedGroup.blocksRaycasts = false;
            AvKit.Outline(panel, new Rect(0, 0, 620, 174), Caution.WithAlpha(0.65f));
            Glyph(panel, new Rect(1, -1, 618, 8), HuntMark.Stripes);
            AvKit.Panel(panel, new Rect(1, -9, 618, 28), Caution);
            Label(panel, new Rect(14, -10, 410, 26), "WARNING  /  ACE HUNT", 17, Ink, true);
            Label(panel, new Rect(436, -10, 170, 26), "HOSTILE INTERCEPT", 12, Ink);

            AvKit.Panel(panel, new Rect(14, -49, 94, 104), new Color32(24, 30, 35, 255));
            portraitFallback = Label(panel, new Rect(18, -68, 86, 60), "NO\nVISUAL", 15, Secondary);
            portrait = AvKit.Panel(panel, new Rect(16, -51, 90, 100), Color.white);
            portrait.type = Image.Type.Simple;
            portrait.preserveAspect = true;
            // Image's aspect fit uses its RectTransform pivot. AvKit defaults to top-left,
            // which pins narrow 2:3 portraits to the left and leaves an uneven empty strip.
            portrait.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            portrait.rectTransform.anchoredPosition = new Vector2(61f, -101f);
            AvKit.CornerTicks(panel, new Rect(14, -49, 94, 104), Caution);
            Label(panel, new Rect(14, -153, 98, 16), "ACE / LEADER", 10, Caution);

            returning = Label(panel, new Rect(124, -43, 470, 17), "", 11, Caution);
            callsign = Label(panel, new Rect(122, -59, 246, 33), "", 27, Color.white, true);
            noAbilities = Label(panel, new Rect(380, -65, 224, 24), "NO ACTIVE ABILITIES", 11, Secondary);
            string[] names = { "TOUGH", "CM", "NOTCH", "GHOST" };
            for (int i = 0; i < abilitySlots.Length; i++)
            {
                RectTransform slot = AvKit.Panel(panel, new Rect(374 + i * 58, -59, 54, 33),
                    new Color32(24, 30, 35, 255)).rectTransform;
                abilitySlots[i] = slot.gameObject;
                Glyph(slot, new Rect(17, -1, 20, 20), (HuntMark)((int)HuntMark.Toughness + i));
                TMP_Text caption = Label(slot, new Rect(0, -21, 54, 12), names[i], 10, Caution);
                caption.alignment = TextAlignmentOptions.Center;
            }
            identity = Label(panel, new Rect(124, -91, 480, 20), "", 12, Secondary);
            proficiency = Skill(panel, 124, HuntMark.Skill, "COMBAT SKILL");
            Skill(panel, 286, HuntMark.Target, "PURSUIT").text = "HUNTER";
            formation = Skill(panel, 448, HuntMark.Formation, "WING LEADER");
            for (int i = 0; i < tierPips.Length; i++)
                tierPips[i] = AvKit.Panel(panel, new Rect(124 + i * 12, -164, 8, 3), Caution);
            status = Label(panel, new Rect(199, -151, 407, 20), "", 11, Caution);
            compactPanel = AvKit.Panel((RectTransform)root.transform, new Rect(0, 0, 380, 36), Ink).rectTransform;
            compactPanel.name = "Minimized Ace Hunt";
            compactPanel.anchorMin = compactPanel.anchorMax = new Vector2(0.5f, 1f);
            compactPanel.pivot = new Vector2(0.5f, 1f);
            compactGroup = compactPanel.gameObject.AddComponent<CanvasGroup>();
            compactGroup.blocksRaycasts = false;
            AvKit.Outline(compactPanel, new Rect(0, 0, 380, 36), Caution.WithAlpha(0.65f));
            Glyph(compactPanel, new Rect(1, -1, 378, 4), HuntMark.Stripes);
            Glyph(compactPanel, new Rect(10, -11, 16, 16), HuntMark.Target);
            compactStatus = Label(compactPanel, new Rect(36, -7, 334, 27), "", 13, Caution);
            // No raycaster, input handler or flashing: this remains safe to read while flying.
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

        private static TMP_Text Skill(RectTransform panel, float x, HuntMark mark, string caption)
        {
            AvKit.Panel(panel, new Rect(x, -114, 154, 34), new Color32(30, 31, 27, 255));
            Glyph(panel, new Rect(x + 4, -120, 22, 22), mark);
            Label(panel, new Rect(x + 33, -115, 119, 13), caption, 9, Secondary);
            return Label(panel, new Rect(x + 33, -128, 119, 18), "", 12, Caution, true);
        }

        private static TMP_Text Label(RectTransform parent, Rect area, string text, float size, Color color, bool bold = false)
        {
            TMP_Text label = AvKit.Label(parent, text, area, color, size,
                bold ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            label.richText = false;
            return label;
        }

        private static void Glyph(RectTransform parent, Rect area, HuntMark mark)
        {
            var go = new GameObject(mark.ToString(), typeof(RectTransform), typeof(HuntGlyph));
            go.transform.SetParent(parent, false);
            AvKit.Place((RectTransform)go.transform, area);
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

    /// <summary>Small vector marks: no font-symbol dependency, texture allocation, or asset lifetime.</summary>
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

using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The missile-cam readout (2026-10-10): a bracketed box low on the HUD canvas, the cluster's own look (HUD ink, the
    /// native weapon label's font, corner brackets), shown only while the orbit camera rides one of our missiles. Header
    /// "MSL 1/2 · AIM-120" and the seeker's state on top, three columns RNG / CLS / TTI in big figures, the target's name
    /// under them. It turns caution (amber) the moment the seeker is searching, passive or blind. Plain UGUI objects under
    /// the native HUD canvas root, geometry from <see cref="ThirdPersonHudLayout.MissileCamBox"/>, text written only on change.
    /// </summary>
    internal sealed class MissileCamPanel
    {
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");
        private const float Bracket = 10f, Stroke = 1.5f;
        private static readonly Color Plate = new Color(0.010f, 0.020f, 0.035f, 0.76f);

        private RectTransform canvasRoot, root;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private AvVector symbols;
        private Image backing;
        private TextMeshProUGUI header, seeker, range, closure, tti, target;
        private string lastHeader, lastSeeker, lastRange, lastClosure, lastTti, lastTarget;
        private int lastKey;
        private bool shown;
        private float lastScale;

        public bool Available => root != null;

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            canvasRoot = null;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null || !hud.iconLayer.gameObject.activeInHierarchy) return false;
            Canvas hudCanvas = hud.iconLayer.GetComponentInParent<Canvas>();
            if (hudCanvas == null) return false;
            Build(hud, hudCanvas);
            return root != null;
        }

        private void Build(CombatHUD hud, Canvas hudCanvas)
        {
            canvasRoot = hudCanvas.transform as RectTransform;
            WeaponStatus weaponStatus = WeaponStatusField?.GetValue(hud) as WeaponStatus;
            TextMeshProUGUI nameLabel = weaponStatus != null ? NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            font = nameLabel != null ? nameLabel.font : null;
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;

            var rootObject = new GameObject("BoscaliMissileCam", typeof(RectTransform), typeof(Image));
            root = (RectTransform)rootObject.transform;
            root.SetParent(canvasRoot, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            backing = rootObject.GetComponent<Image>();
            backing.raycastTarget = false;
            backing.sprite = AvSprites.White;
            backing.color = Plate;

            symbols = AvVector.Create(root, "Brackets", 200);
            header = Label("Header", TextAlignmentOptions.MidlineLeft, 14f);
            seeker = Label("Seeker", TextAlignmentOptions.MidlineRight, 14f);
            range = Label("Range", TextAlignmentOptions.Center, 22f);
            closure = Label("Closure", TextAlignmentOptions.Center, 22f);
            tti = Label("Tti", TextAlignmentOptions.Center, 22f);
            // A four-digit closure is wider than a column: the figures shrink to fit instead of running into their neighbours.
            range.enableAutoSizing = closure.enableAutoSizing = tti.enableAutoSizing = true;
            target = Label("Target", TextAlignmentOptions.Center, 14f);
            root.gameObject.SetActive(false);
            shown = false;
            lastKey = 0;
        }

        private TextMeshProUGUI Label(string name, TextAlignmentOptions alignment, float size)
        {
            TextMeshProUGUI t = HudText.Make(root, name, font, fontMaterial, alignment, size);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return t;
        }

        /// <summary>Refresh from this tick's readout. <paramref name="active"/> false hides the whole panel (one root toggle).</summary>
        public void Present(bool active, in MissileCamData d, AvUnits units, float scale)
        {
            if (root == null) return;
            if (active != shown)
            {
                root.gameObject.SetActive(active);
                shown = active;
            }
            if (!active) return;
            float screenW = canvasRoot.rect.width, screenH = canvasRoot.rect.height;
            if (screenW <= 0f || screenH <= 0f) return;

            ColorTheme theme = ThemeManager.Active.ColorTheme;
            Color ink = MissileCamText.Tone(d) == HudTone.Info ? theme.AllClear : theme.Warning;
            RectF box = ThirdPersonHudLayout.MissileCamBox(screenW, screenH, scale);
            Place(root, box);
            float w = box.Width, h = box.Height, s = scale;
            if (scale != lastScale)
            {
                lastScale = scale;
                header.fontSize = seeker.fontSize = target.fontSize = 14f * s;
                range.fontSizeMax = closure.fontSizeMax = tti.fontSizeMax = 22f * s;
                range.fontSizeMin = closure.fontSizeMin = tti.fontSizeMin = 13f * s;
            }
            Place(header.rectTransform, -w * 0.25f, h * 0.5f - 15f * s, w * 0.5f - 20f * s, 22f * s);
            Place(seeker.rectTransform, w * 0.25f, h * 0.5f - 15f * s, w * 0.5f - 20f * s, 22f * s);
            float column = (w - 24f * s) / 3f;
            // 8 px narrower than the column: a figure that shrinks to fit still leaves a gap to its neighbour.
            Place(range.rectTransform, -column, 2f * s, column - 8f * s, 34f * s);
            Place(closure.rectTransform, 0f, 2f * s, column - 8f * s, 34f * s);
            Place(tti.rectTransform, column, 2f * s, column - 8f * s, 34f * s);
            Place(target.rectTransform, 0f, -h * 0.5f + 14f * s, w - 20f * s, 22f * s);

            Write(header, ref lastHeader, MissileCamText.Header(d));
            Write(seeker, ref lastSeeker, MissileCamText.Seeker(d));
            Write(range, ref lastRange, "<size=55%>RNG </size>" + MissileCamText.Range(d, units));
            Write(closure, ref lastClosure, "<size=55%>CLS </size>" + MissileCamText.Closure(d, units));
            Write(tti, ref lastTti, "<size=55%>TTI </size>" + MissileCamText.Tti(d));
            string targetWords = d.HasTarget ? "TGT " + (string.IsNullOrEmpty(d.Target) ? "--" : d.Target.ToUpperInvariant()) : "NO TARGET";
            Write(target, ref lastTarget, targetWords);
            header.color = seeker.color = range.color = closure.color = tti.color = target.color = ink;

            int key = (int)screenW * 31 + (int)screenH + (int)MissileCamText.Tone(d) * 7 + (int)(scale * 100f) * 13 + ink.GetHashCode() * 17;
            if (key == lastKey) return;
            lastKey = key;
            AvQuadBuffer b = symbols.Buffer;
            b.Clear();
            // The surface fills the box with its origin at the box's bottom-left corner.
            Brackets(b, w, h, ink.ToRgba());
            AvStrokes.Line(b, 10f * s, h - 28f * s, w - 10f * s, h - 28f * s, 1f, ink.ToRgba().WithAlpha(0.4f));
            BufferGuard.Check(b, "missile cam");
            symbols.Commit();
        }

        private static void Write(TextMeshProUGUI text, ref string last, string value)
        {
            if (last == value) return;
            text.text = value;
            last = value;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            Vector2 pos = new Vector2(x, y), size = new Vector2(w, h);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }

        private static void Place(RectTransform rt, RectF rect) => Place(rt, rect.CenterX, rect.CenterY, rect.Width, rect.Height);

        /// <summary>The native HUD's corner-bracket frame: open sides, short arms.</summary>
        private static void Brackets(AvQuadBuffer b, float w, float h, Rgba c)
        {
            float l = 0f, r = w, bt = 0f, t = h;
            AvStrokes.Line(b, l, bt, l, t, Stroke, c);
            AvStrokes.Line(b, l, t, l + Bracket, t, Stroke, c);
            AvStrokes.Line(b, l, bt, l + Bracket, bt, Stroke, c);
            AvStrokes.Line(b, r, bt, r, t, Stroke, c);
            AvStrokes.Line(b, r, t, r - Bracket, t, Stroke, c);
            AvStrokes.Line(b, r, bt, r - Bracket, bt, Stroke, c);
        }

        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            shown = false;
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            symbols = null;
            backing = null;
            header = seeker = range = closure = tti = target = null;
            lastHeader = lastSeeker = lastRange = lastClosure = lastTti = lastTarget = null;
            shown = false;
            lastKey = 0;
            lastScale = 0f;
        }
    }
}

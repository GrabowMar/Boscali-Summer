using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.QoL.Visuals
{
    /// <summary>
    /// What the goggles feel like on the face (2026-10-27): flipping them down covers the eyes and the
    /// picture opens from the middle as the tube comes up, flipping them up is a quick blink, and while they are on a few bright specks
    /// scintillate across the tube (stray radiation on the photocathode), busier for the first second while the tube warms. One overlay
    /// canvas above the HUD (they are the pilot's eyes), built on first use; nothing is drawn while the goggles are off and idle.
    /// </summary>
    internal sealed class NvgOverlay
    {
        private const float OpenSeconds = 0.45f, BlinkClose = 0.1f, BlinkOpen = 0.25f, WarmSeconds = 1f;
        private const int Specks = 12;
        private static readonly Color SpeckColour = new Color(0.9f, 0.96f, 1f);

        private Canvas canvas;
        private RectTransform top, bottom;
        private readonly Image[] specks = new Image[Specks];
        private bool wasOn;
        private float phaseStart = -10f, warmUntil;
        private bool blinking;

        public void Tick(bool on, Transform parent)
        {
            float now = Time.unscaledTime;
            if (on != wasOn)
            {
                wasOn = on;
                blinking = !on;
                phaseStart = now;
                if (on) warmUntil = now + WarmSeconds;
            }
            float closed = Lids(now);
            bool specksOn = on;
            if (closed <= 0f && !specksOn)
            {
                if (canvas != null) canvas.enabled = false;
                return;
            }
            Ensure(parent);
            canvas.enabled = true;
            float half = ((RectTransform)canvas.transform).rect.height * 0.5f;
            // Each lid is a little taller than half the screen so its soft edge still covers when shut.
            float travel = half * 1.25f * (1f - closed);
            top.anchoredPosition = new Vector2(0f, travel);
            bottom.anchoredPosition = new Vector2(0f, -travel);
            top.gameObject.SetActive(closed > 0f);
            bottom.gameObject.SetActive(closed > 0f);

            float chance = !specksOn ? 0f : now < warmUntil ? 0.45f : 0.12f;
            for (int i = 0; i < Specks; i++)
            {
                Image speck = specks[i];
                bool lit = Random.value < chance; // render-only
                speck.enabled = lit;
                if (!lit) continue;
                speck.rectTransform.anchorMin = speck.rectTransform.anchorMax = new Vector2(Random.value, Random.value);
                Color c = SpeckColour;
                c.a = Random.Range(0.35f, 1f);
                speck.color = c;
            }
        }

        /// <summary>0 open, 1 shut: shut at the flip-down and easing open, or the quick blink at the flip-up.</summary>
        private float Lids(float now)
        {
            float t = now - phaseStart;
            if (blinking)
            {
                if (t < BlinkClose) return t / BlinkClose;
                t -= BlinkClose;
                return t < BlinkOpen ? 1f - Mathf.SmoothStep(0f, 1f, t / BlinkOpen) : 0f;
            }
            return t < OpenSeconds ? 1f - Mathf.SmoothStep(0f, 1f, t / OpenSeconds) : 0f;
        }

        public void Release()
        {
            if (canvas != null) Object.Destroy(canvas.gameObject);
            canvas = null;
            wasOn = blinking = false;
            phaseStart = -10f;
        }

        private void Ensure(Transform parent)
        {
            if (canvas != null) return;
            var root = new GameObject("BoscaliNvgOverlay", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(parent, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;
            Sprite lid = LidSprite(), dot = DotSprite();
            top = Lid("TopLid", new Vector2(0.5f, 1f), lid, false);
            bottom = Lid("BottomLid", new Vector2(0.5f, 0f), lid, true);
            for (int i = 0; i < Specks; i++)
            {
                var go = new GameObject("Speck", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root.transform, false);
                Image image = go.GetComponent<Image>();
                image.sprite = dot;
                image.raycastTarget = false;
                image.enabled = false;
                image.rectTransform.sizeDelta = new Vector2(4f, 4f);
                specks[i] = image;
            }
        }

        private RectTransform Lid(string name, Vector2 anchor, Sprite sprite, bool flip)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.black;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, anchor.y);
            rect.anchorMax = new Vector2(1f, anchor.y);
            rect.pivot = anchor;
            // A lid is 62.5 % of the screen tall: half, plus the soft edge that overlaps its partner when shut.
            rect.sizeDelta = new Vector2(0f, Screen.height * 0.625f);
            rect.localScale = flip ? new Vector3(1f, -1f, 1f) : Vector3.one;
            return rect;
        }

        /// <summary>Opaque at the top, a soft lower fifth: the lid's leading edge.</summary>
        private static Sprite LidSprite()
        {
            const int h = 64;
            var texture = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < h; y++)
                texture.SetPixel(0, y, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(y / (h * 0.2f)))));
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite DotSprite()
        {
            const int s = 8;
            var texture = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = new Vector2(x - 3.5f, y - 3.5f).magnitude / 4f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * d)));
                }
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        }
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Floating window chrome (OPS window, event desk): own root canvas, draggable title bar,
    /// close control, blur-behind (opt-in) or frosted glass, scrolling AvFlow body, footer.
    /// </summary>
    public sealed class AvWindow
    {
        private const float TitleH = 30f;
        private readonly AvFrame frame;
        private readonly Image titleBack;
        private readonly TMP_Text titleText;
        private readonly RawImage blur;
        private readonly Image frost;
        private readonly bool hasFrost;
        private readonly GameObject host;
        private readonly Canvas canvas;
        private bool glassy = true;
        private readonly AvScrollView scrollView;
        private bool visible, blurHeld;
        private Vector2 fittedCanvas;
        private readonly Vector3[] corners = new Vector3[4];

        public RectTransform Root { get; }
        public AvTicker Ticker { get; }
        public AvFlow Body { get; }
        public AvFooter Footer { get; }
        /// <summary>The title-bar close control (set its Help to name the hotkey, e.g. "Close (Esc)").</summary>
        public AvControl CloseControl { get; private set; }
        public bool Visible => visible;
        public event Action Closed;

        private AvWindow(Transform uiRoot, string id, string title, float w, float h, int order)
        {
            if (uiRoot == null || uiRoot.GetComponentInParent<Canvas>() == null)
            {
                // Not under a UI canvas: host the window in its own scaled overlay canvas. The window must not
                // BE that root canvas — Unity drives a root canvas's rect to full screen, so the frame would
                // cover the screen while the content kept its w×h layout (the EVN field-archive bug).
                var hostGo = new GameObject("AvWindowHost " + id, typeof(RectTransform));
                if (uiRoot != null) hostGo.transform.SetParent(uiRoot, false);
                var hostCanvas = hostGo.AddComponent<Canvas>();
                hostCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                hostCanvas.sortingOrder = order;
                var scaler = hostGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                uiRoot = hostGo.transform;
                host = hostGo;
            }
            // Never taller or wider than the 1080p reference screen, whoever hosts the window.
            w = Mathf.Min(w, 1880f); h = Mathf.Min(h, 1040f);
            var go = new GameObject("AvWindow " + id, typeof(RectTransform));
            Root = (RectTransform)go.transform;
            Root.SetParent(uiRoot, false);
            canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = order;
            if (host != null) go.AddComponent<Reaper>().Target = host;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            go.AddComponent<GraphicRaycaster>();
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 0.5f);
            Root.sizeDelta = new Vector2(w, h);
            Ticker = go.AddComponent<AvTicker>();
            go.AddComponent<WindowLifetime>().Owner = this;

            blur = new GameObject("Blur", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
            blur.transform.SetParent(Root, false); AvLay.Fill(blur.rectTransform); blur.raycastTarget = false; blur.enabled = false;
            frost = AvLay.Solid(Root, "Frost", Color.white); AvLay.Fill(frost.rectTransform);
            Shader glass = AvBundle.Shader("NOA/UI/Glass");
            hasFrost = glass != null && glass.isSupported;
            if (hasFrost)
            {
                var m = new Material(glass) { name = "NOA Window Frost" };
                if (AvBundle.Noise != null) m.SetTexture("_NoiseTex", AvBundle.Noise);
                m.SetFloat("_Frost", 1f); m.SetFloat("_Reflection", 0.4f);
                frost.material = m;
                go.AddComponent<Reaper>().Target = m;
            }
            else frost.enabled = false;

            frame = AvFrame.Add(Root, "Frame", AvChamfer.Diagonal(6f)); AvLay.Fill(frame.rectTransform);
            frame.Bracket = 6f; frame.raycastTarget = true;   // swallow clicks so they do not reach the map
            titleBack = AvLay.Solid(Root, "TitleBar", Color.clear);
            AvSurfaceGradient.ApplyHeader(titleBack);
            AvLay.Place(titleBack.rectTransform, 0f, 0f, w, TitleH);
            titleText = AvText.Make(Root, "Title", AvTextRole.Head, title);
            AvText.Fit(titleText, false);
            AvLay.Place(titleText.rectTransform, AvGridTokens.Pad, 0f, w - 2f * AvGridTokens.Pad - 36f, TitleH);
            AvControl close = AvControl.Make(Root, new AvControl.Spec("", Hide, AvButtonStyle.Quiet, AvIcon.X));
            close.Help = "Close this window.";
            CloseControl = close;
            AvLay.Place(close.Rect, w - 34f, 3f, 30f, 24f);
            var drag = titleBack.gameObject.AddComponent<DragHandle>(); drag.Target = Root; titleBack.raycastTarget = true;

            RectTransform body = AvLay.Child(Root, "Body");
            scrollView = new AvScrollView(body, true);
            Body = new AvFlow(scrollView.Content, Ticker, w);
            Footer = new AvFooter(Root);
            Root.gameObject.AddComponent<AvHelpScope>().Footer = Footer;
            Ticker.Register(Footer);
            float footerH = AvGridTokens.Footer;
            AvLay.Place(body, 0f, TitleH, w, h - TitleH - footerH);
            scrollView.Place(w, h - TitleH - footerH);
            Footer.Place(new AvSlot(0f, h - footerH, w, footerH));
            Ticker.Add(-1, AvTickRate.Fast, Refresh);
            Ticker.Add(-1, AvTickRate.Slow, Footer.Tick);
            Ticker.Register(new AvRestyleHook(Restyle));
            Restyle();
            go.SetActive(false);
        }

        public static AvWindow Build(Transform uiRoot, string id, string title, float width, float height, int sortingOrder = 200) =>
            new AvWindow(uiRoot, id, title, width, height, sortingOrder);

        public void Show()
        {
            if (visible) return;
            visible = true;
            Root.gameObject.SetActive(true);
            FitIntoCanvas(true);
            SyncBlurLease();
            UpdateBlur();
            Body.Relayout();
        }

        public void Hide()
        {
            if (!visible) return;
            visible = false;
            ReleaseBlur();
            AvInput.ReleaseKeyboardGuard();
            Root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>A solid window: no blur or frost, nothing behind it shows through. For windows that open over other screens' text.</summary>
        public bool Opaque { get => opaque; set { if (opaque == value) return; opaque = value; SyncBlurLease(); UpdateBlur(); Restyle(); } }
        private bool opaque;

        private void Refresh() { FitIntoCanvas(); UpdateBlur(); }

        // Some callers author their maps and columns directly under Root. Fit the entire authored window,
        // preserving those coordinates; resizing only the shared body would leave their controls outside it.
        private void FitIntoCanvas(bool force = false)
        {
            var bounds = canvas.rootCanvas.transform as RectTransform;
            if (bounds == null || Root.parent == null) return;
            Vector2 size = bounds.rect.size;
            if (size.x <= 1f || size.y <= 1f) return;
            Vector3 x = bounds.InverseTransformVector(Root.parent.TransformVector(Vector3.right));
            Vector3 y = bounds.InverseTransformVector(Root.parent.TransformVector(Vector3.up));
            Vector2 authored = Root.rect.size;
            float scale = Mathf.Min(1f, Mathf.Min(Mathf.Max(1f, size.x - 16f) / Mathf.Max(1f, authored.x * x.magnitude),
                Mathf.Max(1f, size.y - 16f) / Mathf.Max(1f, authored.y * y.magnitude)));
            if (!force && size == fittedCanvas && Mathf.Approximately(Root.localScale.x, scale)) return;
            fittedCanvas = size;
            Root.localScale = new Vector3(scale, scale, 1f);
            AvLay.ClampIntoCanvas(Root);
        }

        private void SyncBlurLease()
        {
            bool wantsBlur = visible && !opaque && Root != null && Root.gameObject.activeInHierarchy;
            if (wantsBlur == blurHeld) return;
            if (wantsBlur) { blurHeld = true; AvBlurSource.Acquire(); }
            else ReleaseBlur();
        }

        private void ReleaseBlur()
        {
            if (!blurHeld) return;
            blurHeld = false;
            AvBlurSource.Release();
        }

        private void UpdateBlur()
        {
            if (opaque) { blur.enabled = frost.enabled = glassy = false; return; }
            RenderTexture rt = AvBlurSource.Enabled ? AvBlurSource.Texture : null;
            blur.enabled = rt != null;
            frost.enabled = rt == null && hasFrost && AvFxDriver.Tier != AvFxTier.Off;
            // Without blur or frost behind it the frame is the only thing between the window and the MFD.
            bool nowGlassy = blur.enabled || frost.enabled;
            if (nowGlassy != glassy) { glassy = nowGlassy; Restyle(); }
            if (rt == null) return;
            blur.texture = rt;
            Vector3[] c = corners;
            Root.GetWorldCorners(c);  // overlay canvas: world == screen
            blur.uvRect = new Rect(c[0].x / Screen.width, c[0].y / Screen.height, (c[2].x - c[0].x) / Screen.width, (c[2].y - c[0].y) / Screen.height);
        }

        private void Restyle()
        {
            AvStyle w = AvStyleHost.FuiStyle("window");
            // Keep a little blurred-world transmission without letting bright skies wash out the reading surface.
            frame.Paint(AvStyleHost.Resolve(w.Background, AvTheme.Ground).WithAlpha(opaque ? 1f : glassy ? 0.92f : 0.97f), AvStyleHost.Resolve(w.Border, AvTheme.Frame));
            scrollView?.Restyle();
            frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
            AvStyle t = AvStyleHost.FuiStyle("window-title");
            titleBack.color = AvStyleHost.Resolve(t.Background, AvTheme.SurfaceRaised).WithAlpha(1f);
            titleText.color = AvStyleHost.Resolve(t.Color, AvTheme.TextPrimary);
        }

        private sealed class DragHandle : MonoBehaviour, IDragHandler
        {
            public RectTransform Target;
            public void OnDrag(PointerEventData e)
            {
                var parent = Target.parent as RectTransform;
                if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out Vector2 now) &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position - e.delta, e.pressEventCamera, out Vector2 before))
                    Target.anchoredPosition += now - before;
                AvLay.ClampIntoCanvas(Target);
            }
        }

        private sealed class WindowLifetime : MonoBehaviour
        {
            internal AvWindow Owner;
            private void OnEnable() => Owner?.SyncBlurLease();
            private void OnDisable() => Owner?.ReleaseBlur();
            private void OnDestroy() => Owner?.ReleaseBlur();
        }

        /// <summary>Destroys a window-owned object (the host canvas, the frost material) together with the window.</summary>
        private sealed class Reaper : MonoBehaviour
        {
            public UnityEngine.Object Target;
            private void OnDestroy() { if (Target != null) Destroy(Target); }
        }
    }
}

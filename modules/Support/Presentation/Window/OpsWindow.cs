using System;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Viz;
using LayoutMotion = BoscaliSummer.Features.Support.Domain.Layout.Motion;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Window
{
    /// <summary>
    /// The only shared OPS surface: the dimmed backdrop over the live game, the window's outer
    /// outline and soft shadow, the open/close motion from the button that opened it, the domain
    /// notches in the top margin, input ownership and closing. It draws nothing inside the
    /// outline: each <see cref="IOpsView"/> paints the whole interior itself.
    ///
    /// <para>State (open, active room, input) is set synchronously; every tween is decoration and
    /// reduced motion snaps it. Input is released one frame after closing so the closing key
    /// reaches nothing else.</para>
    /// </summary>
    internal sealed class OpsWindow : MonoBehaviour
    {
        private const float TextInterval = 0.1f;
        private const int Slots = 4;
        private const float BackdropAlpha = 0.62f;
        private const float VignetteAlpha = 0.35f;
        private const float OpenScale = 0.965f;
        private const float CloseScale = 0.98f;

        private sealed class Notch
        {
            public RoomControl Control;
            public AvRoomFrame.NotchChrome Chrome;
            public Image Fill;
            public Image Left, Top, Right, ActiveBar, Icon;
            public TMP_Text Label, Key;
            public bool Active;
        }

        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject content;
        private Image backdrop;
        private Image vignette;
        private RectTransform frame;
        private CanvasGroup frameGroup;
        private Image shadow;
        private Image topLeft, topRight, bottom, left, right;
        private RectTransform roomLayer;
        private readonly Notch[] notches = new Notch[3];
        private Notch closeNotch;
        private readonly IOpsView[] rooms = new IOpsView[Slots];
        private readonly RectTransform[] hosts = new RectTransform[Slots];
        private readonly CanvasGroup[] hostGroups = new CanvasGroup[Slots];
        private readonly Vector2[] builtSize = new Vector2[Slots];
        private readonly IOpsView[] lastByDomain = new IOpsView[3];
        private int current = -1;
        private int outgoing = -1;
        private float outgoingElapsed;
        private float entranceElapsed;
        private bool entranceDone = true;
        private FullscreenInput input;
        private Func<bool> reduceMotion = () => false;
        private TweenState backdropTween;
        private TweenState windowTween;
        private bool closing;
        private float nextText;
        private Box target = WindowGeometry.Default;
        private Vector2 originOffset;
        private bool open;
        private static bool visible;
        private static int closedFrame = -10;

        /// <summary>True while the window is up and for one frame after it closes.</summary>
        public static bool IsOpen => visible || Time.frameCount <= closedFrame + 1;

        public IOpsView Current => current >= 0 ? rooms[current] : null;

        // Harness seams: geometry only, never behaviour.
        internal Box Target => target;
        internal RectTransform Root => (RectTransform)content.transform;
        internal RectTransform RoomLayer => roomLayer;
        internal RectTransform FrameRect => frame;
        internal Image ShadowImage => shadow;

        public static OpsWindow Create(Func<bool> reduce)
        {
            OpsSprites.Ensure();
            var go = new GameObject("BoscaliOpsWindow", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OpsWindowStyle.SortingOrder;
            canvas.enabled = false;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(OpsWindowStyle.ReferenceWidth, OpsWindowStyle.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            OpsWindow window = go.AddComponent<OpsWindow>();
            window.canvas = canvas;
            window.canvasRect = (RectTransform)go.transform;
            window.reduceMotion = reduce ?? (() => false);
            window.input = new FullscreenInput();
            window.Build();
            window.content.SetActive(false);
            return window;
        }

        /// <summary>Make a room reachable from its notch. Up to four rooms (SPACE has two).</summary>
        public void Register(IOpsView room)
        {
            if (room == null || Slot(room) >= 0) return;
            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i] != null) continue;
                rooms[i] = room;
                int domain = DomainIndex(room.Domain);
                if (domain >= 0 && lastByDomain[domain] == null) lastByDomain[domain] = room;
                PaintNotches();
                return;
            }
        }

        /// <summary>Open (or switch to) a room. <paramref name="from"/> is the control that asked.</summary>
        public void Show(IOpsView room, object context, RectTransform from)
        {
            if (!Present(room, context, from)) return;
            input.Hold();
            AvButton.ClearTooltip();
        }

        /// <summary>Everything <see cref="Show"/> does except taking input (the offline harness uses this).</summary>
        internal bool Present(IOpsView room, object context, RectTransform from)
        {
            Register(room);
            int slot = Slot(room);
            if (slot < 0) return false;
            bool wasVisible = open;
            bool reduce = reduceMotion();
            open = true;
            visible = true;
            closing = false;
            canvas.enabled = true;
            content.SetActive(true);
            Rect size = canvasRect.rect;
            Layout(size.width, size.height);
            if (!wasVisible)
            {
                originOffset = Origin(from);
                backdropTween.Value = 0f;
                windowTween.Value = 0f;
                backdropTween.Retarget(1f, LayoutMotion.BackdropIn, reduce);
                windowTween.Retarget(1f, LayoutMotion.WindowIn, reduce);
            }
            Activate(slot, context, wasVisible, reduce);
            ApplyMotion();
            return true;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            visible = false;
            closing = true;
            closedFrame = Time.frameCount;
            AvButton.ClearTooltip();
            bool reduce = reduceMotion();
            backdropTween.Retarget(0f, LayoutMotion.WindowOut, reduce);
            windowTween.Retarget(0f, LayoutMotion.WindowOut, reduce);
            if (reduce) FinishClose();
        }

        private void OnDestroy()
        {
            open = false;
            visible = false;
            closing = false;
            if (current >= 0) rooms[current]?.Hide();
            input?.Release();
            closedFrame = -10;
        }

        // ---- Build ------------------------------------------------------------------------------

        private void Build()
        {
            var contentObject = new GameObject("Content", typeof(RectTransform));
            content = contentObject;
            var root = (RectTransform)contentObject.transform;
            root.SetParent(transform, false);
            AvKit.Stretch(root);

            backdrop = AvRoomFrame.CreateBackdrop(root, BackdropAlpha);
            backdrop.gameObject.AddComponent<BackdropClick>().Window = this;
            vignette = AvKit.Panel(root, new Rect(0f, 0f, 10f, 10f), Color.black.WithAlpha(VignetteAlpha));
            vignette.sprite = OpsSprites.Vignette;
            vignette.type = Image.Type.Simple;
            AvKit.Stretch(vignette.rectTransform);

            frame = AvRoomFrame.CreateFrame(root, "Frame", out frameGroup);

            shadow = AvKit.Panel(frame, new Rect(0f, 0f, 10f, 10f), Color.black.WithAlpha(OpsWindowStyle.ShadowAlpha),
                OpsSprites.Shadow);
            shadow.fillCenter = false;

            var layerObject = new GameObject("Rooms", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster),
                typeof(RectMask2D));
            roomLayer = (RectTransform)layerObject.transform;
            roomLayer.SetParent(frame, false);
            layerObject.AddComponent<RoomClick>().Window = this;

            Color edge = AvTheme.Frame;
            topLeft = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            topRight = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            bottom = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            left = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);
            right = AvRoomFrame.CreateEdge(frame, new Rect(0f, 0f, 1f, 1f), edge);

            for (int i = 0; i < notches.Length; i++)
            {
                int domain = i;
                int icon = i == 0 ? OpsSprites.G.Space : i == 1 ? OpsSprites.G.Cyber : OpsSprites.G.SpecOps;
                notches[i] = BuildNotch(OpsDomains.Tab(OpsDomains.All[i]), "CTRL " + (i + 1),
                    () => SwitchDomain(domain), "Switch the OPS window to " + OpsDomains.Tab(OpsDomains.All[i]) +
                                                " (Ctrl+" + (i + 1) + ", Ctrl+Tab cycles).", icon);
            }
            closeNotch = BuildNotch("× CLOSE", "ESC", Close, "Close the OPS window and return to the map (Esc, or right-click outside the window).");
            Layout(OpsWindowStyle.ReferenceWidth, OpsWindowStyle.ReferenceHeight);
        }

        private Notch BuildNotch(string text, string key, Action click, string tip, int glyph = -1)
        {
            var notch = new Notch();
            notch.Control = RoomControl.Create(frame, new Rect(0f, 0f, 10f, OpsWindowStyle.NotchHeight), click, "Notch");
            RectTransform host = notch.Control.Rect;
            notch.Chrome = AvRoomFrame.CreateNotchChrome(host, text, key, OpsSprites.Notch,
                glyph >= 0 ? OpsSprites.Glyph(glyph) : null);
            notch.Fill = notch.Chrome.Fill;
            notch.Left = notch.Chrome.Left;
            notch.Top = notch.Chrome.Top;
            notch.Right = notch.Chrome.Right;
            notch.ActiveBar = notch.Chrome.ActiveBar;
            notch.Icon = notch.Chrome.Icon;
            notch.Label = notch.Chrome.Label;
            notch.Key = notch.Chrome.Key;
            notch.Control.WithTooltip(tip);
            notch.Control.Changed = _ => PaintNotch(notch);
            return notch;
        }

        /// <summary>Place the window for a canvas size. Rooms built for another size are rebuilt.</summary>
        internal void Layout(float canvasW, float canvasH)
        {
            Box next = WindowGeometry.Window(canvasW, canvasH);
            bool resized = Mathf.Abs(next.Width - target.Width) > 0.5f || Mathf.Abs(next.Height - target.Height) > 0.5f;
            target = next;
            float w = target.Width, h = target.Height;
            frame.sizeDelta = new Vector2(w, h);
            float fall = OpsWindowStyle.ShadowFalloff;
            AvKit.Place(shadow.rectTransform, new Rect(-fall, fall + OpsWindowStyle.ShadowOffsetY, w + fall * 2f, h + fall * 2f));
            AvKit.Place(roomLayer, new Rect(0f, 0f, w, h));
            AvKit.Place(bottom.rectTransform, new Rect(0f, -h + 1f, w, 1f));
            AvKit.Place(left.rectTransform, new Rect(0f, 0f, 1f, h));
            AvKit.Place(right.rectTransform, new Rect(w - 1f, 0f, 1f, h));

            float notchY = OpsWindowStyle.NotchHeight;
            float x = OpsWindowStyle.NotchInset;
            for (int i = 0; i < notches.Length; i++)
            {
                AvKit.Place(notches[i].Control.Rect, new Rect(x, notchY, OpsWindowStyle.NotchWidth, OpsWindowStyle.NotchHeight));
                SizeNotch(notches[i], OpsWindowStyle.NotchWidth);
                x += OpsWindowStyle.NotchWidth + OpsWindowStyle.NotchGap;
            }
            AvKit.Place(closeNotch.Control.Rect, new Rect(w - OpsWindowStyle.NotchInset - OpsWindowStyle.CloseWidth, notchY,
                OpsWindowStyle.CloseWidth, OpsWindowStyle.NotchHeight));
            SizeNotch(closeNotch, OpsWindowStyle.CloseWidth);

            if (resized)
                for (int i = 0; i < hosts.Length; i++)
                    if (hosts[i] != null) builtSize[i] = Vector2.zero;
            PaintNotches();
            ApplyMotion();
        }

        private static void SizeNotch(Notch notch, float width)
        {
            AvRoomFrame.LayoutNotch(notch.Chrome, width, OpsWindowStyle.NotchCut);
        }

        // ---- Rooms ------------------------------------------------------------------------------

        private void Activate(int slot, object context, bool wasVisible, bool reduce)
        {
            IOpsView room = rooms[slot];
            if (current != slot)
            {
                if (wasVisible && current >= 0 && !reduce)
                {
                    if (outgoing >= 0 && outgoing != slot) FinishOutgoing();
                    outgoing = current;
                    outgoingElapsed = 0f;
                }
                else if (current >= 0)
                {
                    rooms[current].Hide();
                    if (hosts[current] != null) hosts[current].gameObject.SetActive(false);
                }
                if (outgoing == slot) outgoing = -1;
                current = slot;
                entranceElapsed = 0f;
                entranceDone = false;
            }
            RectTransform host = Host(slot);
            host.gameObject.SetActive(true);
            host.SetAsLastSibling();
            hostGroups[slot].alpha = 1f;
            var size = new Vector2(target.Width, target.Height);
            if (builtSize[slot] != size)
            {
                for (int i = host.childCount - 1; i >= 0; i--)
                {
                    GameObject child = host.GetChild(i).gameObject;
                    if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
                host.DetachChildren();
                room.Build(host, new Rect(0f, 0f, size.x, size.y));
                builtSize[slot] = size;
                entranceElapsed = 0f;
                entranceDone = false;
            }
            room.Show(context);
            int domain = DomainIndex(room.Domain);
            if (domain >= 0) lastByDomain[domain] = room;
            float progress = LayoutMotion.Progress(entranceElapsed, room.EntranceSeconds, reduce);
            room.Entrance(progress);
            entranceDone = progress >= 1f;
            nextText = 0f;
            PaintNotches();
        }

        private void SwitchDomain(int domain)
        {
            if (domain < 0 || domain >= lastByDomain.Length || lastByDomain[domain] == null) return;
            if (current >= 0 && rooms[current] == lastByDomain[domain]) return;
            Show(lastByDomain[domain], null, null);
        }

        private RectTransform Host(int slot)
        {
            if (hosts[slot] != null) return hosts[slot];
            var go = new GameObject("Room" + slot, typeof(RectTransform), typeof(CanvasGroup));
            hosts[slot] = (RectTransform)go.transform;
            hosts[slot].SetParent(roomLayer, false);
            AvKit.Stretch(hosts[slot]);
            hostGroups[slot] = go.GetComponent<CanvasGroup>();
            return hosts[slot];
        }

        private void FinishOutgoing()
        {
            if (outgoing < 0) return;
            rooms[outgoing]?.Hide();
            if (hosts[outgoing] != null) hosts[outgoing].gameObject.SetActive(false);
            outgoing = -1;
        }

        // ---- Frame -----------------------------------------------------------------------------

        private void Update()
        {
            if (!open && !closing)
            {
                if (input != null && input.Held && Time.frameCount > closedFrame + 1) input.Release();
                return;
            }
            bool reduce = reduceMotion();
            float dt = Time.unscaledDeltaTime;
            backdropTween.Tick(dt);
            windowTween.Tick(dt);
            TickRooms(dt, reduce);
            ApplyMotion();
            if (closing && windowTween.Value <= 0.001f) FinishClose();
            if (!open) return;
            if (!DynamicMap.mapMaximized || GameplayUI.GameIsPaused)
            {
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            if (Chord()) return;
            IOpsView room = Current;
            if (room == null) return;
            room.HandleKeys();
            if (!open) return;
            float time = Time.unscaledTime;
            bool textTick = time >= nextText;
            if (textTick) nextText = time + TextInterval;
            room.Refresh(time, time, textTick);
        }

        private void TickRooms(float dt, bool reduce)
        {
            if (outgoing >= 0)
            {
                outgoingElapsed += dt;
                float fade = 1f - LayoutMotion.Progress(outgoingElapsed, LayoutMotion.RoomSwitch, reduce);
                if (hostGroups[outgoing] != null) hostGroups[outgoing].alpha = fade;
                if (fade <= 0f) FinishOutgoing();
            }
            IOpsView room = Current;
            if (room == null || entranceDone) return;
            entranceElapsed += dt;
            float progress = LayoutMotion.Progress(entranceElapsed, room.EntranceSeconds, reduce);
            room.Entrance(progress);
            entranceDone = progress >= 1f;
        }

        private bool Chord()
        {
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return false;
            for (int i = 0; i < 3; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                SwitchDomain(i);
                return true;
            }
            if (!Input.GetKeyDown(KeyCode.Tab)) return false;
            int from = current >= 0 ? DomainIndex(rooms[current].Domain) : -1;
            for (int step = 1; step <= 3; step++)
            {
                int next = ((from < 0 ? 0 : from) + step) % 3;
                if (lastByDomain[next] == null) continue;
                SwitchDomain(next);
                break;
            }
            return true;
        }

        private void ApplyMotion()
        {
            if (frame == null) return;
            float fade = Mathf.Clamp01(backdropTween.Value);
            backdrop.color = AvTheme.Ground.WithAlpha(BackdropAlpha * fade);
            vignette.color = Color.black.WithAlpha(VignetteAlpha * fade);
            float w = Mathf.Clamp01(windowTween.Value);
            float scale = Mathf.Lerp(closing ? CloseScale : OpenScale, 1f, w);
            Vector2 offset = closing ? Vector2.zero : originOffset * (1f - w);
            frame.anchoredPosition = new Vector2(target.X + target.Width * 0.5f + offset.x,
                -(target.Y + target.Height * 0.5f) + offset.y);
            frame.localScale = new Vector3(scale, scale, 1f);
            frameGroup.alpha = w;
        }

        private void FinishClose()
        {
            closing = false;
            FinishOutgoing();
            if (current >= 0) rooms[current]?.Hide();
            if (current >= 0 && hosts[current] != null) hosts[current].gameObject.SetActive(false);
            current = -1;
            canvas.enabled = false;
            content.SetActive(false);
        }

        /// <summary>The opening control's centre relative to the window's, in canvas units (AvKit Y).</summary>
        private Vector2 Origin(RectTransform from)
        {
            if (from == null || canvas == null) return Vector2.zero;
            var corners = new Vector3[4];
            from.GetWorldCorners(corners);
            Canvas source = from.GetComponentInParent<Canvas>();
            Camera camera = source != null && source.renderMode != RenderMode.ScreenSpaceOverlay ? source.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            Vector2 centre = (a + b) * 0.5f / scale;
            float canvasH = canvasRect.rect.height;
            var windowCentre = new Vector2(target.X + target.Width * 0.5f, target.Y + target.Height * 0.5f);
            var origin = new Vector2(centre.x, canvasH - centre.y);
            Vector2 delta = origin - windowCentre;
            if (float.IsNaN(delta.x) || float.IsNaN(delta.y)) return Vector2.zero;
            return new Vector2(delta.x, -delta.y);
        }

        // ---- Notches ---------------------------------------------------------------------------

        private void PaintNotches()
        {
            int activeDomain = current >= 0 && rooms[current] != null ? DomainIndex(rooms[current].Domain) : -1;
            for (int i = 0; i < notches.Length; i++)
            {
                if (notches[i] == null) continue;
                notches[i].Active = i == activeDomain;
                notches[i].Control.SetEnabled(lastByDomain[i] != null);
                PaintNotch(notches[i]);
            }
            if (closeNotch != null) PaintNotch(closeNotch);
            // The active notch merges into the frame: the top edge opens under it.
            float w = target.Width;
            float gapStart = w, gapEnd = w;
            if (activeDomain >= 0)
            {
                gapStart = OpsWindowStyle.NotchInset + activeDomain * (OpsWindowStyle.NotchWidth + OpsWindowStyle.NotchGap) + 1f;
                gapEnd = gapStart + OpsWindowStyle.NotchWidth - 2f;
            }
            if (topLeft == null) return;
            AvKit.Place(topLeft.rectTransform, new Rect(0f, 0f, Mathf.Min(gapStart, w), 1f));
            topRight.enabled = gapEnd < w;
            if (topRight.enabled) AvKit.Place(topRight.rectTransform, new Rect(gapEnd, 0f, w - gapEnd, 1f));
        }

        private static void PaintNotch(Notch notch)
        {
            bool enabled = notch.Control.Enabled;
            bool hot = enabled && (notch.Active || notch.Control.Hovered);
            notch.Fill.color = notch.Active ? AvTheme.Surface : notch.Control.Hovered && enabled
                ? AvTheme.SurfaceRaised : AvTheme.SurfaceInert.WithAlpha(0.92f);
            Color edge = notch.Active ? AvTheme.RailInfo : AvTheme.Hairline;
            notch.Left.color = notch.Top.color = notch.Right.color = edge;
            notch.ActiveBar.color = notch.Active ? AvTheme.RailReady : notch.Control.Hovered ? AvTheme.RailInfo : AvTheme.Hairline;
            if (notch.Icon != null) notch.Icon.color = !enabled ? AvTheme.Disabled : notch.Active ? AvTheme.RailReady : AvTheme.RailInfo;
            notch.Label.color = !enabled ? AvTheme.Disabled : hot ? AvTheme.TextPrimary : AvTheme.Dim;
            notch.Key.color = !enabled ? AvTheme.Disabled : AvTheme.Dim;
        }

        // ---- Pointer ---------------------------------------------------------------------------

        internal void OnBackdrop(PointerEventData eventData)
        {
            // D5: a left-click on the dimmed game does nothing; a right-click closes.
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right) Close();
        }

        internal void OnInside(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right) Current?.RightClickInside();
        }

        private int Slot(IOpsView room)
        {
            for (int i = 0; i < rooms.Length; i++)
                if (rooms[i] == room) return i;
            return -1;
        }

        private static int DomainIndex(OpsDomain domain)
        {
            for (int i = 0; i < OpsDomains.All.Length; i++)
                if (OpsDomains.All[i] == domain) return i;
            return -1;
        }

        private sealed class BackdropClick : MonoBehaviour, IPointerClickHandler
        {
            public OpsWindow Window;
            public void OnPointerClick(PointerEventData eventData) => Window?.OnBackdrop(eventData);
        }

        private sealed class RoomClick : MonoBehaviour, IPointerClickHandler
        {
            public OpsWindow Window;
            public void OnPointerClick(PointerEventData eventData) => Window?.OnInside(eventData);
        }
    }
}
